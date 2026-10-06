Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'MultiuserCaseAssertions.ps1')

function Assert-MuOwnedPortal {
    param([int]$InstanceId, [string]$ExpectedSid)
    if ($InstanceId -le 0) { throw 'An explicit positive TIA PID is required.' }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$InstanceId"
    if ($null -eq $process -or $process.Name -cne 'Siemens.Automation.Portal.exe') { throw 'Selected PID is not TIA Portal.' }
    $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid
    if ($owner.ReturnValue -ne 0 -or $owner.Sid -cne $ExpectedSid) { throw 'Selected TIA owner does not match its declared identity.' }
    return [pscustomobject]@{ Pid=$InstanceId; OwnerSid=$owner.Sid; CreationUtc=$process.CreationDate.ToUniversalTime().ToString('o'); ParentPid=$process.ParentProcessId }
}
function Assert-MuWorkerRegistration {
    param([string]$WorkerPath)
    $path = (Resolve-Path -LiteralPath $WorkerPath).Path
    $date = (Get-Item -LiteralPath $path).LastWriteTimeUtc.ToString('yyyy/MM/dd HH:mm:ss.fff')
    $sha = [Security.Cryptography.SHA256]::Create(); $stream=[IO.File]::OpenRead($path)
    try { $hash=[Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
    foreach ($key in @('HKLM:\SOFTWARE\Siemens\Automation\Openness\AllowList\OpenTiaToolkit.Worker.V21.exe','HKLM:\SOFTWARE\Siemens\Automation\Openness\21.0\Whitelist\OpenTiaToolkit.Worker.V21.exe')) {
        if (@(Get-ChildItem -LiteralPath $key | Get-ItemProperty | Where-Object { $_.Path -ceq $path -and $_.DateModified -ceq $date -and $_.FileHash -ceq $hash }).Count -lt 1) { throw 'Exact Release V21 worker registration is required.' }
    }
}
function New-MuCommand {
    param([string]$Name, [hashtable]$Arguments=@{}, [hashtable]$Expect=@{})
    return [pscustomobject]@{ name=$Name; arguments=[pscustomobject]$Arguments; expect=[pscustomobject]$Expect }
}
function Invoke-MuProbe {
    param([object]$Context, [string]$Phase, [object[]]$Commands, [ValidateSet('Agent','Coordinator')][string]$IdentityMode='Agent', [string]$ExtraRoots)
    $directory = Resolve-BenchmarkRelativePath -Root $Context.evidenceRoot -RelativePath $Phase
    $commandsPath = $directory+'.commands.json'
    if (Test-Path -LiteralPath $commandsPath) { throw 'Probe phase already exists; no retry/overwrite is allowed.' }
    $Commands | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $commandsPath -Encoding UTF8
    $instance = if ($IdentityMode -eq 'Agent') { $Context.agentPid } else { $Context.coordinatorPid }
    $roots = @($Context.evidenceRoot, $Context.workspacePath, $Context.sessionDirectory, $ExtraRoots) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    & (Join-Path $PSScriptRoot 'Invoke-MultiuserMcpProbe.ps1') -OutputDirectory $directory -IdentityMode $IdentityMode -ExpectedIdentity $Context.agentIdentity -InstanceId $instance -ServerAlias $Context.serverAlias -CommandsPath $commandsPath -AllowedRoots ($roots -join ';') -CaptureWorkerDiagnostics -TimeoutSeconds 180 | Out-Null
    return $directory
}
function Get-MuProbeData {
    param([string]$Directory, [string]$Tool)
    $entries = @(Get-Content -LiteralPath (Join-Path $Directory 'journal.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.request.method -ceq 'tools/call' -and $_.request.params.name -ceq $Tool })
    if ($entries.Count -ne 1) { throw "Probe result is not unique: $Tool" }
    Assert-MuBoolean $entries[0].response.result.structuredContent.success $true 'Probe result'
    return $entries[0].response.result.structuredContent.data
}
function New-MuProof {
    param([object]$Context, [string]$Phase, [object]$Contract)
    $directory = Resolve-BenchmarkRelativePath -Root $Context.evidenceRoot -RelativePath $Phase
    $names = @('journal.jsonl','identity.json','shutdown.json')
    foreach ($plc in $Contract.plcs) { foreach ($suffix in @('tags.csv','blocks.csv','main.xml','hardware.json')) { $names += $plc.prefix+'-'+$suffix } }
    $hashes = [ordered]@{}
    foreach ($name in $names) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $directory $name)).Hash }
    return [pscustomobject]@{ relativeDirectory=$Phase; hashes=[pscustomobject]$hashes }
}
function Get-MuSnapshotCommands {
    param([object]$Context, [string]$Phase, [object]$Contract, [int]$MarkingCount=0)
    $directory = Resolve-BenchmarkRelativePath -Root $Context.evidenceRoot -RelativePath $Phase
    $commands = [Collections.Generic.List[object]]::new()
    $commands.Add((New-MuCommand 'get-project-info' @{} @{ 'data.Name'=$Context.projectName; 'data.Path'=$Context.projectPath; 'data.PortalProcessId'=$Context.agentPid }))
    $commands.Add((New-MuCommand 'get-multiuser-session-status' @{} @{ 'data.IsMultiuser'=$true; 'data.IsOpen'=$true; 'data.IsUpToDate'=$true; 'data.MarkingCount'=$MarkingCount; 'data.ConflictCount'=0 }))
    $commands.Add((New-MuCommand 'list-plcs'))
    foreach ($plc in $Contract.plcs) {
        $commands.Add((New-MuCommand 'export-tags' @{ 'plc-name'=$plc.name; 'output-path'=(Join-Path $directory ($plc.prefix+'-tags.csv')) }))
        $commands.Add((New-MuCommand 'get-blocks' @{ 'plc-name'=$plc.name; 'output-path'=(Join-Path $directory ($plc.prefix+'-blocks.csv')) }))
        $commands.Add((New-MuCommand 'export-block' @{ 'plc-name'=$plc.name; 'block-name'='Main'; 'export-path'=(Join-Path $directory ($plc.prefix+'-main.xml')) }))
        $commands.Add((New-MuCommand 'get-hardware-tree' @{ 'device-name'=$plc.name; 'output-path'=(Join-Path $directory ($plc.prefix+'-hardware.json')) }))
    }
    return $commands.ToArray()
}
function Set-MuDirectoryAccess {
    param([string]$Path, [string]$AgentSid)
    # Only newly allocated, caller-owned run/workspace roots are granted access.
    $acl = Get-Acl -LiteralPath $Path
    $rule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($AgentSid),'Modify','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}
function Get-MuFixtureInputs {
    param([object]$Fixture,[string]$ArchivePath,[string]$EvidenceRoot,[string]$WorkspacePath,[string]$ContractPath)
    Assert-MuEvidenceIsolation $EvidenceRoot $WorkspacePath
    $normalizedEvidence=[IO.Path]::GetFullPath($EvidenceRoot)
    if(Test-Path -LiteralPath $normalizedEvidence){throw 'Use a new private evidence root.'}
    $archive=(Resolve-Path -LiteralPath $ArchivePath).Path
    if((Get-FileHash -LiteralPath $archive).Hash -cne $Fixture.sha256){throw 'Immutable Multiuser archive hash differs.'}
    $retrievedDirectory=Join-Path (Join-Path $normalizedEvidence 'restored') ([IO.Path]::GetFileNameWithoutExtension($archive))
    if($retrievedDirectory.Length -gt 143 -or (Join-Path $normalizedEvidence 'OTT37_yyyyMMddHHmmss_12345678').Length -gt 143){throw 'TIA V21 project directories are limited to 143 characters. Use a short WorkspaceRoot (for example C:\OTT\Bench).'}
    $contract=Get-Content -LiteralPath $ContractPath -Raw | ConvertFrom-Json
    if($contract.archiveSha256 -cne $Fixture.sha256){throw 'Fixture and acceptance archive bindings differ.'}
    return [pscustomobject]@{archive=$archive;contract=$contract}
}
function Initialize-MuAgentSessionDirectory {
    param([string]$Identity,[string]$Sid)
    $credential=Read-MultiuserAgentCredential -ExpectedIdentity $Identity
    $process=$null
    try {
        $parts=$credential.UserName.Split('\',2)
        $profile=[Environment]::ExpandEnvironmentVariables((Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$Sid)).ProfileImagePath)
        $start=[Diagnostics.ProcessStartInfo]::new()
        $start.FileName=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
        $start.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -ExpectedSid "{1}"' -f (Join-Path $PSScriptRoot 'Initialize-MultiuserSessionDirectory.ps1'),$Sid
        $start.WorkingDirectory=$PSScriptRoot;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.LoadUserProfile=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $start.Domain=$parts[0];$start.UserName=$parts[1];$start.Password=$credential.Password.Copy()
        $start.EnvironmentVariables.Clear()
        foreach($name in @('SystemRoot','WINDIR','SystemDrive','COMPUTERNAME','ProgramFiles','ProgramFiles(x86)','ProgramData','OS','PATHEXT')) {
            $value=[Environment]::GetEnvironmentVariable($name)
            if($null -ne $value){$start.EnvironmentVariables[$name]=$value}
        }
        $start.EnvironmentVariables['PATH']=(Join-Path $env:SystemRoot 'System32')+';'+$env:SystemRoot
        $start.EnvironmentVariables['USERPROFILE']=$profile
        $start.EnvironmentVariables['USERNAME']=$parts[1];$start.EnvironmentVariables['USERDOMAIN']=$parts[0]
        $start.EnvironmentVariables['APPDATA']=Join-Path $profile 'AppData/Roaming'
        $start.EnvironmentVariables['LOCALAPPDATA']=Join-Path $profile 'AppData/Local'
        $start.EnvironmentVariables['TEMP']=Join-Path $profile 'AppData/Local/Temp';$start.EnvironmentVariables['TMP']=$start.EnvironmentVariables['TEMP']
        $process=[Diagnostics.Process]::new();$process.StartInfo=$start
        [void]$process.Start()
        $start.Password.Dispose();$start.Password=$null
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(10000)){throw 'Agent directory preparation timed out; no forced termination was attempted.'}
        if($process.ExitCode -ne 0){throw ('Agent directory preparation failed: '+$stderr.Result)}
        $proof=$stdout.Result | ConvertFrom-Json
        Assert-MuBoolean $proof.reparsePointsRejected $true 'Agent directory reparse gate'
        if($proof.sid -cne $Sid -or -not ($proof.directory+'\').StartsWith($profile.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Agent directory identity/profile binding differs.'}
        return $proof
    } finally {
        $credential.Password.Dispose()
        if($null -ne $process){if($null -ne $process.StartInfo.Password){$process.StartInfo.Password.Dispose()};$process.Dispose()}
    }
}
function Initialize-MultiuserBenchmarkFixture {
    param([object]$Fixture, [string]$ArchivePath, [string]$EvidenceRoot, [string]$WorkspacePath, [string]$AgentIdentity, [int]$AgentPid, [int]$CoordinatorPid, [string]$ContractPath)
    $repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $inputs=Get-MuFixtureInputs $Fixture $ArchivePath $EvidenceRoot $WorkspacePath $ContractPath
    . (Join-Path $repo 'scripts/MultiuserAgentCredential.ps1')
    $sid = Get-MultiuserIdentitySid -Identity $AgentIdentity
    $coordinatorSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($sid -ceq $coordinatorSid -or $AgentPid -eq $CoordinatorPid) { throw 'Two distinct Windows identities and TIA PIDs are required.' }
    $agent = Assert-MuOwnedPortal $AgentPid $sid
    $coordinator = Assert-MuOwnedPortal $CoordinatorPid $coordinatorSid
    $archive=$inputs.archive;$contract=$inputs.contract
    $worker = (Resolve-Path -LiteralPath (Join-Path $repo 'src/OpenTiaToolkit.Worker.V21/bin/Release/net48/OpenTiaToolkit.Worker.V21.exe')).Path
    Assert-MuWorkerRegistration $worker
    $directoryProof=Initialize-MuAgentSessionDirectory $AgentIdentity $sid
    $sessionDirectory=$directoryProof.directory
    New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
    Set-MuDirectoryAccess $EvidenceRoot $sid
    Set-MuDirectoryAccess $WorkspacePath $sid
    $projectName = 'OTT37_'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss')+'_'+[Guid]::NewGuid().ToString('N').Substring(0,8)
    $sessionName = $projectName+'_Agent'
    $sessionPath = Join-Path (Join-Path $sessionDirectory $sessionName) ($sessionName+'.als21')
    $projectPath = Join-Path (Join-Path (Join-Path $sessionDirectory $sessionName) 'LS') ($projectName+'.amc21')
    $context = [pscustomobject][ordered]@{
        schemaVersion=1; scope='identity-bound-local-save-ui-handoff'; agentIdentity=$AgentIdentity; agentSid=$sid; coordinatorSid=$coordinatorSid
        agentPid=$AgentPid; coordinatorPid=$CoordinatorPid; agentPortal=$agent; coordinatorPortal=$coordinator
        serverAlias=$Fixture.serverAlias; projectName=$projectName; sessionName=$sessionName; sessionDirectory=$sessionDirectory; sessionDirectoryProof=$directoryProof
        sessionPath=$sessionPath; projectPath=$projectPath; sessionId=$null; archiveSha256=$Fixture.sha256
        workerPath=$worker; workerSha256=(Get-FileHash -LiteralPath $worker).Hash; evidenceRoot=[IO.Path]::GetFullPath($EvidenceRoot)
        workspacePath=[IO.Path]::GetFullPath($WorkspacePath); baseline=$null; readback=$null; readbackFailure=$null; readbackFailureKind=$null
        checkpoint='prepared'; serverPersistenceVerified=$false
    }
    # Prove both instances empty before any publishing/restoring. Never close an
    # existing project, even if its name looks like a benchmark fixture.
    foreach ($actor in @('Agent','Coordinator')) {
        $pidForActor = if ($actor -eq 'Agent') { $AgentPid } else { $CoordinatorPid }
        $ready = Invoke-MuProbe $context ($actor.ToLowerInvariant()+'-readiness') @(
            (New-MuCommand 'list-instances'), (New-MuCommand 'connect-instance' @{ 'instance-id'=$pidForActor }),
            (New-MuCommand 'list-projects' @{} @{ data=@() }), (New-MuCommand 'list-multiuser-projects' @{ 'server-alias'=$context.serverAlias })
        ) -IdentityMode $actor
        if (@((Get-MuProbeData $ready 'list-multiuser-projects') | Where-Object { $_.ProjectName -ceq $projectName }).Count -ne 0) { throw 'Unique server project already exists; refusing reuse.' }
    }
    $serverDirectory = Join-Path $context.evidenceRoot $projectName
    $serverProjectPath = Join-Path $serverDirectory ($projectName+'.ap21')
    [void](Invoke-MuProbe $context 'publish' @(
        (New-MuCommand 'connect-instance' @{ 'instance-id'=$CoordinatorPid }), (New-MuCommand 'list-projects' @{} @{ data=@() }),
        (New-MuCommand 'retrieve-project' @{ 'archive-path'=$archive; 'target-directory'=(Join-Path $context.evidenceRoot 'restored'); confirm=$true }),
        (New-MuCommand 'save-as-project' @{ 'destination-path'=$serverDirectory; confirm=$true }),
        (New-MuCommand 'get-project-info' @{} @{ 'data.Name'=$projectName; 'data.Path'=$serverProjectPath; 'data.PortalProcessId'=$CoordinatorPid }),
        (New-MuCommand 'close-project' @{ save=$false; confirm=$true }), (New-MuCommand 'list-projects' @{} @{ data=@() }),
        (New-MuCommand 'add-project-to-multiuser-server' @{ 'server-alias'=$context.serverAlias; 'project-path'=$serverProjectPath; confirm=$true } @{ 'data.ProjectName'=$projectName; 'data.ServerAlias'=$context.serverAlias })
    ) -IdentityMode Coordinator -ExtraRoots (Split-Path -Parent $archive))
    $commands = @(
        (New-MuCommand 'list-instances'), (New-MuCommand 'connect-instance' @{ 'instance-id'=$AgentPid }),
        (New-MuCommand 'list-projects' @{} @{ data=@() }),
        (New-MuCommand 'create-multiuser-session' @{ 'server-alias'=$context.serverAlias; 'project-name'=$projectName; 'session-name'=$sessionName; 'session-directory'=$sessionDirectory; mode='Multiuser'; confirm=$true } @{ 'data.ProjectPath'=$sessionPath }),
        (New-MuCommand 'open-project' @{ 'project-path'=$sessionPath } @{ 'data.Name'=$projectName; 'data.Path'=$projectPath; 'data.PortalProcessId'=$AgentPid })
    ) + @(Get-MuSnapshotCommands $context 'baseline' $contract)
    foreach ($plc in $contract.plcs) { $commands += New-MuCommand 'compile-software' @{ 'plc-name'=$plc.name } @{ 'data.success'=$true; 'data.errorCount'=0; 'data.warningCount'=0 } }
    $baseline = Invoke-MuProbe $context 'baseline' $commands
    $context.sessionId = (Get-MuProbeData $baseline 'create-multiuser-session').SessionId
    $context.baseline = New-MuProof $context 'baseline' $contract
    $snapshot=Test-MuNativeSnapshot $context $context.baseline $contract
    Assert-MuNativeSessionCreation $context $snapshot
    if ((Get-FileHash -LiteralPath $archive).Hash -cne $Fixture.sha256) { throw 'Immutable archive changed during provisioning.' }
    $context | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath (Join-Path $context.evidenceRoot 'prepared.json') -Encoding UTF8
    return $context
}
function Get-MultiuserBenchmarkReadback {
    param([object]$Context, [string]$ContractPath)
    $contract = Get-Content -LiteralPath $ContractPath -Raw | ConvertFrom-Json
    $commands = @((New-MuCommand 'list-instances'), (New-MuCommand 'connect-instance' @{ 'instance-id'=$Context.agentPid })) + @(Get-MuSnapshotCommands $Context 'readback' $contract -MarkingCount 1)
    try {
        [void](Invoke-MuProbe $Context 'readback' $commands)
        $Context.readback = New-MuProof $Context 'readback' $contract
        $Context.checkpoint = 'awaiting-ui-check-in'
    } catch {
        $Context.readbackFailure = $_.Exception.Message
        $Context.readbackFailureKind = 'harness-error'
        # A successful native response that disagrees with the required context
        # or marking gate is a model outcome, not a transport/export outage.
        $journal=Join-Path $Context.evidenceRoot 'readback/journal.jsonl'
        if ($_.Exception.Message -match '^(?:Probe result does not match required value|Missing required probe result path):' -and (Test-Path -LiteralPath $journal -PathType Leaf)) {
            $last=Get-Content -LiteralPath $journal | Select-Object -Last 1 | ConvertFrom-Json
            if ($null -ne $last.response.PSObject.Properties['result'] -and $last.response.result.isError -is [bool] -and -not $last.response.result.isError -and $last.response.result.structuredContent.success -is [bool] -and $last.response.result.structuredContent.success) { $Context.readbackFailureKind='model-state-mismatch' }
        }
        $Context.checkpoint = 'requires-operator-inspection'
    }
    $Context | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath (Join-Path $Context.evidenceRoot 'handoff.json') -Encoding UTF8
    return $Context
}
function Assert-MuHandoffCompletion {
    param([string]$ContextPath,[string]$CompletionPath)
    $context=Get-Content -LiteralPath $ContextPath -Raw | ConvertFrom-Json
    $completion=Get-Content -LiteralPath $CompletionPath -Raw | ConvertFrom-Json
    Assert-MuBoolean $completion.operatorCheckInConfirmed $true 'Operator UI confirmation'
    Assert-MuBoolean $completion.serverPersistenceVerified $true 'Fresh server persistence'
    if($completion.schemaVersion -ne 1 -or $completion.contextSha256 -ine (Get-FileHash -LiteralPath $ContextPath).Hash -or
        $completion.projectName -cne $context.projectName -or $completion.originalSessionPath -ine $context.sessionPath -or
        $completion.originalSessionId -ne $context.sessionId -or $completion.verificationSessionId -eq $context.sessionId -or
        $completion.verificationSessionPath -ieq $context.sessionPath -or $completion.verificationOwnerSid -cne $context.coordinatorSid -or
        $completion.verificationPid -ne $context.coordinatorPid){throw 'Completion proof is not bound to this handoff and a fresh separate-owner session.'}
    $case=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../cases/ott-37/case.json') -Raw | ConvertFrom-Json
    $assertion=@($case.assertions | Where-Object {$_.type -ceq 'multiuserLocalSave'})[0]
    $contractPath=Resolve-BenchmarkRelativePath -Root $context.workspacePath -RelativePath $assertion.relativePath -WorkspaceInput
    if($completion.contractSha256 -ine $assertion.contractSha256 -or (Get-FileHash -LiteralPath $contractPath).Hash -ine $assertion.contractSha256){throw 'Completion acceptance binding changed.'}
    $contract=Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
    Assert-MuEvidenceIsolation $context.evidenceRoot $context.workspacePath
    $owner=Get-MuNativeRecords $context $completion.ownerCloseProof
    Assert-MuSessionStatus (Get-MuProbeData $owner.Directory 'get-multiuser-session-status') 0
    $close=@($owner.Records | Where-Object {$_.request.params.name -ceq 'close-project'})
    if($close.Count -ne 1 -or $owner.Records[-1].request.params.name -cne 'list-projects' -or @((Get-MuProbeData $owner.Directory 'list-projects')).Count -ne 0){throw 'Completed owner close/empty proof differs.'}
    Assert-MuBoolean $close[0].request.params.arguments.confirm $true 'Completed owner close confirmation'
    Assert-MuBoolean $close[0].request.params.arguments.save $false 'Completed owner close mode'
    $verification=$context | ConvertTo-Json -Depth 80 | ConvertFrom-Json
    $verification.agentSid=$context.coordinatorSid;$verification.agentPid=$context.coordinatorPid
    $verification.sessionPath=$completion.verificationSessionPath;$verification.projectPath=$completion.verificationProjectPath
    $verification.sessionName=$completion.verificationSessionName;$verification.sessionDirectory=$completion.verificationSessionDirectory;$verification.sessionId=$completion.verificationSessionId
    $before=Test-MuNativeSnapshot $context $context.baseline $contract
    Assert-MuNativeSessionCreation $context $before
    $after=Test-MuNativeSnapshot $verification $completion.proof $contract -Changed
    Assert-MuNativeSessionCreation $verification $after
    $create=Get-MuProbeData $after.Directory 'create-multiuser-session'
    if($create.SessionId -ne $completion.verificationSessionId -or $create.ProjectPath -ine $completion.verificationSessionPath){throw 'Fresh session creation response differs from completion proof.'}
    foreach($plc in $contract.plcs){
        $original=Get-Content -LiteralPath (Join-Path $before.Directory ($plc.prefix+'-hardware.json')) -Raw | ConvertFrom-Json
        $current=Get-Content -LiteralPath (Join-Path $after.Directory ($plc.prefix+'-hardware.json')) -Raw | ConvertFrom-Json
        Assert-MuEqual $current $original 'Completed server hardware preservation'
    }
}
