[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'OpenCodeToolOutputs.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaToolOutputTests-' + [Guid]::NewGuid().ToString('N'))
$workspace = Join-Path $testRoot 'execution/workspace'
$nativeRoot = Join-Path $testRoot 'execution/xdg-data/opencode/tool-output'
$artifacts = Join-Path $testRoot 'artifacts'
New-Item -ItemType Directory -Path $workspace,$nativeRoot,$artifacts | Out-Null
$source = Join-Path $nativeRoot 'tool_123ABC'
$full = '{"success":true,"data":"authentic-prefix ' + ('x' * 60000) + ' FULL_RESULT_' + [char]0xE9 + '"}'
$preview = $full.Substring(0, 80) + "`n`n...59980 bytes truncated...`n`nFull output saved to: $source"
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($source, $full, $utf8)
$template = [pscustomobject]@{type='tool_use';part=[pscustomobject]@{
    callID='call_123';tool='opentia_inspect_target';state=[pscustomobject]@{
        status='completed';input=[pscustomobject]@{};output=$preview
        metadata=[pscustomobject]@{truncated=$true;outputPath=$source}
    }
}}
function Copy-TestObject { param([object]$Value) return ($Value | ConvertTo-Json -Depth 50 | ConvertFrom-Json) }
$trace = Join-Path $artifacts 'events.jsonl'
$stop = [pscustomobject]@{type='step_finish';part=[pscustomobject]@{reason='stop';tokens=[pscustomobject]@{total=0;input=0;output=0;reasoning=0};cost=0}}
[IO.File]::WriteAllLines($trace, [string[]]@(($template | ConvertTo-Json -Depth 50 -Compress),($stop | ConvertTo-Json -Depth 50 -Compress)), $utf8)
$traceHash = (Get-FileHash -LiteralPath $trace).Hash
$captures = @(Copy-BenchmarkToolOutputs -TracePath $trace -WorkspacePath $workspace -ArtifactDirectory $artifacts)
if ($captures.Count -ne 1 -or $captures[0].callId -cne 'call_123') { throw 'Native capture identity is wrong.' }
$metadata = [pscustomobject]@{workspacePath=$workspace;toolOutputs=$captures}
$event = Copy-TestObject $template
Restore-BenchmarkToolOutputs -Events @($event) -Metadata $metadata -ArtifactDirectory $artifacts
if ($event.part.state.output -cne $full -or (Get-FileHash -LiteralPath $trace).Hash -cne $traceHash) { throw 'Durable restore changed bytes or the original trace.' }
# Replay must not depend on the ephemeral native output still existing.
Move-Item -LiteralPath $source -Destination ($source + '-retained')
$event = Copy-TestObject $template
Restore-BenchmarkToolOutputs -Events @($event) -Metadata $metadata -ArtifactDirectory $artifacts
if ($event.part.state.output -cne $full) { throw 'Offline durable replay failed.' }
Move-Item -LiteralPath ($source + '-retained') -Destination $source
# Normal untruncated events, including null metadata, remain untouched.
foreach ($normal in @(
    [pscustomobject]@{type='tool_use';part=[pscustomobject]@{state=[pscustomobject]@{metadata=$null;output='normal'}}},
    [pscustomobject]@{type='tool_use';part=[pscustomobject]@{state=[pscustomobject]@{output='normal'}}},
    $stop
)) { Restore-BenchmarkToolOutputs -Events @($normal) -Metadata ([pscustomobject]@{}) -ArtifactDirectory $artifacts }
$rejected = 0
# A semantic glob cap stays in the immutable trace and needs no file capture.
$glob = [pscustomobject]@{type='tool_use';part=[pscustomobject]@{callID='glob_1';tool='glob';state=[pscustomobject]@{
    status='completed';input=[pscustomobject]@{pattern='**/*'};output="C:/workspace/file1`n(Results are truncated: consider using a more specific path or pattern.)"
    metadata=[pscustomobject]@{count=100;truncated=$true}
}}}
$globTrace = Join-Path $testRoot 'glob.jsonl'
[IO.File]::WriteAllLines($globTrace, [string[]]@(($glob | ConvertTo-Json -Depth 50 -Compress),($stop | ConvertTo-Json -Depth 50 -Compress)), $utf8)
if (@(Copy-BenchmarkToolOutputs -TracePath $globTrace -WorkspacePath $workspace -ArtifactDirectory $artifacts).Count -ne 0) { throw 'Semantic glob cap was captured as a byte-truncated output.' }
$globOutput = $glob.part.state.output
Restore-BenchmarkToolOutputs -Events @($glob) -Metadata ([pscustomobject]@{}) -ArtifactDirectory $artifacts
if ($glob.part.state.output -cne $globOutput) { throw 'Semantic glob output was rewritten.' }
$semanticRejected = 0
foreach ($scenario in @('mcp','unknown','grep','wrong-count','string-count','missing-count','failed','byte-marker','file-marker','empty-path')) {
    $event = Copy-TestObject $glob
    switch ($scenario) {
        'mcp' { $event.part.tool='opentia_glob' }
        'unknown' { $event.part.tool='unknown' }
        'grep' { $event.part.tool='grep' }
        'wrong-count' { $event.part.state.metadata.count=99 }
        'string-count' { $event.part.state.metadata.count='100' }
        'missing-count' { $event.part.state.metadata.PSObject.Properties.Remove('count') }
        'failed' { $event.part.state.status='error' }
        'byte-marker' { $event.part.state.output=$preview }
        'file-marker' { $event.part.state.output='Full output saved to: missing' }
        'empty-path' { $event.part.state.metadata | Add-Member outputPath '' }
    }
    try { Restore-BenchmarkToolOutputs -Events @($event) -Metadata ([pscustomobject]@{workspacePath=$workspace}) -ArtifactDirectory $artifacts }
    catch { $semanticRejected++;continue }
    throw "Missing full-output evidence was silently accepted: $scenario"
}
# Even a built-in glob with an explicit full-output path must use authenticated capture.
$byteGlob = Copy-TestObject $template
$byteGlob.part.tool='glob'
$byteGlob.part.state.metadata | Add-Member count 100
$byteGlobMeta = Copy-TestObject $metadata
$byteGlobMeta.toolOutputs[0].tool='glob'
Restore-BenchmarkToolOutputs -Events @($byteGlob) -Metadata $byteGlobMeta -ArtifactDirectory $artifacts
if ($byteGlob.part.state.output -cne $full) { throw 'File-backed glob did not use durable capture.' }
# Native grep's complete capped search response is not a file-backed byte preview.
$grepRows = (1..100 | ForEach-Object { '  Line ' + $_ + ': match' }) -join "`n"
$grep = [pscustomobject]@{type='tool_use';part=[pscustomobject]@{callID='grep_1';tool='grep';state=[pscustomobject]@{
    status='completed';input=[pscustomobject]@{path=$workspace;pattern='match'}
    output="Found 100 matches (more matches available)`n$workspace/candidate.scl`:`n$grepRows`n`n(Results truncated. Consider using a more specific path or pattern.)"
    metadata=[pscustomobject]@{matches=100;truncated=$true}
}}}
$grepTrace = Join-Path $testRoot 'grep-cap.jsonl'
[IO.File]::WriteAllLines($grepTrace, [string[]]@(($grep | ConvertTo-Json -Depth 50 -Compress),($stop | ConvertTo-Json -Depth 50 -Compress)), $utf8)
if (@(Copy-BenchmarkToolOutputs -TracePath $grepTrace -WorkspacePath $workspace -ArtifactDirectory $artifacts).Count -ne 0) { throw 'Native grep cap was captured as a byte-truncated output.' }
$grepOutput = $grep.part.state.output
foreach ($crlf in @($false,$true)) {
    $event = Copy-TestObject $grep
    if ($crlf) { $event.part.state.output=$event.part.state.output.Replace("`n", "`r`n") }
    $unchanged = $event.part.state.output
    Restore-BenchmarkToolOutputs -Events @($event) -Metadata ([pscustomobject]@{}) -ArtifactDirectory $artifacts
    if ($event.part.state.output -cne $unchanged) { throw 'Native grep cap was rewritten.' }
}
$grepRejected = 0
foreach ($scenario in @('mcp','unknown','missing-identity','failed','wrong-count','string-count','missing-count','missing-input','missing-pattern','blank-pattern','missing-path','numeric-path','wrong-header','wrong-footer','short-rows','extra-rows','invalid-line','byte-marker','file-marker','empty-output-path')) {
    $event = Copy-TestObject $grep
    switch ($scenario) {
        'mcp' { $event.part.tool='opentia_grep' }
        'unknown' { $event.part.tool='unknown' }
        'missing-identity' { $event.part.PSObject.Properties.Remove('callID') }
        'failed' { $event.part.state.status='error' }
        'wrong-count' { $event.part.state.metadata.matches=99 }
        'string-count' { $event.part.state.metadata.matches='100' }
        'missing-count' { $event.part.state.metadata.PSObject.Properties.Remove('matches') }
        'missing-input' { $event.part.state.PSObject.Properties.Remove('input') }
        'missing-pattern' { $event.part.state.input.PSObject.Properties.Remove('pattern') }
        'blank-pattern' { $event.part.state.input.pattern=' ' }
        'missing-path' { $event.part.state.input.PSObject.Properties.Remove('path') }
        'numeric-path' { $event.part.state.input.path=123 }
        'wrong-header' { $event.part.state.output=$grepOutput.Replace('Found 100', 'Found 99') }
        'wrong-footer' { $event.part.state.output=$grepOutput.Replace('Results truncated.', 'Other truncation.') }
        'short-rows' { $event.part.state.output=$grepOutput.Replace("  Line 100: match`n", '') }
        'extra-rows' { $event.part.state.output=$grepOutput.Replace('  Line 100: match', "  Line 100: match`n  Line 101: match") }
        'invalid-line' { $event.part.state.output=$grepOutput.Replace('  Line 100:', '  Line 0:') }
        'byte-marker' { $event.part.state.output=$grepOutput + "`n...100 bytes truncated..." }
        'file-marker' { $event.part.state.output=$grepOutput + "`nFull output saved to: missing" }
        'empty-output-path' { $event.part.state.metadata | Add-Member outputPath '' }
    }
    try { Restore-BenchmarkToolOutputs -Events @($event) -Metadata ([pscustomobject]@{workspacePath=$workspace}) -ArtifactDirectory $artifacts }
    catch { $grepRejected++;continue }
    throw "Missing full-output evidence was silently accepted as a grep cap: $scenario"
}
$byteGrep = Copy-TestObject $template
$byteGrep.part.tool='grep'
$byteGrep.part.state.metadata | Add-Member matches 100
$byteGrepMeta = Copy-TestObject $metadata
$byteGrepMeta.toolOutputs[0].tool='grep'
Restore-BenchmarkToolOutputs -Events @($byteGrep) -Metadata $byteGrepMeta -ArtifactDirectory $artifacts
if ($byteGrep.part.state.output -cne $full) { throw 'File-backed grep did not use durable capture.' }
$readPath = Join-Path $workspace 'candidate.scl'
$slice = [pscustomobject]@{type='tool_use';part=[pscustomobject]@{callID='read_1';tool='read';state=[pscustomobject]@{
    status='completed';input=[pscustomobject]@{filePath=$readPath;limit=3}
    output="<path>$readPath</path>`n<type>file</type>`n<content>`n1: first`n2: second`n3: third`n`n(Showing lines 1-3 of 38. Use offset=4 to continue.)`n</content>"
    metadata=[pscustomobject]@{truncated=$true;display=[pscustomobject]@{type='file';path=$readPath;lineStart=1;lineEnd=3;totalLines=38;truncated=$true}}
}}}
$readTrace = Join-Path $testRoot 'read-slice.jsonl'
[IO.File]::WriteAllLines($readTrace, [string[]]@(($slice | ConvertTo-Json -Depth 50 -Compress),($stop | ConvertTo-Json -Depth 50 -Compress)), $utf8)
if (@(Copy-BenchmarkToolOutputs -TracePath $readTrace -WorkspacePath $workspace -ArtifactDirectory $artifacts).Count -ne 0) { throw 'Requested read slice was captured as a byte-truncated output.' }
$sliceOutput = $slice.part.state.output
Restore-BenchmarkToolOutputs -Events @($slice) -Metadata ([pscustomobject]@{}) -ArtifactDirectory $artifacts
if ($slice.part.state.output -cne $sliceOutput) { throw 'Requested read slice was rewritten.' }
# Explicit offsets and CRLF output use the same requested-slice protocol.
$offsetSlice = Copy-TestObject $slice
$offsetSlice.part.state.input | Add-Member offset 4
$offsetSlice.part.state.metadata.display.lineStart=4
$offsetSlice.part.state.metadata.display.lineEnd=6
$offsetSlice.part.state.output=$sliceOutput.Replace('1: first', '4: first').Replace('2: second', '5: second').Replace('3: third', '6: third').Replace('1-3', '4-6').Replace('offset=4', 'offset=7').Replace("`n", "`r`n")
$offsetOutput = $offsetSlice.part.state.output
Restore-BenchmarkToolOutputs -Events @($offsetSlice) -Metadata ([pscustomobject]@{}) -ArtifactDirectory $artifacts
if ($offsetSlice.part.state.output -cne $offsetOutput) { throw 'Offset/CRLF requested read slice was rewritten.' }
# A read with explicit file-backed truncation is never a semantic slice exception.
$byteRead = Copy-TestObject $template
$byteRead.part.tool='read'
$byteReadMeta = Copy-TestObject $metadata
$byteReadMeta.toolOutputs[0].tool='read'
Restore-BenchmarkToolOutputs -Events @($byteRead) -Metadata $byteReadMeta -ArtifactDirectory $artifacts
if ($byteRead.part.state.output -cne $full) { throw 'File-backed read did not use durable capture.' }
$sliceRejected = 0
foreach ($scenario in @('mcp','missing-identity','missing-display','display-type','display-path','display-flag','display-string-flag','range','total','string-range','limit','missing-limit','offset','wrong-preview-path','wrong-continuation','byte-marker','explicit-empty-path')) {
    $event = Copy-TestObject $slice
    switch ($scenario) {
        'mcp' { $event.part.tool='opentia_read' }
        'missing-identity' { $event.part.PSObject.Properties.Remove('callID') }
        'missing-display' { $event.part.state.metadata.PSObject.Properties.Remove('display') }
        'display-type' { $event.part.state.metadata.display.type='other' }
        'display-path' { $event.part.state.metadata.display.path='other' }
        'display-flag' { $event.part.state.metadata.display.truncated=$false }
        'display-string-flag' { $event.part.state.metadata.display.truncated='true' }
        'range' { $event.part.state.metadata.display.lineEnd=2 }
        'total' { $event.part.state.metadata.display.totalLines=3 }
        'string-range' { $event.part.state.metadata.display.lineStart='1' }
        'limit' { $event.part.state.input.limit=2 }
        'missing-limit' { $event.part.state.input.PSObject.Properties.Remove('limit') }
        'offset' { $event.part.state.input | Add-Member offset 2 }
        'wrong-preview-path' { $event.part.state.output=$sliceOutput.Replace('<path>', '<path>OTHER') }
        'wrong-continuation' { $event.part.state.output=$sliceOutput.Replace('offset=4', 'offset=5') }
        'byte-marker' { $event.part.state.output=$preview }
        'explicit-empty-path' { $event.part.state.metadata | Add-Member outputPath '' }
    }
    try { Restore-BenchmarkToolOutputs -Events @($event) -Metadata ([pscustomobject]@{workspacePath=$workspace}) -ArtifactDirectory $artifacts }
    catch { $sliceRejected++;continue }
    throw "Missing full-output evidence was silently accepted as a read slice: $scenario"
}
foreach ($scenario in @('missing-manifest','wrong-call','wrong-tool','duplicate-record','wrong-root','relative-native-path','invalid-native-leaf','wrong-source-identity','path-traversal','wrong-hash','wrong-size','wrong-prefix','missing-marker','nonbool-truncated','capture-error','missing-durable-file')) {
    $event = Copy-TestObject $template
    $meta = Copy-TestObject $metadata
    switch ($scenario) {
        'missing-manifest' { $meta.PSObject.Properties.Remove('toolOutputs') }
        'wrong-call' { $meta.toolOutputs[0].callId='other' }
        'wrong-tool' { $meta.toolOutputs[0].tool='other' }
        'duplicate-record' { $meta.toolOutputs=@($meta.toolOutputs[0],$meta.toolOutputs[0]) }
        'wrong-root' { $event.part.state.metadata.outputPath=Join-Path $workspace 'tool_123ABC' }
        'relative-native-path' { $event.part.state.metadata.outputPath='tool_123ABC' }
        'invalid-native-leaf' { $event.part.state.metadata.outputPath=Join-Path $nativeRoot 'proof.json' }
        'wrong-source-identity' { $meta.toolOutputs[0].sourcePath=Join-Path $nativeRoot 'tool_OTHER' }
        'path-traversal' { $meta.toolOutputs[0].relativePath='../tool_123ABC' }
        'wrong-hash' { $meta.toolOutputs[0].sha256='0' * 64 }
        'wrong-size' { $meta.toolOutputs[0].bytes=1 }
        'wrong-prefix' { $event.part.state.output='FORGED' + $preview }
        'missing-marker' { $event.part.state.output='No native preview' }
        'nonbool-truncated' { $event.part.state.metadata.truncated='true' }
        'capture-error' { $meta | Add-Member toolOutputCaptureError 'Capture failed' }
        'missing-durable-file' { $meta.toolOutputs[0].relativePath='tool-output/tool_ABSENT';$meta.toolOutputs[0].sourcePath=Join-Path $nativeRoot 'tool_ABSENT';$event.part.state.metadata.outputPath=$meta.toolOutputs[0].sourcePath }
    }
    try { Restore-BenchmarkToolOutputs -Events @($event) -Metadata $meta -ArtifactDirectory $artifacts }
    catch { $rejected++;continue }
    throw "Invalid native tool output was accepted: $scenario"
}
# Capture refuses overwrite and a source linked outside the isolated native root.
try { [void](Copy-BenchmarkToolOutputs -TracePath $trace -WorkspacePath $workspace -ArtifactDirectory $artifacts) }
catch { $rejected++ }
if ($rejected -ne 17) { throw 'Existing durable evidence was overwritten.' }
$escape = Join-Path $testRoot 'escape'
New-Item -ItemType Directory -Path $escape | Out-Null
$linkedRoot = Join-Path $testRoot 'linked/xdg-data/opencode'
New-Item -ItemType Directory -Path $linkedRoot | Out-Null
New-Item -ItemType Junction -Path (Join-Path $linkedRoot 'tool-output') -Target $escape | Out-Null
$event = Copy-TestObject $template
$event.part.state.metadata.outputPath=Join-Path $linkedRoot 'tool-output/tool_123ABC'
try { [void](Get-BenchmarkTruncatedOutputPath -Event $event -WorkspacePath (Join-Path $testRoot 'linked/workspace')) }
catch { $rejected++ }
if ($rejected -ne 18) { throw 'Native root junction was accepted.' }
foreach ($scenario in @('missing-native','oversized-native','changed-native-prefix','invalid-utf8')) {
    $event = Copy-TestObject $template
    $event.part.state.metadata.outputPath=Join-Path $nativeRoot 'tool_NEGATIVE'
    switch ($scenario) {
        'missing-native' { }
        'oversized-native' { [IO.File]::WriteAllText($event.part.state.metadata.outputPath, ('x' * 16777217), $utf8) }
        'changed-native-prefix' { [IO.File]::WriteAllText($event.part.state.metadata.outputPath, 'FORGED', $utf8) }
        'invalid-utf8' { [IO.File]::WriteAllBytes($event.part.state.metadata.outputPath, [byte[]]@(0xFF,0xFF)) }
    }
    $negativeTrace = Join-Path $testRoot "$scenario.jsonl"
    [IO.File]::WriteAllText($negativeTrace, ($event | ConvertTo-Json -Depth 50 -Compress), $utf8)
    try { [void](Copy-BenchmarkToolOutputs -TracePath $negativeTrace -WorkspacePath $workspace -ArtifactDirectory (Join-Path $testRoot $scenario)) }
    catch { $rejected++;continue }
    throw "Invalid native capture was accepted: $scenario"
}

# Real scorer integration: verified full output passes; a missing capture is harness-invalid.
$casePath = Join-Path $artifacts 'case.json'
$metaPath = Join-Path $artifacts 'metadata.json'
$resultPath = Join-Path $artifacts 'result.json'
$case = [pscustomobject]@{id='ott-99';fixtureId='synthetic';passScore=100;assertions=@(
    [pscustomobject]@{id='native-output';type='tool';points=100;critical=$true;namePattern='inspect_target$';minCount=1;outputPatterns=@('FULL_RESULT_')}
)}
$scorerMetadata = [pscustomobject]@{
    model='synthetic/model';variant=$null;openCodeVersion='1.18.10';gitCommit='0000000';gitBranch='test'
    startedAtUtc='2026-10-03T09:00:00Z';finishedAtUtc='2026-10-03T09:01:00Z';durationSeconds=60
    exitCode=0;timedOut=$false;workspacePath=$workspace;toolOutputs=$captures
}
[IO.File]::WriteAllText($casePath, ($case | ConvertTo-Json -Depth 30), $utf8)
foreach ($negative in @($false,$true)) {
    $meta = Copy-TestObject $scorerMetadata
    if ($negative) { $meta.toolOutputs=@() }
    [IO.File]::WriteAllText($metaPath, ($meta | ConvertTo-Json -Depth 30), $utf8)
    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath $casePath -TracePath $trace -MetadataPath $metaPath -OutputPath $resultPath
    if ($negative) {
        if ($result.validForModelComparison -or $result.executionFailure.kind -cne 'harness-error') { throw 'Missing native output was scored as a model failure.' }
    }
    elseif (-not $result.passed -or $result.score -ne 100 -or -not $result.validForModelComparison) { throw 'Full native output scorer integration failed.' }
}
if ((Get-FileHash -LiteralPath $trace).Hash -cne $traceHash) { throw 'Scoring modified the raw trace.' }
$globHash = (Get-FileHash -LiteralPath $globTrace).Hash
$case.assertions[0].namePattern='^glob$'
$case.assertions[0].outputPatterns=@('file1')
[IO.File]::WriteAllText($casePath, ($case | ConvertTo-Json -Depth 30), $utf8)
[IO.File]::WriteAllText($metaPath, ($scorerMetadata | ConvertTo-Json -Depth 30), $utf8)
$globResult = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath $casePath -TracePath $globTrace -MetadataPath $metaPath -OutputPath $resultPath
if (-not $globResult.passed -or -not $globResult.validForModelComparison -or (Get-FileHash -LiteralPath $globTrace).Hash -cne $globHash) { throw 'Semantic glob cap scorer integration failed or rewrote its trace.' }
$readHash = (Get-FileHash -LiteralPath $readTrace).Hash
$case.assertions[0].namePattern='^read$'
$case.assertions[0].outputPatterns=@('1: first')
[IO.File]::WriteAllText($casePath, ($case | ConvertTo-Json -Depth 30), $utf8)
$readResult = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath $casePath -TracePath $readTrace -MetadataPath $metaPath -OutputPath $resultPath
if (-not $readResult.passed -or -not $readResult.validForModelComparison -or (Get-FileHash -LiteralPath $readTrace).Hash -cne $readHash) { throw 'Read slice scorer integration failed or rewrote its trace.' }
$grepHash = (Get-FileHash -LiteralPath $grepTrace).Hash
$case.assertions[0].namePattern='^grep$'
$case.assertions[0].outputPatterns=@('Line 100: match')
[IO.File]::WriteAllText($casePath, ($case | ConvertTo-Json -Depth 30), $utf8)
$grepResult = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath $casePath -TracePath $grepTrace -MetadataPath $metaPath -OutputPath $resultPath
if (-not $grepResult.passed -or -not $grepResult.validForModelComparison -or (Get-FileHash -LiteralPath $grepTrace).Hash -cne $grepHash) { throw 'Native grep cap scorer integration failed or rewrote its trace.' }
Write-Host "PASS: durable native output/replay, unchanged raw trace, semantic glob/read/grep, scorer pass/harness-invalid; $rejected adversarial captures + $semanticRejected glob + $sliceRejected read + $grepRejected grep controls. Temporary evidence: $testRoot"
