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

    $sourceAuthPath = Join-Path $testRoot 'source-auth.json'
    $scopedAuthPath = Join-Path $testRoot 'isolated-data\opencode\auth.json'
    [ordered]@{
        openrouter = [ordered]@{ type = 'api'; key = 'test-openrouter-key' }
        opencode   = [ordered]@{ type = 'api'; key = 'test-opencode-key' }
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

    Remove-BenchmarkSecretFile -Path $scopedAuthPath
    if (Test-Path -LiteralPath $scopedAuthPath) {
        throw 'Provider-scoped auth was not removed.'
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
