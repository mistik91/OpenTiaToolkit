[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ExpectedSid)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
try {
    . (Join-Path $PSScriptRoot '../../scripts/MultiuserAgentCredential.ps1')
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($sid -cne $ExpectedSid) { throw 'Session directory preparation requires the owning Windows identity.' }
    $profile=[Environment]::GetFolderPath('UserProfile')
    $documents=[Environment]::GetFolderPath('MyDocuments')
    if ([string]::IsNullOrWhiteSpace($profile) -or [string]::IsNullOrWhiteSpace($documents) -or
        -not ([IO.Path]::GetFullPath($documents)+'\').StartsWith(([IO.Path]::GetFullPath($profile).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Documents must resolve inside the owning profile.' }
    $directory=Join-Path $documents 'OpenTiaSessions'
    Assert-MultiuserNoReparsePoint $directory
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Assert-MultiuserNoReparsePoint $directory
    [pscustomobject]@{sid=$sid;directory=$directory;reparsePointsRejected=$true} | ConvertTo-Json -Compress
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
