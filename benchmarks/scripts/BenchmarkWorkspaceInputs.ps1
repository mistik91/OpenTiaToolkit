Set-StrictMode -Version Latest

function Resolve-BenchmarkRelativePath {
    param([string]$Root, [string]$RelativePath, [switch]$WorkspaceInput)

    $parts = @($RelativePath -split '[\\/]')
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath -match '[<>:"|?*]' -or
        @($parts | Where-Object { $_ -in @('', '.', '..') -or $_ -match '[. ]$' -or
                $_ -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' }).Count -gt 0) {
        throw 'Input paths must be unambiguous relative file paths without traversal or streams.'
    }
    if ($WorkspaceInput -and ($parts[0] -in @('.opencode', '.git', 'task.md', 'opencode.json'))) {
        throw 'Workspace input collides with a reserved harness path.'
    }
    $anchor = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    $path = [IO.Path]::GetFullPath((Join-Path $anchor $RelativePath))
    if (-not $path.StartsWith(($anchor + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Input path escapes its root.'
    }
    # Reject junctions/symlinks rather than trusting lexical containment alone.
    $current = $path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Reparse points are not permitted in benchmark input paths.'
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    return $path
}

function Get-BenchmarkWorkspaceInputPlan {
    param([object]$Case, [string]$CaseDirectory, [string]$WorkspacePath)

    if ($null -eq $Case.PSObject.Properties['workspaceInputs']) { return }
    $inputs = @($Case.workspaceInputs)
    if ($inputs.Count -lt 1 -or $inputs.Count -gt 20) { throw 'workspaceInputs must contain 1-20 files.' }
    $targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $plan = [Collections.Generic.List[object]]::new()
    $totalBytes = 0L
    foreach ($inputFile in $inputs) {
        foreach ($required in @('sourcePath', 'targetPath', 'sha256')) {
            if ($null -eq $inputFile.PSObject.Properties[$required]) { throw "Workspace input requires $required." }
        }
        if ([string]$inputFile.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Workspace input requires an exact SHA-256.' }
        $source = Resolve-BenchmarkRelativePath -Root $CaseDirectory -RelativePath $inputFile.sourcePath
        $target = Resolve-BenchmarkRelativePath -Root $WorkspacePath -RelativePath $inputFile.targetPath -WorkspaceInput
        if (-not $targets.Add($target)) { throw 'Workspace input target paths must be unique.' }
        foreach ($other in $plan) {
            if ($target.StartsWith(($other.TargetPath + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase) -or
                $other.TargetPath.StartsWith(($target + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Workspace input file/directory targets overlap.'
            }
        }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Workspace input source is not a file.' }
        $size = (Get-Item -LiteralPath $source).Length
        if ($size -gt 1048576) { throw 'Workspace input exceeds the 1 MiB per-file limit.' }
        $totalBytes += $size
        if ($totalBytes -gt 4194304) { throw 'Workspace inputs exceed the 4 MiB total limit.' }
        $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if ($hash -ine [string]$inputFile.sha256) { throw 'Workspace input source hash does not match its frozen definition.' }
        $plan.Add([pscustomobject]@{SourcePath=$source; TargetPath=$target; RelativePath=[string]$inputFile.targetPath; Sha256=$hash; Bytes=$size})
    }
    return $plan.ToArray()
}

function Copy-BenchmarkWorkspaceInputs {
    param([object]$Case, [string]$CaseDirectory, [string]$WorkspacePath)

    $plan = @(Get-BenchmarkWorkspaceInputPlan -Case $Case -CaseDirectory $CaseDirectory -WorkspacePath $WorkspacePath)
    # Complete validation and collision checks before copying anything.
    foreach ($item in $plan) {
        if (Test-Path -LiteralPath $item.TargetPath) { throw 'Workspace input target already exists; refusing to overwrite it.' }
    }
    foreach ($item in $plan) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $item.TargetPath) -Force | Out-Null
        Copy-Item -LiteralPath $item.SourcePath -Destination $item.TargetPath
        if ((Get-FileHash -LiteralPath $item.TargetPath -Algorithm SHA256).Hash -ine $item.Sha256) {
            throw 'Staged workspace input hash changed during copying.'
        }
    }
    return $plan
}
