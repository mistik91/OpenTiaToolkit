[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaBenchmarkBatchTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $campaignPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'campaigns\hardware-gsdml-ifm-glm-5-3-high-20260902.json'
    $plan = & (Join-Path $PSScriptRoot 'Invoke-OpenCodeCampaignBatch.ps1') `
        -CampaignPath $campaignPath `
        -OutputRoot $testRoot `
        -BatchSize 2 `
        -PlanOnly
    $checkpoint = @(Get-ChildItem -LiteralPath $testRoot -Filter 'checkpoint.json' -File -Recurse)
    if ($checkpoint.Count -ne 1) {
        throw "Expected one batch checkpoint, got $($checkpoint.Count)."
    }
    if ([string]$plan.status -cne 'planned' -or
        @($plan.workItems).Count -ne 3 -or
        [int]$plan.completedTrialCount -ne 0 -or
        [int]$plan.remainingTrialCount -ne 3 -or
        [int]$plan.nextWorkItem.trialIndex -ne 1 -or
        (@($plan.workItems | ForEach-Object { [int]$_.trialIndex }) -join ',') -cne '1,2,3') {
        throw 'Batch plan did not preserve the ordered three-trial checkpoint.'
    }

    $resumed = & (Join-Path $PSScriptRoot 'Invoke-OpenCodeCampaignBatch.ps1') `
        -CampaignPath $campaignPath `
        -CheckpointPath $checkpoint[0].FullName `
        -PlanOnly
    if (@($resumed.workItems).Count -ne 3 -or
        [int]$resumed.nextWorkItem.trialIndex -ne 1) {
        throw 'Plan-only checkpoint resume changed the pending work items.'
    }

    Write-Host 'OpenCode campaign batch checkpoint tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaBenchmarkBatchTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
