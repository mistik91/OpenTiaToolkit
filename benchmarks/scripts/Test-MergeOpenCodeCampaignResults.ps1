[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaBenchmarkMergeTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $segment1Path = Join-Path $testRoot 'segment-1.json'
    $segment2Path = Join-Path $testRoot 'segment-2.json'
    $outputPath = Join-Path $testRoot 'merged.json'

    [ordered]@{
        campaignId      = 'synthetic'
        openCodeVersion = '1.18.10'
        trialCount      = 2
        totalCostUsd    = 0.01
        selectedCaseIds = @('ott-01')
        models          = @(
            [ordered]@{
                model     = 'provider/model-a'
                variant   = $null
                costClass = 'paid'
                cases     = @(
                    [ordered]@{
                        caseId = 'ott-01'; trialIndex = 1; score = 100; passed = $true
                        criticalFailure = $false; timedOut = $false; exitCode = 0; cost = 0.01
                        durationSeconds = 10; reasoningTokens = 100
                    },
                    [ordered]@{
                        caseId = 'ott-01'; trialIndex = 2; score = 80; passed = $true
                        criticalFailure = $false; timedOut = $false; exitCode = 0; cost = 0
                        durationSeconds = 20; reasoningTokens = 200
                    }
                )
            }
        )
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $segment1Path -Encoding UTF8

    [ordered]@{
        campaignId      = 'synthetic'
        openCodeVersion = '1.18.10'
        trialCount      = 2
        totalCostUsd    = 0
        selectedCaseIds = @('ott-01')
        models          = @(
            [ordered]@{
                model     = 'provider/model-b'
                reasoningEffort = 'max'
                variant   = 'max'
                costClass = 'free'
                cases     = @(
                    [ordered]@{
                        caseId = 'ott-01'; trialIndex = 1; score = 20; passed = $false
                        criticalFailure = $true; timedOut = $false; exitCode = 0; cost = 0
                        durationSeconds = 30; reasoningTokens = 300
                        validForModelComparison = $false
                        executionFailureKind = 'provider-error'
                        validatorBlockedCallCount = 0
                        validatorIssueCount = 0
                    }
                )
            }
        )
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $segment2Path -Encoding UTF8

    $merged = & (Join-Path $PSScriptRoot 'Merge-OpenCodeCampaignResults.ps1') `
        -CampaignResultPath @($segment1Path, $segment2Path) `
        -OutputPath $outputPath
    $modelA = @($merged.models | Where-Object { $_.model -eq 'provider/model-a' })
    $modelB = @($merged.models | Where-Object { $_.model -eq 'provider/model-b' })
    if ($modelA.Count -ne 1 -or [string]$modelA[0].status -ne 'qualified' -or
        [double]$modelA[0].meanScore -ne 90 -or
        [string]$modelA[0].reasoningEffort -ne 'provider-default' -or
        [int]$modelA[0].aggregate.trialCount -ne 2 -or
        [double]$modelA[0].aggregate.passRate -ne 1 -or
        [int]$modelA[0].caseAggregates[0].trialCount -ne 2) {
        throw 'Expected model-a to merge as qualified with mean score 90 and inferred provider-default reasoning.'
    }
    if ($modelB.Count -ne 1 -or [string]$modelB[0].status -ne 'endpoint-invalid' -or
        [int]$modelB[0].aggregate.validTrialCount -ne 0 -or
        $null -ne $modelB[0].aggregate.meanScore -or
        [double]$modelB[0].aggregate.rawMeanScore -ne 20) {
        throw 'Expected model-b to merge as endpoint-invalid with its raw score excluded from the mean.'
    }
    if ([double]$merged.totalCostUsd -ne 0.01) {
        throw "Expected merged cost 0.01, got $($merged.totalCostUsd)."
    }
    if ([int]$merged.trialCount -ne 2 -or [int]$merged.expectedTrialCountPerModel -ne 2) {
        throw 'Expected the merged report to preserve two trials per model.'
    }

    $pairedSegment1Path = Join-Path $testRoot 'paired-segment-1.json'
    $pairedSegment2Path = Join-Path $testRoot 'paired-segment-2.json'
    $pairedOutputPath = Join-Path $testRoot 'paired-merged.json'
    $skillEvaluation = [ordered]@{
        targetSkill = 'hardware-configuration-workflow'
        arms = @('without-skill', 'with-skill')
        routingCases = @(
            [ordered]@{
                caseId = 'ott-14'; routingExpectation = 'explicit'
                expectedSkill = 'hardware-configuration-workflow'
            },
            [ordered]@{
                caseId = 'ott-01'; routingExpectation = 'negative'; expectedSkill = $null
            }
        )
    }
    $commonTrial = [ordered]@{
        trialIndex = 1; passed = $true; criticalFailure = $false
        timedOut = $false; exitCode = 0; cost = 0; durationSeconds = 10
        reasoningTokens = 100; validForModelComparison = $true
        executionFailureKind = $null; validatorBlockedCallCount = 0
        validatorIssueCount = 0; targetSkillAttempted = $false
    }

    foreach ($segmentDefinition in @(
            [pscustomobject]@{
                Path = $pairedSegment1Path; CaseId = 'ott-14'; Routing = 'explicit'
                WithoutScore = 50; WithScore = 100; WithLoaded = $true
            },
            [pscustomobject]@{
                Path = $pairedSegment2Path; CaseId = 'ott-01'; Routing = 'negative'
                WithoutScore = 100; WithScore = 100; WithLoaded = $false
            })) {
        $withoutTrial = [ordered]@{} + $commonTrial
        $withoutTrial.caseId = $segmentDefinition.CaseId
        $withoutTrial.score = $segmentDefinition.WithoutScore
        $withoutTrial.skillEvaluationArm = 'without-skill'
        $withoutTrial.routingExpectation = $segmentDefinition.Routing
        $withoutTrial.expectedSkill = if ($segmentDefinition.Routing -eq 'negative') {
            $null
        }
        else { 'hardware-configuration-workflow' }
        $withoutTrial.targetSkillLoaded = $false

        $withTrial = [ordered]@{} + $commonTrial
        $withTrial.caseId = $segmentDefinition.CaseId
        $withTrial.score = $segmentDefinition.WithScore
        $withTrial.skillEvaluationArm = 'with-skill'
        $withTrial.routingExpectation = $segmentDefinition.Routing
        $withTrial.expectedSkill = $withoutTrial.expectedSkill
        $withTrial.targetSkillLoaded = $segmentDefinition.WithLoaded
        $withTrial.targetSkillAttempted = $segmentDefinition.WithLoaded

        [ordered]@{
            campaignId = 'synthetic-paired'; openCodeVersion = '1.18.10'
            trialCount = 1; skillEvaluation = $skillEvaluation
            selectedCaseIds = @($segmentDefinition.CaseId)
            models = @(
                [ordered]@{
                    model = 'provider/model-a'; reasoningEffort = 'high'; variant = 'high'
                    costClass = 'subscription'; cases = @($withoutTrial, $withTrial)
                }
            )
        } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $segmentDefinition.Path -Encoding UTF8
    }

    $pairedMerged = & (Join-Path $PSScriptRoot 'Merge-OpenCodeCampaignResults.ps1') `
        -CampaignResultPath @($pairedSegment1Path, $pairedSegment2Path) `
        -OutputPath $pairedOutputPath
    if ([int]$pairedMerged.expectedTrialCountPerModel -ne 4 -or
        [int]$pairedMerged.models[0].completedTrialCount -ne 4 -or
        [int]$pairedMerged.models[0].skillLift.pairedOutcomes.validPairCount -ne 2 -or
        [double]$pairedMerged.models[0].skillLift.lift.meanScore -ne 25 -or
        [double]$pairedMerged.models[0].skillLift.routing.accuracy -ne 1) {
        throw 'Expected paired-arm segments to merge without collisions and preserve lift/routing statistics.'
    }

    Write-Host 'Campaign result merge test passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaBenchmarkMergeTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
