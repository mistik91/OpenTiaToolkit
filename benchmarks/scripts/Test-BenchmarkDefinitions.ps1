[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'BenchmarkWorkspaceInputs.ps1')

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $benchmarkRoot
$casesRoot = Join-Path $benchmarkRoot 'cases'
$campaignsRoot = Join-Path $benchmarkRoot 'campaigns'
$skillsRoot = Join-Path $repoRoot 'plugins\opentia-toolkit-opencode\.opencode\skills'
$fixtureManifestPath = Join-Path $benchmarkRoot 'fixtures\manifest.json'

$errors = [System.Collections.Generic.List[string]]::new()
$fixtures = (Get-Content -LiteralPath $fixtureManifestPath -Raw | ConvertFrom-Json).fixtures
$fixtureIds = @($fixtures | ForEach-Object { [string]$_.id })
$validTypes = @('tool', 'toolOperationSet', 'forbiddenTool', 'workspaceFile', 'ladBooleanNetwork', 'ladTimerNetwork', 'plcBehaviorEvidence', 'finalRegex', 'traceRegex', 'run')

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

    try {
        [void]@(Get-BenchmarkWorkspaceInputPlan -Case $case `
                -CaseDirectory (Split-Path -Parent $caseFile.FullName) `
                -WorkspacePath (Join-Path ([IO.Path]::GetTempPath()) 'OpenTiaBenchmarkDefinitionValidation'))
    }
    catch { $errors.Add("$($caseFile.FullName): $($_.Exception.Message)") }

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
        if ($assertion.type -in @('workspaceFile', 'ladBooleanNetwork', 'ladTimerNetwork', 'plcBehaviorEvidence')) {
            $relativePath = [string]$assertion.relativePath
            if ([string]::IsNullOrWhiteSpace($relativePath) -or
                [System.IO.Path]::IsPathRooted($relativePath) -or
                @($relativePath -split '[\\/]' | Where-Object { $_ -eq '..' }).Count -gt 0) {
                $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' needs a workspace-relative relativePath without traversal.")
            }
            if ($null -ne $assertion.PSObject.Properties['minBytes'] -and
                $null -ne $assertion.PSObject.Properties['maxBytes'] -and
                [int64]$assertion.minBytes -gt [int64]$assertion.maxBytes) {
                $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' minBytes cannot exceed maxBytes.")
            }
            foreach ($patternProperty in @('contentPatterns', 'forbiddenContentPatterns')) {
                if ($null -eq $assertion.PSObject.Properties[$patternProperty]) {
                    continue
                }
                foreach ($pattern in @($assertion.$patternProperty)) {
                    try {
                        [void][regex]::new([string]$pattern)
                    }
                    catch {
                        $errors.Add("$($caseFile.FullName): assertion '$($assertion.id)' has an invalid $patternProperty regex.")
                    }
                }
            }
        }
        if ($assertion.type -eq 'plcBehaviorEvidence') {
            if ($null -eq $assertion.PSObject.Properties['vectorSha256'] -or [string]$assertion.vectorSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
                $errors.Add("$($caseFile.FullName): behavior assertion '$($assertion.id)' requires vectorSha256.")
            }
            $inputsProperty = $case.PSObject.Properties['workspaceInputs']
            if ($null -eq $inputsProperty -or @($inputsProperty.Value | Where-Object {
                        $_.targetPath -ceq $assertion.relativePath -and $_.sha256 -ieq $assertion.vectorSha256
                    }).Count -ne 1) {
                $errors.Add("$($caseFile.FullName): behavior assertion '$($assertion.id)' must bind one frozen workspace input.")
            }
        }
        if ($assertion.type -in @('ladBooleanNetwork', 'ladTimerNetwork')) {
            if ($null -eq $assertion.PSObject.Properties['networkIndex'] -or [int]$assertion.networkIndex -lt 1 -or
                $null -eq $assertion.PSObject.Properties['outputName'] -or [string]::IsNullOrWhiteSpace([string]$assertion.outputName)) {
                $errors.Add("$($caseFile.FullName): LAD assertion '$($assertion.id)' requires networkIndex and outputName.")
            }
            if ($assertion.type -eq 'ladBooleanNetwork') {
                if ($null -eq $assertion.PSObject.Properties['inputNames'] -or @($assertion.inputNames).Count -lt 1 -or
                    @($assertion.inputNames).Count -gt 10 -or $null -eq $assertion.PSObject.Properties['expectedTrueRows']) {
                    $errors.Add("$($caseFile.FullName): Boolean assertion '$($assertion.id)' requires a bounded input list and explicit truth table.")
                }
            }
            else {
                foreach ($requiredLadProperty in @('inputName', 'timerInstance', 'presetMilliseconds')) {
                    if ($null -eq $assertion.PSObject.Properties[$requiredLadProperty]) {
                        $errors.Add("$($caseFile.FullName): timer assertion '$($assertion.id)' requires $requiredLadProperty.")
                    }
                }
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

    $skillEvaluationProperty = $campaign.PSObject.Properties['skillEvaluation']
    if ($null -ne $skillEvaluationProperty -and $null -ne $skillEvaluationProperty.Value) {
        $skillEvaluation = $skillEvaluationProperty.Value
        $targetSkill = [string]$skillEvaluation.targetSkill
        if ($targetSkill -notmatch '^[a-z0-9][a-z0-9-]*$' -or
            -not (Test-Path -LiteralPath (Join-Path (Join-Path $skillsRoot $targetSkill) 'SKILL.md') -PathType Leaf)) {
            $errors.Add("$($campaignFile.FullName): unknown skill-evaluation target '$targetSkill'.")
        }
        $arms = @($skillEvaluation.arms | ForEach-Object { [string]$_ })
        if ($arms.Count -ne 2 -or $arms[0] -cne 'without-skill' -or $arms[1] -cne 'with-skill') {
            $errors.Add("$($campaignFile.FullName): skill-evaluation arms must be ordered as without-skill, with-skill.")
        }

        $routingCaseIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $routingExpectations = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($routingCase in @($skillEvaluation.routingCases)) {
            $routingCaseId = [string]$routingCase.caseId
            $routingExpectation = [string]$routingCase.routingExpectation
            if (-not $routingCaseIds.Add($routingCaseId)) {
                $errors.Add("$($campaignFile.FullName): duplicate skill routing case '$routingCaseId'.")
            }
            if (@($campaign.caseIds) -cnotcontains $routingCaseId) {
                $errors.Add("$($campaignFile.FullName): routing case '$routingCaseId' is not in caseIds.")
            }
            if ($routingExpectation -notin @('explicit', 'implicit', 'contextual', 'negative')) {
                $errors.Add("$($campaignFile.FullName): routing case '$routingCaseId' has invalid expectation '$routingExpectation'.")
            }
            else {
                [void]$routingExpectations.Add($routingExpectation)
            }
            $expectedSkillProperty = $routingCase.PSObject.Properties['expectedSkill']
            if ($routingExpectation -eq 'negative') {
                if ($null -eq $expectedSkillProperty -or $null -ne $expectedSkillProperty.Value) {
                    $errors.Add("$($campaignFile.FullName): negative routing case '$routingCaseId' must set expectedSkill to null.")
                }
            }
            elseif ($null -eq $expectedSkillProperty -or
                [string]$expectedSkillProperty.Value -cne $targetSkill) {
                $errors.Add("$($campaignFile.FullName): positive routing case '$routingCaseId' must expect '$targetSkill'.")
            }
        }
        if ($routingCaseIds.Count -ne @($campaign.caseIds).Count) {
            $errors.Add("$($campaignFile.FullName): skill routing cases must cover every campaign case exactly once.")
        }
        foreach ($requiredExpectation in @('explicit', 'implicit', 'contextual', 'negative')) {
            if (-not $routingExpectations.Contains($requiredExpectation)) {
                $errors.Add("$($campaignFile.FullName): skill evaluation is missing a '$requiredExpectation' routing case.")
            }
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
