[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = Get-Location
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$canonicalRoot = Join-Path $repoRoot 'skills'
$pluginSkillRoots = @(
    (Join-Path $repoRoot 'plugins\opentia-toolkit\skills'),
    (Join-Path $repoRoot 'plugins\opentia-toolkit-claude-code\skills'),
    (Join-Path $repoRoot 'plugins\opentia-toolkit-opencode\.opencode\skills')
)

function Assert-ChildPath {
    param(
        [string]$ParentPath,
        [string]$ChildPath
    )

    $parentFull = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\', '/')
    $childFull = [System.IO.Path]::GetFullPath($ChildPath)
    $prefix = $parentFull + [System.IO.Path]::DirectorySeparatorChar

    if (-not $childFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside '$parentFull': $childFull"
    }
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    $baseUri = [System.Uri]::new(([System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar))
    $pathUri = [System.Uri]::new([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Get-FileHashMap {
    param([string]$RootPath)

    $map = @{}
    if (-not (Test-Path -LiteralPath $RootPath)) {
        return $map
    }

    foreach ($file in Get-ChildItem -LiteralPath $RootPath -Recurse -File -Force) {
        $relative = Get-RelativePath -BasePath $RootPath -Path $file.FullName
        $map[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }

    return $map
}

function Compare-SkillDirectory {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    $sourceHashes = Get-FileHashMap -RootPath $SourcePath
    $destinationHashes = Get-FileHashMap -RootPath $DestinationPath
    $differences = New-Object System.Collections.Generic.List[string]

    foreach ($relativePath in $sourceHashes.Keys) {
        if (-not $destinationHashes.ContainsKey($relativePath)) {
            $differences.Add("missing:$relativePath")
        } elseif ($destinationHashes[$relativePath] -ne $sourceHashes[$relativePath]) {
            $differences.Add("changed:$relativePath")
        }
    }

    foreach ($relativePath in $destinationHashes.Keys) {
        if (-not $sourceHashes.ContainsKey($relativePath)) {
            $differences.Add("extra:$relativePath")
        }
    }

    return $differences
}

if (-not (Test-Path -LiteralPath $canonicalRoot -PathType Container)) {
    throw "Canonical skill root does not exist: $canonicalRoot"
}

$drift = New-Object System.Collections.Generic.List[object]
$canonicalSkills = Get-ChildItem -LiteralPath $canonicalRoot -Directory | Sort-Object Name

foreach ($pluginRoot in $pluginSkillRoots) {
    if (-not (Test-Path -LiteralPath $pluginRoot -PathType Container)) {
        if ($CheckOnly) {
            foreach ($skill in $canonicalSkills) {
                $drift.Add([pscustomobject]@{
                    Skill       = $skill.Name
                    Destination = Get-RelativePath -BasePath $repoRoot -Path (Join-Path $pluginRoot $skill.Name)
                    Differences = 'missing:skill-directory'
                })
            }

            continue
        }

        New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null
    }

    foreach ($skill in $canonicalSkills) {
        $destination = Join-Path $pluginRoot $skill.Name
        Assert-ChildPath -ParentPath $pluginRoot -ChildPath $destination
        $differences = @(Compare-SkillDirectory -SourcePath $skill.FullName -DestinationPath $destination)

        if ($differences.Count -eq 0) {
            continue
        }

        $drift.Add([pscustomobject]@{
            Skill       = $skill.Name
            Destination = Get-RelativePath -BasePath $repoRoot -Path $destination
            Differences = $differences -join ', '
        })

        if (-not $CheckOnly) {
            if (Test-Path -LiteralPath $destination) {
                if ($PSCmdlet.ShouldProcess($destination, 'Replace packaged skill copy')) {
                    Remove-Item -LiteralPath $destination -Recurse -Force
                }
            }

            if ($PSCmdlet.ShouldProcess($destination, 'Copy canonical skill')) {
                Copy-Item -LiteralPath $skill.FullName -Destination $pluginRoot -Recurse -Force
            }
        }
    }
}

if ($drift.Count -gt 0) {
    $drift | Format-Table -AutoSize | Out-String | Write-Host

    if ($CheckOnly) {
        throw "Packaged skills differ from canonical skills. Run scripts\Sync-PluginSkills.ps1 to refresh them."
    }
}

[pscustomobject]@{
    CanonicalRoot = $canonicalRoot
    CheckedRoots  = $pluginSkillRoots
    DriftCount    = $drift.Count
    Mode          = if ($CheckOnly) { 'CheckOnly' } else { 'Sync' }
}
