Set-StrictMode -Version Latest
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop

function Assert-MultiuserWindows {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'Multiuser agent credentials require Windows DPAPI.'
    }
}

function Get-MultiuserIdentitySid {
    param([Parameter(Mandatory = $true)][string]$Identity)
    Assert-MultiuserWindows
    if ($Identity -match '[\r\n\x00"]' -or [string]::IsNullOrWhiteSpace($Identity)) {
        throw 'Invalid Windows identity.'
    }
    $qualified = if ($Identity.Contains('\') -or $Identity.Contains('@')) {
        $Identity
    } else {
        [Environment]::MachineName + '\' + $Identity
    }
    try {
        return ([Security.Principal.NTAccount]::new($qualified)).Translate([Security.Principal.SecurityIdentifier]).Value
    } catch {
        throw 'The specified Windows identity cannot be resolved. No account will be created.'
    }
}

function Assert-MultiuserNoReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Credential storage must not traverse a reparse point.'
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}

function Get-MultiuserCredentialPath {
    param([string]$SecretName = 'multiuser-agent')
    Assert-MultiuserWindows
    if ($SecretName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or
        $SecretName -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
        throw 'Invalid secret name; use a simple non-reserved filename, not a path.'
    }
    $localData = [Environment]::GetFolderPath('LocalApplicationData')
    if ([string]::IsNullOrWhiteSpace($localData)) { throw 'LocalApplicationData is unavailable.' }
    $path = Join-Path (Join-Path $localData 'OpenTiaToolkit\Secrets') ($SecretName + '.credential.clixml')
    Assert-MultiuserNoReparsePoint -Path $path
    return $path
}

function Set-MultiuserPrivateAcl {
    param([Parameter(Mandatory = $true)][string]$Path, [switch]$Directory)
    $ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $acl = if ($Directory) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
    $acl.SetOwner($ownerSid)
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($ownerSid, $systemSid)) {
        $rule = if ($Directory) {
            [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        } else {
            [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'Allow')
        }
        $acl.AddAccessRule($rule)
    }
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        if ($Directory) { [IO.Directory]::SetAccessControl($Path, $acl) } else { [IO.File]::SetAccessControl($Path, $acl) }
    } elseif ($Directory) {
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($Path), $acl)
    } else {
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($Path), $acl)
    }
}

function Assert-MultiuserPrivateAcl {
    param([Parameter(Mandatory = $true)][string]$Path)
    $acl = Get-Acl -LiteralPath $Path
    $ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -cne $ownerSid -or -not $acl.AreAccessRulesProtected) {
        throw 'Credential storage is not private to the current Windows user.'
    }
    $ownerAllowed = $false
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $rule.IdentityReference.Value -notin @($ownerSid, 'S-1-5-18')) {
            throw 'Credential storage has unexpected access rules.'
        }
        if ($rule.IdentityReference.Value -ceq $ownerSid) { $ownerAllowed = $true }
    }
    if (-not $ownerAllowed) { throw 'Credential storage has no owner access rule.' }
}

function Save-MultiuserAgentCredential {
    param(
        [Parameter(Mandatory = $true)][Management.Automation.PSCredential]$Credential,
        [string]$SecretName = 'multiuser-agent',
        [switch]$Force
    )
    $path = Get-MultiuserCredentialPath -SecretName $SecretName
    $identitySid = Get-MultiuserIdentitySid -Identity $Credential.UserName
    if ($Credential.Password.Length -eq 0) { throw 'An empty password cannot be stored.' }
    if ((Test-Path -LiteralPath $path) -and -not $Force) { throw 'Secret already exists. Use -Force only to intentionally replace it.' }
    $root = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $root)) {
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        Set-MultiuserPrivateAcl -Path $root -Directory
    }
    Assert-MultiuserPrivateAcl -Path $root
    if (Test-Path -LiteralPath $path) { Assert-MultiuserPrivateAcl -Path $path }
    $canonicalIdentity = ([Security.Principal.SecurityIdentifier]::new($identitySid)).Translate([Security.Principal.NTAccount]).Value
    $normalized = [Management.Automation.PSCredential]::new($canonicalIdentity, $Credential.Password.Copy())
    $temporaryPath = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.credential.clixml')
    try {
        $normalized | Export-Clixml -LiteralPath $temporaryPath -Depth 3
        Set-MultiuserPrivateAcl -Path $temporaryPath
        Assert-MultiuserNoReparsePoint -Path $path
        Move-Item -LiteralPath $temporaryPath -Destination $path -Force:$Force
        Assert-MultiuserPrivateAcl -Path $path
    } finally {
        $normalized.Password.Dispose()
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
    }
    [pscustomobject]@{ SecretName = $SecretName; Identity = $canonicalIdentity; IdentitySid = $identitySid; Storage = 'Windows DPAPI CurrentUser'; Path = $path }
}

function Read-MultiuserAgentCredential {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedIdentity,
        [string]$SecretName = 'multiuser-agent'
    )
    $path = Get-MultiuserCredentialPath -SecretName $SecretName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Agent credential is not provisioned. Run Set-MultiuserAgentCredential.ps1 first.' }
    Assert-MultiuserPrivateAcl -Path (Split-Path -Parent $path)
    Assert-MultiuserPrivateAcl -Path $path
    try { $credential = Import-Clixml -LiteralPath $path } catch { throw 'Cannot decrypt the credential under this Windows user and machine.' }
    if ($credential -isnot [Management.Automation.PSCredential] -or $credential.Password.Length -eq 0) {
        throw 'The stored value is not a nonempty Windows credential.'
    }
    try {
        if ((Get-MultiuserIdentitySid -Identity $credential.UserName) -cne (Get-MultiuserIdentitySid -Identity $ExpectedIdentity)) {
            throw 'The stored credential does not match the explicitly expected identity.'
        }
    } catch {
        $credential.Password.Dispose()
        throw
    }
    return $credential
}

function Assert-MultiuserReadinessProof {
    param(
        [Parameter(Mandatory = $true)]$Proof,
        [Parameter(Mandatory = $true)][string]$ExpectedSid,
        [Parameter(Mandatory = $true)][int]$ExpectedProbeId,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkerPath,
        [string]$ExpectedServerAlias,
        [ValidateSet('Readiness', 'Portal', 'Discovery')][string]$Action,
        [Nullable[int]]$NativeExitCode
    )
    if ($Proof.Succeeded -isnot [bool] -or -not $Proof.Succeeded) { throw 'Agent readiness failed; inspect the redacted result.' }
    if ($Proof.IdentitySid -cne $ExpectedSid -or $Proof.ProbePid -ne $ExpectedProbeId -or $Proof.WorkerPath -cne $ExpectedWorkerPath -or
        $Proof.ProjectMutations -isnot [bool] -or $Proof.ProjectMutations -or
        $Proof.OpennessMembership -isnot [bool] -or -not $Proof.OpennessMembership -or
        $Proof.WorkerRegistration -cne 'Exact V21 identity verified') {
        throw 'Agent readiness evidence does not match the expected identity/read-only boundary.'
    }
    if ($Action -in @('Portal', 'Discovery') -and
        ($Proof.PortalOwnerVerified -isnot [bool] -or -not $Proof.PortalOwnerVerified -or [int]$Proof.PortalPid -le 0)) {
        throw 'TIA process ownership was not verified.'
    }
    if ($Action -eq 'Discovery' -and ($Proof.ServerConnectionDiscoveryTested -isnot [bool] -or -not $Proof.ServerConnectionDiscoveryTested)) {
        throw 'Read-only server discovery was not completed.'
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedServerAlias) -and
        ($Proof.ProjectServerAccessTested -isnot [bool] -or -not $Proof.ProjectServerAccessTested -or $Proof.ProjectServerAlias -cne $ExpectedServerAlias)) {
        throw 'Requested Project Server authorization was not verified.'
    }
    if ($null -ne $NativeExitCode -and $NativeExitCode -ne 0) { throw 'Agent probe exited unsuccessfully despite its readiness result.' }
}
