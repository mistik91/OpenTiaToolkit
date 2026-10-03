Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'BenchmarkWorkspaceInputs.ps1')

function Test-BenchmarkRequestedReadSlice {
    param([object]$Event)
    $state = $Event.part.state
    if ($null -eq $state.PSObject.Properties['input'] -or $null -eq $state.input -or
        $null -eq $state.input.PSObject.Properties['filePath'] -or
        $null -eq $state.input.PSObject.Properties['limit'] -or
        $null -eq $state.metadata.PSObject.Properties['display'] -or $null -eq $state.metadata.display) { return $false }
    $display = $state.metadata.display
    foreach ($name in @('type','path','truncated','lineStart','lineEnd','totalLines')) {
        if ($null -eq $display.PSObject.Properties[$name]) { return $false }
    }
    if ($display.type -cne 'file' -or $display.truncated -isnot [bool] -or -not $display.truncated -or
        $display.path -isnot [string] -or $display.path -cne $state.input.filePath -or
        [string]::IsNullOrWhiteSpace($display.path)) { return $false }
    foreach ($number in @($display.lineStart,$display.lineEnd,$display.totalLines,$state.input.limit)) {
        if (($number -isnot [int] -and $number -isnot [long]) -or $number -lt 1) { return $false }
    }
    $offset = 1
    if ($null -ne $state.input.PSObject.Properties['offset']) {
        $offset = $state.input.offset
        if (($offset -isnot [int] -and $offset -isnot [long]) -or $offset -lt 1) { return $false }
    }
    if ($display.lineStart -ne $offset -or $display.lineEnd -ge $display.totalLines -or
        ([long]$display.lineEnd - [long]$display.lineStart + 1) -ne $state.input.limit) { return $false }
    $output = $state.output.Replace("`r`n", "`n")
    $prefix = '<path>' + $display.path + "</path>`n<type>file</type>`n<content>`n"
    $suffix = '(Showing lines ' + $display.lineStart + '-' + $display.lineEnd + ' of ' + $display.totalLines + '. Use offset=' + ([long]$display.lineEnd + 1) + " to continue.)`n</content>"
    return $output.StartsWith($prefix, [StringComparison]::Ordinal) -and $output.EndsWith($suffix, [StringComparison]::Ordinal)
}

function Test-BenchmarkNativeGrepCap {
    param([object]$Event)
    $state = $Event.part.state
    if ($null -eq $state.metadata.PSObject.Properties['matches'] -or
        ($state.metadata.matches -isnot [int] -and $state.metadata.matches -isnot [long]) -or
        $state.metadata.matches -ne 100 -or
        $null -eq $state.PSObject.Properties['input'] -or $null -eq $state.input) { return $false }
    foreach ($name in @('path','pattern')) {
        if ($null -eq $state.input.PSObject.Properties[$name] -or $state.input.$name -isnot [string] -or
            [string]::IsNullOrWhiteSpace($state.input.$name)) { return $false }
    }
    $output = $state.output.Replace("`r`n", "`n")
    return $output.StartsWith("Found 100 matches (more matches available)`n", [StringComparison]::Ordinal) -and
        $output.EndsWith('(Results truncated. Consider using a more specific path or pattern.)', [StringComparison]::Ordinal) -and
        [regex]::Matches($output, '(?m)^  Line [1-9][0-9]*: ').Count -eq 100 -and
        [regex]::Matches($output, '(?m)^  Line ').Count -eq 100
}

function Test-BenchmarkNativeOutputTruncated {
    param([object]$Event)
    if ($Event.type -ne 'tool_use' -or $null -eq $Event.PSObject.Properties['part'] -or
        $null -eq $Event.part -or $null -eq $Event.part.PSObject.Properties['state'] -or
        $null -eq $Event.part.state -or $null -eq $Event.part.state.PSObject.Properties['metadata'] -or
        $null -eq $Event.part.state.metadata -or
        $null -eq $Event.part.state.metadata.PSObject.Properties['truncated']) { return $false }
    if ($Event.part.state.metadata.truncated -isnot [bool]) { throw 'Invalid native truncation metadata.' }
    # Native semantic caps/slices are not the full-output-file truncation protocol.
    # Keep exceptions narrow: MCP/unknown tools and byte-truncated previews require capture.
    $meta = $Event.part.state.metadata
    if ($meta.truncated -and $null -eq $meta.PSObject.Properties['outputPath'] -and
        $null -ne $Event.part.PSObject.Properties['tool'] -and
        $null -ne $Event.part.PSObject.Properties['callID'] -and -not [string]::IsNullOrWhiteSpace([string]$Event.part.callID) -and
        $null -ne $Event.part.state.PSObject.Properties['status'] -and $Event.part.state.status -ceq 'completed' -and
        $null -ne $Event.part.state.PSObject.Properties['output'] -and $Event.part.state.output -is [string] -and
        [string]$Event.part.state.output -notmatch '(?m)^\.\.\.\d+ bytes truncated\.\.\.\r?$' -and
        [string]$Event.part.state.output -notmatch 'Full output saved to:') {
        if ($Event.part.tool -ceq 'glob' -and $null -ne $meta.PSObject.Properties['count'] -and
            ($meta.count -is [int] -or $meta.count -is [long]) -and $meta.count -eq 100) { return $false }
        if ($Event.part.tool -ceq 'read' -and (Test-BenchmarkRequestedReadSlice -Event $Event)) { return $false }
        if ($Event.part.tool -ceq 'grep' -and (Test-BenchmarkNativeGrepCap -Event $Event)) { return $false }
    }
    return $Event.part.state.metadata.truncated
}

function Get-BenchmarkTruncatedOutputPath {
    param([object]$Event, [string]$WorkspacePath)
    if ([string]::IsNullOrWhiteSpace($WorkspacePath) -or
        [string]::IsNullOrWhiteSpace([string]$Event.part.callID) -or
        [string]::IsNullOrWhiteSpace([string]$Event.part.tool) -or
        $null -eq $Event.part.state.metadata.PSObject.Properties['outputPath'] -or
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
