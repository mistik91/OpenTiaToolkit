[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedSid,
    [Parameter(Mandatory = $true)][string]$CliPath,
    [Parameter(Mandatory = $true)][string]$WorkerPath,
    [string]$PortalPath,
    [switch]$StartPortal,
    [int]$InstanceId,
    [string]$ServerAlias
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$discoveryTranscript = $null
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.User.Value -cne $ExpectedSid) { throw 'Child Windows identity does not match the intended agent.' }
    if ($ServerAlias -match '["\r\n\x00]') { throw 'Unsafe server alias.' }
    if (-not [string]::IsNullOrWhiteSpace($ServerAlias) -and $InstanceId -le 0) { throw 'Server project discovery requires an explicit instance.' }
    $opennessSid = ([Security.Principal.NTAccount]::new([Environment]::MachineName, 'Siemens TIA Openness')).Translate([Security.Principal.SecurityIdentifier]).Value
    if ($opennessSid -notin @($identity.Groups | ForEach-Object Value)) { throw 'Agent token lacks Siemens TIA Openness membership.' }
    $resolvedWorker = (Resolve-Path -LiteralPath $WorkerPath).Path
    $workerDate = (Get-Item -LiteralPath $resolvedWorker).LastWriteTimeUtc.ToString('yyyy/MM/dd HH:mm:ss.fff')
    $hasher = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($resolvedWorker)
    try { $workerHash = [Convert]::ToBase64String($hasher.ComputeHash($stream)) } finally { $stream.Dispose(); $hasher.Dispose() }
    foreach ($registryPath in @('HKLM:\SOFTWARE\Siemens\Automation\Openness\AllowList\OpenTiaToolkit.Worker.V21.exe', 'HKLM:\SOFTWARE\Siemens\Automation\Openness\21.0\Whitelist\OpenTiaToolkit.Worker.V21.exe')) {
        $matches = @(Get-ChildItem -LiteralPath $registryPath | Get-ItemProperty | Where-Object { $_.Path -ceq $resolvedWorker -and $_.DateModified -ceq $workerDate -and $_.FileHash -ceq $workerHash })
        if ($matches.Count -eq 0) { throw 'The selected Release worker lacks its exact V21 registration.' }
    }
    $env:OPEN_TIA_WORKER_V21_EXE = $resolvedWorker
    $cli = (Resolve-Path -LiteralPath $CliPath).Path
    $instancesText = (& $cli list-instances | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Agent read-only instance discovery failed.' }
    $instances = $instancesText | ConvertFrom-Json
    $portalId = $null
    if ($StartPortal) {
        if ([int]$instances.count -ne 0) { throw 'Agent already has a TIA instance; refusing an ambiguous additional launch.' }
        foreach ($candidate in @(Get-CimInstance Win32_Process -Filter "Name='Siemens.Automation.Portal.exe'")) {
            $candidateOwner = Invoke-CimMethod -InputObject $candidate -MethodName GetOwnerSid
            if ($candidateOwner.ReturnValue -eq 0 -and $candidateOwner.Sid -ceq $ExpectedSid) { throw 'Agent already owns a TIA process, possibly still initializing; refusing another launch.' }
        }
        $portal = Start-Process -FilePath (Resolve-Path -LiteralPath $PortalPath).Path -WorkingDirectory (Split-Path -Parent $PortalPath) -WindowStyle Normal -PassThru
        $portalId = $portal.Id
        $owner = Invoke-CimMethod -InputObject (Get-CimInstance Win32_Process -Filter "ProcessId=$portalId") -MethodName GetOwnerSid
        if ($owner.ReturnValue -ne 0 -or $owner.Sid -cne $ExpectedSid) { throw 'Launched TIA process ownership could not be verified.' }
    }
    if ($InstanceId -gt 0) {
        $target = Get-CimInstance Win32_Process -Filter "ProcessId=$InstanceId"
        if ($null -eq $target -or $target.Name -cne 'Siemens.Automation.Portal.exe') { throw 'Requested instance is not a running TIA Portal process.' }
        $owner = Invoke-CimMethod -InputObject $target -MethodName GetOwnerSid
        if ($owner.ReturnValue -ne 0 -or $owner.Sid -cne $ExpectedSid) { throw 'Requested TIA process is not owned by the agent identity.' }
        $portalId = $InstanceId
        $commandsPath = Join-Path ([IO.Path]::GetTempPath()) ('multiuser-readiness-' + [Guid]::NewGuid().ToString('N') + '.txt')
        try {
            $commands = @('list-instances', "connect-instance --instance-id=$InstanceId", 'list-projects', 'list-multiuser-servers')
            if (-not [string]::IsNullOrWhiteSpace($ServerAlias)) { $commands += "list-multiuser-projects --server-alias=`"$ServerAlias`"" }
            $commands | Set-Content -LiteralPath $commandsPath -Encoding UTF8
            $discoveryTranscript = (& $cli --script $commandsPath | Out-String)
            if ($LASTEXITCODE -ne 0) { throw 'Stateful agent TIA/project/server discovery failed.' }
        } finally {
            if (Test-Path -LiteralPath $commandsPath) { Remove-Item -LiteralPath $commandsPath -Force }
        }
    }
    [pscustomobject]@{
        Succeeded = $true; Identity = $identity.Name; IdentitySid = $identity.User.Value
        ProbePid = $PID; OpennessMembership = $true; WorkerRegistration = 'Exact V21 identity verified'
        WorkerPath = $resolvedWorker; InstancesBeforeLaunch = $instances; PortalPid = $portalId
        PortalOwnerVerified = ($null -ne $portalId); ProjectMutations = $false
        ServerConnectionDiscoveryTested = ($InstanceId -gt 0)
        ProjectServerAccessTested = (-not [string]::IsNullOrWhiteSpace($ServerAlias)); ProjectServerAlias = $ServerAlias
        DiscoveryTranscript = $discoveryTranscript
        Note = 'Read-only bootstrap only; no local session or server check-in is claimed.'
    } | ConvertTo-Json -Depth 8
} catch {
    [pscustomobject]@{ Succeeded = $false; Stage = 'Agent readiness'; Message = $_.Exception.Message; ProjectMutations = $false; DiscoveryTranscript = $discoveryTranscript } | ConvertTo-Json -Depth 8
    exit 1
}
