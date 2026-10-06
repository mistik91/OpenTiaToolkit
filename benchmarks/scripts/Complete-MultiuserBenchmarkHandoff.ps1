[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ContextPath,
    [switch]$OperatorCheckInConfirmed
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'MultiuserBenchmark.ps1')
if(-not $OperatorCheckInConfirmed){throw 'Check in in the owning TIA UI first, then explicitly confirm that handoff. This script never performs check-in.'}
$context=Get-Content -LiteralPath $ContextPath -Raw | ConvertFrom-Json
if($context.schemaVersion -ne 1 -or $context.scope -cne 'identity-bound-local-save-ui-handoff' -or $context.checkpoint -cne 'awaiting-ui-check-in'){throw 'Only a saved, independently readable pending handoff can be completed.'}
$coordinatorSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
if($coordinatorSid -cne $context.coordinatorSid -or $coordinatorSid -ceq $context.agentSid){throw 'Use the original, separate coordinator identity.'}
[void](Assert-MuOwnedPortal $context.agentPid $context.agentSid)
[void](Assert-MuOwnedPortal $context.coordinatorPid $context.coordinatorSid)
Assert-MuWorkerRegistration $context.workerPath
if((Get-FileHash -LiteralPath $context.workerPath).Hash -cne $context.workerSha256){throw 'The qualified worker changed.'}
Assert-MuEvidenceIsolation $context.evidenceRoot $context.workspacePath
$completionPath=Join-Path $context.evidenceRoot 'completion.json'
if(Test-Path -LiteralPath $completionPath){throw 'Completion proof already exists; refusing overwrite.'}
$case=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../cases/ott-37/case.json') -Raw | ConvertFrom-Json
$assertion=@($case.assertions | Where-Object {$_.type -ceq 'multiuserLocalSave'})[0]
$contractPath=Resolve-BenchmarkRelativePath -Root $context.workspacePath -RelativePath $assertion.relativePath -WorkspaceInput
if((Get-FileHash -LiteralPath $contractPath).Hash -ine $assertion.contractSha256){throw 'Acceptance contract changed.'}
$contract=Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
$before=Test-MuNativeSnapshot $context $context.baseline $contract
[void](Test-MuNativeSnapshot $context $context.readback $contract -Changed -MarkingCount 1)
$profile=[Environment]::GetFolderPath('UserProfile')
# Documents may be redirected outside this profile. Verification still uses a
# private profile-local directory, not a shared or redirected Documents root.
$directory=Join-Path $profile 'Documents/OpenTiaSessions'
[void](Resolve-BenchmarkRelativePath -Root $directory -RelativePath 'root-check')
New-Item -ItemType Directory -Path $directory -Force | Out-Null
# No close is reached unless the exact old session is current, conflict-free,
# and has no markings following the operator's check-in. Never discard work.
if(-not (Test-Path -LiteralPath (Join-Path $context.evidenceRoot 'post-ui-owner.commands.json'))){[void](Invoke-MuProbe $context 'post-ui-owner' @(
    (New-MuCommand 'connect-instance' @{'instance-id'=$context.agentPid}),
    (New-MuCommand 'get-project-info' @{} @{'data.Name'=$context.projectName;'data.Path'=$context.projectPath;'data.PortalProcessId'=$context.agentPid}),
    (New-MuCommand 'get-multiuser-session-status' @{} @{'data.IsMultiuser'=$true;'data.IsOpen'=$true;'data.IsUpToDate'=$true;'data.ConflictCount'=0;'data.MarkingCount'=0;'data.Markings'=@()}),
    (New-MuCommand 'close-project' @{save=$false;confirm=$true}),
    (New-MuCommand 'list-projects' @{} @{data=@()})
))}
# Resume only a demonstrably completed owner-close phase; never retry a failed
# or incomplete vendor operation. These files stay outside the model workspace.
$ownerHashes=[ordered]@{}
foreach($name in @('journal.jsonl','identity.json','shutdown.json')){$ownerHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $context.evidenceRoot ('post-ui-owner/'+$name))).Hash}
$ownerProof=[pscustomobject]@{relativeDirectory='post-ui-owner';hashes=[pscustomobject]$ownerHashes}
$owner=Get-MuNativeRecords $context $ownerProof
$status=Get-MuProbeData $owner.Directory 'get-multiuser-session-status';Assert-MuSessionStatus $status 0
$closed=@($owner.Records | Where-Object {$_.request.params.name -ceq 'close-project'})
if($closed.Count -ne 1){throw 'Exact owner close proof is absent.'}
Assert-MuBoolean $closed[0].request.params.arguments.save $false 'Owner close without discarded pending work'
Assert-MuBoolean $closed[0].request.params.arguments.confirm $true 'Owner close confirmation'
$empty=Get-MuProbeData $owner.Directory 'list-projects'
if(@($empty).Count -ne 0 -or $owner.Records[-1].request.params.name -cne 'list-projects'){throw 'Owner close did not end with an empty inventory.'}
$verification=$context | ConvertTo-Json -Depth 80 | ConvertFrom-Json
$verification.agentSid=$context.coordinatorSid;$verification.agentPid=$context.coordinatorPid
$verification.sessionDirectory=$directory
$verification.sessionName=$context.projectName+'_Verify_'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$verification.sessionPath=Join-Path (Join-Path $directory $verification.sessionName) ($verification.sessionName+'.als21')
$verification.projectPath=Join-Path (Join-Path (Join-Path $directory $verification.sessionName) 'LS') ($context.projectName+'.amc21')
$commands=@(
    (New-MuCommand 'connect-instance' @{'instance-id'=$context.coordinatorPid}),
    (New-MuCommand 'list-projects' @{} @{data=@()}),
    (New-MuCommand 'create-multiuser-session' @{'server-alias'=$context.serverAlias;'project-name'=$context.projectName;'session-name'=$verification.sessionName;'session-directory'=$directory;mode='Multiuser';confirm=$true} @{'data.ProjectPath'=$verification.sessionPath}),
    (New-MuCommand 'open-project' @{'project-path'=$verification.sessionPath} @{'data.Name'=$context.projectName;'data.Path'=$verification.projectPath;'data.PortalProcessId'=$verification.agentPid})
)+@(Get-MuSnapshotCommands $verification 'server-verification' $contract)
foreach($plc in $contract.plcs){$commands+=New-MuCommand 'compile-software' @{'plc-name'=$plc.name} @{'data.success'=$true;'data.errorCount'=0;'data.warningCount'=0}}
$commands+=New-MuCommand 'close-project' @{save=$false;confirm=$true}
$commands+=New-MuCommand 'list-projects' @{} @{data=@()}
$started=[DateTime]::UtcNow.ToString('o')
$native=Invoke-MuProbe $verification 'server-verification' $commands -IdentityMode Coordinator
$verification.sessionId=(Get-MuProbeData $native 'create-multiuser-session').SessionId
if($verification.sessionId -eq $context.sessionId -or $verification.sessionPath -ieq $context.sessionPath){throw 'Server readback is not a fresh session.'}
$proof=New-MuProof $verification 'server-verification' $contract
$after=Test-MuNativeSnapshot $verification $proof $contract -Changed
Assert-MuNativeSessionCreation $verification $after
foreach($plc in $contract.plcs){
    $original=Get-Content -LiteralPath (Join-Path $before.Directory ($plc.prefix+'-hardware.json')) -Raw | ConvertFrom-Json
    $current=Get-Content -LiteralPath (Join-Path $after.Directory ($plc.prefix+'-hardware.json')) -Raw | ConvertFrom-Json
    Assert-MuEqual $current $original 'Server hardware preservation'
}
[pscustomobject][ordered]@{
    schemaVersion=1;projectName=$context.projectName;originalSessionId=$context.sessionId;originalSessionPath=$context.sessionPath
    operatorCheckInConfirmed=$true;serverPersistenceVerified=$true;verificationStartedAtUtc=$started;verifiedAtUtc=[DateTime]::UtcNow.ToString('o')
    verificationOwnerSid=$context.coordinatorSid;verificationPid=$context.coordinatorPid;verificationSessionId=$verification.sessionId
    verificationSessionPath=$verification.sessionPath;verificationProjectPath=$verification.projectPath
    verificationSessionName=$verification.sessionName;verificationSessionDirectory=$directory;contextSha256=(Get-FileHash -LiteralPath $ContextPath).Hash
    contractSha256=$assertion.contractSha256;proof=$proof;ownerCloseProof=$ownerProof
    scope='Operator UI check-in plus fresh native server-derived verification. Original model local-save score is unchanged.'
} | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $completionPath -Encoding UTF8
Write-Host "Server persistence verified through a fresh coordinator session: $completionPath"
