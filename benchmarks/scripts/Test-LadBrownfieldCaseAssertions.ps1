[CmdletBinding()]
param([string]$GoldWorkspace)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'LadTestFixtures.ps1')
$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $benchmarkRoot 'fixtures/builders/ott-34'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaLadCaseTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

function New-LadCaseEvent {
    param([string]$Name, [object]$InputObject, [object]$Output)
    if ($Output -isnot [string]) { $Output = $Output | ConvertTo-Json -Depth 40 -Compress }
    return @{ type='tool_use'; part=@{ tool=$Name; state=@{ status='completed'; input=$InputObject; output=$Output } } }
}
function New-LadCaseEvents {
    param([string]$Variant)
    $events = [Collections.Generic.List[object]]::new()
    $events.Add((New-LadCaseEvent 'skill' @{name='s7dcl-lad-authoring'} 'loaded'))
    $events.Add((New-LadCaseEvent 'opentia_list-instances' @{} 'one instance'))
    $events.Add((New-LadCaseEvent 'opentia_connect-instance' @{'instance-id'=1234} 'connected'))
    $events.Add((New-LadCaseEvent 'opentia_get-project-info' @{} 'OTT_Benchmark'))
    $events.Add((New-LadCaseEvent 'opentia_open-plc' @{'plc-name'='PLC_Main'} 'opened'))
    $events.Add((New-LadCaseEvent 'opentia_discover-plc-code-capabilities' @{'plc-name'='PLC_Main';'object-name'='OTT34_Conveyor';operation='Modify';'requested-format'='S7DCL'} @{Exists=$true;ExactIdentityResolved=$true;SelectedRoute='DirectDocuments';ProgrammingLanguage='LAD'}))
    foreach ($name in @('OTT34_Conveyor','OTT34_Conveyor_Backup','Main')) { $events.Add((New-LadCaseEvent 'opentia_export-block-as-docs' @{'plc-name'='PLC_Main';'block-name'=$name;'export-path'='C:/Bench/artifacts/baseline'} 'Successfully exported block as SIMATIC SD documents')) }
    if ($Variant -ne 'no-validation') {
        $events.Add((New-LadCaseEvent 'opentia_validate-plc-documents' @{'file-name'='OTT34_Conveyor'} @{Valid=$true;HasUtf8Bom=($Variant -ne 'candidate-no-bom');ResourceRequired=$true;Classification='verified';Errors=@()}))
        $events.Add((New-LadCaseEvent 'opentia_validate-plc-code-artifact' @{'code-format'='s7dcl'} @{Valid=$true;Format='S7DCL-LAD';Classification='verified';Errors=@()}))
    }
    $events.Add((New-LadCaseEvent 'opentia_begin-plc-code-transaction' @{'plc-name'='PLC_Main';'compile-policy'='CleanCompile';'maximum-new-warnings'=0} @{Target='OTT34_Conveyor';CapabilityRoute='DirectDocuments'}))
    $events.Add((New-LadCaseEvent 'opentia_add-plc-code-transaction-operation' @{
        'file-name'='OTT34_Conveyor';'target-mode'=$(if ($Variant -eq 'unguarded') {'create-only'} else {'baseline-bound'})
        'allowed-changes'=$(if ($Variant -eq 'widened-contract') {'Interface,Implementation'} else {'Implementation'});confirm=$true
    } @{TargetMode='BaselineBound';ChangedSections=@('Implementation')}))
    if ($Variant -ne 'no-plan') { $events.Add((New-LadCaseEvent 'opentia_plan-plc-code-transaction' @{} @{State='Planned';Status='Safe';ChangeKind='BodyOnly';ApplyToken='one-use'})) }
    $apply = New-LadCaseEvent 'opentia_apply-plc-code-transaction' @{confirm=$true} @{
        State=$(if ($Variant -eq 'rolled-back') {'RolledBackAndVerified'} else {'Succeeded'})
        Applied=($Variant -ne 'rolled-back');Verified=($Variant -ne 'rolled-back');RollbackAttempted=($Variant -eq 'rolled-back');RecoveryRequired=$false
        FinalCompilation=[ordered]@{Success=$true;ErrorCount=0;WarningCount=0};Target=[ordered]@{Name='OTT34_Conveyor';CanonicalResultHash=('a'*64)}
    }
    $events.Add($apply)
    if ($Variant -eq 'duplicate-apply') { $events.Add($apply) }
    foreach ($name in @('OTT34_Conveyor','OTT34_Conveyor_Backup','Main')) { $events.Add((New-LadCaseEvent 'opentia_export-block-as-docs' @{'plc-name'='PLC_Main';'block-name'=$name;'export-path'='C:/Bench/artifacts/readback'} 'Successfully exported block as SIMATIC SD documents')) }
    $events.Add((New-LadCaseEvent 'opentia_export-block' @{'plc-name'='PLC_Main';'block-name'='OTT34_Conveyor';'export-path'='C:/Bench/artifacts/readback/OTT34_Conveyor.xml'} 'Successfully exported block: OTT34_Conveyor'))
    $events.Add((New-LadCaseEvent 'opentia_compile-software' @{'plc-name'='PLC_Main'} @{message='Compilation successful';errorCount=0;warningCount=$(if ($Variant -eq 'compile-warning') {1} else {0})}))
    if ($Variant -eq 'save') { $events.Add((New-LadCaseEvent 'opentia_save-project' @{confirm=$true} 'saved')) }
    if ($Variant -eq 'raw-import') { $events.Add((New-LadCaseEvent 'opentia_import-block-from-docs' @{confirm=$true} 'imported')) }
    if ($Variant -eq 'unrelated-create') { $events.Add((New-LadCaseEvent 'opentia_create-device' @{confirm=$true} 'created')) }
    if ($Variant -eq 'hardware-apply') { $events.Add((New-LadCaseEvent 'opentia_apply-hardware-patch' @{confirm=$true} 'applied')) }
    if ($Variant -eq 'unrelated-write') { $events.Add((New-LadCaseEvent 'opentia_set-gsd-parameter-value' @{confirm=$true} 'changed')) }
    $extraMutators=@{
        'extra-delete-operation'='add-delete-plc-type-transaction-operation';'add-device'='add-device'
        'multiuser-publish'='add-project-to-multiuser-server';'connect-vci'='connect-vci-block'
        'disconnect'='disconnect-portal';'open-library'='open-global-library'
        'hmi-probe'='probe-hmi-faceplate-interface';'behavior-probe'='run-plc-code-behavior-probe'
    }
    if($extraMutators.ContainsKey($Variant)){ $events.Add((New-LadCaseEvent ('opentia_'+$extraMutators[$Variant]) @{confirm=$true} 'changed')) }
    return @($events)
}
function New-LadCaseXml {
    param([string]$BooleanVariant='', [string]$TimerVariant='')
    $b = [xml](New-TestBooleanXml $BooleanVariant)
    $t = [xml](New-TestTimerXml $TimerVariant)
    $unitPath = '/Document/SW.Blocks.FB/ObjectList/SW.Blocks.CompileUnit'
    $header = $t.SelectSingleNode('/Document/SW.Blocks.FB/AttributeList').OuterXml
    return '<Document><SW.Blocks.FB>' + $header + '<ObjectList><SW.Blocks.CompileUnit />' +
        $b.SelectSingleNode($unitPath).OuterXml + $t.SelectSingleNode($unitPath).OuterXml +
        '<SW.Blocks.CompileUnit /><SW.Blocks.CompileUnit /></ObjectList></SW.Blocks.FB></Document>'
}
function Invoke-LadCaseTest {
    param([string]$Label, [string]$ExpectedFailure='', [string]$Variant='', [string]$ChangedFile='', [string]$From='', [string]$To='', [switch]$WithoutBom, [switch]$UseGold)
    $workspace = Join-Path $testRoot $Label
    foreach ($phase in @('baseline','readback')) {
        $dir = Join-Path $workspace "artifacts/$phase"
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        if ($UseGold) { Copy-Item -Path (Join-Path (Join-Path $GoldWorkspace $phase) '*') -Destination $dir }
        else {
            foreach ($name in @('OTT34_Conveyor.s7dcl','OTT34_Conveyor.s7res','OTT34_Conveyor_Backup.s7dcl')) { Copy-Item -LiteralPath (Join-Path $fixtureRoot $name) -Destination $dir }
            $main = '{ S7_Optimized := "TRUE"; S7_PreferredLanguage := "LAD"; S7_Version := "0.1" } ORGANIZATION_BLOCK "Main" { S7_Language := "LAD" } NETWORK RUNG wire#powerrail END_RUNG END_NETWORK END_ORGANIZATION_BLOCK'
            [IO.File]::WriteAllText((Join-Path $dir 'Main.s7dcl'), $main, [Text.UTF8Encoding]::new($true))
            if ($phase -eq 'readback') {
                $path = Join-Path $dir 'OTT34_Conveyor.s7dcl'
                $body = Get-Content -LiteralPath $path -Raw
                $body = $body.Replace('RUNG wire#powerrail Contact( #AutoStart ) Coil( #RunRequest ) END_RUNG', 'RUNG wire#powerrail Contact( #AutoStart ) wire#w1 Contact( #Permit ) Contact( #GuardClosed ) I_Contact( #Fault ) I_Contact( #Reset ) Coil( #RunRequest ) END_RUNG RUNG wire#powerrail Contact( #JogRequest ) END_RUNG wire#w1')
                $body = $body.Replace('Contact( #AutoStart )' + "`r`n" + '            { S7_Templates', 'Contact( #RunRequest )' + "`r`n" + '            { S7_Templates')
                $body = $body.Replace('Contact( #AutoStart )' + "`n" + '            { S7_Templates', 'Contact( #RunRequest )' + "`n" + '            { S7_Templates').Replace('T#100ms','T#750ms')
                [IO.File]::WriteAllText($path, $body, [Text.UTF8Encoding]::new($true))
                $xml = New-LadCaseXml -BooleanVariant $(if ($Variant -eq 'jog-bypass') {'jog-bypasses-interlocks'} else {''}) -TimerVariant $(if ($Variant -eq 'q-bypass') {'q-bypass'} else {''})
                [IO.File]::WriteAllText((Join-Path $dir 'OTT34_Conveyor.xml'), $xml, [Text.UTF8Encoding]::new($true))
            }
        }
    }
    if ($ChangedFile) {
        $path = Join-Path (Join-Path $workspace 'artifacts') $ChangedFile
        $content = Get-Content -LiteralPath $path -Raw
        if ($From.Length -gt 0) { $content = $content.Replace($From,$To) }
        [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new(-not $WithoutBom))
    }
    $trace = Join-Path $testRoot "$Label.jsonl"; $metadata = Join-Path $testRoot "$Label-metadata.json"
    @(New-LadCaseEvents $Variant) + @(
        @{type='text';part=@{text='OTT34_Conveyor changed via DirectDocuments; Succeeded, not saved. Runtime not tested.'}},
        @{type='step_finish';part=@{reason='stop';tokens=@{input=5;output=5;reasoning=0;total=10};cost=0}}
    ) | ForEach-Object { $_ | ConvertTo-Json -Depth 40 -Compress } | Set-Content -LiteralPath $trace -Encoding UTF8
    @{model='synthetic/model';trialIndex=1;reasoningEffort='high';variant='high';openCodeVersion='1.18.10';gitCommit='0000000';gitBranch='test';gitDirty=$false;gitStateSha256=('0'*64);fixtureSha256='952FC2C686BFA2C40D0D339340D8E6DDDEABF9B14E24367BF4BAAE796671EACC';startedAtUtc='2026-10-01T00:00:00Z';finishedAtUtc='2026-10-01T00:00:01Z';durationSeconds=1;exitCode=0;timedOut=$false;workspacePath=$workspace} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadata -Encoding UTF8
    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath (Join-Path $benchmarkRoot 'cases/ott-34/case.json') -TracePath $trace -MetadataPath $metadata -OutputPath (Join-Path $testRoot "$Label-result.json")
    $failed = @($result.assertions | Where-Object {-not $_.passed} | ForEach-Object {$_.id})
    if (-not $ExpectedFailure) { if (-not $result.passed -or $result.score -ne 100) { throw "Positive '$Label' failed: $($result.score); $($failed -join ', ')" } }
    elseif ($result.passed -or $failed -notcontains $ExpectedFailure) { throw "Negative '$Label' escaped '$ExpectedFailure': $($failed -join ', ')" }
}
try {
    # All current catalog mutations outside the two reviewed PLC transaction
    # primitives must be prohibited. New command exposure fails this test closed.
    $case=Get-Content (Join-Path $benchmarkRoot 'cases/ott-34/case.json') -Raw | ConvertFrom-Json
    $guard=($case.assertions | Where-Object {$_.id -eq 'no-bypass-save-or-unrelated-mutation'}).namePattern
    $catalog=Get-Content (Join-Path (Split-Path -Parent $benchmarkRoot) 'docs/generated/command-catalog.json') -Raw | ConvertFrom-Json
    $allowedMutators=@('add-plc-code-transaction-operation','apply-plc-code-transaction')
    foreach($command in @($catalog.commands | Where-Object {$_.risk -in @('Mutating','Destructive','Online')})) {
        $blocked=('opentia_'+$command.name.Replace('-','_')) -match $guard
        if(($command.name -in $allowedMutators) -eq $blocked){ throw "Incorrect LAD mutation guard for catalog command '$($command.name)'." }
    }
    Invoke-LadCaseTest 'representative'
    Invoke-LadCaseTest 'jog-bypass' 'all-combinational-input-rows' -Variant 'jog-bypass'
    Invoke-LadCaseTest 'q-bypass' 'nonretentive-timer-wiring' -Variant 'q-bypass'
    Invoke-LadCaseTest 'identity' 'interface-and-unrelated-networks-preserved' -ChangedFile 'readback/OTT34_Conveyor.s7dcl' -From '"58434"' -To '"58436"'
    Invoke-LadCaseTest 'interface' 'interface-and-unrelated-networks-preserved' -ChangedFile 'readback/OTT34_Conveyor.s7dcl' -From 'Permit : Bool' -To 'Permit : Int'
    Invoke-LadCaseTest 'static-state' 'interface-and-unrelated-networks-preserved' -ChangedFile 'readback/OTT34_Conveyor.s7dcl' -From 'RunDelay : TON_TIME' -To 'RunDelay : IEC_TIMER'
    Invoke-LadCaseTest 'unrelated-network' 'interface-and-unrelated-networks-preserved' -ChangedFile 'readback/OTT34_Conveyor.s7dcl' -From 'R_Coil( #RequestSeen )' -To 'S_Coil( #RequestSeen )'
    Invoke-LadCaseTest 'resource-text' 'network-resources-preserved' -ChangedFile 'readback/OTT34_Conveyor.s7res' -From 'Start request' -To 'Start Request'
    Invoke-LadCaseTest 'overwritten-baseline' 'untouched-fb-baseline' -ChangedFile 'baseline/OTT34_Conveyor.s7dcl' -From 'T#100ms' -To 'T#750ms'
    Invoke-LadCaseTest 'missing-bom' 'interface-and-unrelated-networks-preserved' -ChangedFile 'readback/OTT34_Conveyor.s7dcl' -WithoutBom
    Invoke-LadCaseTest 'changed-backup' 'backup-preserved' -ChangedFile 'readback/OTT34_Conveyor_Backup.s7dcl' -From 'I_Contact' -To 'Contact'
    Invoke-LadCaseTest 'changed-main' 'main-preserved' -ChangedFile 'readback/Main.s7dcl' -From 'powerrail' -To 'other'
    foreach ($item in @(@('no-validation','verified-document-pair'),@('unguarded','implementation-only-baseline-bound-operation'),@('widened-contract','implementation-only-baseline-bound-operation'),@('no-plan','single-body-only-safe-plan'),@('rolled-back','single-verified-apply'),@('duplicate-apply','single-verified-apply'),@('compile-warning','fresh-clean-compile'),@('save','no-bypass-save-or-unrelated-mutation'),@('raw-import','no-bypass-save-or-unrelated-mutation'),@('unrelated-create','no-bypass-save-or-unrelated-mutation'),@('hardware-apply','no-bypass-save-or-unrelated-mutation'),@('unrelated-write','no-bypass-save-or-unrelated-mutation'))) { Invoke-LadCaseTest $item[0] $item[1] -Variant $item[0] }
    Invoke-LadCaseTest 'candidate-no-bom' 'verified-document-pair' -Variant 'candidate-no-bom'
    foreach($variant in @('extra-delete-operation','add-device','multiuser-publish','connect-vci','disconnect','open-library','hmi-probe','behavior-probe')) { Invoke-LadCaseTest $variant 'no-bypass-save-or-unrelated-mutation' -Variant $variant }
    if ($GoldWorkspace) { Invoke-LadCaseTest 'independent-live-gold' -UseGold }
    Write-Host 'Brownfield LAD scorer passed (1 synthetic positive, 32 negatives, catalog mutation coverage; optional independent live-gold replay).'
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaLadCaseTests-',[StringComparison]::Ordinal)) { Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue }
}
