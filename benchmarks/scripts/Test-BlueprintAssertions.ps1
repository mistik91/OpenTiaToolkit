[CmdletBinding()]
param([string]$GoldRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'BlueprintAssertions.ps1')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaBlueprintTests-'+[Guid]::NewGuid().ToString('N'))
$utf8=[Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $testRoot | Out-Null
$contract=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../cases/ott-36/acceptance.json') -Raw | ConvertFrom-Json
$main='<Document><DocumentInfo>volatile</DocumentInfo><SW.Blocks.OB ID="0"><AttributeList><Name>Main</Name></AttributeList></SW.Blocks.OB></Document>'
[xml]$mainXml=$main;[void]$mainXml.Document.RemoveChild($mainXml.SelectSingleNode('/Document/DocumentInfo'))
$contract.mainHash=[BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($mainXml.OuterXml))).Replace('-','').ToLowerInvariant()
$contractPath=Join-Path $testRoot 'inputs/acceptance.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $contractPath) | Out-Null
[IO.File]::WriteAllText($contractPath,($contract | ConvertTo-Json -Depth 30),$utf8)
$assertion=[pscustomobject]@{relativePath='inputs/acceptance.json';contractSha256=(Get-FileHash -LiteralPath $contractPath).Hash}
$metadata=[pscustomobject]@{fixtureRestoreRoot=(Join-Path $testRoot 'restored');tiaInstanceId=1234}
$projectPath=Join-Path $metadata.fixtureRestoreRoot 'OTT_Benchmark/OTT_Benchmark.ap21'
$events=[Collections.Generic.List[object]]::new()
function Add-Event {
    param([string]$Name,[object]$InputObject,[object]$Data,[bool]$Success=$true,[string]$Message='completed')
    $events.Add([pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool=('opentia_'+$Name);state=[pscustomobject]@{status='completed';input=$InputObject;output=[pscustomobject]@{success=$Success;message=$Message;data=$Data}}}})
}
function New-TagStep {
    param([string]$Id,[string]$Comment,[object]$Target=$contract.primary)
    return [pscustomobject]@{Id=$Id;Command='modify-tags';ContinueOnError=$false;Arguments=[pscustomobject]@{
        'plc-name'=$contract.plcName;'dry-run'=$true;operations=@([pscustomobject]@{action='update';match=[pscustomobject]@{tagTable=$contract.tagTable;name=$Target.name;logicalAddress=$Target.address};set=[pscustomobject]@{comment=$Comment}})}}
}
function New-HmiStep {
    param([string]$Hash)
    return [pscustomobject]@{Id='label';Command='apply-hmi-screen-json-patch';ContinueOnError=$false;Arguments=[pscustomobject]@{
        'hmi-name'=$contract.hmiName;'screen-path'=$contract.screenPath;'base-hash'=$Hash;'item-names'=@($contract.itemName);properties=$contract.properties;'dry-run'=$true;backup=$true;
        operations=@([pscustomobject]@{op='set-property';item=$contract.itemName;property='Text';value=$contract.label})}}
}
function Add-Plan {
    param([string]$Name,[object[]]$Steps,[string]$Id)
    $bp=[pscustomobject]@{SchemaVersion='1.0';Name=$Name;Description=$null;RequiredWorker='V21';Steps=$Steps}
    $path=Join-Path $testRoot ('artifacts/blueprints/'+$Name+'.json')
    Add-Event 'validate-blueprint' @{'blueprint-path'=$path} $bp
    $plan=[pscustomobject]@{PlanId=($Id*32);ApplyToken=('token-'+$Name);StateFingerprint=('f'*64);BlueprintHash=(Get-BenchmarkBlueprintHash $bp);Steps=@($Steps | ForEach-Object {[pscustomobject]@{Id=$_.Id;Command=$_.Command}})}
    Add-Event 'plan-blueprint' @{'blueprint-path'=$path} $plan
    return $plan
}
function Add-Preview {
    param([object]$Step,[switch]$Blocked)
    $data=if($Step.Command -eq 'modify-tags'){[pscustomobject]@{DryRun=$true;Summary=[pscustomobject]@{Planned=1;Failed=0}}}else{[pscustomobject]@{DryRun=$true;SafetyBlocked=[bool]$Blocked;SafeToApply=(-not $Blocked)}}
    Add-Event $Step.Command $Step.Arguments $data
}
function Add-State {
    param([string]$Phase,[string]$Comment,[string]$Text='Start',[string]$Hash='baseline-hash')
    $folder=Join-Path $testRoot ('artifacts/'+$Phase);New-Item -ItemType Directory -Path $folder | Out-Null
    $tags=@([pscustomobject]@{TagTableName=$contract.tagTable;Name=$contract.primary.name;DataType=$contract.primary.type;LogicalAddress=$contract.primary.address;Comment=$Comment},[pscustomobject]@{TagTableName=$contract.tagTable;Name=$contract.sentinel.name;DataType=$contract.sentinel.type;LogicalAddress=$contract.sentinel.address;Comment=$contract.baselineComment})
    $path=Join-Path $folder 'tags.csv';$tags | Export-Csv -LiteralPath $path -NoTypeInformation
    Add-Event 'export-tags' @{'plc-name'=$contract.plcName;'output-path'=$path} ([pscustomobject]@{OutputPath=$path;Format='csv';ItemCount=2;BytesWritten=(Get-Item -LiteralPath $path).Length})
    $props=$contract.baselineItemProperties | ConvertTo-Json | ConvertFrom-Json;$props.Text=$Text
    $screen=[pscustomobject]@{Name='Overview';Width=800;Height=480;Items=@([pscustomobject]@{Name=$contract.itemName;Type='HmiButton';Properties=$props})}
    Add-Event 'get-hmi-screen-json' @{'hmi-name'=$contract.hmiName;'screen-path'=$contract.screenPath;'item-names'=@($contract.itemName);properties=$contract.properties;'working-path'=$folder} ([pscustomobject]@{HmiName=$contract.hmiName;ScreenPath=$contract.screenPath;BaseHash=$Hash;Screen=$screen})
}
function Add-Main {
    param([string]$Phase)
    $path=Join-Path $testRoot ('artifacts/Main-'+$Phase+'.xml');[IO.File]::WriteAllText($path,$main,$utf8)
    Add-Event 'export-block' @{'plc-name'=$contract.plcName;'block-name'='Main';'export-path'=$path} @{}
}
function New-TagEvidence {
    param([string]$Before,[string]$After)
    $b=[pscustomobject]@{TagTable=$contract.tagTable;Name=$contract.primary.name;DataType=$contract.primary.type;LogicalAddress=$contract.primary.address;Comment=$Before}
    $a=$b | ConvertTo-Json | ConvertFrom-Json;$a.Comment=$After
    return [pscustomobject]@{DryRun=$false;Summary=[pscustomobject]@{Applied=1;Failed=0};Operations=@([pscustomobject]@{Status='Applied';Before=$b;After=$a})}
}
function New-StepOutcome {
    param([object]$Step,[object]$Data,[bool]$Success=$true)
    return [pscustomobject]@{Id=$Step.Id;Command=$Step.Command;Success=$Success;TransportSuccess=$true;Data=$Data}
}
$clean=[pscustomobject]@{success=$true;errorCount=0;warningCount=0}
Add-Event 'connect-instance' @{'instance-id'=1234} @{}
Add-Event 'get-project-info' @{} ([pscustomobject]@{Path=$projectPath})
Add-State 'baseline' $contract.baselineComment;Add-Main 'before';Add-Event 'compile-software' @{'plc-name'=$contract.plcName} $clean
$staleStep=New-TagStep 'stale' $contract.finalComment;Add-Preview $staleStep
$stale=Add-Plan 'stale' @($staleStep) '1'
Add-Event 'close-project' @{confirm=$true;save=$false} @{}
Add-Event 'apply-blueprint' @{'apply-token'=$stale.ApplyToken;confirm=$true} $null $false 'State changed; token stale.'
Add-Event 'open-project' @{'project-path'=$projectPath} @{}
Add-State 'after-stale' $contract.baselineComment
$prefix=New-TagStep 'prefix' $contract.prefixComment;$blocked=New-HmiStep ('0'*64);$suffix=New-TagStep 'suffix' $contract.sentinelComment $contract.sentinel
Add-Preview $prefix;Add-Preview $blocked -Blocked
$failed=Add-Plan 'failed' @($prefix,$blocked,$suffix) '2'
$partial=[pscustomobject]@{PlanId=$failed.PlanId;Applied=$false;StateMatched=$true;Steps=@((New-StepOutcome $prefix (New-TagEvidence $contract.baselineComment $contract.prefixComment)),(New-StepOutcome $blocked ([pscustomobject]@{SafetyBlocked=$true;Applied=$false;DryRun=$false}) $false))}
Add-Event 'apply-blueprint' @{'apply-token'=$failed.ApplyToken;confirm=$true} $partial $false
Add-State 'after-failure' $contract.prefixComment
Add-Event 'apply-blueprint' @{'apply-token'=$failed.ApplyToken;confirm=$true} $null $false 'Token already consumed.'
$finalTag=New-TagStep 'final-tag' $contract.finalComment;$finalHmi=New-HmiStep 'baseline-hash'
Add-Preview $finalTag;Add-Preview $finalHmi
$recovery=Add-Plan 'recovery' @($finalTag,$finalHmi) '3'
$success=[pscustomobject]@{PlanId=$recovery.PlanId;Applied=$true;StateMatched=$true;Steps=@((New-StepOutcome $finalTag (New-TagEvidence $contract.prefixComment $contract.finalComment)),(New-StepOutcome $finalHmi ([pscustomobject]@{Applied=$true;SafetyBlocked=$false;DryRun=$false})))}
Add-Event 'apply-blueprint' @{'apply-token'=$recovery.ApplyToken;confirm=$true} $success
Add-State 'readback' $contract.finalComment $contract.label 'final-hash'
Add-Event 'apply-blueprint' @{'apply-token'=$recovery.ApplyToken;confirm=$true} $null $false 'Token already consumed.'
Add-Main 'after';Add-Event 'compile-software' @{'plc-name'=$contract.plcName} $clean
[void](Test-BenchmarkBlueprintLifecycle -ToolEvents $events.ToArray() -Metadata $metadata -WorkspaceRoot $testRoot -Assertion $assertion)
$rejected=0
foreach($scenario in @('model-text','missing-validation','wrong-hash','wrong-token','wrong-plan','continued-suffix','false-whole-success','missing-step-data','transport-false','missing-stale','wrong-reopen','save-on-close','foreign-instance','foreign-project','direct-apply','missing-prefix-preview','missing-recovery-preview','blocked-recovery-preview','missing-failed-replay','successful-replay','wrong-read-filter','wrong-preview-filter','wrong-tag-comment','wrong-tag-address','wrong-compile','missing-main-export','shell-proof','proof-write','missing-confirm','string-state-matched','string-applied','unblocked-failure','applied-failure','dry-run-recovery','wrong-hmi-label','wrong-hmi-geometry','unchanged-final-hash','continue-on-error','embedded-confirm','allow-stale-base','foreign-artifact','foreign-preview-root','unsafe-preview-default','extra-wrapper-key','changed-preview-label','no-preview-backup')){
    $copy=$events.ToArray() | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $plans=@($copy | Where-Object {$_.part.tool -eq 'opentia_plan-blueprint'})
    $validations=@($copy | Where-Object {$_.part.tool -eq 'opentia_validate-blueprint'})
    $applies=@($copy | Where-Object {$_.part.tool -eq 'opentia_apply-blueprint'})
    $previews=@($copy | Where-Object {$_.part.tool -in @('opentia_modify-tags','opentia_apply-hmi-screen-json-patch')})
    switch($scenario){
        'model-text'{$applies[1].part.tool='write'}
        'missing-validation'{$validations[1].part.tool='read'}
        'wrong-hash'{$plans[1].part.state.output.data.BlueprintHash='0'*64}
        'wrong-token'{$applies[1].part.state.input.'apply-token'='other'}
        'wrong-plan'{$applies[1].part.state.output.data.PlanId='0'*32}
        'continued-suffix'{$applies[1].part.state.output.data.Steps+=New-StepOutcome $suffix @{} }
        'false-whole-success'{$applies[1].part.state.output.data.Applied=$true}
        'missing-step-data'{$applies[1].part.state.output.data.Steps[1].Data=$null}
        'transport-false'{$applies[1].part.state.output.data.Steps[1].TransportSuccess=$false}
        'missing-stale'{$applies[0].part.state.output.message='Rejected for another reason.'}
        'wrong-reopen'{@($copy | Where-Object {$_.part.tool -eq 'opentia_open-project'})[0].part.state.input.'project-path'='C:/foreign/OTT_Benchmark.ap21'}
        'save-on-close'{@($copy | Where-Object {$_.part.tool -eq 'opentia_close-project'})[0].part.state.input.save=$true}
        'foreign-instance'{$copy[0].part.state.input.'instance-id'=5678}
        'foreign-project'{$copy[1].part.state.output.data.Path='C:/foreign/OTT_Benchmark.ap21'}
        'direct-apply'{$previews[0].part.state.input.'dry-run'=$false}
        'missing-prefix-preview'{$previews[1].part.tool='read'}
        'missing-recovery-preview'{$previews[3].part.tool='read'}
        'blocked-recovery-preview'{$previews[4].part.state.output.data.SafetyBlocked=$true}
        'missing-failed-replay'{$applies[2].part.tool='read'}
        'successful-replay'{$applies[4].part.state.output.success=$true}
        'wrong-read-filter'{@($copy | Where-Object {$_.part.tool -eq 'opentia_get-hmi-screen-json'})[-1].part.state.input.properties=@('Text')}
        'wrong-preview-filter'{$previews[4].part.state.input.properties=@('Text')}
        'wrong-tag-comment'{$applies[3].part.state.output.data.Steps[0].Data.Operations[0].After.Comment='wrong'}
        'wrong-tag-address'{$applies[3].part.state.output.data.Steps[0].Data.Operations[0].After.LogicalAddress='%M0.0'}
        'wrong-compile'{@($copy | Where-Object {$_.part.tool -eq 'opentia_compile-software'})[-1].part.state.output.data.warningCount=1}
        'missing-main-export'{@($copy | Where-Object {$_.part.tool -eq 'opentia_export-block'})[-1].part.tool='read'}
        'shell-proof'{$copy+=[pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool='bash';state=[pscustomobject]@{input=@{};status='completed';output='claimed proof'}}}}
        'proof-write'{$copy+=[pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool='write';state=[pscustomobject]@{input=[pscustomobject]@{filePath=(Join-Path $testRoot 'artifacts/readback/tags.csv')};status='completed';output='claimed proof'}}}}
        'missing-confirm'{$applies[3].part.state.input.PSObject.Properties.Remove('confirm')}
        'string-state-matched'{$applies[3].part.state.output.data.StateMatched='true'}
        'string-applied'{$applies[3].part.state.output.data.Applied='true'}
        'unblocked-failure'{$applies[1].part.state.output.data.Steps[1].Data.SafetyBlocked=$false}
        'applied-failure'{$applies[1].part.state.output.data.Steps[1].Data.Applied=$true}
        'dry-run-recovery'{$applies[3].part.state.output.data.Steps[0].Data.DryRun=$true}
        'wrong-hmi-label'{@($copy | Where-Object {$_.part.tool -eq 'opentia_get-hmi-screen-json'})[-1].part.state.output.data.Screen.Items[0].Properties.Text='Other'}
        'wrong-hmi-geometry'{@($copy | Where-Object {$_.part.tool -eq 'opentia_get-hmi-screen-json'})[-1].part.state.output.data.Screen.Items[0].Properties.Left='41'}
        'unchanged-final-hash'{@($copy | Where-Object {$_.part.tool -eq 'opentia_get-hmi-screen-json'})[-1].part.state.output.data.BaseHash='baseline-hash'}
        'continue-on-error'{$validations[1].part.state.output.data.Steps[1].ContinueOnError=$true;$plans[1].part.state.output.data.BlueprintHash=Get-BenchmarkBlueprintHash $validations[1].part.state.output.data}
        'embedded-confirm'{$validations[1].part.state.output.data.Steps[0].Arguments | Add-Member -NotePropertyName confirm -NotePropertyValue $true;$plans[1].part.state.output.data.BlueprintHash=Get-BenchmarkBlueprintHash $validations[1].part.state.output.data}
        'allow-stale-base'{$validations[1].part.state.output.data.Steps[1].Arguments | Add-Member -NotePropertyName 'allow-stale-base' -NotePropertyValue $true;$plans[1].part.state.output.data.BlueprintHash=Get-BenchmarkBlueprintHash $validations[1].part.state.output.data}
        'foreign-artifact'{@($copy | Where-Object {$_.part.tool -eq 'opentia_export-tags'})[-1].part.state.input.'output-path'='C:/foreign/tags.csv'}
        'foreign-preview-root'{$previews[4].part.state.input | Add-Member -NotePropertyName 'working-path' -NotePropertyValue 'C:/foreign'}
        'unsafe-preview-default'{$previews[4].part.state.input | Add-Member -NotePropertyName 'fail-on-warning' -NotePropertyValue $false}
        'extra-wrapper-key'{$previews[4].part.state.input.operations=[pscustomobject]@{operations=$previews[4].part.state.input.operations;extra='unexpected'}}
        'changed-preview-label'{$previews[4].part.state.input.operations[0].value='Other'}
        'no-preview-backup'{$previews[4].part.state.input.backup=$false}
    }
    try{[void](Test-BenchmarkBlueprintLifecycle $copy $metadata $testRoot $assertion)}catch{$rejected++;continue}
    throw "Invalid lifecycle accepted: $scenario"
}
$textCopy=$events.ToArray() | ConvertTo-Json -Depth 100 | ConvertFrom-Json
foreach($event in $textCopy){$event.part.state.output=$event.part.state.output | ConvertTo-Json -Depth 100 -Compress}
[void](Test-BenchmarkBlueprintLifecycle $textCopy $metadata $testRoot $assertion)
$nativeFailures=$textCopy | ConvertTo-Json -Depth 100 | ConvertFrom-Json
foreach($event in $nativeFailures){
    $envelope=$event.part.state.output | ConvertFrom-Json
    if(-not $envelope.success){$event.part.state.status='error';$event.part.state | Add-Member -NotePropertyName error -NotePropertyValue $event.part.state.output;$event.part.state.PSObject.Properties.Remove('output')}
}
[void](Test-BenchmarkBlueprintLifecycle $nativeFailures $metadata $testRoot $assertion)
$listed=$nativeFailures | ConvertTo-Json -Depth 100 | ConvertFrom-Json
$listed[1].part.tool='opentia_list-projects'
$listed[1].part.state.output=([pscustomobject]@{success=$true;message='listed';data=@([pscustomobject]@{Path=$projectPath})} | ConvertTo-Json -Depth 100 -Compress)
[void](Test-BenchmarkBlueprintLifecycle $listed $metadata $testRoot $assertion)
$equivalentPreview=$listed | ConvertTo-Json -Depth 100 | ConvertFrom-Json
foreach($preview in @($equivalentPreview | Where-Object {$_.part.tool -eq 'opentia_apply-hmi-screen-json-patch'})){
    $preview.part.state.input.operations=[pscustomobject]@{operations=@([pscustomobject]@{op='set-properties';itemName=$contract.itemName;properties=[pscustomobject]@{Text=$contract.label}})}
    $preview.part.state.input.PSObject.Properties.Remove('backup')
    $preview.part.state.input | Add-Member -NotePropertyName 'working-path' -NotePropertyValue (Join-Path $testRoot 'artifacts/preview')
    $preview.part.state.input | Add-Member -NotePropertyName 'fail-on-warning' -NotePropertyValue $true
    $preview.part.state.input.properties=@($contract.properties | Sort-Object -Descending)
}
[void](Test-BenchmarkBlueprintLifecycle $equivalentPreview $metadata $testRoot $assertion)
$invalidError=$nativeFailures | ConvertTo-Json -Depth 100 | ConvertFrom-Json
$rejectedError=@($invalidError | Where-Object {$_.part.state.status -eq 'error'})[1]
$rejectedError.part.state.error='Model claim: the blueprint failed safely.'
try{[void](Test-BenchmarkBlueprintLifecycle $invalidError $metadata $testRoot $assertion)}catch{$rejected++}
# Equivalent supported scalar operations must not be rejected as a syntax preference.
foreach($style in @('itemName','set-properties')){
    $equivalent=$events.ToArray() | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    foreach($event in @($equivalent | Where-Object {$_.part.tool -eq 'opentia_validate-blueprint'})){
        foreach($step in $event.part.state.output.data.Steps){
            if($step.Command -eq 'apply-hmi-screen-json-patch'){
                $step.Arguments.operations=@($(if($style -eq 'itemName'){[pscustomobject]@{op='set-property';itemName=$contract.itemName;propertyName='Text';value=$contract.label}}else{[pscustomobject]@{op='set-properties';item=$contract.itemName;properties=[pscustomobject]@{Text=$contract.label}}}))
            }
        }
        $path=$event.part.state.input.'blueprint-path'
        @($equivalent | Where-Object {$_.part.tool -eq 'opentia_plan-blueprint' -and $_.part.state.input.'blueprint-path' -eq $path})[0].part.state.output.data.BlueprintHash=Get-BenchmarkBlueprintHash $event.part.state.output.data
    }
    foreach($preview in @($equivalent | Where-Object {$_.part.tool -eq 'opentia_apply-hmi-screen-json-patch'})){
        $preview.part.state.input.operations=@($(if($style -eq 'itemName'){[pscustomobject]@{op='set-property';itemName=$contract.itemName;propertyName='Text';value=$contract.label}}else{[pscustomobject]@{op='set-properties';item=$contract.itemName;properties=[pscustomobject]@{Text=$contract.label}}}))
    }
    [void](Test-BenchmarkBlueprintLifecycle $equivalent $metadata $testRoot $assertion)
}
# Complementary incomplete calls must never be aggregated into a complete apply.
$split=$events.ToArray() | ConvertTo-Json -Depth 100 | ConvertFrom-Json
$target=@($split | Where-Object {$_.part.tool -eq 'opentia_apply-blueprint'})[3]
$other=$target | ConvertTo-Json -Depth 100 | ConvertFrom-Json
$target.part.state.output.data.Steps[0].Success=$false;$other.part.state.output.data.Steps[1].Success=$false
$split+=$other
try{[void](Test-BenchmarkBlueprintLifecycle $split $metadata $testRoot $assertion)}catch{$rejected++}
if($rejected -ne 48){throw 'Split evidence was accepted or adversarial count drifted.'}
$savedContract=[IO.File]::ReadAllText($contractPath)
[IO.File]::AppendAllText($contractPath,' ')
try{[void](Test-BenchmarkBlueprintLifecycle $events.ToArray() $metadata $testRoot $assertion)}catch{$rejected++}
[IO.File]::WriteAllText($contractPath,$savedContract,$utf8)
$tagPath=Join-Path $testRoot 'artifacts/readback/tags.csv';$savedTags=[IO.File]::ReadAllText($tagPath)
[IO.File]::WriteAllText($tagPath,$savedTags.Replace('%MD108','%MD109'),$utf8)
try{[void](Test-BenchmarkBlueprintLifecycle $events.ToArray() $metadata $testRoot $assertion)}catch{$rejected++}
[IO.File]::WriteAllText($tagPath,$savedTags,$utf8)
if($rejected -ne 50){throw 'Modified contract or unrelated tag artifact was accepted.'}
# Invoke the actual scorer entry point with authentic envelopes, not just the helper.
$case=[pscustomobject]@{id='ott-99';fixtureId='synthetic';passScore=100;assertions=@([pscustomobject]@{id='lifecycle';type='blueprintLifecycle';points=100;critical=$true;relativePath=$assertion.relativePath;contractSha256=$assertion.contractSha256})}
$scorerMetadata=[pscustomobject]@{model='synthetic/model';variant=$null;openCodeVersion='1.18.10';gitCommit='0000000';gitBranch='test';startedAtUtc='2026-10-04T00:00:00Z';finishedAtUtc='2026-10-04T00:10:00Z';durationSeconds=600;exitCode=0;timedOut=$false;workspacePath=$testRoot;fixtureRestoreRoot=$metadata.fixtureRestoreRoot;tiaInstanceId=1234}
$stop=[pscustomobject]@{type='step_finish';part=[pscustomobject]@{reason='stop';cost=0;tokens=[pscustomobject]@{total=0;input=0;output=0;reasoning=0}}}
foreach($pair in @(@('case.json',$case),@('metadata.json',$scorerMetadata))){[IO.File]::WriteAllText((Join-Path $testRoot $pair[0]),($pair[1] | ConvertTo-Json -Depth 100),$utf8)}
foreach($negative in @($false,$true)){
    $scorerEvents=$textCopy | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    if($negative){$scorerEvents[0].part.state.input.'instance-id'=5678}
    [IO.File]::WriteAllLines((Join-Path $testRoot 'trace.jsonl'),[string[]]@(@($scorerEvents)+$stop | ForEach-Object {$_ | ConvertTo-Json -Depth 100 -Compress}),$utf8)
    $result=& (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath (Join-Path $testRoot 'case.json') -TracePath (Join-Path $testRoot 'trace.jsonl') -MetadataPath (Join-Path $testRoot 'metadata.json') -OutputPath (Join-Path $testRoot 'result.json')
    if($result.passed -eq $negative -or $result.score -ne $(if($negative){0}else{100})){throw 'Full scorer integration disagrees.'}
}
if($GoldRoot){
    $gold=Get-Content -LiteralPath (Join-Path $GoldRoot 'mcp.jsonl') | ConvertFrom-Json
    # Replay adapter: preserve authentic envelopes but relocate their evidence paths.
    $goldWorkspace=Join-Path $testRoot 'gold';New-Item -ItemType Directory -Path (Join-Path $goldWorkspace 'artifacts') -Force | Out-Null
    $goldContract=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../cases/ott-36/acceptance.json') -Raw
    New-Item -ItemType Directory -Path (Join-Path $goldWorkspace 'inputs') | Out-Null
    [IO.File]::WriteAllText((Join-Path $goldWorkspace 'inputs/acceptance.json'),$goldContract,$utf8)
    foreach($phase in @('baseline','after-stale','after-failure','readback')){New-Item -ItemType Directory -Path (Join-Path $goldWorkspace ('artifacts/'+$phase)) | Out-Null;Copy-Item -LiteralPath (Join-Path $GoldRoot ($phase+'/tags.csv')) -Destination (Join-Path $goldWorkspace ('artifacts/'+$phase+'/tags.csv'))}
    foreach($phase in @('before','after')){Copy-Item -LiteralPath (Join-Path $GoldRoot ('Main-'+$phase+'.xml')) -Destination (Join-Path $goldWorkspace ('artifacts/Main-'+$phase+'.xml'))}
    $goldEvents=[Collections.Generic.List[object]]::new()
    $goldEvents.Add([pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool='opentia_connect-instance';state=[pscustomobject]@{status='completed';input=@{'instance-id'=1234};output=[pscustomobject]@{success=$true;data=@{}}}}})
    foreach($line in $gold){
        if($line.request.method -ne 'tools/call' -or $line.request.params.name -in @('open-tia-portal','retrieve-project','close-tia-portal')){continue}
        if($line.request.params.name -eq 'close-project' -and $line.request.id -gt @($gold | Where-Object {$_.request.method -eq 'tools/call' -and $_.request.params.name -eq 'apply-blueprint'})[-1].request.id){continue}
        $event=[pscustomobject]@{type='tool_use';part=[pscustomobject]@{tool=('opentia_'+$line.request.params.name);state=[pscustomobject]@{status='completed';input=$line.request.params.arguments;output=$line.response.result.structuredContent}}}
        $json=$event | ConvertTo-Json -Depth 100 -Compress
        foreach($phase in @('baseline','after-stale','after-failure','readback')){$json=$json.Replace((Join-Path $GoldRoot $phase).Replace('\','\\'),(Join-Path $goldWorkspace ('artifacts/'+$phase)).Replace('\','\\'))}
        foreach($phase in @('before','after')){$json=$json.Replace((Join-Path $GoldRoot ('Main-'+$phase+'.xml')).Replace('\','\\'),(Join-Path $goldWorkspace ('artifacts/Main-'+$phase+'.xml')).Replace('\','\\'))}
        foreach($name in @('OTT36_Stale','OTT36_StopOnFailure','OTT36_Recover')){$json=$json.Replace((Join-Path $GoldRoot ($name+'.json')).Replace('\','\\'),(Join-Path $goldWorkspace ('artifacts/blueprints/'+$name+'.json')).Replace('\','\\'))}
        $goldEvents.Add(($json | ConvertFrom-Json))
    }
    $actualProject=@($goldEvents | Where-Object {$_.part.tool -eq 'opentia_get-project-info'})[0].part.state.output.data.Path
    $goldMetadata=[pscustomobject]@{fixtureRestoreRoot=(Split-Path -Parent (Split-Path -Parent $actualProject));tiaInstanceId=1234}
    $goldAssertion=[pscustomobject]@{relativePath='inputs/acceptance.json';contractSha256=(Get-FileHash -LiteralPath (Join-Path $goldWorkspace 'inputs/acceptance.json')).Hash}
    [void](Test-BenchmarkBlueprintLifecycle $goldEvents.ToArray() $goldMetadata $goldWorkspace $goldAssertion)
    Write-Host 'PASS: independent live-gold replay (adapted paths; not a model trial).'
}
Write-Host "PASS: object/text lifecycle positives, full scorer positive/negative, $rejected adversarial controls. Evidence: $testRoot"
