[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OpenCodeBenchmark.Common.ps1')

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaBenchmarkCommonTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    if ((Get-OpenCodeModelProvider -Model 'openrouter/example/model') -cne 'openrouter') {
        throw 'Provider parsing did not preserve the OpenCode provider id.'
    }

    $listing = @(
        'openrouter/example/model',
        '{',
        '  "id": "example/model",',
        '  "providerID": "openrouter",',
        '  "status": "active",',
        '  "cost": {',
        '    "input": 0.1,',
        '    "output": 0.2',
        '  },',
        '  "capabilities": {',
        '    "toolcall": true',
        '  }',
        '}'
    )
    $model = ConvertFrom-OpenCodeModelListing -Lines $listing -Model 'openrouter/example/model'
    if ([string]$model.providerID -cne 'openrouter' -or -not [bool]$model.capabilities.toolcall) {
        throw 'Verbose model metadata parsing returned the wrong model.'
    }

    $statistics = Get-BenchmarkTrialStatistics -Trials @(
        [pscustomobject]@{
            caseId = 'ott-01'; score = 100; passed = $true; criticalFailure = $false
            timedOut = $false; exitCode = 0; cost = 0.01; durationSeconds = 10
            reasoningTokens = 100
        },
        [pscustomobject]@{
            caseId = 'ott-01'; score = 80; passed = $true; criticalFailure = $false
            timedOut = $false; exitCode = 0; cost = 0.02; durationSeconds = 20
            reasoningTokens = 200
        },
        [pscustomobject]@{
            caseId = 'ott-01'; score = 20; passed = $false; criticalFailure = $true
            timedOut = $false; exitCode = 0; cost = 0.03; durationSeconds = 90
            reasoningTokens = 300
        }
    )
    if ([int]$statistics.trialCount -ne 3 -or
        [double]$statistics.passRate -ne 0.6667 -or
        [double]$statistics.criticalFailureRate -ne 0.3333 -or
        [double]$statistics.meanScore -ne 66.67 -or
        [double]$statistics.p50DurationSeconds -ne 20 -or
        [double]$statistics.totalCostUsd -ne 0.06 -or
        [double]$statistics.costPerPassedTrialUsd -ne 0.03 -or
        [double]$statistics.meanReasoningTokens -ne 200) {
        throw 'Trial aggregation returned unexpected statistics.'
    }

    $sourceAuthPath = Join-Path $testRoot 'source-auth.json'
    $scopedAuthPath = Join-Path $testRoot 'isolated-data\opencode\auth.json'
    [ordered]@{
        openrouter = [ordered]@{ type = 'api'; key = 'test-openrouter-key' }
        opencode   = [ordered]@{ type = 'api'; key = 'test-opencode-key' }
        openai     = [ordered]@{
            type = 'oauth'; refresh = 'test-refresh-token'; access = 'test-access-token'
            expires = 4102444800000; accountId = 'test-account-id'
        }
    } |
        ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $sourceAuthPath -Encoding UTF8

    [void](New-ProviderScopedOpenCodeAuthFile `
            -SourceAuthPath $sourceAuthPath `
            -Provider 'openrouter' `
            -DestinationPath $scopedAuthPath)
    $scopedAuth = Get-Content -LiteralPath $scopedAuthPath -Raw | ConvertFrom-Json
    if (@($scopedAuth.PSObject.Properties).Count -ne 1 -or
        $null -eq $scopedAuth.PSObject.Properties['openrouter'] -or
        [string]$scopedAuth.openrouter.key -cne 'test-openrouter-key') {
        throw 'Provider-scoped auth contained the wrong credentials.'
    }

    $scopedOauthPath = Join-Path $testRoot 'isolated-oauth-data\opencode\auth.json'
    [void](New-ProviderScopedOpenCodeAuthFile `
            -SourceAuthPath $sourceAuthPath `
            -Provider 'openai' `
            -DestinationPath $scopedOauthPath)
    $scopedOauth = Get-Content -LiteralPath $scopedOauthPath -Raw | ConvertFrom-Json
    if (@($scopedOauth.PSObject.Properties).Count -ne 1 -or
        $null -eq $scopedOauth.PSObject.Properties['openai'] -or
        [string]$scopedOauth.openai.type -cne 'oauth' -or
        [string]$scopedOauth.openai.access -cne 'test-access-token' -or
        $null -ne $scopedOauth.PSObject.Properties['openrouter']) {
        throw 'Provider-scoped OAuth contained the wrong credentials.'
    }

    Remove-BenchmarkSecretFile -Path $scopedAuthPath
    if (Test-Path -LiteralPath $scopedAuthPath) {
        throw 'Provider-scoped auth was not removed.'
    }
    Remove-BenchmarkSecretFile -Path $scopedOauthPath
    if (Test-Path -LiteralPath $scopedOauthPath) {
        throw 'Provider-scoped OAuth was not removed.'
    }

    Write-Host 'OpenCode benchmark common helper tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaBenchmarkCommonTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
