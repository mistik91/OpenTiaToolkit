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
Write-Host "PASS: durable native output/replay, unchanged raw trace, scorer pass/harness-invalid; $rejected adversarial captures. Temporary evidence: $testRoot"
