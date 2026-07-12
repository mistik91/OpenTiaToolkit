[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedIdentity,

    [Parameter(Mandatory = $true)]
    [string]$ServerAlias,

    [Parameter(Mandatory = $true)]
    [string]$ProjectName,

    [Parameter(Mandatory = $true)]
    [string]$SessionDirectory,

    [string]$SessionName = ("AgentSmoke-{0:yyyyMMdd-HHmmss}" -f (Get-Date)),
    [int]$InstanceId,
    [string]$CommitComment = "OpenTiaToolkit full agent-session smoke test",
    [string]$CliPath = (Join-Path $PSScriptRoot '..\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe'),
    [switch]$ConfirmCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
if (-not ($identity.Equals($ExpectedIdentity, [StringComparison]::OrdinalIgnoreCase) -or
          $identity.EndsWith("\$ExpectedIdentity", [StringComparison]::OrdinalIgnoreCase))) {
    throw "Identity check failed. Expected '$ExpectedIdentity', running as '$identity'. Log in as the agent account and retry."
}

$resolvedCli = (Resolve-Path -LiteralPath $CliPath).Path
if (-not (Test-Path -LiteralPath $SessionDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $SessionDirectory -Force | Out-Null
}

$sessionRoot = Join-Path $SessionDirectory $SessionName
$sessionFile = Join-Path $sessionRoot ($SessionName + '.als21')
$connection = if ($InstanceId -gt 0) { "connect-instance --instance-id=$InstanceId" } else { 'connect-portal' }
$commands = New-Object System.Collections.Generic.List[string]
$commands.Add($connection)
$commands.Add("list-multiuser-servers")
$commands.Add("list-multiuser-projects --server-alias=`"$ServerAlias`"")
$commands.Add("create-multiuser-session --server-alias=`"$ServerAlias`" --project-name=`"$ProjectName`" --session-name=`"$SessionName`" --session-directory=`"$SessionDirectory`" --mode=Multiuser --confirm=true")
$commands.Add("open-project --project-path=`"$sessionFile`"")
$commands.Add('get-multiuser-session-status')
$commands.Add('save-multiuser-session --confirm=true')
$commands.Add('get-multiuser-session-status')
if ($ConfirmCommit) {
    $commands.Add("commit-multiuser-session --comment=`"$CommitComment`" --require-up-to-date=true --require-no-conflicts=true --confirm=true")
} else {
    $commands.Add('close-project --save=true --confirm=true')
}

$commandFile = Join-Path ([IO.Path]::GetTempPath()) ("opentia-multiuser-{0}.txt" -f [Guid]::NewGuid().ToString('N'))
try {
    $commands | Set-Content -LiteralPath $commandFile -Encoding UTF8
    & $resolvedCli --script $commandFile
    if ($LASTEXITCODE -ne 0) { throw "Multiuser agent-session smoke test failed with exit code $LASTEXITCODE." }
    [pscustomobject]@{
        Identity       = $identity
        ServerAlias    = $ServerAlias
        ProjectName    = $ProjectName
        SessionName    = $SessionName
        SessionFile    = $sessionFile
        CommitRequested = [bool]$ConfirmCommit
        Succeeded      = $true
    }
} finally {
    Remove-Item -LiteralPath $commandFile -Force -ErrorAction SilentlyContinue
}
