[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CampaignPath,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OpenCodeBenchmark.Common.ps1')

function Get-RequiredUniformValue {
    param(
        [Parameter(Mandatory = $true)][object[]]$Results,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $values = @($Results | ForEach-Object {
            $property = $_.PSObject.Properties[$PropertyName]
            if ($null -eq $property) {
                throw "Result '$($_.caseId)' has no '$PropertyName' property."
            }
            [string]$property.Value
        } | Sort-Object -Unique)
    if ($values.Count -ne 1) {
        throw "Case results do not share one '$PropertyName' value: $($values -join ', ')"
    }
    return $values[0]
}

function Get-ResultSkillEvaluationArm {
    param([Parameter(Mandatory = $true)][object]$Result)

    $property = $Result.PSObject.Properties['skillEvaluation']
    if ($null -eq $property -or $null -eq $property.Value -or
        [string]::IsNullOrWhiteSpace([string]$property.Value.arm)) {
        return 'standard'
    }
    return [string]$property.Value.arm
}

$resolvedCampaignPath = (Resolve-Path -LiteralPath $CampaignPath).Path
$resolvedArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$campaign = Get-Content -LiteralPath $resolvedCampaignPath -Raw | ConvertFrom-Json
$resultFiles = @(Get-ChildItem -LiteralPath $resolvedArtifactDirectory -Recurse -Filter result.json -File |
        Sort-Object FullName)
if ($resultFiles.Count -eq 0) {
    throw "No result.json files were found below '$resolvedArtifactDirectory'."
}

$resultRecords = @($resultFiles | ForEach-Object {
        [pscustomobject]@{
            path   = $_.FullName
            result = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        }
    })
$results = @($resultRecords.result)
$knownModels = @($campaign.models | ForEach-Object { [string]$_.model })
$knownCaseIds = @($campaign.caseIds | ForEach-Object { [string]$_ })
$skillEvaluationProperty = $campaign.PSObject.Properties['skillEvaluation']
$skillEvaluationDefinition = if ($null -eq $skillEvaluationProperty) {
    $null
}
else {
    $skillEvaluationProperty.Value
}
$skillEvaluationArms = if ($null -eq $skillEvaluationDefinition) {
    @('standard')
}
else {
    @($skillEvaluationDefinition.arms | ForEach-Object { [string]$_ })
}
$routingCasesById = @{}
if ($null -ne $skillEvaluationDefinition) {
    foreach ($routingCase in @($skillEvaluationDefinition.routingCases)) {
        $routingCasesById[[string]$routingCase.caseId] = $routingCase
    }
}
$seenKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($record in $resultRecords) {
    $result = $record.result
    if ([string]$result.model -cnotin $knownModels) {
        throw "Result '$($record.path)' references model '$($result.model)' outside the campaign."
    }
    if ([string]$result.caseId -cnotin $knownCaseIds) {
        throw "Result '$($record.path)' references case '$($result.caseId)' outside the campaign."
    }
    if ([int]$result.trialIndex -lt 1 -or [int]$result.trialIndex -gt [int]$campaign.trialCount) {
        throw "Result '$($record.path)' has trialIndex '$($result.trialIndex)' outside 1..$($campaign.trialCount)."
    }

    $skillEvaluationArm = Get-ResultSkillEvaluationArm -Result $result
    if ($skillEvaluationArm -cnotin $skillEvaluationArms) {
        throw "Result '$($record.path)' has skill-evaluation arm '$skillEvaluationArm' outside the campaign."
    }
    if ($null -ne $skillEvaluationDefinition -and
        [string]$result.skillEvaluation.targetSkill -cne [string]$skillEvaluationDefinition.targetSkill) {
        throw "Result '$($record.path)' does not match the campaign skill-evaluation target."
    }

    $key = "$($result.model)|$($result.caseId)|$($result.trialIndex)|$skillEvaluationArm"
    if (-not $seenKeys.Add($key)) {
        throw "Duplicate result for model '$($result.model)', case '$($result.caseId)', trial $($result.trialIndex), and arm '$skillEvaluationArm'."
    }

    $modelEntry = @($campaign.models | Where-Object { [string]$_.model -ceq [string]$result.model })[0]
    if ([string]$result.reasoningEffort -cne [string]$modelEntry.reasoningEffort -or
        [string]$result.variant -cne [string]$modelEntry.variant) {
        throw "Result '$($record.path)' does not match the campaign reasoning configuration."
    }
}

$openCodeVersion = Get-RequiredUniformValue -Results $results -PropertyName 'openCodeVersion'
$gitCommit = Get-RequiredUniformValue -Results $results -PropertyName 'gitCommit'
$gitBranch = Get-RequiredUniformValue -Results $results -PropertyName 'gitBranch'
$gitDirty = Get-RequiredUniformValue -Results $results -PropertyName 'gitDirty'
$gitStateSha256 = Get-RequiredUniformValue -Results $results -PropertyName 'gitStateSha256'
$fixtureIdentities = @($results |
        Group-Object -Property fixtureId |
        Sort-Object Name |
        ForEach-Object {
            if ([string]::IsNullOrWhiteSpace([string]$_.Name)) {
                throw 'A case result has no fixtureId.'
            }
            $fixtureHashes = @($_.Group | ForEach-Object {
                    $property = $_.PSObject.Properties['fixtureSha256']
                    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                        throw "Result '$($_.caseId)' has no fixtureSha256."
                    }
                    [string]$property.Value
                } | Sort-Object -Unique)
            if ($fixtureHashes.Count -ne 1) {
                throw "Results for fixture '$($_.Name)' do not share one fixtureSha256: $($fixtureHashes -join ', ')"
            }
            [pscustomobject][ordered]@{
                fixtureId     = [string]$_.Name
                fixtureSha256 = $fixtureHashes[0]
            }
        })
$fixtureSha256 = if ($fixtureIdentities.Count -eq 1) {
    [string]$fixtureIdentities[0].fixtureSha256
}
else {
    $null
}

$modelResults = [System.Collections.Generic.List[object]]::new()
foreach ($modelEntry in $campaign.models) {
    $modelId = [string]$modelEntry.model
    $trials = [System.Collections.Generic.List[object]]::new()

    foreach ($caseId in $knownCaseIds) {
        for ($trialIndex = 1; $trialIndex -le [int]$campaign.trialCount; $trialIndex++) {
            foreach ($skillEvaluationArm in $skillEvaluationArms) {
                $match = @($resultRecords | Where-Object {
                        [string]$_.result.model -ceq $modelId -and
                        [string]$_.result.caseId -ceq $caseId -and
                        [int]$_.result.trialIndex -eq $trialIndex -and
                        (Get-ResultSkillEvaluationArm -Result $_.result) -ceq $skillEvaluationArm
                    })
                if ($match.Count -eq 0) {
                    continue
                }
                if ($match.Count -ne 1) {
                    throw "Expected one result for '$modelId' / '$caseId' / trial $trialIndex / arm '$skillEvaluationArm', got $($match.Count)."
                }

                $result = $match[0].result
                $criticalFailures = @($result.assertions |
                        Where-Object { [bool]$_.critical -and -not [bool]$_.passed })
                $validForModelComparison = Test-BenchmarkTrialValidForModelComparison -Trial $result
                $executionFailureKind = Get-BenchmarkTrialExecutionFailureKind -Trial $result
                $validatorProperty = $result.PSObject.Properties['validatorInterventions']
                $validatorBlockedCallCount = if ($null -ne $validatorProperty) {
                    [int]$validatorProperty.Value.blockedCallCount
                }
                else { 0 }
                $validatorIssueCount = if ($null -ne $validatorProperty) {
                    [int]$validatorProperty.Value.issueCount
                }
                else { 0 }
                $targetSkillCalls = if ($skillEvaluationArm -eq 'standard') {
                    @()
                }
                else {
                    @($result.skillUsage.calls | Where-Object {
                            $null -ne $_ -and
                            $null -ne $_.PSObject.Properties['name'] -and
                            [string]$_.PSObject.Properties['name'].Value -ceq
                                [string]$skillEvaluationDefinition.targetSkill
                        })
                }
                $routingCase = if ($routingCasesById.ContainsKey($caseId)) {
                    $routingCasesById[$caseId]
                }
                else {
                    $null
                }
                $trials.Add([pscustomobject][ordered]@{
                    caseId          = [string]$result.caseId
                    trialIndex      = [int]$result.trialIndex
                    score           = [double]$result.score
                    passed          = [bool]$result.passed
                    criticalFailure = $criticalFailures.Count -gt 0
                    timedOut        = [bool]$result.timedOut
                    exitCode        = [int]$result.exitCode
                    validForModelComparison = $validForModelComparison
                    executionFailureKind = $executionFailureKind
                    durationSeconds = [double]$result.durationSeconds
                    cost            = [double]$result.usage.cost
                    reasoningTokens = [long]$result.usage.tokens.reasoning
                    validatorBlockedCallCount = $validatorBlockedCallCount
                    validatorIssueCount = $validatorIssueCount
                    skillEvaluationArm = if ($skillEvaluationArm -eq 'standard') {
                        $null
                    }
                    else { $skillEvaluationArm }
                    skillEvaluationTarget = if ($skillEvaluationArm -eq 'standard') {
                        $null
                    }
                    else { [string]$skillEvaluationDefinition.targetSkill }
                    routingExpectation = if ($null -eq $routingCase) {
                        $null
                    }
                    else { [string]$routingCase.routingExpectation }
                    expectedSkill = if ($null -eq $routingCase) {
                        $null
                    }
                    else { $routingCase.expectedSkill }
                    targetSkillAttempted = @($targetSkillCalls).Count -gt 0
                    targetSkillLoaded = @($targetSkillCalls | Where-Object {
                            [string]$_.status -ceq 'completed'
                        }).Count -gt 0
                    resultPath      = [string]$match[0].path
                })
            }
        }
    }

    $trialItems = @($trials)
    $expectedTrialCount = @($knownCaseIds).Count * [int]$campaign.trialCount * @($skillEvaluationArms).Count
    $status = Get-BenchmarkModelStatus `
        -Trials $trialItems `
        -ExpectedTrialCount $expectedTrialCount

    $modelRecord = [ordered]@{
            model           = $modelId
            reasoningEffort = [string]$modelEntry.reasoningEffort
            variant         = $modelEntry.variant
            costClass       = [string]$modelEntry.costClass
            status          = $status
            cases           = $trials
            aggregate       = Get-BenchmarkTrialStatistics -Trials $trialItems
            caseAggregates  = @(Get-BenchmarkCaseAggregates -Trials $trialItems)
        }
    if ($null -ne $skillEvaluationDefinition) {
        $modelRecord.skillLift = Get-BenchmarkSkillLiftStatistics `
            -Trials $trialItems `
            -TargetSkill ([string]$skillEvaluationDefinition.targetSkill)
    }
    $modelResults.Add([pscustomobject]$modelRecord)
}

$startedAt = @($results | ForEach-Object { [DateTime]::Parse([string]$_.startedAtUtc) } |
        Sort-Object | Select-Object -First 1)[0]
$finishedAt = @($results | ForEach-Object { [DateTime]::Parse([string]$_.finishedAtUtc) } |
        Sort-Object -Descending | Select-Object -First 1)[0]
$expectedResultCount = @($knownModels).Count * @($knownCaseIds).Count * [int]$campaign.trialCount * @($skillEvaluationArms).Count
$campaignStatus = if ($results.Count -eq $expectedResultCount) { 'completed-recovered' } else { 'incomplete-recovered' }
$totalCost = [double](($results | ForEach-Object { [double]$_.usage.cost } | Measure-Object -Sum).Sum)
$catalogSnapshotPath = Join-Path $resolvedArtifactDirectory 'catalog-snapshot.json'

$recovered = [ordered]@{
    schemaVersion       = 2
    campaignId          = [string]$campaign.id
    title               = [string]$campaign.title
    status              = $campaignStatus
    failure             = $null
    recoverySource      = 'case-results'
    recoveredAtUtc      = [DateTime]::UtcNow.ToString('o')
    sourceResultCount   = $results.Count
    expectedResultCount = $expectedResultCount
    openCodeVersion     = $openCodeVersion
    gitCommit           = $gitCommit
    gitBranch           = $gitBranch
    gitDirty            = [bool]::Parse($gitDirty)
    gitStateSha256      = $gitStateSha256
    fixtureSha256       = $fixtureSha256
    fixtures            = $fixtureIdentities
    campaignPath        = $resolvedCampaignPath
    catalogSnapshotPath = if (Test-Path -LiteralPath $catalogSnapshotPath -PathType Leaf) {
        $catalogSnapshotPath
    }
    else {
        $null
    }
    artifactDirectory   = $resolvedArtifactDirectory
    startedAtUtc        = $startedAt.ToUniversalTime().ToString('o')
    finishedAtUtc       = $finishedAt.ToUniversalTime().ToString('o')
    durationSeconds     = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
    maxCostUsd          = [double]$campaign.maxCostUsd
    totalCostUsd        = [Math]::Round($totalCost, 8)
    trialCount          = [int]$campaign.trialCount
    skillEvaluation     = $skillEvaluationDefinition
    selectedCaseIds     = $knownCaseIds
    selectedModels      = $knownModels
    models              = $modelResults
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$recovered | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $resolvedOutputPath -Encoding UTF8
Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
