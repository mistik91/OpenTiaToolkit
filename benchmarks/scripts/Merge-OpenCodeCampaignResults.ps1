[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$CampaignResultPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
            if (@($mergedModel.cases | Where-Object { [string]$_.caseId -eq $caseId }).Count -gt 0) {
                throw "Duplicate result for model '$modelId' and case '$caseId'."
            }
            $mergedModel.cases.Add($case)
        }
    }
}

$mergedModels = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $modelsById.GetEnumerator()) {
    $model = $entry.Value
    $cases = @($model.cases)
    $hasRuntimeFailure = @($cases | Where-Object {
            [bool]$_.timedOut -or [int]$_.exitCode -ne 0
        }).Count -gt 0
    $hasCriticalFailure = @($cases | Where-Object { [bool]$_.criticalFailure }).Count -gt 0
    $hasAllCases = @($expectedCaseIds | Where-Object {
            $caseId = $_
            @($cases | Where-Object { [string]$_.caseId -eq $caseId }).Count -eq 1
        }).Count -eq $expectedCaseIds.Count
    $allPassed = $hasAllCases -and @($cases | Where-Object { -not [bool]$_.passed }).Count -eq 0

    $status = if ($hasRuntimeFailure) {
        'runtime-failure'
    }
    elseif ($hasCriticalFailure) {
        'disqualified-critical-failure'
    }
    elseif ($allPassed) {
        'qualified'
    }
    elseif ($hasAllCases) {
        'not-qualified'
    }
    else {
        'incomplete'
    }

    $meanScore = if ($cases.Count -gt 0) {
        [Math]::Round([double](($cases | Measure-Object -Property score -Average).Average), 2)
    }
    else {
        0.0
    }
    $modelCost = if ($cases.Count -gt 0) {
        [double](($cases | Measure-Object -Property cost -Sum).Sum)
    }
    else {
        0.0
    }
    $mergedModels.Add([pscustomobject][ordered]@{
            model         = $model.model
            reasoningEffort = $model.reasoningEffort
            variant       = $model.variant
            costClass     = $model.costClass
            status        = $status
            caseCount     = $cases.Count
            meanScore     = $meanScore
            totalCostUsd  = [Math]::Round($modelCost, 8)
            sourceResults = $model.sourceResults
            cases         = $model.cases
        })
}

$totalCost = [double](($segments | Measure-Object -Property totalCostUsd -Sum).Sum)
$result = [ordered]@{
    schemaVersion         = 1
    campaignId            = $campaignIds[0]
    openCodeVersion       = $openCodeVersions[0]
    mergedAtUtc           = [DateTime]::UtcNow.ToString('o')
    expectedCaseIds       = $expectedCaseIds
    sourceCampaignResults = $resolvedPaths
    totalCostUsd          = [Math]::Round($totalCost, 8)
    models                = $mergedModels
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$result | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $resolvedOutputPath -Encoding UTF8
Get-Content -LiteralPath $resolvedOutputPath -Raw | ConvertFrom-Json
