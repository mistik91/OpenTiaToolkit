param(
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')
$targets = @(
    'artifacts',
    'src\OpenTiaToolkit.CLI\artifacts',
    'src\OpenTiaToolkit.Core\artifacts',
    'src\OpenTiaToolkit.MCP\artifacts',
    'tests\OpenTiaToolkit.Tests\TestResults'
)

$removed = 0

foreach ($relativePath in $targets) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }

    $resolved = Resolve-Path -LiteralPath $path
    if (-not $resolved.Path.StartsWith($repoRoot.Path, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean path outside the repository: $($resolved.Path)"
    }

    if ($WhatIf) {
        Write-Host "Would remove $($resolved.Path)"
    } else {
        Remove-Item -LiteralPath $resolved.Path -Recurse -Force
        Write-Host "Removed $($resolved.Path)"
        $removed++
    }
}

Write-Host "Cleaned $removed artifact root(s)."
