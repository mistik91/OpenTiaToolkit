Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'BenchmarkWorkspaceInputs.ps1')

function Get-BenchmarkBlueprintHash {
    param([object]$Blueprint)
    # Use the worker's vendor-free DTO/serializer, including defaults, dictionary
    # order and escaping. Do not approximate its state-bound hash with regex.
    if (-not ('OpenTiaBenchmark.ContractResolver' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
namespace OpenTiaBenchmark {
    public static class ContractResolver {
        private static string root;
        public static void Initialize(string path) {
            root = path;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }
        private static Assembly Resolve(object sender, ResolveEventArgs args) {
            string path = Path.Combine(root, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
    }
}
'@
        $assemblyRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src/OpenTiaToolkit.CLI/bin/Release/net48'
        [OpenTiaBenchmark.ContractResolver]::Initialize($assemblyRoot)
        [void][Reflection.Assembly]::LoadFrom((Join-Path $assemblyRoot 'System.Text.Json.dll'))
        [void][Reflection.Assembly]::LoadFrom((Join-Path $assemblyRoot 'OpenTiaToolkit.Contracts.dll'))
    }
    $options = [OpenTiaToolkit.Contracts.Protocol.ContractJson]::CreateOptions($false)
    $value = [System.Text.Json.JsonSerializer]::Deserialize(
        ($Blueprint | ConvertTo-Json -Depth 100 -Compress), [OpenTiaToolkit.Contracts.Blueprints.EngineeringBlueprint], $options)
    $canonical = [System.Text.Json.JsonSerializer]::Serialize($value, [OpenTiaToolkit.Contracts.Blueprints.EngineeringBlueprint], $options)
    return [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-','').ToLowerInvariant()
}

function Assert-BlueprintBoolean {
    param([object]$Value, [bool]$Expected, [string]$Label)
    if ($Value -isnot [bool] -or $Value -ne $Expected) { throw "$Label is not proven $Expected." }
}

function ConvertTo-BlueprintCanonicalValue {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $ordered = [ordered]@{}
        foreach ($property in @($Value.PSObject.Properties | Sort-Object Name -CaseSensitive)) {
            $ordered[$property.Name] = ConvertTo-BlueprintCanonicalValue $property.Value
        }
        return $ordered
    }
    if ($Value -is [Array]) { return ,@($Value | ForEach-Object { ConvertTo-BlueprintCanonicalValue $_ }) }
    return $Value
}

function Assert-BlueprintEqual {
    param([AllowNull()][object]$Actual, [AllowNull()][object]$Expected, [string]$Label)
    $a = ConvertTo-BlueprintCanonicalValue $Actual | ConvertTo-Json -Depth 100 -Compress
    $b = ConvertTo-BlueprintCanonicalValue $Expected | ConvertTo-Json -Depth 100 -Compress
    if ($a -cne $b) { throw "$Label differs from the exact acceptance state." }
}

function Resolve-BlueprintArtifact {
    param([string]$WorkspaceRoot, [string]$Path, [string]$RelativePath)
    $expected = Resolve-BenchmarkRelativePath -Root $WorkspaceRoot -RelativePath $RelativePath
    if (-not [IO.Path]::IsPathRooted($Path) -or [IO.Path]::GetFullPath($Path) -ine $expected) { throw 'Artifact path is not the exact workspace target.' }
    return $expected
}

function Test-BenchmarkBlueprintLifecycle {
    param([object[]]$ToolEvents, [object]$Metadata, [string]$WorkspaceRoot, [object]$Assertion)
    $contractPath = Resolve-BenchmarkRelativePath -Root $WorkspaceRoot -RelativePath $Assertion.relativePath -WorkspaceInput
    if ((Get-FileHash -LiteralPath $contractPath).Hash -ine $Assertion.contractSha256) { throw 'Acceptance contract was modified.' }
    $contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
    $records = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $ToolEvents.Count; $index++) {
        $event = $ToolEvents[$index]
        $name = [string]$event.part.tool -replace '-', '_'
        if ($name -notmatch '^opentia_') {
            if ($name -in @('write','edit')) {
                $path = [string]$event.part.state.input.filePath
                $relative = 'artifacts/blueprints/' + [IO.Path]::GetFileName($path)
                [void](Resolve-BlueprintArtifact $WorkspaceRoot $path $relative)
            }
            elseif ($name -in @('bash','apply_patch','patch','multiedit')) { throw 'Uncontrolled shell or proof-file mutation is forbidden.' }
            continue
        }
        if ($event.part.state.status -notin @('completed','error')) { continue }
        # OpenCode preserves MCP isError results as JSON in state.error, not output.
        # Only a complete structured failure envelope is eligible; prose/errors
        # without a command outcome never become engineering evidence.
        $envelope = if($event.part.state.status -eq 'error'){$event.part.state.error}else{$event.part.state.output}
        if ($envelope -is [string]) { try { $envelope = $envelope | ConvertFrom-Json } catch { continue } }
        if ($null -eq $envelope -or $null -eq $envelope.PSObject.Properties['success'] -or $envelope.success -isnot [bool]) { continue }
        if($event.part.state.status -eq 'error' -and $envelope.success){continue}
        $records.Add([pscustomobject]@{Index=$index;Name=$name.Substring(8);Input=$event.part.state.input;Envelope=$envelope;Data=$envelope.data})
    }
    # Every write outside the two approved domains is denied. Direct domain calls
    # may only preview; actual mutations must be represented by blueprint outcomes.
    foreach ($r in $records) {
        if ($r.Name -eq 'connect_portal' -or ($r.Name -eq 'connect_instance' -and
            ($null -eq $Metadata.PSObject.Properties['tiaInstanceId'] -or $r.Input.'instance-id' -ne $Metadata.tiaInstanceId))) { throw 'Foreign or implicit TIA selection.' }
        if ($r.Name -match '^(?:save_|archive_|retrieve_|import_|generate_|delete_|set_|copy_|ensure_|commit_|remove_|write_|add_|download_|sync_|begin_|approve_|discard_|start_cpu|stop_cpu|open_tia_portal|close_tia_portal)') { throw 'Out-of-scope engineering mutation.' }
        if ($r.Name -in @('modify_tags','apply_hmi_screen_json_patch')) { Assert-BlueprintBoolean $r.Input.'dry-run' $true 'Standalone domain preview' }
        elseif ($r.Name -match '^(?:apply_|find_replace_)' -and $r.Name -ne 'apply_blueprint') { throw 'Unexpected standalone mutation route.' }
    }
    $plans = [Collections.Generic.List[object]]::new()
    foreach ($r in @($records | Where-Object {$_.Name -eq 'plan_blueprint' -and $_.Envelope.success})) {
        $validation = @($records | Where-Object {$_.Index -lt $r.Index -and $_.Name -eq 'validate_blueprint' -and $_.Envelope.success -and $_.Input.'blueprint-path' -ieq $r.Input.'blueprint-path'} | Select-Object -Last 1)
        if ($validation.Count -ne 1 -or (Get-BenchmarkBlueprintHash $validation[0].Data) -cne $r.Data.BlueprintHash) { continue }
        $blueprint = $validation[0].Data
        [void](Resolve-BlueprintArtifact $WorkspaceRoot $r.Input.'blueprint-path' ('artifacts/blueprints/' + [IO.Path]::GetFileName($r.Input.'blueprint-path')))
        if ($r.Data.PlanId -notmatch '^[a-f0-9]{32}$' -or [string]::IsNullOrWhiteSpace($r.Data.ApplyToken) -or $r.Data.StateFingerprint -notmatch '^[a-f0-9]{64}$') { continue }
        if (@($blueprint.Steps).Count -ne @($r.Data.Steps).Count) { continue }
        for ($i=0; $i -lt @($blueprint.Steps).Count; $i++) {
            $s=$blueprint.Steps[$i]; $p=$r.Data.Steps[$i]
            if ($s.Id -cne $p.Id -or $s.Command -cne $p.Command) { throw 'Planned step identity mismatch.' }
            Assert-BlueprintBoolean $s.ContinueOnError $false 'Stop-on-error policy'
            if ($null -ne $s.Arguments.PSObject.Properties['confirm']) { throw 'Embedded confirmation.' }
        }
        $plans.Add([pscustomobject]@{Record=$r;Blueprint=$blueprint})
    }
    $failures = [Collections.Generic.List[string]]::new()
    foreach ($stale in @($plans | Where-Object {@($_.Blueprint.Steps).Count -eq 1})) {
        try {
            $staleApply = Get-BlueprintApply $records $stale.Record $stale.Record.Index $false
            if ($staleApply.Envelope.message -notmatch 'stale|state changed') { throw 'Stale rejection is missing.' }
            $close = @($records | Where-Object {$_.Name -eq 'close_project' -and $_.Index -gt $stale.Record.Index -and $_.Index -lt $staleApply.Index -and $_.Envelope.success})
            if ($close.Count -ne 1) { throw 'Stale plan lacks a controlled context change.' }
            Assert-BlueprintBoolean $close[0].Input.confirm $true 'Close confirmation'
            Assert-BlueprintBoolean $close[0].Input.save $false 'Unsaved context change'
            $project = @($records | Where-Object {$_.Name -in @('get_project_info','list_projects') -and $_.Index -lt $close[0].Index -and $_.Envelope.success} | ForEach-Object {
                $identityRecord=$_
                foreach($identity in @($identityRecord.Data)){
                    if($null -ne $identity.PSObject.Properties['Path']){[pscustomobject]@{Index=$identityRecord.Index;Data=$identity}}
                }
            } | Select-Object -Last 1)
            if ($project.Count -ne 1 -or $null -eq $Metadata.PSObject.Properties['fixtureRestoreRoot']) { throw 'Harness-selected project identity is unproven.' }
            $restorePrefix=[IO.Path]::GetFullPath($Metadata.fixtureRestoreRoot).TrimEnd([char[]]'\/')+[IO.Path]::DirectorySeparatorChar
            if (-not [IO.Path]::GetFullPath($project[0].Data.Path).StartsWith($restorePrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Project is outside the harness-selected restore.' }
            if (@($records | Where-Object {$_.Name -eq 'connect_instance' -and $_.Envelope.success -and $_.Index -lt $project[0].Index}).Count -eq 0) { throw 'Harness-selected TIA attachment is unproven.' }
            $reopen = @($records | Where-Object {$_.Name -eq 'open_project' -and $_.Index -gt $staleApply.Index -and $_.Envelope.success -and $_.Input.'project-path' -ieq $project[0].Data.Path} | Select-Object -First 1)
            if ($reopen.Count -ne 1) { throw 'Exact disposable project was not reopened.' }
            $base = Get-BlueprintReadback $records $WorkspaceRoot $contract 'baseline' -1 $stale.Record.Index
            $unchanged = Get-BlueprintReadback $records $WorkspaceRoot $contract 'after-stale' $reopen[0].Index ([int]::MaxValue)
            Assert-BlueprintEqual $unchanged.Tags $base.Tags 'Stale tag preservation'
            Assert-BlueprintEqual $unchanged.Screen $base.Screen 'Stale HMI preservation'
            Assert-BlueprintEqual $unchanged.Hash $base.Hash 'Stale HMI hash'
            Assert-BlueprintTagStep $stale.Blueprint.Steps[0] $contract $contract.finalComment $contract.primary
            Assert-BlueprintPreview $records $stale.Blueprint.Steps[0] $base.LastIndex $stale.Record.Index
            foreach ($failed in @($plans | Where-Object {$_.Record.Index -gt $unchanged.LastIndex -and @($_.Blueprint.Steps).Count -eq 3})) {
                try {
                    Assert-BlueprintTagStep $failed.Blueprint.Steps[0] $contract $contract.prefixComment $contract.primary
                    Assert-BlueprintHmiStep $failed.Blueprint.Steps[1] $contract ('0'*64)
                    Assert-BlueprintTagStep $failed.Blueprint.Steps[2] $contract $contract.sentinelComment $contract.sentinel
                    Assert-BlueprintPreview $records $failed.Blueprint.Steps[0] $unchanged.LastIndex $failed.Record.Index
                    Assert-BlueprintPreview $records $failed.Blueprint.Steps[1] $unchanged.LastIndex $failed.Record.Index -Blocked -WorkspaceRoot $WorkspaceRoot
                    $partial = Get-BlueprintApply $records $failed.Record $failed.Record.Index $false
                    Assert-BlueprintApplyState $partial $failed.Record.Data 2 $false
                    Assert-BlueprintBoolean $partial.Data.Steps[0].Success $true 'Successful prefix'
                    Assert-BlueprintBoolean $partial.Data.Steps[1].Success $false 'Blocked step'
                    Assert-BlueprintBoolean $partial.Data.Steps[1].Data.SafetyBlocked $true 'Retained HMI safety block'
                    Assert-BlueprintBoolean $partial.Data.Steps[1].Data.Applied $false 'Blocked HMI apply'
                    Assert-BlueprintBoolean $partial.Data.Steps[1].Data.DryRun $false 'Actual blueprint HMI guard'
                    Assert-BlueprintBoolean $partial.Data.Steps[1].TransportSuccess $true 'Standalone HMI transport semantics'
                    Assert-BlueprintTagEvidence $partial.Data.Steps[0].Data $contract $contract.primary $contract.baselineComment $contract.prefixComment
                    $afterFailure = Get-BlueprintReadback $records $WorkspaceRoot $contract 'after-failure' $partial.Index ([int]::MaxValue)
                    Assert-BlueprintExpectedTags $afterFailure.Tags $contract $contract.prefixComment
                    Assert-BlueprintEqual $afterFailure.Screen $base.Screen 'Failure HMI preservation'
                    Assert-BlueprintEqual $afterFailure.Hash $base.Hash 'Failure HMI hash'
                    [void](Get-BlueprintReplay $records $failed.Record $partial.Index)
                    foreach ($recovery in @($plans | Where-Object {$_.Record.Index -gt $afterFailure.LastIndex -and @($_.Blueprint.Steps).Count -eq 2})) {
                        Assert-BlueprintTagStep $recovery.Blueprint.Steps[0] $contract $contract.finalComment $contract.primary
                        Assert-BlueprintHmiStep $recovery.Blueprint.Steps[1] $contract $afterFailure.Hash
                        foreach ($s in $recovery.Blueprint.Steps) { Assert-BlueprintPreview $records $s $afterFailure.LastIndex $recovery.Record.Index -WorkspaceRoot $WorkspaceRoot }
                        $applied = Get-BlueprintApply $records $recovery.Record $recovery.Record.Index $true
                        Assert-BlueprintApplyState $applied $recovery.Record.Data 2 $true
                        foreach ($s in $applied.Data.Steps) { Assert-BlueprintBoolean $s.Success $true 'Recovery completion'; Assert-BlueprintBoolean $s.TransportSuccess $true 'Recovery transport' }
                        Assert-BlueprintTagEvidence $applied.Data.Steps[0].Data $contract $contract.primary $contract.prefixComment $contract.finalComment
                        Assert-BlueprintBoolean $applied.Data.Steps[1].Data.Applied $true 'Applied HMI label'
                        Assert-BlueprintBoolean $applied.Data.Steps[1].Data.SafetyBlocked $false 'HMI safety'
                        $final = Get-BlueprintReadback $records $WorkspaceRoot $contract 'readback' $applied.Index ([int]::MaxValue)
                        Assert-BlueprintExpectedTags $final.Tags $contract $contract.finalComment
                        $wanted = $base.Screen | ConvertTo-Json -Depth 100 | ConvertFrom-Json
                        @($wanted.Items | Where-Object {$_.Name -ceq $contract.itemName})[0].Properties.Text=$contract.label
                        Assert-BlueprintEqual $final.Screen $wanted 'Final exact focused HMI'
                        if ($final.Hash -ceq $base.Hash) { throw 'Final HMI hash did not change.' }
                        [void](Get-BlueprintReplay $records $recovery.Record $applied.Index)
                        Assert-BlueprintMainPreservation $records $WorkspaceRoot $contract $stale.Record.Index $applied.Index
                        $compiles = @($records | Where-Object {$_.Name -eq 'compile_software' -and $_.Input.'plc-name' -ceq $contract.plcName -and $_.Envelope.success})
                        foreach ($bounds in @(@(-1,$stale.Record.Index),@($final.LastIndex,[int]::MaxValue))) {
                            $clean=@($compiles | Where-Object {$_.Index -gt $bounds[0] -and $_.Index -lt $bounds[1] -and $_.Data.success -eq $true -and $_.Data.errorCount -eq 0 -and $_.Data.warningCount -eq 0})
                            if ($clean.Count -eq 0) { throw 'Clean baseline/final compilation is missing.' }
                        }
                        if (@($records | Where-Object {$_.Name -eq 'close_project' -and $_.Index -ne $close[0].Index}).Count) { throw 'Unexpected project close.' }
                        $allowedTokens=@($stale.Record.Data.ApplyToken,$failed.Record.Data.ApplyToken,$recovery.Record.Data.ApplyToken)
                        $allowedIds=@($stale.Record.Data.PlanId,$failed.Record.Data.PlanId,$recovery.Record.Data.PlanId)
                        if(@($allowedTokens | Select-Object -Unique).Count -ne 3 -or @($allowedIds | Select-Object -Unique).Count -ne 3){throw 'Plan identities/tokens were reused.'}
                        foreach($other in @($records | Where-Object {$_.Name -eq 'apply_blueprint'})){
                            if($other.Input.'apply-token' -cnotin $allowedTokens -and $null -ne $other.Data -and
                                $null -ne $other.Data.PSObject.Properties['Steps'] -and @($other.Data.Steps).Count -gt 0){throw 'An unrelated blueprint executed engineering steps.'}
                        }
                        return 'Authentic stale/partial/recovery chain; exact readback, token replay and preservation proven.'
                    }
                    throw 'No complete recovery plan followed the failed blueprint.'
                } catch { $failures.Add($_.Exception.Message) }
            }
            throw 'No complete stopped blueprint followed stale rejection.'
        } catch { $failures.Add($_.Exception.Message) }
    }
    throw "No complete authentic blueprint lifecycle. $($failures -join '; ')"
}

function Get-BlueprintApply {
    param([object[]]$Records,[object]$Plan,[int]$After,[bool]$Success)
    $matches=@($Records | Where-Object {$_.Name -eq 'apply_blueprint' -and $_.Index -gt $After -and $_.Input.'apply-token' -ceq $Plan.Data.ApplyToken} | Select-Object -First 1)
    if ($matches.Count -ne 1) { throw 'Exact plan token was not applied.' }
    Assert-BlueprintBoolean $matches[0].Input.confirm $true 'Apply confirmation'
    Assert-BlueprintBoolean $matches[0].Envelope.success $Success 'Apply envelope'
    return $matches[0]
}

function Get-BlueprintReplay {
    param([object[]]$Records,[object]$Plan,[int]$After)
    $r=Get-BlueprintApply $Records $Plan $After $false
    if ($r.Envelope.message -notmatch 'consumed|invalid') { throw 'Consumed token replay was not rejected.' }
    return $r
}

function Assert-BlueprintApplyState {
    param([object]$Apply,[object]$Plan,[int]$StepCount,[bool]$Applied)
    if ($Apply.Data.PlanId -cne $Plan.PlanId -or @($Apply.Data.Steps).Count -ne $StepCount) { throw 'Apply plan/step identity mismatch.' }
    Assert-BlueprintBoolean $Apply.Data.Applied $Applied 'Whole blueprint completion'
    Assert-BlueprintBoolean $Apply.Data.StateMatched $true 'Plan context guard'
    for($i=0;$i -lt $StepCount;$i++) { if($Apply.Data.Steps[$i].Id -cne $Plan.Steps[$i].Id -or $Apply.Data.Steps[$i].Command -cne $Plan.Steps[$i].Command){throw 'Executed step identity mismatch.'} }
}

function Assert-BlueprintTagStep {
    param([object]$Step,[object]$Contract,[string]$Comment,[object]$Target)
    if($Step.Command -cne 'modify-tags'){throw 'Expected a narrow tag update.'}
    $a=$Step.Arguments
    Assert-BlueprintBoolean $a.'dry-run' $true 'Planned tag preview flag'
    if($a.'plc-name' -cne $Contract.plcName -or @($a.PSObject.Properties).Count -ne 3 -or @($a.operations).Count -ne 1){throw 'Tag step scope differs.'}
    $wanted=[pscustomobject]@{action='update';match=[pscustomobject]@{tagTable=$Contract.tagTable;name=$Target.name;logicalAddress=$Target.address};set=[pscustomobject]@{comment=$Comment}}
    Assert-BlueprintEqual $a.operations[0] $wanted 'Exact tag operation'
}

function Assert-BlueprintHmiStep {
    param([object]$Step,[object]$Contract,[string]$Hash)
    if($Step.Command -cne 'apply-hmi-screen-json-patch'){throw 'Expected a focused HMI patch.'}
    $a=$Step.Arguments
    Assert-BlueprintBoolean $a.'dry-run' $true 'Planned HMI preview flag'
    Assert-BlueprintBoolean $a.backup $true 'HMI backup'
    $keys=@($a.PSObject.Properties.Name | Sort-Object)
    Assert-BlueprintEqual $keys @('backup','base-hash','dry-run','hmi-name','item-names','operations','properties','screen-path') 'HMI argument surface'
    if($a.'hmi-name' -cne $Contract.hmiName -or $a.'screen-path' -cne $Contract.screenPath -or $a.'base-hash' -cne $Hash){throw 'HMI identity/hash differs.'}
    Assert-BlueprintEqual @($a.'item-names' | ForEach-Object {$_.ToLowerInvariant()} | Sort-Object) @($Contract.itemName.ToLowerInvariant()) 'HMI item filter'
    Assert-BlueprintEqual @($a.properties | Sort-Object) @($Contract.properties | Sort-Object) 'HMI property filters'
    if(@($a.operations).Count -ne 1){throw 'HMI operation count differs.'}
    $op=$a.operations[0]
    $itemName=if($null -ne $op.PSObject.Properties['item']){$op.item}else{$op.itemName}
    if($itemName -cne $Contract.itemName){throw 'HMI operation item differs.'}
    $itemKey=if($null -ne $op.PSObject.Properties['item']){'item'}else{'itemName'}
    if($op.op -ceq 'set-property'){
        $propertyKey=if($null -ne $op.PSObject.Properties['property']){'property'}else{'propertyName'}
        if($op.$propertyKey -cne 'Text' -or $op.value -cne $Contract.label){throw 'HMI scalar intent differs.'}
        Assert-BlueprintEqual @($op.PSObject.Properties.Name | Sort-Object) @(@('op',$itemKey,$propertyKey,'value') | Sort-Object) 'HMI scalar argument surface'
    }elseif($op.op -ceq 'set-properties'){
        Assert-BlueprintEqual $op.properties ([pscustomobject]@{Text=$Contract.label}) 'HMI scalar intent'
        Assert-BlueprintEqual @($op.PSObject.Properties.Name | Sort-Object) @(@('op',$itemKey,'properties') | Sort-Object) 'HMI scalar argument surface'
    }else{throw 'HMI operation is not an equivalent focused scalar edit.'}
}

function Assert-BlueprintTagEvidence {
    param([object]$Data,[object]$Contract,[object]$Target,[string]$Before,[string]$After)
    Assert-BlueprintBoolean $Data.DryRun $false 'Applied tag result'
    if(@($Data.Operations).Count -ne 1 -or $Data.Summary.Applied -ne 1 -or $Data.Summary.Failed -ne 0 -or $Data.Operations[0].Status -cne 'Applied'){throw 'Applied tag evidence is incomplete.'}
    foreach($pair in @(@('Before',$Before),@('After',$After))){
        Assert-BlueprintEqual $Data.Operations[0].($pair[0]) ([pscustomobject]@{TagTable=$Contract.tagTable;Name=$Target.name;DataType=$Target.type;LogicalAddress=$Target.address;Comment=$pair[1]}) 'Exact tag before/after evidence'
    }
}

function ConvertTo-BlueprintHmiPreviewIntent {
    param([object]$Arguments,[string]$WorkspaceRoot)
    $intent=$Arguments | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    # Command defaults and the documented operations wrapper are equivalent
    # intent. Artifact placement is not engineering intent, but must stay scoped.
    if($null -eq $intent.PSObject.Properties['backup']){$intent | Add-Member -NotePropertyName backup -NotePropertyValue $true}
    foreach($pair in @(@('allow-stale-base',$false),@('fail-on-warning',$true),@('require-clean-preflight',$true))){
        if($null -ne $intent.PSObject.Properties[$pair[0]]){
            Assert-BlueprintBoolean $intent.($pair[0]) $pair[1] 'HMI safety default'
            $intent.PSObject.Properties.Remove($pair[0])
        }
    }
    if($null -ne $intent.PSObject.Properties['working-path']){
        $path=[IO.Path]::GetFullPath($intent.'working-path')
        $prefix=[IO.Path]::GetFullPath((Join-Path $WorkspaceRoot 'artifacts')).TrimEnd([char[]]'\/')+[IO.Path]::DirectorySeparatorChar
        if(-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'HMI preview artifact root is outside workspace artifacts.'}
        [void](Resolve-BenchmarkRelativePath -Root $WorkspaceRoot -RelativePath ('artifacts/'+$path.Substring($prefix.Length)))
        $intent.PSObject.Properties.Remove('working-path')
    }
    $operations=$intent.operations
    if($operations -is [Management.Automation.PSCustomObject] -and $null -ne $operations.PSObject.Properties['operations']){
        Assert-BlueprintEqual @($operations.PSObject.Properties.Name) @('operations') 'HMI operations wrapper'
        $operations=$operations.operations
    }
    $normalized=@(foreach($op in @($operations)){
        $itemKey=if($null -ne $op.PSObject.Properties['item']){'item'}else{'itemName'}
        if($op.op -ceq 'set-property'){
            $propertyKey=if($null -ne $op.PSObject.Properties['property']){'property'}else{'propertyName'}
            Assert-BlueprintEqual @($op.PSObject.Properties.Name | Sort-Object) @(@('op',$itemKey,$propertyKey,'value') | Sort-Object) 'HMI preview scalar surface'
            [pscustomobject]@{op='set-property';item=$op.$itemKey;property=$op.$propertyKey;value=$op.value}
        }elseif($op.op -ceq 'set-properties'){
            Assert-BlueprintEqual @($op.PSObject.Properties.Name | Sort-Object) @(@('op',$itemKey,'properties') | Sort-Object) 'HMI preview scalar surface'
            Assert-BlueprintEqual @($op.properties.PSObject.Properties.Name) @('Text') 'HMI preview property surface'
            [pscustomobject]@{op='set-property';item=$op.$itemKey;property='Text';value=$op.properties.Text}
        }else{throw 'HMI preview is not a focused scalar edit.'}
    })
    $intent.operations=$normalized
    $intent.'item-names'=@($intent.'item-names' | ForEach-Object {$_.ToLowerInvariant()} | Sort-Object)
    $intent.properties=@($intent.properties | Sort-Object)
    return $intent
}

function Assert-BlueprintPreview {
    param([object[]]$Records,[object]$Step,[int]$After,[int]$Before,[switch]$Blocked,[string]$WorkspaceRoot)
    $matches=@($Records | Where-Object {$_.Name -eq ($Step.Command -replace '-','_') -and $_.Index -gt $After -and $_.Index -lt $Before -and $_.Envelope.success})
    foreach($r in $matches){
        $inputClone=$r.Input | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        $inputClone.PSObject.Properties.Remove('confirm')
        try {
            $expected=$Step.Arguments
            if($Step.Command -eq 'apply-hmi-screen-json-patch'){
                $inputClone=ConvertTo-BlueprintHmiPreviewIntent $inputClone $WorkspaceRoot
                $expected=ConvertTo-BlueprintHmiPreviewIntent $expected $WorkspaceRoot
            }
            Assert-BlueprintEqual $inputClone $expected 'Matching recovery preview'; Assert-BlueprintBoolean $r.Data.DryRun $true 'Domain dry run'
            if($Step.Command -eq 'modify-tags'){if($r.Data.Summary.Failed -ne 0 -or $r.Data.Summary.Planned -ne 1){throw 'Tag preview failed.'}}
            else{Assert-BlueprintBoolean $r.Data.SafetyBlocked ([bool]$Blocked) 'HMI preview safety'; Assert-BlueprintBoolean $r.Data.SafeToApply (-not $Blocked) 'HMI preview applicability'}
            return
        } catch { continue }
    }
    throw 'Fresh exact recovery domain preview is missing.'
}

function Assert-BlueprintExpectedTags {
    param([object[]]$Tags,[object]$Contract,[string]$PrimaryComment)
    $expected=@([pscustomobject]@{TagTableName=$Contract.tagTable;Name=$Contract.primary.name;DataType=$Contract.primary.type;LogicalAddress=$Contract.primary.address;Comment=$PrimaryComment},
        [pscustomobject]@{TagTableName=$Contract.tagTable;Name=$Contract.sentinel.name;DataType=$Contract.sentinel.type;LogicalAddress=$Contract.sentinel.address;Comment=$Contract.baselineComment})
    Assert-BlueprintEqual @($Tags | Sort-Object Name) @($expected | Sort-Object Name) 'Full tag inventory'
}

function Get-BlueprintReadback {
    param([object[]]$Records,[string]$WorkspaceRoot,[object]$Contract,[string]$Phase,[int]$After,[int]$Before)
    $relative='artifacts/'+$Phase+'/tags.csv'
    $path=Resolve-BenchmarkRelativePath -Root $WorkspaceRoot -RelativePath $relative
    $exports=@($Records | Where-Object {$_.Name -eq 'export_tags' -and $_.Index -gt $After -and $_.Index -lt $Before -and $_.Envelope.success -and $_.Input.'output-path' -ieq $path -and $_.Input.'plc-name' -ceq $Contract.plcName})
    if($exports.Count -ne 1){throw "Exact $Phase native tag export is missing or reused."}
    if($exports[0].Data.OutputPath -ine $path -or $exports[0].Data.Format -cne 'csv' -or $exports[0].Data.ItemCount -ne 2 -or (Get-Item -LiteralPath $path).Length -ne $exports[0].Data.BytesWritten){throw 'Tag export manifest does not match its artifact.'}
    $reads=@($Records | Where-Object {$_.Name -eq 'get_hmi_screen_json' -and $_.Index -gt $After -and $_.Index -lt $Before -and $_.Envelope.success -and $_.Input.'working-path' -ieq (Split-Path -Parent $path) -and $_.Input.'hmi-name' -ceq $Contract.hmiName -and $_.Input.'screen-path' -ceq $Contract.screenPath})
    if($reads.Count -ne 1){throw "Exact $Phase native HMI snapshot is missing or reused."}
    $read=$reads[0]
    Assert-BlueprintEqual @($read.Input.'item-names' | ForEach-Object {$_.ToLowerInvariant()} | Sort-Object) @($Contract.itemName.ToLowerInvariant()) 'Readback item filter'
    Assert-BlueprintEqual @($read.Input.properties | Sort-Object) @($Contract.properties | Sort-Object) 'Readback property filters'
    if($read.Data.HmiName -cne $Contract.hmiName -or $read.Data.ScreenPath -cne $Contract.screenPath -or [string]::IsNullOrWhiteSpace($read.Data.BaseHash)){throw 'HMI snapshot identity is missing.'}
    $tags=@(Import-Csv -LiteralPath $path)
    if($Phase -eq 'baseline'){
        Assert-BlueprintExpectedTags $tags $Contract $Contract.baselineComment
        $s=$read.Data.Screen; $item=@($s.Items)
        if($item.Count -ne 1 -or $item[0].Name -cne $Contract.itemName -or $item[0].Type -cne 'HmiButton'){throw 'Baseline HMI item identity differs.'}
        Assert-BlueprintEqual $item[0].Properties $Contract.baselineItemProperties 'Baseline HMI properties'
    }
    return [pscustomobject]@{Tags=$tags;Screen=$read.Data.Screen;Hash=$read.Data.BaseHash;LastIndex=[Math]::Max($exports[0].Index,$read.Index)}
}

function Assert-BlueprintMainPreservation {
    param([object[]]$Records,[string]$WorkspaceRoot,[object]$Contract,[int]$Before,[int]$After)
    foreach($phase in @('before','after')){
        $path=Resolve-BenchmarkRelativePath -Root $WorkspaceRoot -RelativePath ('artifacts/Main-'+$phase+'.xml')
        $exports=@($Records | Where-Object {$_.Name -eq 'export_block' -and $_.Envelope.success -and $_.Input.'plc-name' -ceq $Contract.plcName -and $_.Input.'block-name' -ceq 'Main' -and $_.Input.'export-path' -ieq $path -and (($phase -eq 'before' -and $_.Index -lt $Before) -or ($phase -eq 'after' -and $_.Index -gt $After))})
        if($exports.Count -ne 1){throw 'Independent Main export is missing.'}
        $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
        $reader=[Xml.XmlReader]::Create($path,$settings)
        try{$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader);foreach($info in @($xml.SelectNodes('/Document/DocumentInfo'))){[void]$info.ParentNode.RemoveChild($info)}
            $hash=[BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($xml.OuterXml))).Replace('-','').ToLowerInvariant()
            if($hash -cne $Contract.mainHash){throw 'Unrelated Main differs from the immutable fixture.'}
        }finally{$reader.Dispose()}
    }
}
