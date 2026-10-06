[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CampaignPath,

    [string[]]$Model,

    [string[]]$CaseId,

    [string]$FixtureArchivePath,

    [string]$MultiuserSessionPath,

    [int]$TiaInstanceId,

    [string]$MultiuserAgentIdentity = 'Agent',

    [int]$MultiuserCoordinatorInstanceId,

    [string]$MultiuserHandoffCompletionPath,

    [ValidateRange(1, 100)]
    [int]$BatchSize = 3,

    [string]$CheckpointPath,

    [string]$OutputRoot,

    [string]$WorkspaceRoot,

    [string]$CliPath,

    [string]$McpPath,

    [switch]$UseUserOpenCodeAuth,

    [string]$UserAuthPath,

    [switch]$AllowPaidModels,

    [ValidateRange(1, 3600)]
    [int]$TimeoutOverrideSeconds,

    [switch]$AllowPrimaryBranch,

    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OpenCodeBenchmark.Common.ps1')
. (Join-Path $PSScriptRoot 'MultiuserBenchmark.ps1')

function Save-BatchCheckpoint {
    param(
        [Parameter(Mandatory = $true)][object]$State,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $completed = @($State.workItems | Where-Object { [string]$_.status -ceq 'completed' })
    $next = @($State.workItems | Where-Object { [string]$_.status -cne 'completed' } | Select-Object -First 1)
    $State.completedTrialCount = $completed.Count
    $State.remainingTrialCount = @($State.workItems).Count - $completed.Count
    $State.nextWorkItem = if ($next.Count -eq 1) {
        [pscustomobject][ordered]@{
            model      = [string]$next[0].model
            caseId     = [string]$next[0].caseId
            trialIndex = [int]$next[0].trialIndex
        }
    }
    else {
        $null
    }
    $State.updatedAtUtc = [DateTime]::UtcNow.ToString('o')
    $State | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Get-SelectedValues {
    param(
        [Parameter(Mandatory = $true)][object[]]$Known,
        [AllowEmptyCollection()][string[]]$Requested,
        [Parameter(Mandatory = $true)][scriptblock]$Selector,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $requestedValues = @($Requested | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_)
        })
    if ($requestedValues.Count -eq 0) {
        return @($Known)
    }

    $knownByValue = @{}
    foreach ($item in @($Known)) {
        $knownByValue[[string](& $Selector $item)] = $item
    }
    $selected = [System.Collections.Generic.List[object]]::new()
    foreach ($value in $requestedValues) {
        if (-not $knownByValue.ContainsKey([string]$value)) {
            throw "Unknown $Label '$value'."
        }
        $selected.Add($knownByValue[[string]$value])
    }
    return @($selected)
}

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $benchmarkRoot
$campaignRunner = Join-Path $PSScriptRoot 'Invoke-OpenCodeCampaign.ps1'
$mergeRunner = Join-Path $PSScriptRoot 'Merge-OpenCodeCampaignResults.ps1'
$resolvedCampaignPath = (Resolve-Path -LiteralPath $CampaignPath).Path
$campaign = Get-Content -LiteralPath $resolvedCampaignPath -Raw | ConvertFrom-Json

if ($null -ne $campaign.PSObject.Properties['skillEvaluation'] -and
    $null -ne $campaign.skillEvaluation) {
    throw 'Checkpoint batching currently supports standard model campaigns only, not paired skill-evaluation arms.'
}

$selectedModels = @(Get-SelectedValues `
        -Known @($campaign.models) `
        -Requested $Model `
        -Selector { param($entry) [string]$entry.model } `
        -Label 'campaign model')
$selectedCases = @(Get-SelectedValues `
        -Known @($campaign.caseIds) `
        -Requested $CaseId `
        -Selector { param($entry) [string]$entry } `
        -Label 'campaign case')

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$branch = (& git -C $repoRoot branch --show-current).Trim()
$gitStatus = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all)
if (-not $PlanOnly -and $gitStatus.Count -gt 0) {
    throw 'Checkpointed campaign execution requires a clean Git worktree.'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $benchmarkRoot 'artifacts'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$isResume = -not [string]::IsNullOrWhiteSpace($CheckpointPath)
if ($isResume) {
    $resolvedCheckpointPath = (Resolve-Path -LiteralPath $CheckpointPath).Path
    $batchRoot = Split-Path -Parent $resolvedCheckpointPath
    $state = Get-Content -LiteralPath $resolvedCheckpointPath -Raw | ConvertFrom-Json
    if ([string]$state.campaignId -cne [string]$campaign.id -or
        [string]$state.campaignPath -cne $resolvedCampaignPath) {
        throw 'Checkpoint campaign identity does not match the requested campaign.'
    }
    if ([string]$state.gitCommit -cne $commit) {
        throw "Checkpoint commit '$($state.gitCommit)' does not match current commit '$commit'."
    }
}
else {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $batchRoot = Join-Path $OutputRoot "$stamp-batch-$($campaign.id)"
    New-Item -ItemType Directory -Path $batchRoot -Force | Out-Null
    $resolvedCheckpointPath = Join-Path $batchRoot 'checkpoint.json'
    $workItems = [System.Collections.Generic.List[object]]::new()
    foreach ($modelEntry in $selectedModels) {
        foreach ($selectedCase in $selectedCases) {
            for ($trialIndex = 1; $trialIndex -le [int]$campaign.trialCount; $trialIndex++) {
                $workItems.Add([pscustomobject][ordered]@{
                        model      = [string]$modelEntry.model
                        caseId     = [string]$selectedCase
                        trialIndex = $trialIndex
                        status     = 'pending'
                    })
            }
        }
    }
    $state = [pscustomobject][ordered]@{
        schemaVersion               = 1
        campaignId                  = [string]$campaign.id
        campaignPath                = $resolvedCampaignPath
        status                      = 'planned'
        startedAtUtc                = [DateTime]::UtcNow.ToString('o')
        updatedAtUtc                = $null
        gitCommit                   = $commit
        gitBranch                   = $branch
        configuredBatchSize         = $BatchSize
        completedTrialCount         = 0
        remainingTrialCount         = $workItems.Count
        nextWorkItem                = $null
        rateLimit                   = $null
        lastError                   = $null
        pendingMultiuserHandoff      = $null
        completedMultiuserHandoffs   = @()
        mergedCampaignResultPath    = $null
        acceptedCampaignResultPaths = @()
        attempts                    = @()
        workItems                   = @($workItems)
    }
    Save-BatchCheckpoint -State $state -Path $resolvedCheckpointPath
}

if ($PlanOnly) {
    if ($null -eq $state.PSObject.Properties['pendingMultiuserHandoff'] -or $null -eq $state.pendingMultiuserHandoff) { $state.status = 'planned' }
    Save-BatchCheckpoint -State $state -Path $resolvedCheckpointPath
    Write-Host "Batch plan: $resolvedCheckpointPath"
    Get-Content -LiteralPath $resolvedCheckpointPath -Raw | ConvertFrom-Json
    return
}

if ($null -ne $state.PSObject.Properties['pendingMultiuserHandoff'] -and $null -ne $state.pendingMultiuserHandoff) {
    if ([string]::IsNullOrWhiteSpace($MultiuserHandoffCompletionPath)) { throw 'A Multiuser handoff is pending. Complete its UI check-in and fresh server verification, then resume with -MultiuserHandoffCompletionPath.' }
    Assert-MuHandoffCompletion $state.pendingMultiuserHandoff.contextPath $MultiuserHandoffCompletionPath
    $state.completedMultiuserHandoffs=@($state.completedMultiuserHandoffs)+@([pscustomobject]@{contextPath=$state.pendingMultiuserHandoff.contextPath;completionPath=[IO.Path]::GetFullPath($MultiuserHandoffCompletionPath)})
    $state.pendingMultiuserHandoff=$null
    Save-BatchCheckpoint -State $state -Path $resolvedCheckpointPath
}

if ([string]::IsNullOrWhiteSpace($FixtureArchivePath) -and
    [string]::IsNullOrWhiteSpace($MultiuserSessionPath)) {
    throw 'Batch execution requires -FixtureArchivePath or -MultiuserSessionPath.'
}

$state.status = 'running'
$state.rateLimit = $null
$state.lastError = $null
$state.configuredBatchSize = $BatchSize
Save-BatchCheckpoint -State $state -Path $resolvedCheckpointPath
$newTrialCount = 0

foreach ($workItem in @($state.workItems | Where-Object { [string]$_.status -cne 'completed' })) {
    if ($newTrialCount -ge $BatchSize) {
        $state.status = 'paused-batch-limit'
        break
    }

    $attemptNumber = @($state.attempts | Where-Object {
            [string]$_.model -ceq [string]$workItem.model -and
            [string]$_.caseId -ceq [string]$workItem.caseId -and
            [int]$_.trialIndex -eq [int]$workItem.trialIndex
        }).Count + 1
    $safeModel = ([string]$workItem.model) -replace '[^A-Za-z0-9._-]', '_'
    $segmentRoot = Join-Path $batchRoot ('segments\{0}-{1}-t{2}-a{3}-{4}' -f
            ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')),
            $workItem.caseId,
            $workItem.trialIndex,
            $attemptNumber,
            $safeModel)
    New-Item -ItemType Directory -Path $segmentRoot -Force | Out-Null

    $invokeParameters = @{
        CampaignPath       = $resolvedCampaignPath
        Model              = @([string]$workItem.model)
        CaseId             = @([string]$workItem.caseId)
        TrialIndex         = @([int]$workItem.trialIndex)
        OutputRoot         = $segmentRoot
        UseUserOpenCodeAuth = [bool]$UseUserOpenCodeAuth
        AllowPaidModels    = [bool]$AllowPaidModels
        AllowPrimaryBranch = [bool]$AllowPrimaryBranch
    }
    if (-not [string]::IsNullOrWhiteSpace($FixtureArchivePath)) {
        $invokeParameters.FixtureArchivePath = $FixtureArchivePath
    }
    if (-not [string]::IsNullOrWhiteSpace($MultiuserSessionPath)) {
        $invokeParameters.MultiuserSessionPath = $MultiuserSessionPath
    }
    if ($TiaInstanceId -gt 0) { $invokeParameters.TiaInstanceId = $TiaInstanceId }
    $invokeParameters.MultiuserAgentIdentity=$MultiuserAgentIdentity
    if ($MultiuserCoordinatorInstanceId -gt 0) { $invokeParameters.MultiuserCoordinatorInstanceId=$MultiuserCoordinatorInstanceId }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceRoot)) { $invokeParameters.WorkspaceRoot = $WorkspaceRoot }
    if (-not [string]::IsNullOrWhiteSpace($CliPath)) { $invokeParameters.CliPath = $CliPath }
    if (-not [string]::IsNullOrWhiteSpace($McpPath)) { $invokeParameters.McpPath = $McpPath }
    if ($UseUserOpenCodeAuth -and -not [string]::IsNullOrWhiteSpace($UserAuthPath)) {
        $invokeParameters.UserAuthPath = $UserAuthPath
    }
    if ($TimeoutOverrideSeconds -gt 0) {
        $invokeParameters.TimeoutOverrideSeconds = $TimeoutOverrideSeconds
    }

    try {
        [void]@(& $campaignRunner @invokeParameters)
        $segmentResults = @(Get-ChildItem -LiteralPath $segmentRoot -Filter 'campaign-result.json' -File -Recurse)
        if ($segmentResults.Count -ne 1) {
            throw "Expected one campaign result below '$segmentRoot', got $($segmentResults.Count)."
        }
        $segmentResultPath = $segmentResults[0].FullName
        $segment = Get-Content -LiteralPath $segmentResultPath -Raw | ConvertFrom-Json
        $caseResults = @($segment.models | Where-Object { [string]$_.model -ceq [string]$workItem.model } |
                ForEach-Object { $_.cases } | Where-Object {
                    [string]$_.caseId -ceq [string]$workItem.caseId -and
                    [int]$_.trialIndex -eq [int]$workItem.trialIndex
                })
        if ($caseResults.Count -ne 1) {
            throw "Expected one case result for checkpoint item, got $($caseResults.Count)."
        }
        $caseResult = $caseResults[0]
        $result = Get-Content -LiteralPath ([string]$caseResult.resultPath) -Raw | ConvertFrom-Json
        $failureKind = Get-BenchmarkTrialExecutionFailureKind -Trial $result
        $rateLimited = Test-BenchmarkTrialRateLimited -Trial $result
        $resetAtUtc = Get-BenchmarkRateLimitResetAtUtc -Trial $result
        $failureMessage = if ($null -ne $result.executionFailure) {
            [string]$result.executionFailure.message
        }
        else { $null }
        $statusCode = if ($null -ne $result.executionFailure) {
            $result.executionFailure.statusCode
        }
        else { $null }
        $accepted = [bool]$result.validForModelComparison
        $state.attempts = @($state.attempts) + @([pscustomobject][ordered]@{
                attemptedAtUtc     = [DateTime]::UtcNow.ToString('o')
                model              = [string]$workItem.model
                caseId             = [string]$workItem.caseId
                trialIndex         = [int]$workItem.trialIndex
                attemptNumber      = $attemptNumber
                accepted           = $accepted
                campaignResultPath = $segmentResultPath
                caseResultPath     = [string]$caseResult.resultPath
                executionFailureKind = $failureKind
                statusCode         = $statusCode
                resetAtUtc         = $resetAtUtc
                message            = $failureMessage
            })

        if ($accepted) {
            $workItem.status = 'completed'
            $state.acceptedCampaignResultPaths = @($state.acceptedCampaignResultPaths) + @($segmentResultPath)
            $newTrialCount++
            Save-BatchCheckpoint -State $state -Path $resolvedCheckpointPath
            if ($null -eq $result.PSObject.Properties['multiuserCheckpoint'] -or $null -eq $result.multiuserCheckpoint) { continue }
        }

        if ($null -ne $result.PSObject.Properties['multiuserCheckpoint'] -and $null -ne $result.multiuserCheckpoint) {
            $metadata=Get-Content -LiteralPath (Join-Path (Split-Path -Parent ([string]$caseResult.resultPath)) 'metadata.json') -Raw | ConvertFrom-Json
            if ($null -eq $state.PSObject.Properties['pendingMultiuserHandoff']) { $state | Add-Member -NotePropertyName pendingMultiuserHandoff -NotePropertyValue $null }
            if ($null -eq $state.PSObject.Properties['completedMultiuserHandoffs']) { $state | Add-Member -NotePropertyName completedMultiuserHandoffs -NotePropertyValue @() }
            $state.pendingMultiuserHandoff=[pscustomobject]@{contextPath=(Join-Path $metadata.multiuser.evidenceRoot 'handoff.json');caseResultPath=[string]$caseResult.resultPath;checkpoint=[string]$result.multiuserCheckpoint}
            $state.status='paused-multiuser-handoff'
            break
        }

        if ($rateLimited) {
            $state.status = 'paused-rate-limit'
            $state.rateLimit = [pscustomobject][ordered]@{
                detectedAtUtc = [DateTime]::UtcNow.ToString('o')
                model         = [string]$workItem.model
                caseId        = [string]$workItem.caseId
                trialIndex    = [int]$workItem.trialIndex
                statusCode    = $statusCode
                resetAtUtc    = $resetAtUtc
                message       = $failureMessage
            }
        }
        elseif ($failureKind -ceq 'provider-error') {
            $state.status = 'paused-provider-error'
            $state.lastError = $failureMessage
        }
        elseif ($failureKind -ceq 'harness-error') {
            $state.status = 'paused-harness-error'
            $state.lastError = $failureMessage
        }
        else {
            $state.status = 'paused-execution-error'
            $state.lastError = $failureMessage
        }
        break
    }
    catch {
        $state.status = 'paused-harness-error'
        $state.lastError = $_.Exception.Message
        break
    }
}

$remaining = @($state.workItems | Where-Object { [string]$_.status -cne 'completed' })
if ($remaining.Count -eq 0 -and ($null -eq $state.PSObject.Properties['pendingMultiuserHandoff'] -or $null -eq $state.pendingMultiuserHandoff)) {
    $mergedPath = Join-Path $batchRoot 'merged-campaign-result.json'
    [void]@(& $mergeRunner `
            -CampaignResultPath @($state.acceptedCampaignResultPaths) `
            -OutputPath $mergedPath)
    $state.status = 'completed'
    $state.mergedCampaignResultPath = $mergedPath
}
elseif ([string]$state.status -ceq 'running') {
    $state.status = 'paused-batch-limit'
}

Save-BatchCheckpoint -State $state -Path $resolvedCheckpointPath
Write-Host "Batch status: $($state.status)"
Write-Host "Batch checkpoint: $resolvedCheckpointPath"
if ([string]$state.status -ceq 'paused-rate-limit') {
    Write-Warning ("Provider rate limit detected for {0}/{1}/trial-{2}; resetAtUtc={3}. Resume with -CheckpointPath after the provider resets." -f
        $state.rateLimit.model,
        $state.rateLimit.caseId,
        $state.rateLimit.trialIndex,
        $state.rateLimit.resetAtUtc)
}
Get-Content -LiteralPath $resolvedCheckpointPath -Raw | ConvertFrom-Json
