[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedIdentity,
    [string]$SecretName = 'multiuser-agent',
    [string]$McpPath,
    [string]$WorkerPath,
    [string]$SharedTempDirectory,
    [ValidateRange(1, 120)][int]$ShutdownSeconds = 30
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MultiuserAgentCredential.ps1')
$process = $null
$credential = $null
$forwardingStarted = $false
try {
    $sid = Get-MultiuserIdentitySid -Identity $ExpectedIdentity
    if ($sid -ceq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) { throw 'MCP requires a separate agent identity.' }
    if ([string]::IsNullOrWhiteSpace($McpPath)) { $McpPath = Join-Path $PSScriptRoot '..\src\OpenTiaToolkit.MCP\bin\Release\net48\OpenTiaToolkit.MCP.exe' }
    if ([string]::IsNullOrWhiteSpace($WorkerPath)) { $WorkerPath = Join-Path $PSScriptRoot '..\src\OpenTiaToolkit.Worker.V21\bin\Release\net48\OpenTiaToolkit.Worker.V21.exe' }
    $mcp = (Resolve-Path -LiteralPath $McpPath).Path
    $worker = (Resolve-Path -LiteralPath $WorkerPath).Path
    if (-not (Test-Path -LiteralPath $mcp -PathType Leaf) -or -not (Test-Path -LiteralPath $worker -PathType Leaf)) { throw 'MCP/worker must be executable files.' }
    $profile = [Environment]::ExpandEnvironmentVariables((Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\' + $sid)).ProfileImagePath)
    if (-not (Test-Path -LiteralPath $profile -PathType Container)) { throw 'Agent profile must already be provisioned.' }
    $temp = Join-Path $profile 'AppData\Local\Temp'
    if (-not [string]::IsNullOrWhiteSpace($SharedTempDirectory)) {
        $temp = (Resolve-Path -LiteralPath $SharedTempDirectory).Path
        if (-not (Test-Path -LiteralPath $temp -PathType Container)) { throw 'Shared temp must be an existing per-run directory.' }
        Assert-MultiuserNoReparsePoint -Path $temp
        $allowed = @(([string]$env:OPEN_TIA_MCP_ALLOWED_ROOTS).Split(';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $insideAllowed = $false
        foreach ($root in $allowed) {
            $normalizedRoot = [IO.Path]::GetFullPath($root).TrimEnd('\', '/') + '\'
            if (($temp.TrimEnd('\', '/') + '\').StartsWith($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) { $insideAllowed = $true }
        }
        if (-not $insideAllowed) { throw 'Shared temp must be inside an explicitly allowed MCP run root.' }
    }
    Add-Type -Path (Join-Path $PSScriptRoot 'MultiuserStdioRelay.cs') | Out-Null
    $credential = Read-MultiuserAgentCredential -ExpectedIdentity $ExpectedIdentity -SecretName $SecretName
    $parts = $credential.UserName.Split('\', 2)
    if ($parts.Count -ne 2) { throw 'A canonical domain\user credential is required.' }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $mcp; $start.WorkingDirectory = Split-Path -Parent $mcp
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.LoadUserProfile = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.Domain = $parts[0]; $start.UserName = $parts[1]; $start.Password = $credential.Password.Copy()
    # Do not inherit the coordinator's model auth, XDG state, profile, TEMP, or arbitrary secrets.
    $start.EnvironmentVariables.Clear()
    foreach ($name in @('SystemRoot', 'WINDIR', 'COMPUTERNAME', 'SystemDrive', 'OS', 'ProgramFiles', 'ProgramFiles(x86)', 'CommonProgramFiles', 'CommonProgramFiles(x86)', 'ProgramData', 'PROCESSOR_ARCHITECTURE', 'NUMBER_OF_PROCESSORS', 'PATHEXT')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $start.EnvironmentVariables[$name] = $value }
    }
    $start.EnvironmentVariables['PATH'] = (Join-Path $env:SystemRoot 'System32') + ';' + $env:SystemRoot
    $start.EnvironmentVariables['USERPROFILE'] = $profile
    $start.EnvironmentVariables['USERNAME'] = $parts[1]; $start.EnvironmentVariables['USERDOMAIN'] = $parts[0]
    $start.EnvironmentVariables['APPDATA'] = Join-Path $profile 'AppData\Roaming'
    $start.EnvironmentVariables['LOCALAPPDATA'] = Join-Path $profile 'AppData\Local'
    $start.EnvironmentVariables['TEMP'] = $temp; $start.EnvironmentVariables['TMP'] = $temp
    foreach ($name in @('OPEN_TIA_PORTAL_ROOT', 'OPEN_TIA_MCP_LOG_LEVEL', 'OPEN_TIA_MCP_ALLOWED_ROOTS', 'OPEN_TIA_MCP_TOOL_TIMEOUT_SECONDS', 'OPEN_TIA_MCP_RESET_GRACE_SECONDS', 'OPEN_TIA_MCP_MAX_INPUT_FILE_BYTES', 'OPEN_TIA_MCP_ALLOW_FILE_OVERWRITE', 'OPEN_TIA_AUDIT_JOURNAL', 'OPEN_TIA_WORKER_FORWARD_STDERR')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $start.EnvironmentVariables[$name] = $value }
    }
    $start.EnvironmentVariables['OPEN_TIA_WORKER_V21_EXE'] = $worker
    $start.EnvironmentVariables['OPEN_TIA_WORKER_EXE'] = $worker
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    if (-not $process.Start()) { throw 'Agent MCP process did not start.' }
    $start.Password.Dispose(); $start.Password = $null
    $credential.Password.Dispose(); $credential = $null
    $owner = Invoke-CimMethod -InputObject (Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)") -MethodName GetOwnerSid
    if ($owner.ReturnValue -ne 0 -or $owner.Sid -cne $sid) { throw 'Agent MCP process ownership does not match the expected SID.' }
    [Console]::Error.WriteLine('OpenTiaAgentMcp: ' + ([ordered]@{ processId = $process.Id; ownerSid = $sid; workerPath = $worker; tempRoot = $temp; credentialsInChildArguments = $false } | ConvertTo-Json -Compress))
    $forwardingStarted = $true
    $exitCode = [OpenTiaToolkit.Harness.MultiuserStdioRelay]::Forward($process, $ShutdownSeconds)
    exit $exitCode
} catch {
    [Console]::Error.WriteLine('Agent MCP bridge failed: ' + $_.Exception.Message)
    # Before forwarding, no client command could have reached this newly created MCP host.
    if ($null -ne $process -and -not $forwardingStarted) { try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null } } catch { } }
    exit 1
} finally {
    if ($null -ne $credential) { $credential.Password.Dispose() }
    if ($null -ne $process) { $process.Dispose() }
}
