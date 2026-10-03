[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PlcBehaviorAssertions.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaBehaviorOracleTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$vector = [pscustomobject]@{schemaVersion='1.0';vectorId='synthetic';reset=@(@{tag='IO.Reset';type='Bool';value=$true});cleanup=@(@{tag='IO.Reset';type='Bool';value=$true});steps=@(
    [pscustomobject]@{name='observed';writes=@(@{tag='IO.Reset';type='Bool';value=$false});assertions=@([pscustomobject]@{tag='IO.Active';type='Bool';operator='Equal';expected=$true;stableMilliseconds=50})}
)}
$vectorPath = Join-Path $testRoot 'vector.json'
[IO.File]::WriteAllText($vectorPath, ($vector | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
$assertion = [pscustomobject]@{relativePath='vector.json';vectorSha256=(Get-FileHash -LiteralPath $vectorPath).Hash}
$metadata = [pscustomobject]@{startedAtUtc='2026-10-03T09:00:00Z';finishedAtUtc='2026-10-03T09:10:00Z'}
$clean = [pscustomobject]@{Success=$true;ErrorCount=0;WarningCount=0}
$compare = [pscustomobject]@{Success=$true;ComparedToOnline=$true;DifferenceCount=0;Entries=@()}
$download = [pscustomobject]@{Success=$true;Applied=$true;ToSimulator=$true;DryRun=$false}
$transactionId = 'a' * 32
$probe = [pscustomobject]@{
    ReusedEvidence=$false
    Evidence=[pscustomobject]@{
        EvidenceId=('b' * 32);TransactionId=$transactionId;EvidenceKind='Behavior';Status='Verified';CleanupProven=$true
        StartedAtUtc='2026-10-03T09:01:00Z';CompletedAtUtc='2026-10-03T09:02:00Z'
        BaselineCompilation=$clean;ProbeCompilation=$clean;CleanupCompilation=$clean
        DeclaredObjects=@(@{Name='IO';ObjectType='GlobalDB'},@{Name='Cycle';ObjectType='OB'})
        CanonicalResultHashes=@(('c' * 64),('d' * 64))
        Behavior=[pscustomobject]@{
            PlcName='PLC_Main';VectorId='synthetic';VectorHash=$assertion.vectorSha256;SimulatorFingerprint=('e' * 64);SimulatorCleanupProven=$true
            BaselineCompare=$compare;ProbeCompare=$compare;CleanupCompare=$compare;ProbeDownload=$download;CleanupDownload=$download
            Execution=[pscustomobject]@{Passed=$true;ResetApplied=$true;CleanupApplied=$true;Steps=@(
                [pscustomobject]@{Name='observed';Passed=$true;WrittenTags=@('IO.Reset');Assertions=@(
                    [pscustomobject]@{Tag='IO.Active';Operator='Equal';Expected='True';Passed=$true;ElapsedMilliseconds=50;Observations=@(
                        [pscustomobject]@{Tag='IO.Active';Type='Bool';Value='True';Matched=$true}
                    )}
                )}
            )}
        }
    }
    ApplyResult=[pscustomobject]@{Verified=$true;TransactionId=$transactionId;Transaction=[pscustomobject]@{State='Succeeded';BaselineCompilation=$clean;FinalCompilation=$clean}}
    CleanupResult=[pscustomobject]@{TransactionId=$transactionId;RolledBackAndVerified=$true;RecoveryRequired=$false}
}
$template = [pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool='opentia_run-plc-code-behavior-probe';state=[pscustomobject]@{status='completed';input=[pscustomobject]@{confirm=$true;'vector-path'=$vectorPath;'plc-name'='PLC_Main'};output=[pscustomobject]@{success=$true;data=$probe}}}}
[void](Test-BenchmarkPlcBehaviorEvidence -ToolEvents @($template) -Metadata $metadata -WorkspaceRoot $testRoot -Assertion $assertion)
$rejected = 0
foreach ($scenario in @('model-text', 'get-evidence-replay', 'reused', 'failed', 'quarantined', 'missing-cleanup', 'missing-simulator-cleanup', 'wrong-vector', 'wrong-path', 'old', 'missing-compile', 'warnings', 'missing-hash', 'comparison-difference', 'dry-run', 'real-hardware', 'missing-reset', 'missing-step', 'wrong-step', 'missing-assertion', 'wrong-type', 'wrong-value', 'too-short', 'failed-observation', 'wrong-transaction', 'recovery-required')) {
    $event = $template | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $data = $event.part.state.output.data
    $evidence = $data.Evidence
    $behavior = $evidence.Behavior
    switch ($scenario) {
        'model-text' { $event.part.tool='edit' }
        'get-evidence-replay' { $event.part.tool='opentia_get-plc-code-evidence' }
        'reused' { $data.ReusedEvidence=$true }
        'failed' { $evidence.Status='Failed' }
        'quarantined' { $evidence.Status='Quarantined' }
        'missing-cleanup' { $evidence.CleanupProven=$false }
        'missing-simulator-cleanup' { $behavior.SimulatorCleanupProven=$false }
        'wrong-vector' { $behavior.VectorHash='0' * 64 }
        'wrong-path' { $event.part.state.input.'vector-path'=Join-Path $testRoot 'other.json' }
        'old' { $evidence.StartedAtUtc='2026-10-02T09:00:00Z' }
        'missing-compile' { $evidence.ProbeCompilation=$null }
        'warnings' { $evidence.ProbeCompilation.WarningCount=1 }
        'missing-hash' { $evidence.CanonicalResultHashes=@('c' * 64) }
        'comparison-difference' { $behavior.CleanupCompare.DifferenceCount=1 }
        'dry-run' { $behavior.ProbeDownload.DryRun=$true }
        'real-hardware' { $behavior.ProbeDownload.ToSimulator=$false }
        'missing-reset' { $behavior.Execution.ResetApplied=$false }
        'missing-step' { $behavior.Execution.Steps=@() }
        'wrong-step' { $behavior.Execution.Steps[0].Name='other' }
        'missing-assertion' { $behavior.Execution.Steps[0].Assertions=@() }
        'wrong-type' { $behavior.Execution.Steps[0].Assertions[0].Observations[0].Type='UInt32' }
        'wrong-value' { $behavior.Execution.Steps[0].Assertions[0].Observations[0].Value='False' }
        'too-short' { $behavior.Execution.Steps[0].Assertions[0].ElapsedMilliseconds=1 }
        'failed-observation' { $behavior.Execution.Steps[0].Assertions[0].Observations[0].Matched=$false }
        'wrong-transaction' { $data.CleanupResult.TransactionId='f' * 32 }
        'recovery-required' { $data.CleanupResult.RecoveryRequired=$true }
    }
    try { [void](Test-BenchmarkPlcBehaviorEvidence -ToolEvents @($event) -Metadata $metadata -WorkspaceRoot $testRoot -Assertion $assertion) }
    catch { $rejected++; continue }
    throw "Invalid behavior evidence was accepted: $scenario"
}
# Complementary partial results must not become a pass when combined.
$first = $template | ConvertTo-Json -Depth 100 | ConvertFrom-Json
$second = $template | ConvertTo-Json -Depth 100 | ConvertFrom-Json
$first.part.state.output.data.Evidence.CleanupProven=$false
$second.part.state.output.data.Evidence.Behavior.Execution.Passed=$false
try { [void](Test-BenchmarkPlcBehaviorEvidence -ToolEvents @($first,$second) -Metadata $metadata -WorkspaceRoot $testRoot -Assertion $assertion) }
catch { $rejected++ }
if ($rejected -ne 27) { throw 'Split-output evidence was accepted.' }
# OpenCode emits JSON text, not just already-decoded objects.
$textEvent = $template | ConvertTo-Json -Depth 100 | ConvertFrom-Json
$textEvent.part.state.output = $textEvent.part.state.output | ConvertTo-Json -Depth 100 -Compress
[void](Test-BenchmarkPlcBehaviorEvidence -ToolEvents @($textEvent) -Metadata $metadata -WorkspaceRoot $testRoot -Assertion $assertion)

# Exercise the actual scorer entry point, not only its helper.
$casePath = Join-Path $testRoot 'case.json'
$tracePath = Join-Path $testRoot 'events.jsonl'
$metadataPath = Join-Path $testRoot 'metadata.json'
$resultPath = Join-Path $testRoot 'result.json'
$scorerCase = [pscustomobject]@{
    id='ott-99';fixtureId='synthetic';passScore=100
    assertions=@([pscustomobject]@{id='behavior';type='plcBehaviorEvidence';points=100;critical=$true;relativePath='vector.json';vectorSha256=$assertion.vectorSha256})
}
$scorerMetadata = [pscustomobject]@{
    model='synthetic/model';variant=$null;openCodeVersion='1.18.10';gitCommit='0000000';gitBranch='test'
    startedAtUtc=$metadata.startedAtUtc;finishedAtUtc=$metadata.finishedAtUtc;durationSeconds=600
    exitCode=0;timedOut=$false;workspacePath=$testRoot
}
[IO.File]::WriteAllText($casePath, ($scorerCase | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($metadataPath, ($scorerMetadata | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
$stop = [pscustomobject]@{type='step_finish';part=[pscustomobject]@{reason='stop';tokens=[pscustomobject]@{total=0;input=0;output=0;reasoning=0};cost=0}}
foreach ($isNegative in @($false,$true)) {
    $scorerEvent = $template | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    if ($isNegative) { $scorerEvent.part.state.output.data.Evidence.CleanupProven=$false }
    $scorerEvent.part.state.output = $scorerEvent.part.state.output | ConvertTo-Json -Depth 100 -Compress
    $lines = @($scorerEvent,$stop) | ForEach-Object { $_ | ConvertTo-Json -Depth 100 -Compress }
    [IO.File]::WriteAllLines($tracePath, [string[]]$lines, [Text.UTF8Encoding]::new($false))
    $score = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath $casePath -TracePath $tracePath -MetadataPath $metadataPath -OutputPath $resultPath
    if (($isNegative -and ($score.passed -or $score.score -ne 0)) -or
        (-not $isNegative -and (-not $score.passed -or $score.score -ne 100))) { throw 'Structured evidence scorer integration is wrong.' }
}
[IO.File]::AppendAllText($vectorPath, ' ')
try { [void](Test-BenchmarkPlcBehaviorEvidence -ToolEvents @($template) -Metadata $metadata -WorkspaceRoot $testRoot -Assertion $assertion) }
catch { $rejected++ }
if ($rejected -ne 28) { throw 'Modified staged vector was accepted.' }
Write-Host "PASS: structured and JSON-text proofs, full scorer positive/negative; $rejected adversarial behavior cases. Temporary evidence: $testRoot"
