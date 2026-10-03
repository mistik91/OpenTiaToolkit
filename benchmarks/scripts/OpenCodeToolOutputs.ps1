Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'BenchmarkWorkspaceInputs.ps1')

function Test-BenchmarkNativeOutputTruncated {
    param([object]$Event)
    if ($Event.type -ne 'tool_use' -or $null -eq $Event.PSObject.Properties['part'] -or
        $null -eq $Event.part -or $null -eq $Event.part.PSObject.Properties['state'] -or
        $null -eq $Event.part.state -or $null -eq $Event.part.state.PSObject.Properties['metadata'] -or
        $null -eq $Event.part.state.metadata -or
        $null -eq $Event.part.state.metadata.PSObject.Properties['truncated']) { return $false }
    if ($Event.part.state.metadata.truncated -isnot [bool]) { throw 'Invalid native truncation metadata.' }
    return $Event.part.state.metadata.truncated
}

function Get-BenchmarkTruncatedOutputPath {
    param([object]$Event, [string]$WorkspacePath)
    if ([string]::IsNullOrWhiteSpace($WorkspacePath) -or
        [string]::IsNullOrWhiteSpace([string]$Event.part.callID) -or
        [string]::IsNullOrWhiteSpace([string]$Event.part.tool) -or
        -not [IO.Path]::IsPathRooted([string]$Event.part.state.metadata.outputPath)) {
        throw 'Truncated output requires an absolute native path and exact tool call identity.'
    }
    $nativeRoot = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($WorkspacePath))) 'xdg-data/opencode/tool-output'
    $path = [IO.Path]::GetFullPath([string]$Event.part.state.metadata.outputPath)
    $leaf = [IO.Path]::GetFileName($path)
    if ($leaf -notmatch '^tool_[A-Za-z0-9]+$') { throw 'Invalid native OpenCode tool-output filename.' }
    $expected = Resolve-BenchmarkRelativePath -Root $nativeRoot -RelativePath $leaf
    if ($expected -ine $path) { throw 'Native tool output is outside this execution isolated data root.' }
    return $path
}

function Assert-BenchmarkToolOutputPrefix {
    param([object]$Event, [string]$FullText)
    $preview = [string]$Event.part.state.output
    $marker = [regex]::Match($preview, '(?m)^\.\.\.\d+ bytes truncated\.\.\.\r?$')
    if (-not $marker.Success -or $marker.Index -eq 0) { throw 'Native truncation preview marker is missing.' }
    $prefix = $preview.Substring(0, $marker.Index).TrimEnd([char[]]"`r`n")
    if ($prefix.Length -eq 0 -or -not $FullText.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Full tool output does not match its authentic trace preview.' }
}

function Copy-BenchmarkToolOutputs {
    param([string]$TracePath, [string]$WorkspacePath, [string]$ArtifactDirectory)
    $records = [Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $TracePath -PathType Leaf)) { return }
    foreach ($line in [IO.File]::ReadLines($TracePath)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $event = $line | ConvertFrom-Json
        if (-not (Test-BenchmarkNativeOutputTruncated -Event $event)) { continue }
        $source = Get-BenchmarkTruncatedOutputPath -Event $event -WorkspacePath $WorkspacePath
        $size = (Get-Item -LiteralPath $source).Length
        if ($size -gt 16777216) { throw 'Native tool output exceeds the 16 MiB capture limit.' }
        $text = [IO.File]::ReadAllText($source, [Text.UTF8Encoding]::new($false, $true))
        Assert-BenchmarkToolOutputPrefix -Event $event -FullText $text
        $leaf = [IO.Path]::GetFileName($source)
        $target = Resolve-BenchmarkRelativePath -Root $ArtifactDirectory -RelativePath "tool-output/$leaf"
        if (Test-Path -LiteralPath $target) { throw 'Tool output evidence already exists; refusing overwrite.' }
        $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $hash) { throw 'Tool output changed during capture.' }
        $records.Add([pscustomobject]@{callId=[string]$event.part.callID;tool=[string]$event.part.tool;sourcePath=$source;relativePath="tool-output/$leaf";sha256=$hash;bytes=$size})
    }
    return $records.ToArray()
}

function Restore-BenchmarkToolOutputs {
    param([object[]]$Events, [object]$Metadata, [string]$ArtifactDirectory)
    if ($null -ne $Metadata.PSObject.Properties['toolOutputCaptureError'] -and
        -not [string]::IsNullOrWhiteSpace([string]$Metadata.toolOutputCaptureError)) {
        throw [string]$Metadata.toolOutputCaptureError
    }
    foreach ($event in $Events) {
        if (-not (Test-BenchmarkNativeOutputTruncated -Event $event)) { continue }
        $source = Get-BenchmarkTruncatedOutputPath -Event $event -WorkspacePath $Metadata.workspacePath
        if ($null -eq $Metadata.PSObject.Properties['toolOutputs']) { throw 'Truncated tool output was not captured by this harness run.' }
        $records = @($Metadata.toolOutputs | Where-Object { $_.callId -ceq [string]$event.part.callID -and $_.tool -ceq [string]$event.part.tool })
        if ($records.Count -ne 1) { throw 'Truncated tool output needs one exact call-bound capture record.' }
        $record = $records[0]
        if ($record.sourcePath -ine $source -or $record.relativePath -cne ('tool-output/' + [IO.Path]::GetFileName($source)) -or
            [string]$record.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Tool output capture identity is inconsistent.' }
        $path = Resolve-BenchmarkRelativePath -Root $ArtifactDirectory -RelativePath $record.relativePath
        $size = (Get-Item -LiteralPath $path).Length
        if ($size -ne $record.bytes -or $size -gt 16777216 -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine [string]$record.sha256) { throw 'Captured tool output bytes changed.' }
        $text = [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false, $true))
        Assert-BenchmarkToolOutputPrefix -Event $event -FullText $text
        # Expand only the in-memory scorer view. Original events.jsonl stays immutable.
        $event.part.state.output = $text
    }
}
