[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $benchmarkRoot
$casesRoot = Join-Path $benchmarkRoot 'cases'
$campaignsRoot = Join-Path $benchmarkRoot 'campaigns'
$skillsRoot = Join-Path $repoRoot 'plugins\opentia-toolkit-opencode\.opencode\skills'
$fixtureManifestPath = Join-Path $benchmarkRoot 'fixtures\manifest.json'

$errors = [System.Collections.Generic.List[string]]::new()
$fixtures = (Get-Content -LiteralPath $fixtureManifestPath -Raw | ConvertFrom-Json).fixtures
$fixtureIds = @($fixtures | ForEach-Object { [string]$_.id })
$validTypes = @('tool', 'toolOperationSet', 'forbiddenTool', 'finalRegex', 'traceRegex', 'run')

foreach ($fixture in $fixtures) {
    if ([string]::IsNullOrWhiteSpace([string]$fixture.id)) {
        $errors.Add('A fixture has no id.')
    }
    if ($fixture.kind -eq 'tia-project-archive' -and [string]$fixture.sha256 -notmatch '^[A-F0-9]{64}$') {
        $errors.Add("Fixture '$($fixture.id)' has an invalid SHA-256.")
    }
}

$caseFiles = @(Get-ChildItem -LiteralPath $casesRoot -Filter case.json -File -Recurse | Sort-Object FullName)
if ($caseFiles.Count -eq 0) {
    $errors.Add('No benchmark cases were found.')
}

$caseIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($caseFile in $caseFiles) {
    try {
        $case = Get-Content -LiteralPath $caseFile.FullName -Raw | ConvertFrom-Json
    }
    catch {
        $errors.Add("$($caseFile.FullName): invalid JSON: $($_.Exception.Message)")
        continue
    }

    $caseDirectoryName = Split-Path -Leaf (Split-Path -Parent $caseFile.FullName)
    if ([string]$case.id -ne $caseDirectoryName) {
        $errors.Add("$($caseFile.FullName): id '$($case.id)' must match directory '$caseDirectoryName'.")
    }
    if ([string]$case.id -notmatch '^ott-[0-9]{2}$') {
        $errors.Add("$($caseFile.FullName): invalid case id '$($case.id)'.")
    }
    if (-not $caseIds.Add([string]$case.id)) {
        $errors.Add("$($caseFile.FullName): duplicate case id '$($case.id)'.")
    }
    if ([int]$case.schemaVersion -ne 1) {
        $errors.Add("$($caseFile.FullName): unsupported schemaVersion '$($case.schemaVersion)'.")
    }
    if ($fixtureIds -notcontains [string]$case.fixtureId) {
        $errors.Add("$($caseFile.FullName): unknown fixture '$($case.fixtureId)'.")
    }
    if ([int]$case.timeoutSeconds -lt 30 -or [int]$case.timeoutSeconds -gt 3600) {
        $errors.Add("$($caseFile.FullName): timeoutSeconds must be between 30 and 3600.")
    }
    if ([double]$case.passScore -lt 0 -or [double]$case.passScore -gt 100) {
        $errors.Add("$($caseFile.FullName): passScore must be between 0 and 100.")
    }

    $promptPath = Join-Path (Split-Path -Parent $caseFile.FullName) ([string]$case.promptFile)
    if (-not (Test-Path -LiteralPath $promptPath -PathType Leaf)) {
        $errors.Add("$($caseFile.FullName): prompt file '$promptPath' does not exist.")
    }
    elseif ((Get-Item -LiteralPath $promptPath).Length -eq 0) {
        $errors.Add("$($caseFile.FullName): prompt file is empty.")
    }

    foreach ($skill in $case.allowedSkills) {
        $skillPath = Join-Path (Join-Path $skillsRoot ([string]$skill)) 'SKILL.md'
        if (-not (Test-Path -LiteralPath $skillPath -PathType Leaf)) {
            $errors.Add("$($caseFile.FullName): shipped OpenCode skill '$skill' does not exist.")
        }
    }

    $assertionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $points = 0.0
    foreach ($assertion in $case.assertions) {
        $points += [double]$assertion.points
        if (-not $assertionIds.Add([string]$assertion.id)) {
            $errors.Add("$($caseFile.FullName): duplicate assertion id '$($assertion.id)'.")
        }
        if ($validTypes -notcontains [string]$assertion.type) {
            $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' has unsupported type '$($assertion.type)'.")
            continue
        }
        if ([double]$assertion.points -le 0) {
            $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' points must be positive.")
        }

        if ($assertion.type -in @('tool', 'toolOperationSet', 'forbiddenTool')) {
            if ([string]::IsNullOrWhiteSpace([string]$assertion.namePattern)) {
                $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' needs namePattern.")
            }
            else {
                try {
                    [void][regex]::new([string]$assertion.namePattern)
                }
                catch {
                    $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' has invalid namePattern.")
                }
            }
        }
        if ($assertion.type -eq 'toolOperationSet') {
            if ([string]::IsNullOrWhiteSpace([string]$assertion.operationPath)) {
                $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' needs operationPath.")
            }
            if ($null -eq $assertion.PSObject.Properties['expectedOperations'] -or
                @($assertion.expectedOperations).Count -eq 0) {
                $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' needs expectedOperations.")
            }
        }
        if ($assertion.type -in @('finalRegex', 'traceRegex')) {
            if ([string]::IsNullOrWhiteSpace([string]$assertion.pattern)) {
                $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' needs pattern.")
            }
            else {
                try {
                    [void][regex]::new([string]$assertion.pattern)
                }
                catch {
                    $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' has an invalid regex pattern.")
                }
            }
        }
    }
    if ($points -le 0) {
        $errors.Add("$($caseFile.FullName): assertion points total must be positive.")
    }
}

$campaignFiles = @(Get-ChildItem -LiteralPath $campaignsRoot -Filter '*.json' -File | Sort-Object FullName)
if ($campaignFiles.Count -eq 0) {
    $errors.Add('No benchmark campaigns were found.')
}

$campaignIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($campaignFile in $campaignFiles) {
    try {
        $campaign = Get-Content -LiteralPath $campaignFile.FullName -Raw | ConvertFrom-Json
    }
    catch {
        $errors.Add("$($campaignFile.FullName): invalid JSON: $($_.Exception.Message)")
        continue
    }

    if ([int]$campaign.schemaVersion -ne 2) {
        $errors.Add("$($campaignFile.FullName): unsupported schemaVersion '$($campaign.schemaVersion)'.")
    }
    if ([string]$campaign.id -notmatch '^[a-z0-9][a-z0-9-]*$') {
        $errors.Add("$($campaignFile.FullName): invalid campaign id '$($campaign.id)'.")
    }
    if (-not $campaignIds.Add([string]$campaign.id)) {
        $errors.Add("$($campaignFile.FullName): duplicate campaign id '$($campaign.id)'.")
    }
    if ([double]$campaign.maxCostUsd -le 0) {
        $errors.Add("$($campaignFile.FullName): maxCostUsd must be positive.")
    }
    if ([int]$campaign.trialCount -lt 1 -or [int]$campaign.trialCount -gt 10) {
        $errors.Add("$($campaignFile.FullName): trialCount must be between 1 and 10.")
    }
    if (@($campaign.caseIds).Count -eq 0) {
        $errors.Add("$($campaignFile.FullName): at least one caseId is required.")
    }
    foreach ($caseId in $campaign.caseIds) {
        if (-not $caseIds.Contains([string]$caseId)) {
            $errors.Add("$($campaignFile.FullName): unknown case '$caseId'.")
        }
    }

    $modelIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $hasPaidModel = $false
    foreach ($model in $campaign.models) {
        if ([string]$model.model -notmatch '^[A-Za-z0-9._-]+/.+$') {
            $errors.Add("$($campaignFile.FullName): invalid OpenCode model id '$($model.model)'.")
        }
        if (-not $modelIds.Add([string]$model.model)) {
            $errors.Add("$($campaignFile.FullName): duplicate model '$($model.model)'.")
        }
        if ([string]$model.costClass -notin @('free', 'paid', 'subscription')) {
            $errors.Add("$($campaignFile.FullName): model '$($model.model)' has invalid costClass '$($model.costClass)'.")
        }
        $reasoningEffortProperty = $model.PSObject.Properties['reasoningEffort']
        $variantProperty = $model.PSObject.Properties['variant']
        $reasoningEffort = if ($null -eq $reasoningEffortProperty) {
            $null
        }
        else {
            [string]$reasoningEffortProperty.Value
        }
        if ($null -eq $reasoningEffortProperty -or
            [string]::IsNullOrWhiteSpace($reasoningEffort)) {
            $errors.Add("$($campaignFile.FullName): model '$($model.model)' must state reasoningEffort.")
        }
        if ($null -eq $variantProperty) {
            $errors.Add("$($campaignFile.FullName): model '$($model.model)' must state variant, including null for provider-default.")
        }
        elseif ($reasoningEffort -eq 'provider-default' -and
            $null -ne $variantProperty.Value) {
            $errors.Add("$($campaignFile.FullName): model '$($model.model)' uses provider-default reasoning and must set variant to null.")
        }
        elseif (-not [string]::IsNullOrWhiteSpace($reasoningEffort) -and
            $reasoningEffort -ne 'provider-default' -and
            [string]::IsNullOrWhiteSpace([string]$variantProperty.Value)) {
            $errors.Add("$($campaignFile.FullName): model '$($model.model)' uses explicit reasoning effort and must set variant.")
        }
        if ([string]$model.costClass -eq 'paid') {
            $hasPaidModel = $true
        }
    }
    if ($hasPaidModel -and -not [bool]$campaign.allowPaidModels) {
        $errors.Add("$($campaignFile.FullName): paid models require allowPaidModels=true.")
    }
}

$package = Get-Content -LiteralPath (Join-Path $benchmarkRoot 'package.json') -Raw | ConvertFrom-Json
if ([string]$package.dependencies.'opencode-ai' -ne '1.18.10') {
    $errors.Add('package.json must pin opencode-ai exactly to 1.18.10.')
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    throw "Benchmark definition validation failed with $($errors.Count) error(s)."
}

Write-Host "Validated $($caseFiles.Count) benchmark cases, $($campaignFiles.Count) campaigns, $($fixtures.Count) fixtures, and all referenced OpenCode skills."
