[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$UserName,
    [Management.Automation.PSCredential]$Credential,
    [string]$SecretName = 'multiuser-agent',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MultiuserAgentCredential.ps1')
$path = Get-MultiuserCredentialPath -SecretName $SecretName
if (-not $PSCmdlet.ShouldProcess($path, 'Store Windows-user-bound encrypted agent credential')) { return }
if ($null -ne $Credential -and -not [string]::IsNullOrWhiteSpace($UserName)) {
    throw 'Pass either -Credential or -UserName, not both.'
}
$ownsPassword = $false
try {
    if ($null -eq $Credential) {
        if ([string]::IsNullOrWhiteSpace($UserName)) { $UserName = Read-Host 'Agent Windows account (machine\user or domain\user)' }
        [void](Get-MultiuserIdentitySid -Identity $UserName)
        $password = Read-Host 'Agent password (not echoed or recorded in arguments)' -AsSecureString
        $ownsPassword = $true
        $Credential = [Management.Automation.PSCredential]::new($UserName, $password)
    }
    Save-MultiuserAgentCredential -Credential $Credential -SecretName $SecretName -Force:$Force
} finally {
    if ($ownsPassword) { $password.Dispose() }
}
