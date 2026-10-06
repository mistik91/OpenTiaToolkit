Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'BenchmarkWorkspaceInputs.ps1')

function ConvertTo-MuCanonical {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in @($Value.Keys | Sort-Object -CaseSensitive)) { $result[$key] = ConvertTo-MuCanonical $Value[$key] }
        return $result
    }
    if ($Value -is [Management.Automation.PSCustomObject]) {
        $result = [ordered]@{}
        foreach ($p in @($Value.PSObject.Properties | Sort-Object Name -CaseSensitive)) { $result[$p.Name] = ConvertTo-MuCanonical $p.Value }
        return $result
    }
    if ($Value -is [Array]) { return ,@($Value | ForEach-Object { ConvertTo-MuCanonical $_ }) }
    return $Value
}
function Assert-MuEqual {
    param([AllowNull()][object]$Actual, [AllowNull()][object]$Expected, [string]$Label)
    $a = ConvertTo-Json -InputObject (ConvertTo-MuCanonical $Actual) -Depth 80 -Compress
    $b = ConvertTo-Json -InputObject (ConvertTo-MuCanonical $Expected) -Depth 80 -Compress
    if ($a -cne $b) { throw "$Label differs from the exact contract." }
}
function Assert-MuBoolean {
    param([object]$Value, [bool]$Expected, [string]$Label)
    if ($Value -isnot [bool] -or $Value -ne $Expected) { throw "$Label is not proven $Expected." }
}
function Assert-MuCount {
    param([object]$Value, [int]$Expected, [string]$Label)
    if (($Value -isnot [int] -and $Value -isnot [long]) -or $Value -ne $Expected) { throw "$Label is not the expected integer count." }
}
function Assert-MuEvidenceIsolation {
    param([string]$EvidenceRoot, [string]$WorkspacePath)
    $evidence=[IO.Path]::GetFullPath($EvidenceRoot).TrimEnd([char[]]'\/')
    $workspace=[IO.Path]::GetFullPath($WorkspacePath).TrimEnd([char[]]'\/')
    $separator=[IO.Path]::DirectorySeparatorChar
    if ($evidence -ieq $workspace -or $evidence.StartsWith($workspace+$separator,[StringComparison]::OrdinalIgnoreCase) -or $workspace.StartsWith($evidence+$separator,[StringComparison]::OrdinalIgnoreCase)) { throw 'Private evidence and model workspace must be disjoint roots.' }
    [void](Resolve-BenchmarkRelativePath -Root $evidence -RelativePath 'root-check')
    [void](Resolve-BenchmarkRelativePath -Root $workspace -RelativePath 'root-check')
}
function Get-MuMainHash {
    param([string]$Path)
    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $created = $document.SelectSingleNode('/Document/DocumentInfo/Created')
    if ($null -eq $created) { throw 'Main export timestamp is absent.' }
    [void]$created.ParentNode.RemoveChild($created)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($document.OuterXml)))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Get-MuExpectedTags {
    param([object]$Plc, [object]$Contract, [switch]$Changed)
    $clone = $Plc.tags | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    $rows = @($clone)
    if ($Changed -and $Plc.name -ceq $Contract.targetPlc) {
        $tag = @($rows | Where-Object { $_.Name -ceq $Contract.operation.match.name })
        if ($tag.Count -ne 1) { throw 'Acceptance target is not unique.' }
        $tag[0].LogicalAddress = $Contract.operation.set.logicalAddress
        $tag[0].Comment = $Contract.operation.set.comment
    }
    return ,@($rows | Sort-Object TagTableName,Name -CaseSensitive)
}
function Assert-MuSessionStatus {
    param([object]$Status, [int]$MarkingCount)
    foreach ($name in @('IsMultiuser','IsOpen','IsUpToDate')) { Assert-MuBoolean $Status.$name $true "Session $name" }
    Assert-MuCount $Status.ConflictCount 0 'Session conflicts'
    Assert-MuCount $Status.MarkingCount $MarkingCount 'Session markings'
    if (@($Status.Markings).Count -ne $MarkingCount) { throw 'Session marking list count differs.' }
    if ($MarkingCount -eq 1) {
        $mark = $Status.Markings[0]
        if ($mark.ObjectName -cne 'PumpEnable' -or $mark.ObjectType -cne 'Siemens.Engineering.SW.Tags.PlcTag' -or $mark.State -cne 'IsUptoDate, IsMarkedByMe') { throw 'The exact Agent-owned PumpEnable marking is not proven.' }
    }
}
function Get-MuNativeRecords {
    param([object]$Context, [object]$Proof)
    $directory = Resolve-BenchmarkRelativePath -Root $Context.evidenceRoot -RelativePath $Proof.relativeDirectory
    foreach ($item in $Proof.hashes.PSObject.Properties) {
        $file = Resolve-BenchmarkRelativePath -Root $directory -RelativePath $item.Name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file).Hash -ine $item.Value) { throw 'Private native evidence is absent or changed.' }
    }
    foreach ($required in @('journal.jsonl','identity.json','shutdown.json')) {
        if ($null -eq $Proof.hashes.PSObject.Properties[$required]) { throw 'Native proof lacks a required bound file.' }
    }
    $identity = Get-Content -LiteralPath (Join-Path $directory 'identity.json') -Raw | ConvertFrom-Json
    if ($identity.expectedSid -cne $Context.agentSid -or $identity.workerOwnerSid -cne $Context.agentSid -or $identity.portalOwnerSid -cne $Context.agentSid -or $identity.portalPid -ne $Context.agentPid) { throw 'Native Agent identity proof differs.' }
    $shutdown = Get-Content -LiteralPath (Join-Path $directory 'shutdown.json') -Raw | ConvertFrom-Json
    Assert-MuBoolean $shutdown.Exited $true 'Native probe shutdown'
    if ($shutdown.ExitCode -ne 0) { throw 'Native probe exit is not zero.' }
    $records = @(Get-Content -LiteralPath (Join-Path $directory 'journal.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.request.method -ceq 'tools/call' })
    foreach ($record in $records) {
        Assert-MuBoolean $record.response.result.isError $false 'Native tool error flag'
        Assert-MuBoolean $record.response.result.structuredContent.success $true 'Native command success'
    }
    $doctor = @($records | Where-Object { $_.request.params.name -ceq 'doctor' })
    if ($doctor.Count -ne 1 -or $doctor[0].response.result.structuredContent.data.workerPath -ine $Context.workerPath -or $doctor[0].response.result.structuredContent.data.processId -ne $identity.workerPid) { throw 'Native worker doctor binding differs.' }
    Assert-MuBoolean $doctor[0].response.result.structuredContent.data.healthy $true 'Native worker health'
    Assert-MuBoolean $doctor[0].response.result.structuredContent.data.failureInjectionEnabled $false 'Native failure injection'
    $info = @($records | Where-Object { $_.request.params.name -ceq 'get-project-info' })
    if ($info.Count -ne 1) { throw 'Native project context is not unique.' }
    $data = $info[0].response.result.structuredContent.data
    if ($data.Name -cne $Context.projectName -or $data.Path -ine $Context.projectPath -or $data.PortalProcessId -ne $Context.agentPid) { throw 'Native project/session context differs.' }
    return [pscustomobject]@{ Directory=$directory; Records=$records }
}
function Test-MuNativeSnapshot {
    param([object]$Context, [object]$Proof, [object]$Contract, [switch]$Changed, [int]$MarkingCount = 0)
    $native = Get-MuNativeRecords $Context $Proof
    $statuses = @($native.Records | Where-Object { $_.request.params.name -ceq 'get-multiuser-session-status' })
    if ($statuses.Count -lt 1) { throw 'Native session status is absent.' }
    foreach ($status in $statuses) { Assert-MuSessionStatus $status.response.result.structuredContent.data $MarkingCount }
    foreach ($plc in $Contract.plcs) {
        foreach ($suffix in @('tags.csv','blocks.csv','main.xml','hardware.json')) {
            if ($null -eq $Proof.hashes.PSObject.Properties[($plc.prefix+'-'+$suffix)]) { throw 'Native artifact binding is incomplete.' }
        }
        $rows = @(Import-Csv -LiteralPath (Join-Path $native.Directory ($plc.prefix+'-tags.csv')) | Sort-Object TagTableName,Name -CaseSensitive)
        Assert-MuEqual $rows (Get-MuExpectedTags $plc $Contract -Changed:$Changed) "Native $($plc.name) tags"
        $blocks = @(Import-Csv -LiteralPath (Join-Path $native.Directory ($plc.prefix+'-blocks.csv')) | Sort-Object Name -CaseSensitive)
        Assert-MuEqual $blocks @($Contract.blocks) "Native $($plc.name) blocks"
        if ((Get-MuMainHash (Join-Path $native.Directory ($plc.prefix+'-main.xml'))) -cne $plc.mainSha256) { throw 'Native Main content differs from the frozen baseline.' }
        # Bind every exported file to a successful exact-target vendor command.
        foreach ($spec in @(@('export-tags','output-path','tags.csv'),@('get-blocks','output-path','blocks.csv'),@('export-block','export-path','main.xml'),@('get-hardware-tree','output-path','hardware.json'))) {
            $path = Join-Path $native.Directory ($plc.prefix+'-'+$spec[2])
            $calls = @($native.Records | Where-Object { $_.request.params.name -ceq $spec[0] -and $_.request.params.arguments.($spec[1]) -ieq $path -and $(if($spec[0] -eq 'get-hardware-tree'){ $_.request.params.arguments.'device-name' -ceq $plc.name }else{ $_.request.params.arguments.'plc-name' -ceq $plc.name }) })
            if ($calls.Count -ne 1) { throw 'Native artifact producer is not unique/exact.' }
            if ($spec[0] -eq 'export-block' -and $calls[0].request.params.arguments.'block-name' -cne 'Main') { throw 'Native block producer differs.' }
        }
    }
    return $native
}
function Assert-MuNativeSessionCreation {
    param([object]$Context,[object]$Native)
    $creates=@($Native.Records | Where-Object {$_.request.params.name -ceq 'create-multiuser-session'})
    $opens=@($Native.Records | Where-Object {$_.request.params.name -ceq 'open-project'})
    if($creates.Count -ne 1 -or $opens.Count -ne 1){throw 'Fresh native create/open provenance is absent or ambiguous.'}
    $input=$creates[0].request.params.arguments;$data=$creates[0].response.result.structuredContent.data
    if($input.'server-alias' -cne $Context.serverAlias -or $input.'project-name' -cne $Context.projectName -or
        $input.'session-name' -cne $Context.sessionName -or $input.'session-directory' -ine $Context.sessionDirectory -or $input.mode -cne 'Multiuser' -or
        $data.SessionId -ne $Context.sessionId -or $data.ProjectPath -ine $Context.sessionPath -or $opens[0].request.params.arguments.'project-path' -ine $Context.sessionPath){throw 'Fresh native session is not bound to this fixture.'}
    Assert-MuBoolean $input.confirm $true 'Native session creation confirmation'
    if($creates[0].request.id -ge $opens[0].request.id){throw 'Native session opening did not follow creation.'}
}
function Test-BenchmarkMultiuserLocalSave {
    param([object[]]$ToolEvents, [object]$Metadata, [string]$WorkspaceRoot, [object]$Assertion)
    $contractPath = Resolve-BenchmarkRelativePath -Root $WorkspaceRoot -RelativePath $Assertion.relativePath -WorkspaceInput
    if ((Get-FileHash -LiteralPath $contractPath).Hash -ine $Assertion.contractSha256) { throw 'Acceptance contract was modified.' }
    $contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
    $context = $Metadata.multiuser
    if ($context.schemaVersion -ne 1 -or $context.archiveSha256 -cne $contract.archiveSha256 -or $context.agentSid -ceq $context.coordinatorSid -or $context.agentPid -eq $context.coordinatorPid -or $context.agentPid -le 0 -or $context.sessionPath -notmatch '\.als21$' -or $context.projectPath -notmatch '\.amc21$') { throw 'Separate-account fixture binding is invalid.' }
    Assert-MuEvidenceIsolation $context.evidenceRoot $WorkspaceRoot
    $records = [Collections.Generic.List[object]]::new()
    for ($index=0; $index -lt $ToolEvents.Count; $index++) {
        $event = $ToolEvents[$index]; $name = [string]$event.part.tool -replace '-','_'
        if ($name -in @('write','edit','bash','apply_patch','patch','multiedit')) { throw 'Native-only task forbids workspace/proof authoring.' }
        if ($name -notmatch '^opentia_' -or $event.part.state.status -notin @('completed','error')) { continue }
        $input = $event.part.state.input
        $name = $name.Substring(8)
        # Read-only MCP operation diagnostics are valid recovery evidence, not
        # substitutes for project identity, freshness or explicit local save.
        # Cancellation and session reset remain out of scope.
        $allowed = @('doctor','portal_status','operation_status','list_instances','connect_instance','list_projects','get_project_info','list_devices','list_multiuser_servers','list_multiuser_projects','list_multiuser_sessions','get_multiuser_session_status','list_plcs','open_plc','get_software_tree','search_blocks','export_tags','get_blocks','get_block_info','get_cross_references','export_block','get_hardware_tree','compile_software','modify_tags','save_multiuser_session')
        if ($name -cnotin $allowed) { throw "Out-of-scope command: $name" }
        if ($name -eq 'connect_instance' -and $input.'instance-id' -ne $context.agentPid) { throw 'Foreign TIA instance selection.' }
        if ($null -ne $input.PSObject.Properties['plc-name'] -and $input.'plc-name' -cnotin @($contract.plcs.name)) { throw 'Foreign PLC selection.' }
        $envelope = if($event.part.state.status -eq 'error'){$event.part.state.error}else{$event.part.state.output}
        if ($envelope -is [string]) { try { $envelope = $envelope | ConvertFrom-Json } catch { $envelope=$null } }
        $records.Add([pscustomobject]@{ Index=$index; Name=$name; Input=$input; Envelope=$envelope })
    }
    $applies = @($records | Where-Object { $_.Name -ceq 'modify_tags' -and $_.Input.'dry-run' -is [bool] -and -not $_.Input.'dry-run' })
    if ($applies.Count -ne 1) { throw 'Exactly one explicit apply is required.' }
    $apply = $applies[0]
    $connections = @($records | Where-Object { $_.Index -lt $apply.Index -and $_.Name -ceq 'connect_instance' -and $_.Envelope.success })
    if ($connections.Count -lt 1) { throw 'Explicit model Agent attachment is absent.' }
    $previews = @($records | Where-Object { $_.Index -lt $apply.Index -and $_.Name -ceq 'modify_tags' -and $_.Input.'dry-run' -is [bool] -and $_.Input.'dry-run' -and $_.Envelope.success -is [bool] -and $_.Envelope.success })
    if ($previews.Count -lt 1) { throw 'Successful exact preview before apply is absent.' }
    foreach ($r in @($records | Where-Object { $_.Name -ceq 'modify_tags' })) {
        if ($r.Input.'plc-name' -cne $contract.targetPlc -or $null -ne $r.Input.PSObject.Properties['input-path']) { throw 'Tag mutation route/target differs.' }
        if ($null -ne $r.Input.operations.PSObject.Properties['operations'] -and @($r.Input.operations.PSObject.Properties).Count -ne 1) { throw 'Unexpected operation wrapper fields.' }
        $ops = @(if ($null -ne $r.Input.operations.PSObject.Properties['operations']) { $r.Input.operations.operations } else { $r.Input.operations })
        Assert-MuEqual $ops @($contract.operation) 'Exact tag selector/update'
        if ($r.Input.'dry-run' -isnot [bool]) { throw 'Dry-run mode must be explicit.' }
    }
    Assert-MuBoolean $apply.Input.confirm $true 'Apply confirmation'
    foreach ($r in @($previews[-1],$apply)) {
        Assert-MuBoolean $r.Envelope.success $true 'Tag command success'
        Assert-MuBoolean $r.Envelope.data.DryRun $r.Input.'dry-run' 'Observed dry-run mode'
        Assert-MuCount $r.Envelope.data.Summary.Total 1 'Tag operation total'
        Assert-MuCount $r.Envelope.data.Summary.Failed 0 'Failed tag operations'
        Assert-MuCount $r.Envelope.data.Summary.Applied $(if($r.Input.'dry-run'){0}else{1}) 'Applied tag operations'
        Assert-MuCount $r.Envelope.data.Summary.Planned $(if($r.Input.'dry-run'){1}else{0}) 'Planned tag operations'
        if (@($r.Envelope.data.Operations).Count -ne 1 -or $r.Envelope.data.Operations[0].Status -cne $(if($r.Input.'dry-run'){'Planned'}else{'Applied'})) { throw 'Tag outcome is incomplete.' }
        $tag = @($contract.plcs | Where-Object { $_.name -ceq $contract.targetPlc })[0].tags | Where-Object { $_.Name -ceq $contract.operation.match.name }
        $expectedBefore = [pscustomobject]@{ TagTable=$tag.TagTableName; Name=$tag.Name; DataType=$tag.DataType; LogicalAddress=$tag.LogicalAddress; Comment=$tag.Comment }
        $expectedAfter = $expectedBefore | ConvertTo-Json | ConvertFrom-Json
        $expectedAfter.LogicalAddress=$contract.operation.set.logicalAddress; $expectedAfter.Comment=$contract.operation.set.comment
        Assert-MuEqual $r.Envelope.data.Operations[0].Before $expectedBefore 'Observed tag before snapshot'
        Assert-MuEqual $r.Envelope.data.Operations[0].After $expectedAfter 'Observed tag after snapshot'
    }
    $infos = @($records | Where-Object { $_.Index -gt $connections[-1].Index -and $_.Index -lt $apply.Index -and $_.Name -cin @('get_project_info','list_projects') -and $_.Envelope.success })
    if ($infos.Count -lt 1) { throw 'Model context verification is absent.' }
    foreach ($r in $infos) {
        $data=$r.Envelope.data
        $expectedNames=@($context.projectName)
        if($r.Name -ceq 'list_projects'){
            if(@($data).Count -ne 1){throw 'Model project inventory must prove one exact target.'}
            $data=@($data)[0]
            # Siemens' project inventory decorates the session display name;
            # get-project-info returns its undecorated engineering identity.
            $expectedNames+=($context.projectName+' (Session)')
        }
        if ($data.Name -cnotin $expectedNames -or $data.Path -ine $context.projectPath -or $data.PortalProcessId -ne $context.agentPid) { throw 'Model project/session context differs.' }
    }
    $initial = @($records | Where-Object { $_.Index -lt $apply.Index -and $_.Name -ceq 'get_multiuser_session_status' -and $_.Envelope.success })
    if ($initial.Count -lt 1) { throw 'Model initial freshness/marking gate is absent.' }
    foreach ($r in $initial) { Assert-MuSessionStatus $r.Envelope.data 0 }
    $saves = @($records | Where-Object { $_.Index -gt $apply.Index -and $_.Name -ceq 'save_multiuser_session' -and $_.Envelope.success })
    if ($saves.Count -lt 1) { throw 'Explicit successful local save after apply is absent.' }
    foreach ($save in $saves) { Assert-MuBoolean $save.Input.confirm $true 'Local save confirmation'; Assert-MuSessionStatus $save.Envelope.data 1 }
    $lastSave = $saves[-1]
    $finalStatuses = @($records | Where-Object { $_.Index -gt $lastSave.Index -and $_.Name -ceq 'get_multiuser_session_status' -and $_.Envelope.success })
    if ($finalStatuses.Count -lt 1) { throw 'Post-save status verification is absent.' }
    foreach ($r in $finalStatuses) { Assert-MuSessionStatus $r.Envelope.data 1 }
    foreach ($plc in $contract.plcs) {
        $compiles = @($records | Where-Object { $_.Index -gt $apply.Index -and $_.Index -lt $lastSave.Index -and $_.Name -ceq 'compile_software' -and $_.Input.'plc-name' -ceq $plc.name -and $_.Envelope.success })
        if ($compiles.Count -lt 1) { throw 'Both PLCs must compile before local save.' }
        foreach ($r in $compiles) { Assert-MuBoolean $r.Envelope.data.success $true 'PLC compilation'; Assert-MuCount $r.Envelope.data.errorCount 0 'PLC compile errors'; Assert-MuCount $r.Envelope.data.warningCount 0 'PLC compile warnings' }
    }
    $before = Test-MuNativeSnapshot $context $context.baseline $contract
    Assert-MuNativeSessionCreation $context $before
    $after = Test-MuNativeSnapshot $context $context.readback $contract -Changed -MarkingCount 1
    foreach ($plc in $contract.plcs) {
        $a = Get-Content -LiteralPath (Join-Path $before.Directory ($plc.prefix+'-hardware.json')) -Raw | ConvertFrom-Json
        $b = Get-Content -LiteralPath (Join-Path $after.Directory ($plc.prefix+'-hardware.json')) -Raw | ConvertFrom-Json
        Assert-MuEqual $b $a 'Complete native hardware tree'
        foreach ($phase in @('baseline','readback')) {
            foreach ($spec in @(@('export_tags','output-path','tags.csv'),@('export_block','export-path','main.xml'))) {
                $path = Resolve-BenchmarkRelativePath -Root $WorkspaceRoot -RelativePath ('artifacts/'+$phase+'/'+$plc.prefix+'-'+$spec[2])
                $calls = @($records | Where-Object { $_.Name -ceq $spec[0] -and $_.Input.'plc-name' -ceq $plc.name -and $_.Envelope.success -and $(
                    $value=[string]$_.Input.($spec[1]); $actual=if([IO.Path]::IsPathRooted($value)){[IO.Path]::GetFullPath($value)}else{[IO.Path]::GetFullPath((Join-Path $WorkspaceRoot $value))}; $actual -ieq $path
                ) -and $(if($phase -eq 'baseline'){ $_.Index -lt $apply.Index }else{ $_.Index -gt $lastSave.Index }) })
                if ($calls.Count -lt 1) { throw 'Model baseline/readback export provenance is absent.' }
                if ($spec[0] -eq 'export_block' -and @($calls | Where-Object { $_.Input.'block-name' -cne 'Main' }).Count -gt 0) { throw 'Model block producer differs.' }
                if ($spec[0] -eq 'export_tags') { Assert-MuEqual @(Import-Csv -LiteralPath $path | Sort-Object TagTableName,Name -CaseSensitive) (Get-MuExpectedTags $plc $contract -Changed:($phase -eq 'readback')) 'Model-exported tags' }
                elseif ((Get-MuMainHash $path) -cne $plc.mainSha256) { throw 'Model-exported Main differs.' }
            }
        }
    }
    return 'Separate Agent identity, exact preview/apply, both PLC compiles, explicit local save, sole owned marking, native tag/block/hardware preservation and pending UI handoff qualified. Server persistence is not scored in this phase.'
}
