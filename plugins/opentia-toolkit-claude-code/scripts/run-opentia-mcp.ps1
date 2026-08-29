[CmdletBinding()]
param(
    [switch]$ResolveOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
$candidates = [System.Collections.Generic.List[string]]::new()

if (-not [string]::IsNullOrWhiteSpace($env:OPEN_TIA_MCP_EXE)) {
    if (-not (Test-Path -LiteralPath $env:OPEN_TIA_MCP_EXE -PathType Leaf)) {
        throw "OPEN_TIA_MCP_EXE points to a missing file: $env:OPEN_TIA_MCP_EXE"
    }

    $candidates.Add($env:OPEN_TIA_MCP_EXE)
}

$current = [System.IO.DirectoryInfo]::new([System.IO.Path]::GetFullPath($scriptRoot))
while ($null -ne $current) {
    $candidates.Add((Join-Path $current.FullName 'mcp\OpenTiaToolkit.MCP.exe'))
    $current = $current.Parent
}

$defaultInstallRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'OpenTiaToolkit'
$candidates.Add((Join-Path $defaultInstallRoot 'mcp\OpenTiaToolkit.MCP.exe'))

$repoRoot = Find-RepoRoot -StartPath $scriptRoot
if (-not [string]::IsNullOrWhiteSpace($repoRoot)) {
    $candidates.Add((Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe'))
    $candidates.Add((Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\bin\Release\net48\OpenTiaToolkit.MCP.exe'))
}

$resolvedExecutable = $null
foreach ($candidate in $candidates | Select-Object -Unique) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $resolvedExecutable = [System.IO.Path]::GetFullPath($candidate)
        break
    }
}

if ([string]::IsNullOrWhiteSpace($resolvedExecutable)) {
    throw "OpenTiaToolkit.MCP.exe was not found. Install OpenTiaToolkit, build the MCP project in this repo, or set OPEN_TIA_MCP_EXE to the executable path."
}

if ($ResolveOnly) {
    $resolvedExecutable
    exit 0
}

& $resolvedExecutable
exit $LASTEXITCODE
