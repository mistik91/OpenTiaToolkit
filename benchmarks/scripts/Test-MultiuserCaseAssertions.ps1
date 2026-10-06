[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'MultiuserBenchmark.ps1')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaMultiuserOracle-'+[Guid]::NewGuid().ToString('N'))
$workspace=Join-Path $testRoot 'workspace'; $evidence=Join-Path $testRoot 'operator'
New-Item -ItemType Directory -Path (Join-Path $workspace 'inputs'),$evidence -Force | Out-Null
$contract=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../cases/ott-37/acceptance.json') -Raw | ConvertFrom-Json
$main='<Document><DocumentInfo><Created>2026-01-01T00:00:00Z</Created></DocumentInfo><SW.Blocks.OB><AttributeList><Name>Main</Name><Number>1</Number><ProgrammingLanguage>LAD</ProgrammingLanguage></AttributeList></SW.Blocks.OB></Document>'
$sampleMain=Join-Path $testRoot 'main.xml'; [IO.File]::WriteAllText($sampleMain,$main)
foreach($plc in $contract.plcs){$plc.mainSha256=Get-MuMainHash $sampleMain}
$contractPath=Join-Path $workspace 'inputs/acceptance.json'; $contract | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $contractPath -Encoding UTF8
$assertion=[pscustomobject]@{ relativePath='inputs/acceptance.json'; contractSha256=(Get-FileHash -LiteralPath $contractPath).Hash }
$context=[pscustomobject]@{
    schemaVersion=1; archiveSha256=$contract.archiveSha256; agentPid=1234; coordinatorPid=4321
    agentSid='S-1-5-21-1-2-3-1001'; coordinatorSid='S-1-5-21-1-2-3-1002'; workerPath=(Join-Path $testRoot 'worker.exe')
    serverAlias='Test Server';projectName='OTT37_Test';sessionName='Trial';sessionDirectory=(Join-Path $testRoot 'Agent');sessionId=1
    sessionPath=(Join-Path $testRoot 'Agent/Trial.als21'); projectPath=(Join-Path $testRoot 'Agent/LS/OTT37_Test.amc21')
    evidenceRoot=$evidence; baseline=$null; readback=$null
}
function New-TestStatus {
    param([int]$Count=0)
    return [pscustomobject]@{ IsMultiuser=$true; IsOpen=$true; IsUpToDate=$true; ConflictCount=0; MarkingCount=$Count; Markings=@(if($Count -eq 1){[pscustomobject]@{ObjectName='PumpEnable';ObjectType='Siemens.Engineering.SW.Tags.PlcTag';State='IsUptoDate, IsMarkedByMe'}}) }
}
$info=[pscustomobject]@{ Name=$context.projectName; Path=$context.projectPath; PortalProcessId=$context.agentPid }
function New-TestNative {
    param([string]$Phase,[switch]$Changed)
    $directory=Join-Path $evidence $Phase; New-Item -ItemType Directory -Path $directory | Out-Null
    $commands=@((New-MuCommand 'doctor'),(New-MuCommand 'get-project-info'),(New-MuCommand 'get-multiuser-session-status'))
    if(-not $Changed){$commands+=New-MuCommand 'create-multiuser-session' @{'server-alias'=$context.serverAlias;'project-name'=$context.projectName;'session-name'=$context.sessionName;'session-directory'=$context.sessionDirectory;mode='Multiuser';confirm=$true};$commands+=New-MuCommand 'open-project' @{'project-path'=$context.sessionPath}}
    foreach($plc in $contract.plcs){
        Get-MuExpectedTags $plc $contract -Changed:$Changed | ForEach-Object { $_ } | Export-Csv -LiteralPath (Join-Path $directory ($plc.prefix+'-tags.csv')) -NoTypeInformation
        $contract.blocks | Export-Csv -LiteralPath (Join-Path $directory ($plc.prefix+'-blocks.csv')) -NoTypeInformation
        [IO.File]::WriteAllText((Join-Path $directory ($plc.prefix+'-main.xml')),$main)
        [pscustomobject]@{Name=$plc.name;Slot=1;OrderNumber='CPU';Children=@()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory ($plc.prefix+'-hardware.json')) -Encoding UTF8
        foreach($spec in @(@('export-tags','output-path','tags.csv'),@('get-blocks','output-path','blocks.csv'),@('export-block','export-path','main.xml'),@('get-hardware-tree','output-path','hardware.json'))){
            $arguments=@{}; $arguments[$spec[1]]=Join-Path $directory ($plc.prefix+'-'+$spec[2])
            if($spec[0] -eq 'get-hardware-tree'){$arguments['device-name']=$plc.name}else{$arguments['plc-name']=$plc.name}
            if($spec[0] -eq 'export-block'){$arguments['block-name']='Main'}
            $commands+=New-MuCommand $spec[0] $arguments
        }
    }
    $requestId=0
    $journal=@(foreach($command in $commands){
        $requestId++
        $data=if($command.name -eq 'doctor'){[pscustomobject]@{workerPath=$context.workerPath;processId=9999;healthy=$true;failureInjectionEnabled=$false}}elseif($command.name -eq 'get-project-info'){$info}elseif($command.name -eq 'get-multiuser-session-status'){New-TestStatus $(if($Changed){1}else{0})}elseif($command.name -eq 'create-multiuser-session'){[pscustomobject]@{SessionId=$context.sessionId;ProjectPath=$context.sessionPath}}else{[pscustomobject]@{}}
        [pscustomobject]@{request=[pscustomobject]@{id=$requestId;method='tools/call';params=[pscustomobject]@{name=$command.name;arguments=$command.arguments}};response=[pscustomobject]@{result=[pscustomobject]@{isError=$false;structuredContent=[pscustomobject]@{success=$true;data=$data}}}}
    })
    $journal | ForEach-Object { $_ | ConvertTo-Json -Depth 70 -Compress } | Set-Content -LiteralPath (Join-Path $directory 'journal.jsonl') -Encoding UTF8
    [pscustomobject]@{expectedSid=$context.agentSid;workerOwnerSid=$context.agentSid;portalOwnerSid=$context.agentSid;portalPid=$context.agentPid;workerPid=9999} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'identity.json') -Encoding UTF8
    [pscustomobject]@{Exited=$true;ExitCode=0} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'shutdown.json') -Encoding UTF8
    return New-MuProof $context $Phase $contract
}
$context.baseline=New-TestNative 'baseline'; $context.readback=New-TestNative 'readback' -Changed
$metadata=[pscustomobject]@{multiuser=$context}
$events=[Collections.Generic.List[object]]::new()
function Add-TestEvent {
    param([string]$Name,[object]$InputObject=@{},[object]$Data=@{})
    $events.Add([pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool=('opentia_'+$Name);state=[pscustomobject]@{status='completed';input=[pscustomobject]$InputObject;output=[pscustomobject]@{success=$true;data=$Data}}}})
}
function New-TestOperationStatusEvent {
    param([switch]$Active)
    $data=if($Active){[pscustomobject]@{operationId='synthetic-read';command='get-blocks';state='running'}}else{$null}
    return [pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool='opentia_operation-status';state=[pscustomobject]@{status='completed';input=[pscustomobject]@{};output=[pscustomobject]@{success=$true;data=$data}}}}
}
function Add-TestExports {
    param([string]$Phase,[switch]$Changed)
    $directory=Join-Path $workspace ('artifacts/'+$Phase);New-Item -ItemType Directory -Path $directory -Force | Out-Null
    foreach($plc in $contract.plcs){
        $tagsPath=Join-Path $directory ($plc.prefix+'-tags.csv');Get-MuExpectedTags $plc $contract -Changed:$Changed | ForEach-Object {$_} | Export-Csv -LiteralPath $tagsPath -NoTypeInformation
        Add-TestEvent 'export-tags' @{'plc-name'=$plc.name;'output-path'=$tagsPath}
        $mainPath=Join-Path $directory ($plc.prefix+'-main.xml');[IO.File]::WriteAllText($mainPath,$main)
        Add-TestEvent 'export-block' @{'plc-name'=$plc.name;'block-name'='Main';'export-path'=$mainPath}
    }
}
function New-TestTagOutcome {
    param([bool]$DryRun)
    $before=[pscustomobject]@{TagTable='Default tag table';Name='PumpEnable';DataType='Bool';LogicalAddress='%M20.0';Comment='Process pump enable - baseline'}
    $after=$before | ConvertTo-Json | ConvertFrom-Json;$after.LogicalAddress=$contract.operation.set.logicalAddress;$after.Comment=$contract.operation.set.comment
    return [pscustomobject]@{DryRun=$DryRun;Summary=[pscustomobject]@{Total=1;Planned=$(if($DryRun){1}else{0});Applied=$(if($DryRun){0}else{1});Failed=0};Operations=@([pscustomobject]@{Status=$(if($DryRun){'Planned'}else{'Applied'});Before=$before;After=$after})}
}
Add-TestEvent 'connect-instance' @{'instance-id'=1234}
Add-TestEvent 'get-project-info' @{} $info
Add-TestEvent 'get-multiuser-session-status' @{} (New-TestStatus)
Add-TestExports 'baseline'
Add-TestEvent 'modify-tags' @{'plc-name'='PLC_Process';'dry-run'=$true;operations=@($contract.operation)} (New-TestTagOutcome $true)
Add-TestEvent 'modify-tags' @{'plc-name'='PLC_Process';'dry-run'=$false;confirm=$true;operations=@($contract.operation)} (New-TestTagOutcome $false)
foreach($plc in $contract.plcs){Add-TestEvent 'compile-software' @{'plc-name'=$plc.name} ([pscustomobject]@{success=$true;errorCount=0;warningCount=0})}
Add-TestEvent 'save-multiuser-session' @{confirm=$true} (New-TestStatus 1)
Add-TestEvent 'get-multiuser-session-status' @{} (New-TestStatus 1)
Add-TestExports 'readback' -Changed
try {
    [void](Test-BenchmarkMultiuserLocalSave $events.ToArray() $metadata $workspace $assertion)
    $rejected=0
    foreach($scenario in @('foreign-instance','foreign-context','listed-wrong-context','listed-missing-pid','listed-extra-project','listed-display-trick','same-identity','missing-preview','wrong-selector','selector-alias','wrong-update','double-apply','no-apply','missing-confirm','string-confirm','string-dry-run','failed-apply','wrong-before','wrong-after','string-applied','missing-compile','warning','compile-after-save','missing-save','save-before-apply','stale-before','conflicts-before','wrong-mark','unowned-mark','string-current','string-count','missing-final-status','unsupported-commit','exclusive','hardware-mutation','close-discard','save-as','shell-proof','proof-write','same-evidence-root','wrong-native-sid','changed-native-file','foreign-native-producer','changed-model-main','changed-model-tags','missing-model-export','modified-contract','cancel-operation','reset-session','diagnostic-not-context','diagnostic-not-freshness','diagnostic-not-save','unknown-diagnostic')){
        $clone=$events.ToArray() | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        $copy=@($clone)
        $meta=$metadata | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        $apply=@($copy | Where-Object {$_.part.tool -eq 'opentia_modify-tags' -and -not $_.part.state.input.'dry-run'})[0]
        $save=@($copy | Where-Object {$_.part.tool -eq 'opentia_save-multiuser-session'})[0]
        $initial=@($copy | Where-Object {$_.part.tool -eq 'opentia_get-multiuser-session-status'})[0]
        if($scenario -like 'listed-*'){
            $copy[1].part.tool='opentia_list-projects'
            $listed=$copy[1].part.state.output.data | ConvertTo-Json | ConvertFrom-Json
            $listed.Name=$context.projectName+' (Session)'
            $copy[1].part.state.output.data=@($listed)
        }
        $restorePath=$null;$restoreText=$null;$restoreBytes=$null
        switch($scenario){
            'foreign-instance' {$copy[0].part.state.input.'instance-id'=4321}
            'foreign-context' {$copy[1].part.state.output.data.Path='C:\Wrong\wrong.amc21'}
            'listed-wrong-context' {$listed.Path='C:\Wrong\wrong.amc21'}
            'listed-missing-pid' {$listed.PSObject.Properties.Remove('PortalProcessId')}
            'listed-extra-project' {$copy[1].part.state.output.data+= $listed}
            'listed-display-trick' {$listed.Name=$context.projectName+' (Session) extra'}
            'same-identity' {$meta.multiuser.coordinatorSid=$meta.multiuser.agentSid}
            'missing-preview' {$copy=@($copy | Where-Object { -not ($_.part.tool -eq 'opentia_modify-tags' -and $_.part.state.input.'dry-run')})}
            'wrong-selector' {$apply.part.state.input.operations[0].match.logicalAddress='%M20.1'}
            'selector-alias' {$apply.part.state.input.operations[0].match | Add-Member -NotePropertyName address -NotePropertyValue '%M20.0'}
            'wrong-update' {$apply.part.state.input.operations[0].set.comment='wrong'}
            'double-apply' {$copy+= $apply}
            'no-apply' {$copy=@($copy | Where-Object { $_ -ne $apply })}
            'missing-confirm' {$apply.part.state.input.PSObject.Properties.Remove('confirm')}
            'string-confirm' {$apply.part.state.input.confirm='true'}
            'string-dry-run' {$apply.part.state.input.'dry-run'='false'}
            'failed-apply' {$apply.part.state.output.success=$false}
            'wrong-before' {$apply.part.state.output.data.Operations[0].Before.DataType='Int'}
            'wrong-after' {$apply.part.state.output.data.Operations[0].After.Name='PumpFeedback'}
            'string-applied' {$apply.part.state.output.data.Summary.Applied='1'}
            'missing-compile' {$copy=@($copy | Where-Object {$_.part.tool -ne 'opentia_compile-software'})}
            'warning' {@($copy | Where-Object {$_.part.tool -eq 'opentia_compile-software'})[0].part.state.output.data.warningCount=1}
            'compile-after-save' {$comp=@($copy | Where-Object {$_.part.tool -eq 'opentia_compile-software'});$copy=@($copy | Where-Object {$_.part.tool -ne 'opentia_compile-software'})+$comp}
            'missing-save' {$copy=@($copy | Where-Object {$_.part.tool -ne 'opentia_save-multiuser-session'})}
            'save-before-apply' {$copy=@($save)+@($copy | Where-Object {$_ -ne $save})}
            'stale-before' {$initial.part.state.output.data.IsUpToDate=$false}
            'conflicts-before' {$initial.part.state.output.data.ConflictCount=1}
            'wrong-mark' {$save.part.state.output.data.Markings[0].ObjectName='PumpFeedback'}
            'unowned-mark' {$save.part.state.output.data.Markings[0].State='IsMarkedByOther'}
            'string-current' {$save.part.state.output.data.IsUpToDate='true'}
            'string-count' {$save.part.state.output.data.MarkingCount='1'}
            'missing-final-status' {$copy=@($copy | Where-Object { -not ($_.part.tool -eq 'opentia_get-multiuser-session-status' -and $_.part.state.output.data.MarkingCount -eq 1) })}
            'unsupported-commit' {$copy[0].part.tool='opentia_commit-multiuser-session'}
            'exclusive' {$copy[0].part.tool='opentia_create-multiuser-session'}
            'hardware-mutation' {$copy[0].part.tool='opentia_apply-hardware-patch'}
            'close-discard' {$copy[0].part.tool='opentia_close-project'}
            'save-as' {$copy[0].part.tool='opentia_save-as-project'}
            'shell-proof' {$copy[0].part.tool='bash'}
            'proof-write' {$copy[0].part.tool='write'}
            'cancel-operation' {$extra=New-TestOperationStatusEvent;$extra.part.tool='opentia_cancel-operation';$copy+= $extra}
            'reset-session' {$extra=New-TestOperationStatusEvent;$extra.part.tool='opentia_reset-session';$copy+= $extra}
            'diagnostic-not-context' {$copy[1].part.tool='opentia_operation-status'}
            'diagnostic-not-freshness' {$initial.part.tool='opentia_operation-status'}
            'diagnostic-not-save' {$save.part.tool='opentia_operation-status'}
            'unknown-diagnostic' {$extra=New-TestOperationStatusEvent;$extra.part.tool='opentia_operation-status-extra';$copy+= $extra}
            'same-evidence-root' {$meta.multiuser.evidenceRoot=$workspace}
            'wrong-native-sid' {$meta.multiuser.agentSid='foreign'}
            'changed-native-file' {$meta.multiuser.readback.hashes.'process-tags.csv'='0'*64}
            'foreign-native-producer' {$restorePath=Join-Path $evidence 'readback/journal.jsonl';$restoreBytes=[IO.File]::ReadAllBytes($restorePath);$restoreText=Get-Content -LiteralPath $restorePath -Raw;[IO.File]::WriteAllText($restorePath,($restoreText.Replace('PLC_Process','wrong')));$meta.multiuser.readback.hashes.'journal.jsonl'=(Get-FileHash -LiteralPath $restorePath).Hash}
            'changed-model-main' {$restorePath=Join-Path $workspace 'artifacts/readback/process-main.xml';$restoreBytes=[IO.File]::ReadAllBytes($restorePath);[IO.File]::WriteAllText($restorePath,$main.Replace('<Number>1</Number>','<Number>2</Number>'))}
            'changed-model-tags' {$restorePath=Join-Path $workspace 'artifacts/readback/process-tags.csv';$restoreBytes=[IO.File]::ReadAllBytes($restorePath);$restoreText=Get-Content -LiteralPath $restorePath -Raw;[IO.File]::WriteAllText($restorePath,$restoreText.Replace('%M24.0','%M24.1'))}
            'missing-model-export' {$copy=@($copy | Where-Object {$_.part.tool -ne 'opentia_export-block'})}
            'modified-contract' {$assertionBad=$assertion | ConvertTo-Json | ConvertFrom-Json;$assertionBad.contractSha256='0'*64}
        }
        $failed=$false
        try {[void](Test-BenchmarkMultiuserLocalSave $copy $meta $workspace $(if($scenario -eq 'modified-contract'){$assertionBad}else{$assertion}))} catch {$failed=$true}
        finally {if($null -ne $restorePath){[IO.File]::WriteAllBytes($restorePath,$restoreBytes)}}
        if(-not $failed){throw "Unsafe oracle control passed: $scenario"}
        $rejected++
    }
    [void](Test-BenchmarkMultiuserLocalSave $events.ToArray() $metadata $workspace $assertion)
    $textEvents=$events.ToArray() | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    foreach($event in $textEvents){$event.part.state.output=$event.part.state.output | ConvertTo-Json -Depth 100 -Compress}
    [void](Test-BenchmarkMultiuserLocalSave $textEvents $metadata $workspace $assertion)
    $listedEvents=$events.ToArray() | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $listedEvents[1].part.tool='opentia_list-projects'
    $listedEvents[1].part.state.output.data.Name=$context.projectName+' (Session)'
    $listedEvents[1].part.state.output.data=@($listedEvents[1].part.state.output.data)
    [void](Test-BenchmarkMultiuserLocalSave $listedEvents $metadata $workspace $assertion)
    $idleEvents=@($listedEvents)+@(New-TestOperationStatusEvent)
    [void](Test-BenchmarkMultiuserLocalSave $idleEvents $metadata $workspace $assertion)
    # A rejected parallel read can recover through read-only active/idle status
    # and a serial retry. All independent safety/preservation gates still apply.
    $readInput=[pscustomobject]@{'plc-name'='PLC_Auxiliary'}
    $rejectedRead=[pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool='opentia_get-blocks';state=[pscustomobject]@{status='error';input=$readInput;error='{"success":false,"message":"Another TIA worker operation is active.","errorCode":"Conflict"}'}}}
    $retriedRead=[pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool='opentia_get-blocks';state=[pscustomobject]@{status='completed';input=$readInput;output=[pscustomobject]@{success=$true;data=@()}}}}
    $idle=New-TestOperationStatusEvent;$idle.part.state.output=$idle.part.state.output | ConvertTo-Json -Compress
    $recoveryEvents=@($listedEvents[0..6])+@($rejectedRead,(New-TestOperationStatusEvent -Active),$idle,$retriedRead)+@($listedEvents[7..($listedEvents.Count-1)])
    [void](Test-BenchmarkMultiuserLocalSave $recoveryEvents $metadata $workspace $assertion)
    $testCase=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../cases/ott-37/case.json') -Raw | ConvertFrom-Json
    foreach($a in $testCase.assertions){if($a.type -eq 'multiuserLocalSave'){$a.contractSha256=$assertion.contractSha256}}
    $casePath=Join-Path $testRoot 'case.json';$testCase | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $casePath -Encoding UTF8
    $tracePath=Join-Path $testRoot 'events.jsonl';$metadataPath=Join-Path $testRoot 'metadata.json';$resultPath=Join-Path $testRoot 'result.json'
    $trace=@([pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool='skill';state=[pscustomobject]@{status='completed';input=[pscustomobject]@{name='multiuser-collaboration-workflow'};output='synthetic skill'}}})+@($recoveryEvents)+@(
        [pscustomobject]@{type='text';part=[pscustomobject]@{text='Saved the local session. Check-in is pending in the owning TIA UI; no server persistence is claimed.'}},
        [pscustomobject]@{type='step_finish';part=[pscustomobject]@{reason='stop';cost=0;tokens=[pscustomobject]@{total=0;input=0;output=0;reasoning=0}}}
    )
    $trace | ForEach-Object {$_ | ConvertTo-Json -Depth 100 -Compress} | Set-Content -LiteralPath $tracePath -Encoding UTF8
    $scoringMetadata=[pscustomobject]@{model='synthetic/model';trialIndex=1;reasoningEffort='high';variant='high';openCodeVersion='1.18.10';gitCommit='0000000';gitBranch='test';fixtureSha256=$contract.archiveSha256;startedAtUtc='2026-01-01T00:00:00Z';finishedAtUtc='2026-01-01T00:00:01Z';durationSeconds=1;exitCode=0;timedOut=$false;workspacePath=$workspace;multiuser=$context}
    $context | Add-Member -NotePropertyName checkpoint -NotePropertyValue 'awaiting-ui-check-in'
    $context | Add-Member -NotePropertyName readbackFailureKind -NotePropertyValue $null
    $context | Add-Member -NotePropertyName readbackFailure -NotePropertyValue $null
    $scoringMetadata | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
    $result=& (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath $casePath -TracePath $tracePath -MetadataPath $metadataPath -OutputPath $resultPath
    if(-not $result.passed -or $result.score -ne 100 -or $result.serverPersistenceVerified -ne $false -or $result.multiuserCheckpoint -cne 'awaiting-ui-check-in'){throw ('Full Multiuser scorer positive failed: '+($result.assertions | ConvertTo-Json -Depth 20 -Compress))}
    $savedReadback=$context.readback;$context.readback=$null
    foreach($kind in @('model-state-mismatch','harness-error')){
        $context.readbackFailureKind=$kind;$context.readbackFailure='synthetic readback failure';$context.checkpoint='requires-operator-inspection'
        $scoringMetadata | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
        $result=& (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath $casePath -TracePath $tracePath -MetadataPath $metadataPath -OutputPath $resultPath
        if($result.passed -or $result.validForModelComparison -ne ($kind -eq 'model-state-mismatch')){throw 'Full scorer conflated a model failure with a native readback outage.'}
    }
    $context.readback=$savedReadback
    Write-Host "Multiuser local-save oracle passed (6 positives, $rejected adversarial controls, full scorer positive/model-failure/harness-invalid; no credentials/TIA used)."
} finally {
    $resolved=[IO.Path]::GetFullPath($testRoot);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char[]]'\/')+[IO.Path]::DirectorySeparatorChar
    if($resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolved).StartsWith('OpenTiaMultiuserOracle-',[StringComparison]::Ordinal)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
