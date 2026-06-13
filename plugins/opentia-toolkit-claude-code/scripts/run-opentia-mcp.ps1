[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [string]::IsNullOrWhiteSpace($env:OPEN_TIA_MCP_EXE)) {
    if (-not (Test-Path -LiteralPath $env:OPEN_TIA_MCP_EXE -PathType Leaf)) {
        throw "OPEN_TIA_MCP_EXE points to a missing file: $env:OPEN_TIA_MCP_EXE"
    }

    & $env:OPEN_TIA_MCP_EXE
    exit $LASTEXITCODE
}

function Find-RepoRoot {
    param([string]$StartPath)

    $current = [System.IO.DirectoryInfo]::new([System.IO.Path]::GetFullPath($StartPath))
    while ($null -ne $current) {
        if (Test-Path -LiteralPath (Join-Path $current.FullName 'src\OpenTiaToolkit.MCP\OpenTiaToolkit.MCP.csproj') -PathType Leaf) {
            return $current.FullName
        }

        $current = $current.Parent
    }

    return $null
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Find-RepoRoot -StartPath $scriptRoot
$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($repoRoot)) {
    $candidates = @(
        (Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe'),
        (Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\bin\Release\net48\OpenTiaToolkit.MCP.exe')
    )
}

foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        & $candidate
        exit $LASTEXITCODE
    }
}

throw "OpenTiaToolkit.MCP.exe was not found. Build the MCP project in this repo or set OPEN_TIA_MCP_EXE to the executable path."
