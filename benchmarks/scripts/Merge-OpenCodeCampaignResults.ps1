[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$CampaignResultPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OpenCodeBenchmark.Common.ps1')

function Get-ModelReasoningEffort {
    param([Parameter(Mandatory = $true)][object]$Model)

    $reasoningProperty = $Model.PSObject.Properties['reasoningEffort']
    if ($null -ne $reasoningProperty -and
        -not [string]::IsNullOrWhiteSpace([string]$reasoningProperty.Value)) {
        return [string]$reasoningProperty.Value
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$Model.variant)) {
        return [string]$Model.variant
    }
    return 'provider-default'
}

$resolvedPaths = @($CampaignResultPath | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
if ($resolvedPaths.Count -eq 0) {
    throw 'At least one campaign result is required.'
}

$segments = @($resolvedPaths | ForEach-Object {
        Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json
    })
$campaignIds = @($segments | ForEach-Object { [string]$_.campaignId } | Sort-Object -Unique)
if ($campaignIds.Count -ne 1) {
    throw "Campaign results do not share one campaignId: $($campaignIds -join ', ')"
}
$openCodeVersions = @($segments | ForEach-Object { [string]$_.openCodeVersion } | Sort-Object -Unique)
if ($openCodeVersions.Count -ne 1) {
    throw "Campaign results do not share one OpenCode version: $($openCodeVersions -join ', ')"
}
$trialCounts = @($segments | ForEach-Object {
        $property = $_.PSObject.Properties['trialCount']
        if ($null -eq $property) {
            1
        }
        else {
            [int]$property.Value
        }
    } | Sort-Object -Unique)
if ($trialCounts.Count -ne 1) {
    throw "Campaign results do not share one trialCount: $($trialCounts -join ', ')"
}
$trialCount = [int]$trialCounts[0]

$expectedCaseIdList = [System.Collections.Generic.List[string]]::new()
foreach ($segment in $segments) {
    $selectedCasesProperty = $segment.PSObject.Properties['selectedCaseIds']
    if ($null -ne $selectedCasesProperty) {
        foreach ($caseId in @($selectedCasesProperty.Value)) {
            $expectedCaseIdList.Add([string]$caseId)
        }
        continue
    }

    $campaignPathProperty = $segment.PSObject.Properties['campaignPath']
    if ($null -ne $campaignPathProperty -and
        (Test-Path -LiteralPath ([string]$campaignPathProperty.Value) -PathType Leaf)) {
        $campaignDefinition = Get-Content -LiteralPath ([string]$campaignPathProperty.Value) -Raw | ConvertFrom-Json
        foreach ($caseId in @($campaignDefinition.caseIds)) {
            $expectedCaseIdList.Add([string]$caseId)
        }
        continue
    }

    foreach ($caseId in @($segment.models.cases | ForEach-Object { [string]$_.caseId })) {
        $expectedCaseIdList.Add($caseId)
    }
}
$expectedCaseIds = @($expectedCaseIdList | Sort-Object -Unique)
$modelsById = [ordered]@{}

for ($segmentIndex = 0; $segmentIndex -lt $segments.Count; $segmentIndex++) {
    $segment = $segments[$segmentIndex]
    foreach ($model in $segment.models) {
        $modelId = [string]$model.model
        $reasoningEffort = Get-ModelReasoningEffort -Model $model
        if (-not $modelsById.Contains($modelId)) {
            $modelsById[$modelId] = [pscustomobject][ordered]@{
                model         = $modelId
                reasoningEffort = $reasoningEffort
                variant       = $model.variant
                costClass     = [string]$model.costClass
                sourceResults = [System.Collections.Generic.List[string]]::new()
                cases         = [System.Collections.Generic.List[object]]::new()
            }
        }

        $mergedModel = $modelsById[$modelId]
        if ([string]$mergedModel.reasoningEffort -cne $reasoningEffort -or
            [string]$mergedModel.variant -cne [string]$model.variant) {
            throw "Campaign results disagree on reasoning configuration for model '$modelId'."
        }
        $mergedModel.sourceResults.Add($resolvedPaths[$segmentIndex])
        foreach ($case in $model.cases) {
            $caseId = [string]$case.caseId
            $trialProperty = $case.PSObject.Properties['trialIndex']
            $trialIndex = if ($null -eq $trialProperty) { 1 } else { [int]$trialProperty.Value }
            if (@($mergedModel.cases | Where-Object {
                        [string]$_.caseId -eq $caseId -and [int]$_.trialIndex -eq $trialIndex
                    }).Count -gt 0) {
                throw "Duplicate result for model '$modelId', case '$caseId', and trial $trialIndex."
            }
            $case | Add-Member -NotePropertyName trialIndex -NotePropertyValue $trialIndex -Force
            if ($null -eq $case.PSObject.Properties['validForModelComparison']) {
                $case | Add-Member -NotePropertyName validForModelComparison -NotePropertyValue $true
            }
            if ($null -eq $case.PSObject.Properties['executionFailureKind']) {
                $case | Add-Member -NotePropertyName executionFailureKind -NotePropertyValue $null
            }
            if ($null -eq $case.PSObject.Properties['validatorBlockedCallCount']) {
                $case | Add-Member -NotePropertyName validatorBlockedCallCount -NotePropertyValue 0
            }
            if ($null -eq $case.PSObject.Properties['validatorIssueCount']) {
                $case | Add-Member -NotePropertyName validatorIssueCount -NotePropertyValue 0
            }
            if ($null -eq $case.PSObject.Properties['reasoningTokens']) {
                $reasoningTokens = 0L
                $resultPathProperty = $case.PSObject.Properties['resultPath']
                if ($null -ne $resultPathProperty -and
                    (Test-Path -LiteralPath ([string]$resultPathProperty.Value) -PathType Leaf)) {
                    $caseResult = Get-Content -LiteralPath ([string]$resultPathProperty.Value) -Raw |
                        ConvertFrom-Json
                    $reasoningTokens = [long]$caseResult.usage.tokens.reasoning
                }
                $case | Add-Member -NotePropertyName reasoningTokens -NotePropertyValue $reasoningTokens
            }
            $mergedModel.cases.Add($case)
        }
    }
}

$mergedModels = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $modelsById.GetEnumerator()) {
    $model = $entry.Value
    $cases = @($model.cases)
    $status = Get-BenchmarkModelStatus `
        -Trials $cases `
        -ExpectedTrialCount ($expectedCaseIds.Count * $trialCount)

    $statistics = Get-BenchmarkTrialStatistics -Trials $cases
    $caseAggregates = @(Get-BenchmarkCaseAggregates -Trials $cases)
    $mergedModels.Add([pscustomobject][ordered]@{
            model         = $model.model
            reasoningEffort = $model.reasoningEffort
            variant       = $model.variant
            costClass     = $model.costClass
            status        = $status
            caseCount     = @($cases.caseId | Sort-Object -Unique).Count
            completedTrialCount = $cases.Count
            meanScore     = $statistics.meanScore
            totalCostUsd  = $statistics.totalCostUsd
            aggregate     = $statistics
            caseAggregates = $caseAggregates
            sourceResults = $model.sourceResults
            cases         = $model.cases
        })
}

$totalCost = [double](($mergedModels | Measure-Object -Property totalCostUsd -Sum).Sum)
$result = [ordered]@{
    schemaVersion         = 2
    campaignId            = $campaignIds[0]
    openCodeVersion       = $openCodeVersions[0]
    mergedAtUtc           = [DateTime]::UtcNow.ToString('o')
    expectedCaseIds       = $expectedCaseIds
    trialCount            = $trialCount
    expectedTrialCountPerModel = $expectedCaseIds.Count * $trialCount
    sourceCampaignResults = $resolvedPaths
    totalCostUsd          = [Math]::Round($totalCost, 8)
    models                = $mergedModels
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$result | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $resolvedOutputPath -Encoding UTF8
Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
