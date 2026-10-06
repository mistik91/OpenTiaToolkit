[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'MultiuserBenchmark.ps1')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaMuSetup-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $workspace=Join-Path $testRoot 'ws';New-Item -ItemType Directory -Path $workspace | Out-Null
    $archive=Join-Path $testRoot 'baseline.zap21';[IO.File]::WriteAllText($archive,'synthetic archive; no TIA')
    $fixture=[pscustomobject]@{sha256=(Get-FileHash -LiteralPath $archive).Hash}
    $contract=Join-Path $workspace 'contract.json';[pscustomobject]@{archiveSha256=$fixture.sha256} | ConvertTo-Json | Set-Content -LiteralPath $contract -Encoding UTF8
    $evidence=Join-Path $testRoot 'proof'
    [void](Get-MuFixtureInputs $fixture $archive $evidence $workspace $contract)
    $rejected=0
    foreach($scenario in @('equal-roots','proof-inside-workspace','workspace-inside-proof','existing-proof','wrong-archive','wrong-contract','missing-archive','long-path','invalid-pid','non-tia-pid','missing-completion')){
        $failed=$false
        try {
            switch($scenario){
                'equal-roots' {[void](Get-MuFixtureInputs $fixture $archive $workspace $workspace $contract)}
                'proof-inside-workspace' {[void](Get-MuFixtureInputs $fixture $archive (Join-Path $workspace 'nested') $workspace $contract)}
                'workspace-inside-proof' {[void](Get-MuFixtureInputs $fixture $archive $testRoot $workspace $contract)}
                'existing-proof' {$existing=Join-Path $testRoot 'existing-proof';New-Item -ItemType Directory -Path $existing | Out-Null;[void](Get-MuFixtureInputs $fixture $archive $existing $workspace $contract)}
                'wrong-archive' {[void](Get-MuFixtureInputs ([pscustomobject]@{sha256=('0'*64)}) $archive $evidence $workspace $contract)}
                'wrong-contract' {$bad=Join-Path $workspace 'bad.json';[IO.File]::WriteAllText($bad,'{"archiveSha256":"wrong"}');[void](Get-MuFixtureInputs $fixture $archive (Join-Path $testRoot 'new-proof') $workspace $bad)}
                'missing-archive' {[void](Get-MuFixtureInputs $fixture (Join-Path $testRoot 'missing.zap21') (Join-Path $testRoot 'new-proof') $workspace $contract)}
                'long-path' {[void](Get-MuFixtureInputs $fixture $archive (Join-Path $testRoot ('x'*100)) $workspace $contract)}
                'invalid-pid' {[void](Assert-MuOwnedPortal 0 'fake')}
                'non-tia-pid' {[void](Assert-MuOwnedPortal $PID 'fake')}
                'missing-completion' {Assert-MuHandoffCompletion (Join-Path $testRoot 'absent-context.json') (Join-Path $testRoot 'absent-completion.json')}
            }
        } catch {$failed=$true}
        if(-not $failed){throw "Unsafe setup accepted: $scenario"}
        $rejected++
    }
    # Replace only the native probe for deterministic post-run classification.
    # Never log on as another account or attach to TIA in this test.
    function Invoke-MuProbe {
        param($Context,$Phase,$Commands)
        $directory=Join-Path $Context.evidenceRoot $Phase
        New-Item -ItemType Directory -Path $directory | Out-Null
        [pscustomobject]@{response=[pscustomobject]@{result=[pscustomobject]@{isError=$false;structuredContent=[pscustomobject]@{success=$true}}}} | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath (Join-Path $directory 'journal.jsonl') -Encoding UTF8
        if($script:scenario -eq 'state-mismatch'){throw 'Probe result does not match required value: data.MarkingCount'}
        if($script:scenario -eq 'transport-timeout'){throw 'Timed out waiting for tools/call; no retry or forced termination was attempted.'}
        throw 'Tool export-tags failed; inspect its retained structured response.'
    }
    $assertionContract=Join-Path $PSScriptRoot '../cases/ott-37/acceptance.json'
    foreach($script:scenario in @('state-mismatch','transport-timeout','vendor-error')){
        $root=Join-Path $testRoot $script:scenario;New-Item -ItemType Directory -Path $root | Out-Null
        $context=[pscustomobject]@{agentPid=1234;projectName='test';projectPath='test.amc21';evidenceRoot=$root;readback=$null;readbackFailure=$null;readbackFailureKind=$null;checkpoint='prepared'}
        $result=Get-MultiuserBenchmarkReadback $context $assertionContract
        $expected=if($script:scenario -eq 'state-mismatch'){'model-state-mismatch'}else{'harness-error'}
        if($result.readbackFailureKind -cne $expected -or $result.checkpoint -cne 'requires-operator-inspection' -or $null -ne $result.readback){throw 'Readback failure was incorrectly classified or silently completed.'}
    }
    Write-Host "Multiuser setup tests passed (1 positive, $rejected safety controls, 3 readback classifications; no credentials/TIA used)."
} finally {
    $resolved=[IO.Path]::GetFullPath($testRoot);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char[]]'\/')+[IO.Path]::DirectorySeparatorChar
    if($resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolved).StartsWith('OpenTiaMuSetup-',[StringComparison]::Ordinal)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
