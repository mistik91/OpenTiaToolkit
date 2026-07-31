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
        totalCostUsd    = 0.01
        models          = @(
            [ordered]@{
                model     = 'provider/model-a'
                variant   = $null
                costClass = 'paid'
                cases     = @(
                    [ordered]@{
                        caseId = 'ott-01'; score = 100; passed = $true; criticalFailure = $false
                        timedOut = $false; exitCode = 0; cost = 0.01
                    },
                    [ordered]@{
                        caseId = 'ott-03'; score = 80; passed = $true; criticalFailure = $false
                        timedOut = $false; exitCode = 0; cost = 0
                    }
                )
            }
        )
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $segment1Path -Encoding UTF8

    [ordered]@{
        campaignId      = 'synthetic'
        openCodeVersion = '1.18.10'
        totalCostUsd    = 0
        selectedCaseIds = @('ott-01', 'ott-03')
        models          = @(
            [ordered]@{
                model     = 'provider/model-b'
                reasoningEffort = 'max'
                variant   = 'max'
                costClass = 'free'
                cases     = @(
                    [ordered]@{
                        caseId = 'ott-01'; score = 20; passed = $false; criticalFailure = $true
                        timedOut = $false; exitCode = 0; cost = 0
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
        [string]$modelA[0].reasoningEffort -ne 'provider-default') {
        throw 'Expected model-a to merge as qualified with mean score 90 and inferred provider-default reasoning.'
    }
    if ($modelB.Count -ne 1 -or [string]$modelB[0].status -ne 'disqualified-critical-failure') {
        throw 'Expected model-b to merge as critically disqualified.'
    }
    if ([double]$merged.totalCostUsd -ne 0.01) {
        throw "Expected merged cost 0.01, got $($merged.totalCostUsd)."
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
