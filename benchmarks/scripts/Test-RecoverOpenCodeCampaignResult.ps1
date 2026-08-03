[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaBenchmarkRecoveryTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    $campaignPath = Join-Path $testRoot 'campaign.json'
    $artifactDirectory = Join-Path $testRoot 'artifacts'
    $outputPath = Join-Path $artifactDirectory 'campaign-result.recovered.json'
    New-Item -ItemType Directory -Path $artifactDirectory | Out-Null

    [ordered]@{
        schemaVersion  = 2
        id             = 'synthetic'
        title          = 'Synthetic recovery'
        caseIds        = @('ott-01')
        trialCount     = 2
        maxCostUsd     = 1
        allowPaidModels = $true
        models         = @(
            [ordered]@{
                model = 'provider/model-a'; reasoningEffort = 'provider-default'
                variant = $null; costClass = 'paid'
            }
        )
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $campaignPath -Encoding UTF8

    for ($trialIndex = 1; $trialIndex -le 2; $trialIndex++) {
        $trialDirectory = Join-Path $artifactDirectory "trial-$trialIndex"
        New-Item -ItemType Directory -Path $trialDirectory | Out-Null
        [ordered]@{
            schemaVersion   = 2
            caseId          = 'ott-01'
            model           = 'provider/model-a'
            trialIndex      = $trialIndex
            reasoningEffort = 'provider-default'
            variant         = $null
            openCodeVersion = '1.18.10'
            gitCommit       = '0123456789012345678901234567890123456789'
            gitBranch       = 'experiment/test'
            gitDirty        = $false
            gitStateSha256  = ('A' * 64)
            fixtureSha256   = ('B' * 64)
            startedAtUtc    = "2026-01-01T00:00:0$trialIndex.0000000Z"
            finishedAtUtc   = "2026-01-01T00:00:1$trialIndex.0000000Z"
            durationSeconds = 10
            exitCode        = 0
            timedOut        = $false
            validForModelComparison = $trialIndex -eq 1
            executionFailure = if ($trialIndex -eq 1) {
                $null
            }
            else {
                [ordered]@{
                    kind = 'provider-error'; statusCode = 503
                    message = 'Streaming response failed: [503] queue full.'
                }
            }
            validatorInterventions = [ordered]@{
                blockedCallCount = 0; rejectedBeforeTiaCount = 0
                correctedOnNextPatchCount = 0; issueCount = 0; codes = @(); calls = @()
            }
            score           = if ($trialIndex -eq 1) { 100 } else { 0 }
            passed          = $trialIndex -eq 1
            assertions      = @(
                [ordered]@{ id = 'run'; critical = $true; passed = ($trialIndex -eq 1) }
            )
            usage            = [ordered]@{
                tokens = [ordered]@{ total = 100; input = 80; output = 10; reasoning = 10 }
                cost   = 0.01
            }
        } | ConvertTo-Json -Depth 20 |
            Set-Content -LiteralPath (Join-Path $trialDirectory 'result.json') -Encoding UTF8
    }

    $recovered = & (Join-Path $PSScriptRoot 'Recover-OpenCodeCampaignResult.ps1') `
        -CampaignPath $campaignPath `
        -ArtifactDirectory $artifactDirectory `
        -OutputPath $outputPath
    if ([string]$recovered.status -ne 'completed-recovered' -or
        [int]$recovered.sourceResultCount -ne 2 -or
        [double]$recovered.totalCostUsd -ne 0.02 -or
        [string]$recovered.models[0].status -ne 'endpoint-invalid' -or
        [int]$recovered.models[0].aggregate.validTrialCount -ne 1 -or
        [int]$recovered.models[0].aggregate.endpointInvalidCount -ne 1 -or
        [double]$recovered.models[0].aggregate.meanScore -ne 100 -or
        [double]$recovered.models[0].aggregate.rawMeanScore -ne 50) {
        throw 'Recovered campaign summary contained unexpected values.'
    }

    Write-Host 'Campaign result recovery test passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaBenchmarkRecoveryTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
