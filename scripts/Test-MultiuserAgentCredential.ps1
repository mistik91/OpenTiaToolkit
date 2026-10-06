[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MultiuserAgentCredential.ps1')
Assert-MultiuserWindows
$checks = 0
$testName = 'test-' + [Guid]::NewGuid().ToString('N')
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$secretText = 'fixture-' + [Guid]::NewGuid().ToString('N')
$secret = ConvertTo-SecureString -String $secretText -AsPlainText -Force
$credential = [Management.Automation.PSCredential]::new($identity, $secret)
$testPath = Get-MultiuserCredentialPath -SecretName $testName
$reparseRoot = Join-Path ([IO.Path]::GetTempPath()) ('MultiuserCredentialPaths-' + [Guid]::NewGuid().ToString('N'))
function Assert-Rejected {
    param([scriptblock]$Action, [string]$MessagePattern)
    try { & $Action | Out-Null } catch {
        if ($MessagePattern -and $_.Exception.Message -notmatch $MessagePattern) { throw }
        $script:checks++; return
    }
    throw 'Expected a fail-closed rejection.'
}
try {
    foreach ($invalidName in @('../escape', 'folder/name', 'folder\name', '', 'bad:name', "bad`nname", 'CON', 'nul.xml', 'LPT1', ('x' * 65))) {
        Assert-Rejected { Get-MultiuserCredentialPath -SecretName $invalidName } -MessagePattern 'Invalid secret name'
    }
    $reparseTarget = Join-Path $reparseRoot 'target'
    $reparseLink = Join-Path $reparseRoot 'link'
    New-Item -ItemType Directory -Path $reparseTarget -Force | Out-Null
    New-Item -ItemType Junction -Path $reparseLink -Target $reparseTarget | Out-Null
    Assert-Rejected { Assert-MultiuserNoReparsePoint -Path (Join-Path $reparseLink 'secret.credential.clixml') } -MessagePattern 'reparse point'
    $empty = [Security.SecureString]::new()
    try { Assert-Rejected { Save-MultiuserAgentCredential -Credential ([Management.Automation.PSCredential]::new($identity, $empty)) -SecretName $testName } }
    finally { $empty.Dispose() }
    $stored = Save-MultiuserAgentCredential -Credential $credential -SecretName $testName
    if ($stored.PSObject.Properties.Name -contains 'Password' -or (Get-Content -LiteralPath $testPath -Raw).Contains($secretText)) { throw 'Secret appeared in metadata or persisted plaintext.' }
    $checks++
    Assert-MultiuserPrivateAcl -Path $testPath
    $checks++
    $read = Read-MultiuserAgentCredential -ExpectedIdentity $identity -SecretName $testName
    try {
        if ($read.GetNetworkCredential().Password -cne $secretText -or $read.UserName -cne $identity) { throw 'DPAPI credential round-trip failed.' }
        $checks++
    } finally { $read.Password.Dispose() }
    Assert-Rejected { Save-MultiuserAgentCredential -Credential $credential -SecretName $testName }
    $differentIdentity = ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')).Translate([Security.Principal.NTAccount]).Value
    Assert-Rejected { Read-MultiuserAgentCredential -ExpectedIdentity $differentIdentity -SecretName $testName }
    Assert-Rejected { Read-MultiuserAgentCredential -ExpectedIdentity $identity -SecretName ('missing-' + [Guid]::NewGuid().ToString('N')) }
    Assert-Rejected { Read-MultiuserAgentCredential -ExpectedIdentity ('missing-' + [Guid]::NewGuid().ToString('N')) -SecretName $testName }
    Save-MultiuserAgentCredential -Credential $credential -SecretName $testName -Force | Out-Null
    $checks++
    $broadAcl = [Security.AccessControl.FileSecurity]::new()
    $broadAcl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
        $broadAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'Allow'))
    }
    $broadAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), 'Read', 'Allow'))
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        [IO.File]::SetAccessControl($testPath, $broadAcl)
    } else {
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($testPath), $broadAcl)
    }
    Assert-Rejected { Read-MultiuserAgentCredential -ExpectedIdentity $identity -SecretName $testName }
    Assert-Rejected { Save-MultiuserAgentCredential -Credential $credential -SecretName $testName -Force }
    Set-MultiuserPrivateAcl -Path $testPath
    [pscustomobject]@{ NotACredential = $true } | Export-Clixml -LiteralPath $testPath
    Assert-Rejected { Read-MultiuserAgentCredential -ExpectedIdentity $identity -SecretName $testName }
    $parseErrors = @()
    foreach ($scriptPath in @('MultiuserAgentCredential.ps1', 'Set-MultiuserAgentCredential.ps1', 'Start-MultiuserAgent.ps1', 'Invoke-MultiuserAgentReadiness.ps1')) {
        $tokens = $null; $errors = $null
        [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $scriptPath), [ref]$tokens, [ref]$errors)
        $parseErrors += @($errors)
    }
    if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
    $checks++
    & (Join-Path $PSScriptRoot 'Set-MultiuserAgentCredential.ps1') -SecretName ($testName + '-whatif') -WhatIf
    if (Test-Path -LiteralPath (Get-MultiuserCredentialPath -SecretName ($testName + '-whatif'))) { throw 'WhatIf wrote a credential.' }
    $checks++
    Assert-Rejected { & (Join-Path $PSScriptRoot 'Start-MultiuserAgent.ps1') -ExpectedIdentity $identity -WhatIf } -MessagePattern 'separate agent Windows identity'
    $probe = & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Invoke-MultiuserAgentReadiness.ps1') -ExpectedSid S-1-0-0 -CliPath unused -WorkerPath unused | Out-String | ConvertFrom-Json
    if ($LASTEXITCODE -eq 0 -or $probe.Succeeded -or $probe.Message -notmatch 'identity') { throw 'Wrong child identity was not rejected before any worker or portal launch.' }
    $checks++
    $proof = [pscustomobject]@{
        Succeeded = $true; IdentitySid = 'test-sid'; ProbePid = 42; WorkerPath = 'test-worker'
        ProjectMutations = $false; OpennessMembership = $true; WorkerRegistration = 'Exact V21 identity verified'
        PortalPid = 123; PortalOwnerVerified = $true; ServerConnectionDiscoveryTested = $true
        ProjectServerAccessTested = $true; ProjectServerAlias = 'test-server'
    }
    foreach ($action in @('Readiness', 'Portal', 'Discovery')) {
        Assert-MultiuserReadinessProof -Proof $proof -ExpectedSid test-sid -ExpectedProbeId 42 -ExpectedWorkerPath test-worker -ExpectedServerAlias test-server -Action $action
        $checks++
    }
    Assert-Rejected { Assert-MultiuserReadinessProof -Proof $proof -ExpectedSid test-sid -ExpectedProbeId 42 -ExpectedWorkerPath test-worker -Action Readiness -NativeExitCode 1 } -MessagePattern 'exited unsuccessfully'
    foreach ($variant in @('unsuccessful', 'string-success', 'wrong-sid', 'wrong-pid', 'wrong-worker', 'mutated', 'string-mutated', 'no-openness', 'unregistered', 'no-owner', 'string-owner', 'no-portal-pid', 'no-connections', 'no-server-access', 'wrong-server', 'missing-field')) {
        $badProof = $proof | ConvertTo-Json | ConvertFrom-Json
        switch ($variant) {
            'unsuccessful' { $badProof.Succeeded = $false }
            'string-success' { $badProof.Succeeded = 'true' }
            'wrong-sid' { $badProof.IdentitySid = 'wrong' }
            'wrong-pid' { $badProof.ProbePid = 43 }
            'wrong-worker' { $badProof.WorkerPath = 'wrong' }
            'mutated' { $badProof.ProjectMutations = $true }
            'string-mutated' { $badProof.ProjectMutations = 'false' }
            'no-openness' { $badProof.OpennessMembership = $false }
            'unregistered' { $badProof.WorkerRegistration = 'not verified' }
            'no-owner' { $badProof.PortalOwnerVerified = $false }
            'string-owner' { $badProof.PortalOwnerVerified = 'true' }
            'no-portal-pid' { $badProof.PortalPid = 0 }
            'no-connections' { $badProof.ServerConnectionDiscoveryTested = $false }
            'no-server-access' { $badProof.ProjectServerAccessTested = $false }
            'wrong-server' { $badProof.ProjectServerAlias = 'wrong' }
            'missing-field' { $badProof.PSObject.Properties.Remove('OpennessMembership') }
        }
        Assert-Rejected { Assert-MultiuserReadinessProof -Proof $badProof -ExpectedSid test-sid -ExpectedProbeId 42 -ExpectedWorkerPath test-worker -ExpectedServerAlias test-server -Action Discovery }
    }
    Write-Host "PASS: $checks multiuser credential/bootstrap checks; no real account logon or TIA launch."
} finally {
    $secret.Dispose()
    if (Test-Path -LiteralPath $testPath) { Remove-Item -LiteralPath $testPath -Force }
    if (Test-Path -LiteralPath $reparseRoot) {
        # Only these exact, empty test-owned paths are removed; never recurse into a junction.
        if (Test-Path -LiteralPath $reparseLink) { [IO.Directory]::Delete($reparseLink) }
        if (Test-Path -LiteralPath $reparseTarget) { [IO.Directory]::Delete($reparseTarget) }
        [IO.Directory]::Delete($reparseRoot)
    }
}
