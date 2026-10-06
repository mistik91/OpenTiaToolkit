[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateSet('Agent', 'Coordinator')][string]$IdentityMode = 'Agent',
    [string]$ExpectedIdentity = 'Agent',
    [Parameter(Mandatory = $true)][int]$InstanceId,
    [Parameter(Mandatory = $true)][string]$ServerAlias,
    [string]$CommandsPath,
    [string]$AllowedRoots,
    [switch]$CaptureWorkerDiagnostics,
    [ValidateRange(5, 300)][int]$TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $PSScriptRoot 'MultiuserMcpProbeAssertions.ps1')
$mcp = Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\bin\Release\net48\OpenTiaToolkit.MCP.exe'
$worker = Join-Path $repoRoot 'src\OpenTiaToolkit.Worker.V21\bin\Release\net48\OpenTiaToolkit.Worker.V21.exe'
$root = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($evidenceFile in @('journal.jsonl', 'requests.jsonl', 'tools.json', 'shutdown.json')) {
    if (Test-Path -LiteralPath (Join-Path $root $evidenceFile)) { throw 'Use a new output directory; never overwrite prior probe evidence.' }
}
New-Item -ItemType Directory -Path $root -Force | Out-Null
$temp = Join-Path $root 'temp'
New-Item -ItemType Directory -Path $temp -Force | Out-Null
if ([string]::IsNullOrWhiteSpace($AllowedRoots)) { $AllowedRoots = $root }
$info = [Diagnostics.ProcessStartInfo]::new()
if ($IdentityMode -eq 'Agent') {
    $info.FileName = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $info.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -ExpectedIdentity "{1}" -McpPath "{2}" -WorkerPath "{3}" -SharedTempDirectory "{4}"' -f (Join-Path $repoRoot 'scripts\Start-MultiuserAgentMcp.ps1'), $ExpectedIdentity, $mcp, $worker, $temp
} else { $info.FileName = $mcp }
$info.WorkingDirectory = $root
$info.UseShellExecute = $false; $info.CreateNoWindow = $true
$info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
$info.EnvironmentVariables['OPEN_TIA_WORKER_V21_EXE'] = $worker
$info.EnvironmentVariables['OPEN_TIA_WORKER_EXE'] = $worker
$info.EnvironmentVariables['OPEN_TIA_MCP_LOG_LEVEL'] = 'Error'
$info.EnvironmentVariables['OPEN_TIA_WORKER_FORWARD_STDERR'] = $(if ($CaptureWorkerDiagnostics) { 'true' } else { 'false' })
$info.EnvironmentVariables['OPEN_TIA_MCP_ALLOWED_ROOTS'] = $AllowedRoots
$process = [Diagnostics.Process]::new(); $process.StartInfo = $info
[void]$process.Start()
$errorTask = $process.StandardError.ReadToEndAsync()
$counter = 0; $journal = Join-Path $root 'journal.jsonl'
function Request {
    param([string]$Method, $Arguments)
    $script:counter++
    $request = [ordered]@{ jsonrpc = '2.0'; id = $script:counter; method = $Method; params = $Arguments }
    [ordered]@{ request = $request; utc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 80 -Compress | Add-Content -LiteralPath (Join-Path $root 'requests.jsonl') -Encoding UTF8
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Depth 60 -Compress))
    $process.StandardInput.Flush()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $read = $process.StandardOutput.ReadLineAsync()
        $remaining = [Math]::Max(1, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if (-not $read.Wait($remaining)) { throw "Timed out waiting for $Method; no retry or forced termination was attempted." }
        if ($null -eq $read.Result) { throw 'MCP protocol stream ended before the response.' }
        $response = $read.Result | ConvertFrom-Json
    } while ($null -eq $response.PSObject.Properties['id'] -or $response.id -ne $script:counter)
    [ordered]@{ request = $request; response = $response; utc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 80 -Compress | Add-Content -LiteralPath $journal -Encoding UTF8
    if ($null -ne $response.PSObject.Properties['error']) { throw 'MCP request returned a protocol error; inspect the journal.' }
    return $response.result
}
function Call {
    param([string]$Name, [hashtable]$Arguments = @{})
    if (@($tools.tools | Where-Object { $_.name -ceq $Name }).Count -ne 1) { throw "Tool $Name is absent from the live catalog." }
    $result = Request 'tools/call' ([ordered]@{ name = $Name; arguments = $Arguments })
    if ($result.isError) { throw "Tool $Name failed; inspect its retained structured response." }
    return $result
}
try {
    [void](Request 'initialize' @{ protocolVersion = '2025-03-26'; capabilities = @{}; clientInfo = @{ name = 'ott37-independent-probe'; version = '1.0' } })
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $process.StandardInput.Flush()
    $tools = Request 'tools/list' @{}
    $tools | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath (Join-Path $root 'tools.json') -Encoding UTF8
    $doctor = Call 'doctor'
    Assert-MultiuserMcpProbeResult -Result $doctor.structuredContent -Expect ([pscustomobject]@{ 'data.healthy' = $true; 'data.workerPath' = $worker; 'data.failureInjectionEnabled' = $false })
    $workerPid = [int]$doctor.structuredContent.data.processId
    $expectedSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($IdentityMode -eq 'Agent') {
        . (Join-Path $repoRoot 'scripts\MultiuserAgentCredential.ps1')
        $expectedSid = Get-MultiuserIdentitySid -Identity $ExpectedIdentity
    }
    $workerOwner = Invoke-CimMethod -InputObject (Get-CimInstance Win32_Process -Filter "ProcessId=$workerPid") -MethodName GetOwnerSid
    $portalOwner = Invoke-CimMethod -InputObject (Get-CimInstance Win32_Process -Filter "ProcessId=$InstanceId") -MethodName GetOwnerSid
    if ($workerOwner.ReturnValue -ne 0 -or $workerOwner.Sid -cne $expectedSid -or $portalOwner.ReturnValue -ne 0 -or $portalOwner.Sid -cne $expectedSid) { throw 'Worker/TIA owners do not match the requested identity.' }
    [ordered]@{ expectedSid = $expectedSid; workerPid = $workerPid; workerOwnerSid = $workerOwner.Sid; portalPid = $InstanceId; portalOwnerSid = $portalOwner.Sid } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'identity.json') -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($CommandsPath)) {
        [void](Call 'list-instances')
        [void](Call 'connect-instance' @{ 'instance-id' = $InstanceId })
        [void](Call 'list-projects')
        [void](Call 'list-multiuser-servers')
        [void](Call 'list-multiuser-projects' @{ 'server-alias' = $ServerAlias })
    } else {
        $commands = Get-Content -LiteralPath $CommandsPath -Raw | ConvertFrom-Json
        foreach ($command in $commands) {
            $arguments = @{}
            foreach ($property in $command.arguments.PSObject.Properties) { $arguments[$property.Name] = $property.Value }
            $result = Call $command.name $arguments
            if ($null -ne $command.PSObject.Properties['expect']) {
                Assert-MultiuserMcpProbeResult -Result $result.structuredContent -Expect $command.expect
            }
        }
    }
    $summary = [pscustomobject]@{ Succeeded = $true; IdentityMode = $IdentityMode; WrapperPid = $process.Id; ToolCount = @($tools.tools).Count; Journal = $journal; CommandScriptSupplied = (-not [string]::IsNullOrWhiteSpace($CommandsPath)) }
} catch {
    [pscustomobject]@{ Succeeded = $false; Message = $_.Exception.Message; Journal = $journal; LastRequestId = $counter } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'failure.json') -Encoding UTF8
    throw
} finally {
    $process.StandardInput.Close()
    if ($process.WaitForExit(35000)) {
        $errorTask.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $root 'stderr.log') -Encoding UTF8
        $shutdown = [pscustomobject]@{ Exited = $true; ExitCode = $process.ExitCode; WrapperPid = $process.Id }
    } else {
        $shutdown = [pscustomobject]@{ Exited = $false; ExitCode = $null; WrapperPid = $process.Id; Note = 'Inspect active operation; no process was forcibly terminated.' }
    }
    $shutdown | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'shutdown.json') -Encoding UTF8
    $process.Dispose()
}
if (-not $shutdown.Exited -or $shutdown.ExitCode -ne 0) { throw 'Protocol calls completed but process shutdown did not pass; inspect retained shutdown evidence.' }
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $root 'summary.json')
