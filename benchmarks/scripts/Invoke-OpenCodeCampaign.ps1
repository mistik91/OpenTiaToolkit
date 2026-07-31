[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CampaignPath,

    [string[]]$Model,

    [string[]]$CaseId,

    [string]$FixtureArchivePath,

    [string]$MultiuserSessionPath,

    [int]$TiaInstanceId,

    [string]$OutputRoot,

    [string]$WorkspaceRoot,

    [string]$CliPath,

    [string]$McpPath,

    [switch]$UseUserOpenCodeAuth,

    [string]$UserAuthPath,

    [switch]$AllowPaidModels,

    [ValidateRange(1, 3600)]
    [int]$TimeoutOverrideSeconds,

    [switch]$PreflightOnly,

    [switch]$AllowPrimaryBranch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OpenCodeBenchmark.Common.ps1')

function Get-OptionalProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$runnerPath = Join-Path $PSScriptRoot 'Invoke-OpenCodeBenchmark.ps1'
$openCodeExecutable = Join-Path $benchmarkRoot 'node_modules\opencode-ai\bin\opencode.exe'

if (-not (Test-Path -LiteralPath $openCodeExecutable -PathType Leaf)) {
    throw "Pinned OpenCode executable not found. Run 'npm ci' in '$benchmarkRoot'."
}

& (Join-Path $PSScriptRoot 'Test-BenchmarkDefinitions.ps1')

$resolvedCampaignPath = (Resolve-Path -LiteralPath $CampaignPath).Path
$campaign = Get-Content -LiteralPath $resolvedCampaignPath -Raw | ConvertFrom-Json
$requestedModelFilters = @($Model | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
$requestedCaseFilters = @($CaseId | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
$selectedModelEntries = if ($requestedModelFilters.Count -gt 0) {
    $requestedModels = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]$requestedModelFilters,
        [System.StringComparer]::Ordinal)
    $matches = @($campaign.models | Where-Object { $requestedModels.Contains([string]$_.model) })
    if ($matches.Count -ne $requestedModels.Count) {
        $known = @($campaign.models | ForEach-Object { [string]$_.model })
        $missing = @($requestedModelFilters | Where-Object { $known -cnotcontains $_ })
        throw "Campaign '$($campaign.id)' does not contain requested model(s): $($missing -join ', ')"
    }
    $matches
}
else {
    @($campaign.models)
}
$selectedCaseIds = if ($requestedCaseFilters.Count -gt 0) {
    $knownCases = @($campaign.caseIds | ForEach-Object { [string]$_ })
    $missingCases = @($requestedCaseFilters | Where-Object { $knownCases -cnotcontains $_ })
    if ($missingCases.Count -gt 0) {
        throw "Campaign '$($campaign.id)' does not contain requested case(s): $($missingCases -join ', ')"
    }
    @($campaign.caseIds | Where-Object { $requestedCaseFilters -ccontains [string]$_ })
}
else {
    @($campaign.caseIds)
}

$paidModels = @($selectedModelEntries | Where-Object { [string]$_.costClass -eq 'paid' })
if ($paidModels.Count -gt 0 -and
    (-not [bool]$campaign.allowPaidModels -or -not $AllowPaidModels)) {
    throw "Campaign '$($campaign.id)' contains paid models. Both allowPaidModels=true in the manifest and -AllowPaidModels at runtime are required."
}
if (-not $PreflightOnly -and [string]::IsNullOrWhiteSpace($FixtureArchivePath)) {
    throw 'Live campaign execution requires -FixtureArchivePath.'
}

$resolvedUserAuthPath = $null
if ($UseUserOpenCodeAuth) {
    $resolvedUserAuthPath = Resolve-OpenCodeUserAuthPath -ExplicitPath $UserAuthPath
    foreach ($provider in @($selectedModelEntries |
            ForEach-Object { Get-OpenCodeModelProvider -Model ([string]$_.model) } |
            Sort-Object -Unique)) {
        [void](Get-OpenCodeProviderCredential -AuthPath $resolvedUserAuthPath -Provider $provider)
    }
}
elseif ($paidModels.Count -gt 0) {
    throw 'Paid campaign execution requires -UseUserOpenCodeAuth.'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $benchmarkRoot 'artifacts'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$campaignStarted = [DateTime]::UtcNow
$campaignStamp = $campaignStarted.ToString('yyyyMMddTHHmmssfffZ')
$campaignDirectory = Join-Path $OutputRoot "$campaignStamp-campaign-$($campaign.id)"
New-Item -ItemType Directory -Path $campaignDirectory -Force | Out-Null
$catalogSnapshotPath = Join-Path $campaignDirectory 'catalog-snapshot.json'
$summaryPath = Join-Path $campaignDirectory 'campaign-result.json'

$openCodeVersion = (& $openCodeExecutable --version | Out-String).Trim()
$catalogEntries = [System.Collections.Generic.List[object]]::new()

foreach ($modelEntry in $selectedModelEntries) {
    $modelId = [string]$modelEntry.model
    $reasoningEffort = [string]$modelEntry.reasoningEffort
    $variant = Get-OptionalProperty -Object $modelEntry -Name 'variant'
    $modelMetadata = Get-OpenCodeModelMetadata `
        -OpenCodeExecutable $openCodeExecutable `
        -Model $modelId `
        -Refresh

    if ([string]$modelMetadata.status -ne 'active') {
        throw "Model '$modelId' is not active in the refreshed OpenCode catalog."
    }
    if (-not [bool]$modelMetadata.capabilities.toolcall) {
        throw "Model '$modelId' does not advertise tool-call support."
    }

    $availableVariants = @($modelMetadata.variants.PSObject.Properties.Name)
    if (-not [string]::IsNullOrWhiteSpace([string]$variant) -and
        $availableVariants -cnotcontains [string]$variant) {
        throw "Model '$modelId' does not advertise requested variant '$variant'. Available variants: $($availableVariants -join ', ')"
    }

    $inputCost = [double]$modelMetadata.cost.input
    $outputCost = [double]$modelMetadata.cost.output
    if ([string]$modelEntry.costClass -eq 'free' -and
        ($inputCost -ne 0.0 -or $outputCost -ne 0.0)) {
        throw "Model '$modelId' is declared free but the refreshed catalog reports input/output cost $inputCost/$outputCost USD per million tokens."
    }

    $catalogEntries.Add([pscustomobject][ordered]@{
            model        = $modelId
            reasoningEffort = $reasoningEffort
            variant      = $variant
            availableVariants = $availableVariants
            costClass    = [string]$modelEntry.costClass
            provider     = Get-OpenCodeModelProvider -Model $modelId
            providerId   = [string]$modelMetadata.providerID
            catalogId    = [string]$modelMetadata.id
            name         = [string]$modelMetadata.name
            status       = [string]$modelMetadata.status
            releaseDate  = Get-OptionalProperty -Object $modelMetadata -Name 'release_date'
            cost         = $modelMetadata.cost
            limits       = $modelMetadata.limit
            capabilities = $modelMetadata.capabilities
        })
}

$catalogSnapshot = [ordered]@{
    schemaVersion   = 1
    campaignId      = [string]$campaign.id
    checkedAtUtc    = [DateTime]::UtcNow.ToString('o')
    openCodeVersion = $openCodeVersion
    models          = $catalogEntries
}
$catalogSnapshot | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $catalogSnapshotPath -Encoding UTF8

$modelResults = [System.Collections.Generic.List[object]]::new()
$campaignStatus = if ($PreflightOnly) { 'preflight-completed' } else { 'running' }
$failureMessage = $null
$totalCost = 0.0
$stopCampaign = $false

try {
    if (-not $PreflightOnly) {
        foreach ($modelEntry in $selectedModelEntries) {
            if ($stopCampaign) {
                break
            }

            $modelId = [string]$modelEntry.model
            $reasoningEffort = [string]$modelEntry.reasoningEffort
            $variant = Get-OptionalProperty -Object $modelEntry -Name 'variant'
            $modelRecord = [pscustomobject][ordered]@{
                model           = $modelId
                reasoningEffort = $reasoningEffort
                variant         = $variant
                costClass       = [string]$modelEntry.costClass
                status          = 'running'
                cases           = [System.Collections.Generic.List[object]]::new()
            }
            $modelResults.Add($modelRecord)

            foreach ($selectedCaseId in $selectedCaseIds) {
                if ($totalCost -ge [double]$campaign.maxCostUsd) {
                    $modelRecord.status = 'not-completed-budget-guard'
                    $campaignStatus = 'budget-guard-reached'
                    $stopCampaign = $true
                    break
                }

                $existingSuitePaths = @(
                    Get-ChildItem -LiteralPath $campaignDirectory -Filter 'suite-*.json' -File |
                        ForEach-Object { $_.FullName }
                )
                $runnerParameters = @{
                    CaseId                 = @([string]$selectedCaseId)
                    Model                  = $modelId
                    ReasoningEffort        = $reasoningEffort
                    FixtureArchivePath     = $FixtureArchivePath
                    OutputRoot             = $campaignDirectory
                    UseUserOpenCodeAuth    = [bool]$UseUserOpenCodeAuth
                    AllowPrimaryBranch     = [bool]$AllowPrimaryBranch
                }
                if (-not [string]::IsNullOrWhiteSpace([string]$variant)) {
                    $runnerParameters.Variant = [string]$variant
                }
                if (-not [string]::IsNullOrWhiteSpace($MultiuserSessionPath)) {
                    $runnerParameters.MultiuserSessionPath = $MultiuserSessionPath
                }
                if ($TiaInstanceId -gt 0) {
                    $runnerParameters.TiaInstanceId = $TiaInstanceId
                }
                if (-not [string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
                    $runnerParameters.WorkspaceRoot = $WorkspaceRoot
                }
                if (-not [string]::IsNullOrWhiteSpace($CliPath)) {
                    $runnerParameters.CliPath = $CliPath
                }
                if (-not [string]::IsNullOrWhiteSpace($McpPath)) {
                    $runnerParameters.McpPath = $McpPath
                }
                if ($UseUserOpenCodeAuth) {
                    $runnerParameters.UserAuthPath = $resolvedUserAuthPath
                }
                if ($TimeoutOverrideSeconds -gt 0) {
                    $runnerParameters.TimeoutOverrideSeconds = $TimeoutOverrideSeconds
                }

                [void]@(& $runnerPath @runnerParameters)

                $newSuite = @(Get-ChildItem -LiteralPath $campaignDirectory -Filter 'suite-*.json' -File |
                        Where-Object { $existingSuitePaths -notcontains $_.FullName } |
                        Sort-Object LastWriteTimeUtc -Descending |
                        Select-Object -First 1)
                if ($newSuite.Count -ne 1) {
                    throw "Expected one new suite result for '$modelId' / '$selectedCaseId', got $($newSuite.Count)."
                }
                $suite = Get-Content -LiteralPath $newSuite[0].FullName -Raw | ConvertFrom-Json
                if (@($suite.results).Count -ne 1) {
                    throw "Expected one case result in '$($newSuite[0].FullName)', got $(@($suite.results).Count)."
                }
                $resultPath = [string]$suite.results[0].resultPath
                if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
                    throw "Suite result references a missing case result: $resultPath"
                }
                $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json

                $caseCost = [double]$result.usage.cost
                $totalCost += $caseCost
                $criticalFailures = @($result.assertions |
                        Where-Object { [bool]$_.critical -and -not [bool]$_.passed })
                $modelRecord.cases.Add([pscustomobject][ordered]@{
                        caseId          = [string]$result.caseId
                        score           = [double]$result.score
                        passed          = [bool]$result.passed
                        criticalFailure = $criticalFailures.Count -gt 0
                        timedOut        = [bool]$result.timedOut
                        exitCode        = [int]$result.exitCode
                        durationSeconds = [double]$result.durationSeconds
                        cost            = $caseCost
                        resultPath      = $resultPath
                    })

                if ([bool]$result.timedOut -or [int]$result.exitCode -ne 0) {
                    $modelRecord.status = 'runtime-failure'
                    $campaignStatus = 'stopped-on-runtime-failure'
                    $stopCampaign = $true
                    break
                }
                if ($criticalFailures.Count -gt 0) {
                    $modelRecord.status = 'disqualified-critical-failure'
                    break
                }
            }

            if ([string]$modelRecord.status -eq 'running') {
                $allPassed = @($modelRecord.cases | Where-Object { -not [bool]$_.passed }).Count -eq 0
                $modelRecord.status = if ($allPassed) { 'qualified' } else { 'not-qualified' }
            }
        }

        if ([string]$campaignStatus -eq 'running') {
            $campaignStatus = 'completed'
        }
    }
}
catch {
    $campaignStatus = 'failed'
    $failureMessage = $_.Exception.Message
    throw
}
finally {
    $campaignFinished = [DateTime]::UtcNow
    $campaignResult = [ordered]@{
        schemaVersion       = 1
        campaignId          = [string]$campaign.id
        title               = [string]$campaign.title
        status              = $campaignStatus
        failure             = $failureMessage
        openCodeVersion     = $openCodeVersion
        campaignPath        = $resolvedCampaignPath
        catalogSnapshotPath = $catalogSnapshotPath
        startedAtUtc        = $campaignStarted.ToString('o')
        finishedAtUtc       = $campaignFinished.ToString('o')
        durationSeconds     = [Math]::Round(($campaignFinished - $campaignStarted).TotalSeconds, 3)
        maxCostUsd          = [double]$campaign.maxCostUsd
        totalCostUsd        = [Math]::Round($totalCost, 8)
        selectedCaseIds     = $selectedCaseIds
        selectedModels      = @($selectedModelEntries | ForEach-Object { [string]$_.model })
        models              = $modelResults
    }
    $campaignResult | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
}

Write-Host "Campaign status: $campaignStatus"
Write-Host "Campaign summary: $summaryPath"
Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
