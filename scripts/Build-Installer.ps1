[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputRoot = '.\artifacts\installer',
    [string]$InstallScript = '.\installer\OpenTiaToolkit.iss',
    [string]$Version,
    [switch]$SkipBuild,
    [switch]$SkipSkillSyncCheck,
    [switch]$StageOnly,
    [string]$InnoCompilerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepoPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Copy-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Required directory was not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required file was not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-BuildOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Required build output directory was not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $sourceRoot = [System.IO.Path]::GetFullPath($Source).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

    Get-ChildItem -LiteralPath $Source -Recurse -File |
        Where-Object { $_.Name -notlike 'Siemens.*' } |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($sourceRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
            $targetPath = Join-Path $Destination $relativePath
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $targetPath) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Force
        }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$repoRoot = $repoRoot.Path
$resolvedOutputRoot = Resolve-RepoPath -Path $OutputRoot
$stageRoot = Join-Path $resolvedOutputRoot 'stage'
$mcpProject = Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\OpenTiaToolkit.MCP.csproj'
$cliProject = Join-Path $repoRoot 'src\OpenTiaToolkit.CLI\OpenTiaToolkit.CLI.csproj'
$mcpBin = Join-Path $repoRoot "src\OpenTiaToolkit.MCP\bin\$Configuration\net48"
$cliBin = Join-Path $repoRoot "src\OpenTiaToolkit.CLI\bin\$Configuration\net48"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
    $Version = $props.Project.PropertyGroup.Version
}

if (-not $SkipSkillSyncCheck) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\Sync-PluginSkills.ps1') -CheckOnly
}

if (-not $SkipBuild) {
    dotnet build $mcpProject -c $Configuration
    dotnet build $cliProject -c $Configuration
}

$mcpExe = Join-Path $mcpBin 'OpenTiaToolkit.MCP.exe'
$cliExe = Join-Path $cliBin 'OpenTiaToolkit.CLI.exe'
if (-not (Test-Path -LiteralPath $mcpExe -PathType Leaf)) {
    throw "MCP executable was not found at '$mcpExe'. Build first or omit -SkipBuild."
}

if (-not (Test-Path -LiteralPath $cliExe -PathType Leaf)) {
    throw "CLI executable was not found at '$cliExe'. Build first or omit -SkipBuild."
}

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
$mcpStageRoot = Join-Path $stageRoot 'mcp'
$cliStageRoot = Join-Path $stageRoot 'cli'
Copy-BuildOutput -Source $mcpBin -Destination $mcpStageRoot
Copy-BuildOutput -Source $cliBin -Destination $cliStageRoot

Copy-Directory -Source (Join-Path $repoRoot 'plugins') -Destination (Join-Path $stageRoot 'plugins')
Copy-Directory -Source (Join-Path $repoRoot 'skills') -Destination (Join-Path $stageRoot 'skills')
Copy-Directory -Source (Join-Path $repoRoot 'docs') -Destination (Join-Path $stageRoot 'docs')
Copy-Directory -Source (Join-Path $repoRoot 'schemas') -Destination (Join-Path $stageRoot 'schemas')

Copy-RequiredFile -Source (Join-Path $repoRoot 'scripts\Register-OpennessAllowList.ps1') -Destination (Join-Path $stageRoot 'scripts\Register-OpennessAllowList.ps1')
Copy-RequiredFile -Source (Join-Path $repoRoot 'README.md') -Destination (Join-Path $stageRoot 'README.md')
Copy-RequiredFile -Source (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $stageRoot 'LICENSE')
Copy-RequiredFile -Source (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $stageRoot 'THIRD_PARTY_NOTICES.md')

$manifest = [ordered]@{
    Name = 'OpenTiaToolkit'
    Version = $Version
    Configuration = $Configuration
    InstallDirectory = 'C:\Program Files\OpenTiaToolkit'
    McpExecutable = 'C:\Program Files\OpenTiaToolkit\mcp\OpenTiaToolkit.MCP.exe'
    CliExecutable = 'C:\Program Files\OpenTiaToolkit\cli\OpenTiaToolkit.CLI.exe'
    PluginDirectory = 'C:\Program Files\OpenTiaToolkit\plugins'
    SkillDirectory = 'C:\Program Files\OpenTiaToolkit\skills'
    SchemaDirectory = 'C:\Program Files\OpenTiaToolkit\schemas'
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stageRoot 'install-manifest.json') -Encoding UTF8

$stagedMcpExe = Join-Path $mcpStageRoot 'OpenTiaToolkit.MCP.exe'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\Test-McpStdio.ps1') -ExecutablePath $stagedMcpExe
if ($LASTEXITCODE -ne 0) {
    throw "Staged MCP stdio initialization smoke failed with exit code $LASTEXITCODE."
}

if ($StageOnly) {
    [pscustomobject]@{
        StageRoot = $stageRoot
        Version = $Version
        Installer = $null
    }
    return
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) {
        $InnoCompilerPath = $command.Source
    }
    else {
        $defaultInnoPaths = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        )

        foreach ($defaultInno in $defaultInnoPaths) {
            if (Test-Path -LiteralPath $defaultInno -PathType Leaf) {
                $InnoCompilerPath = $defaultInno
                break
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or -not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6, pass -InnoCompilerPath, or run with -StageOnly to validate the staged payload."
}

$resolvedInstallScript = Resolve-RepoPath -Path $InstallScript
if (-not (Test-Path -LiteralPath $resolvedInstallScript -PathType Leaf)) {
    throw "Installer script was not found: $resolvedInstallScript"
}

$env:OPEN_TIA_INSTALLER_VERSION = $Version
try {
    & $InnoCompilerPath $resolvedInstallScript
}
finally {
    Remove-Item Env:\OPEN_TIA_INSTALLER_VERSION -ErrorAction SilentlyContinue
}

$installerPath = Join-Path $resolvedOutputRoot "OpenTiaToolkit-$Version-Setup.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Expected installer was not produced: $installerPath"
}

[pscustomobject]@{
    StageRoot = $stageRoot
    Version = $Version
    Installer = $installerPath
}
