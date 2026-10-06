[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedIdentity,
    [ValidateSet('Readiness', 'Portal', 'Discovery')][string]$Action = 'Readiness',
    [int]$InstanceId,
    [string]$ServerAlias,
    [string]$SecretName = 'multiuser-agent',
    [string]$CliPath,
    [string]$WorkerPath,
    [string]$PortalPath = 'C:\Program Files\Siemens\Automation\Portal V21\Bin\Siemens.Automation.Portal.exe',
    [ValidateRange(0, 45)][int]$WaitSeconds = 10
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MultiuserAgentCredential.ps1')
if ([string]::IsNullOrWhiteSpace($CliPath)) { $CliPath = Join-Path $PSScriptRoot '..\src\OpenTiaToolkit.CLI\bin\Release\net48\OpenTiaToolkit.CLI.exe' }
if ([string]::IsNullOrWhiteSpace($WorkerPath)) { $WorkerPath = Join-Path $PSScriptRoot '..\src\OpenTiaToolkit.Worker.V21\bin\Release\net48\OpenTiaToolkit.Worker.V21.exe' }
$expectedSid = Get-MultiuserIdentitySid -Identity $ExpectedIdentity
if ($Action -eq 'Discovery' -and $InstanceId -le 0) { throw 'Discovery requires an explicit positive -InstanceId.' }
if ($Action -ne 'Discovery' -and $InstanceId -ne 0) { throw '-InstanceId is valid only for Discovery.' }
if (-not [string]::IsNullOrWhiteSpace($ServerAlias) -and $Action -ne 'Discovery') { throw '-ServerAlias is valid only for Discovery.' }
if ($ServerAlias -match '["\r\n\x00]') { throw 'Unsafe server alias.' }
if ($expectedSid -ceq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) {
    throw 'Use a separate agent Windows identity, not the current user.'
}
foreach ($value in @($CliPath, $WorkerPath, $PortalPath, $PSScriptRoot)) {
    if ($value -match '["\r\n\x00]') { throw 'Unsafe launch path.' }
}
$resolvedCli = (Resolve-Path -LiteralPath $CliPath).Path
$resolvedWorker = (Resolve-Path -LiteralPath $WorkerPath).Path
if ($Action -eq 'Portal') { $PortalPath = (Resolve-Path -LiteralPath $PortalPath).Path }
if (-not $PSCmdlet.ShouldProcess($ExpectedIdentity, "$Action under the agent identity (no project changes)")) { return }
$credential = Read-MultiuserAgentCredential -ExpectedIdentity $ExpectedIdentity -SecretName $SecretName
try {
    $runRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('OpenTiaToolkit\AgentRuns\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $stdoutPath = Join-Path $runRoot 'readiness.stdout.json'
    $stderrPath = Join-Path $runRoot 'readiness.stderr.log'
    $probePath = Join-Path $PSScriptRoot 'Invoke-MultiuserAgentReadiness.ps1'
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -ExpectedSid "{1}" -CliPath "{2}" -WorkerPath "{3}"' -f $probePath, $expectedSid, $resolvedCli, $resolvedWorker
    if ($Action -eq 'Portal') { $arguments += ' -StartPortal -PortalPath "{0}"' -f $PortalPath }
    if ($Action -eq 'Discovery') { $arguments += ' -InstanceId {0}' -f $InstanceId }
    if (-not [string]::IsNullOrWhiteSpace($ServerAlias)) { $arguments += ' -ServerAlias "{0}"' -f $ServerAlias }
    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $arguments -Credential $credential -LoadUserProfile -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    $completed = $process.WaitForExit($WaitSeconds * 1000)
    $nativeExitCode = if ($completed) { $process.ExitCode } else { $null }
    $readinessPassed = $null
    $failureMessage = $null
    if ($completed) {
        try {
            $readiness = Get-Content -LiteralPath $stdoutPath -Raw | ConvertFrom-Json
            Assert-MultiuserReadinessProof -Proof $readiness -ExpectedSid $expectedSid -ExpectedProbeId $process.Id -ExpectedWorkerPath $resolvedWorker -ExpectedServerAlias $ServerAlias -Action $Action -NativeExitCode $nativeExitCode
            $readinessPassed = $true
        } catch {
            $readinessPassed = $false
            $failureMessage = 'Agent probe failed or produced incomplete readiness evidence; inspect the returned stdout/stderr paths.'
        }
    }
    $result = [pscustomobject]@{
        Action = $Action; ExpectedIdentity = $credential.UserName; ExpectedSid = $expectedSid
        ProcessId = $process.Id; Status = $(if (-not $completed) { 'Running' } elseif ($readinessPassed) { 'Succeeded' } else { 'Failed' })
        ExitCode = $nativeExitCode; ReadinessPassed = $readinessPassed
        PortalPid = $(if ($readinessPassed) { $readiness.PortalPid } else { $null })
        StdoutPath = $stdoutPath; StderrPath = $stderrPath; RunDirectory = $runRoot
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'launch.json') -Encoding UTF8
    $result
    if ($failureMessage) { throw $failureMessage }
} finally {
    $credential.Password.Dispose()
}
