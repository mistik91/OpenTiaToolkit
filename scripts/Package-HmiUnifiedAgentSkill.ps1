[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SkillSourcePath,

    [string]$OutputDirectory,

    [string]$PackageName = 'hmi-unified-agent-editing.zip',

    [switch]$Install,

    [string]$CodexHome,

    [string]$ValidatorPath,

    [switch]$SkipValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = Get-Location
}

if ([string]::IsNullOrWhiteSpace($SkillSourcePath)) {
    $SkillSourcePath = Join-Path $scriptRoot '..\skills\hmi-unified-agent-editing'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $scriptRoot '..\artifacts\skills'
}

function Get-FullPath {
    param([string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-ChildPath {
    param(
        [string]$ParentPath,
        [string]$ChildPath
    )

    $parentFull = (Get-FullPath -Path $ParentPath).TrimEnd('\', '/')
    $childFull = Get-FullPath -Path $ChildPath
    $prefix = $parentFull + [System.IO.Path]::DirectorySeparatorChar

    if (-not $childFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside '$parentFull': $childFull"
    }
}

function Remove-DirectorySafely {
    param(
        [string]$Path,
        [string]$SafeParentPath,
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Assert-ChildPath -ParentPath $SafeParentPath -ChildPath $Path

    if ($PSCmdlet.ShouldProcess($Path, "Remove existing $Description")) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-DefaultCodexHome {
    if (-not [string]::IsNullOrWhiteSpace($env:CODEX_HOME)) {
        return $env:CODEX_HOME
    }

    return Join-Path $HOME '.codex'
}

function Find-SkillValidator {
    if (-not [string]::IsNullOrWhiteSpace($ValidatorPath)) {
        return $ValidatorPath
    }

    $codexHomeCandidate = Join-Path (Get-DefaultCodexHome) 'skills\.system\skill-creator\scripts\quick_validate.py'
    if (Test-Path -LiteralPath $codexHomeCandidate -PathType Leaf) {
        return $codexHomeCandidate
    }

    return $null
}

function Invoke-SkillValidation {
    param([string]$Path)

    if ($SkipValidation) {
        return 'Skipped'
    }

    $validator = Find-SkillValidator
    if ([string]::IsNullOrWhiteSpace($validator)) {
        Write-Warning 'Skill validator was not found; skipping quick_validate.py.'
        return 'SkippedMissingValidator'
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) {
        Write-Warning 'python was not found; skipping quick_validate.py.'
        return 'SkippedMissingPython'
    }

    $validationOutput = & $python.Source $validator $Path
    if ($LASTEXITCODE -ne 0) {
        if ($validationOutput) {
            $validationOutput | Write-Error
        }

        throw "Skill validation failed for $Path"
    }

    foreach ($line in $validationOutput) {
        Write-Verbose $line
    }

    return 'Passed'
}

function Copy-DirectoryContents {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null

    foreach ($item in Get-ChildItem -LiteralPath $SourcePath -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $DestinationPath -Recurse -Force
    }
}

$resolvedSource = (Resolve-Path -LiteralPath $SkillSourcePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedSource 'SKILL.md') -PathType Leaf)) {
    throw "Skill source does not contain SKILL.md: $resolvedSource"
}

$skillName = Split-Path -Leaf $resolvedSource
if (-not $PackageName.EndsWith('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    $PackageName = "$PackageName.zip"
}

$resolvedOutput = Get-FullPath -Path $OutputDirectory
$packageFolder = Join-Path $resolvedOutput $skillName
$packagePath = Join-Path $resolvedOutput $PackageName

New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
Remove-DirectorySafely -Path $packageFolder -SafeParentPath $resolvedOutput -Description 'skill package folder'

if (Test-Path -LiteralPath $packagePath) {
    Assert-ChildPath -ParentPath $resolvedOutput -ChildPath $packagePath
    if ($PSCmdlet.ShouldProcess($packagePath, 'Remove existing skill package zip')) {
        Remove-Item -LiteralPath $packagePath -Force
    }
}

if ($PSCmdlet.ShouldProcess($packageFolder, 'Copy skill package files')) {
    Copy-DirectoryContents -SourcePath $resolvedSource -DestinationPath $packageFolder
}

$sourceValidation = Invoke-SkillValidation -Path $resolvedSource
$packageValidation = Invoke-SkillValidation -Path $packageFolder

if ($PSCmdlet.ShouldProcess($packagePath, 'Create skill package zip')) {
    Compress-Archive -Path $packageFolder -DestinationPath $packagePath -Force
}

$installedPath = $null
if ($Install) {
    if ([string]::IsNullOrWhiteSpace($CodexHome)) {
        $CodexHome = Get-DefaultCodexHome
    }

    $resolvedCodexHome = Get-FullPath -Path $CodexHome
    $skillsDirectory = Join-Path $resolvedCodexHome 'skills'
    $installedPath = Join-Path $skillsDirectory $skillName

    New-Item -ItemType Directory -Force -Path $skillsDirectory | Out-Null
    Remove-DirectorySafely -Path $installedPath -SafeParentPath $skillsDirectory -Description 'installed skill'

    if ($PSCmdlet.ShouldProcess($installedPath, 'Install skill into Codex home')) {
        Copy-Item -LiteralPath $packageFolder -Destination $skillsDirectory -Recurse -Force
    }
}

[pscustomobject]@{
    SkillName         = $skillName
    SourcePath        = $resolvedSource
    PackageFolder     = $packageFolder
    PackagePath       = $packagePath
    InstalledPath     = $installedPath
    SourceValidation  = $sourceValidation
    PackageValidation = $packageValidation
}
