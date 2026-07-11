[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [switch]$LiveProject,

    [string]$DisposableArchivePath = '',

    [switch]$DisposableProjectOnly,

    [ValidateRange(5, 300)]
    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $resolvedExecutable
$startInfo.WorkingDirectory = Split-Path -Parent $resolvedExecutable
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.EnvironmentVariables['OPEN_TIA_MCP_LOG_LEVEL'] = 'None'

# A staged MCP payload must exercise the worker shipped beside it instead of
# inheriting a developer-machine OPEN_TIA_WORKER_EXE value.
$payloadRoot = Split-Path -Parent (Split-Path -Parent $resolvedExecutable)
$stagedWorker = Join-Path $payloadRoot 'worker-v21\OpenTiaToolkit.Worker.V21.exe'
if (Test-Path -LiteralPath $stagedWorker -PathType Leaf) {
    $startInfo.EnvironmentVariables['OPEN_TIA_WORKER_EXE'] = $stagedWorker
}

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (-not $process.Start()) {
    throw "Failed to start MCP executable '$resolvedExecutable'."
}

$requestId = 0
$stderrTask = $process.StandardError.ReadToEndAsync()

function Read-McpResponse {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ExpectedId
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = [Math]::Max(1, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        $readTask = $process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remaining)) {
            break
        }

        $line = $readTask.Result
        if ($null -eq $line) {
            break
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $response = $line | ConvertFrom-Json
        if ($null -ne $response.id -and [int]$response.id -eq $ExpectedId) {
            $errorProperty = $response.PSObject.Properties['error']
            if ($null -ne $errorProperty -and $null -ne $errorProperty.Value) {
                throw "MCP request $ExpectedId failed: $($errorProperty.Value | ConvertTo-Json -Compress -Depth 20)"
            }

            return $response
        }
    }

    throw "Timed out waiting for MCP response id $ExpectedId from '$resolvedExecutable'."
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [object]$Params
    )

    $script:requestId++
    $request = [ordered]@{
        jsonrpc = '2.0'
        id = $script:requestId
        method = $Method
        params = $Params
    }
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress -Depth 30))
    $process.StandardInput.Flush()
    return Read-McpResponse -ExpectedId $script:requestId
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [hashtable]$Arguments = @{}
    )

    $response = Invoke-McpRequest -Method 'tools/call' -Params ([ordered]@{
        name = $Name
        arguments = $Arguments
    })
    $isErrorProperty = $response.result.PSObject.Properties['isError']
    if ($null -ne $isErrorProperty -and $isErrorProperty.Value -eq $true) {
        $text = @($response.result.content | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text }) -join [Environment]::NewLine
        throw "MCP tool '$Name' returned an error: $text"
    }

    return $response
}

try {
    $initialize = Invoke-McpRequest -Method 'initialize' -Params ([ordered]@{
        protocolVersion = '2024-11-05'
        capabilities = @{}
        clientInfo = [ordered]@{ name = 'OpenTiaToolkit staged payload smoke'; version = '1.0' }
    })
    if ([string]::IsNullOrWhiteSpace([string]$initialize.result.serverInfo.name)) {
        throw 'MCP initialize response did not contain serverInfo.name.'
    }

    $initialized = [ordered]@{
        jsonrpc = '2.0'
        method = 'notifications/initialized'
        params = @{}
    }
    $process.StandardInput.WriteLine(($initialized | ConvertTo-Json -Compress -Depth 10))
    $process.StandardInput.Flush()

    $toolsResponse = Invoke-McpRequest -Method 'tools/list' -Params @{}
    $tools = @($toolsResponse.result.tools)
    if ($tools.Count -lt 100) {
        throw "Expected the shared command surface, but tools/list returned only $($tools.Count) tools."
    }

    foreach ($requiredTool in @('list-instances', 'connect-instance', 'list-projects', 'list-devices', 'reset-session')) {
        if (-not ($tools.name -contains $requiredTool)) {
            throw "tools/list did not include required tool '$requiredTool'."
        }
    }

    $summary = [ordered]@{
        Executable = $resolvedExecutable
        Server = [string]$initialize.result.serverInfo.name
        ProtocolVersion = [string]$initialize.result.protocolVersion
        ToolCount = $tools.Count
        LiveProject = $LiveProject.IsPresent
    }

    if ($LiveProject) {
        if ($DisposableProjectOnly) {
            if ([string]::IsNullOrWhiteSpace($DisposableArchivePath)) {
                throw '-DisposableProjectOnly requires -DisposableArchivePath.'
            }
            $archiveFullPath = [System.IO.Path]::GetFullPath($DisposableArchivePath)
            $archiveDirectory = Split-Path -Parent $archiveFullPath
            New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
            $fixtureProjectName = 'phase2-mcp-fixture-' + [Guid]::NewGuid().ToString('N')
            $fixtureProjectPath = Join-Path $archiveDirectory ($fixtureProjectName + '.ap21')
            [void](Invoke-McpTool -Name 'open-tia-portal' -Arguments @{ 'with-ui' = $false })
            [void](Invoke-McpTool -Name 'create-project' -Arguments @{
                'project-path' = $fixtureProjectPath
                'confirm' = $true
            })
            $projectsResponse = Invoke-McpTool -Name 'list-projects'
            $projects = @($projectsResponse.result.structuredContent.data)
            if ($projects.Count -ne 1) { throw "Disposable worker expected one project; found $($projects.Count)." }
            [void](Invoke-McpTool -Name 'archive-project' -Arguments @{
                'archive-path' = $archiveFullPath
                'mode' = 'Compressed'
                'confirm' = $true
            })
            if (-not (Test-Path -LiteralPath $archiveFullPath -PathType Leaf)) {
                throw "Disposable archive was not produced at '$archiveFullPath'."
            }
            [void](Invoke-McpTool -Name 'doctor')
            [void](Invoke-McpTool -Name 'reset-session')
            $summary['DisposableArchiveCreated'] = $true
            $summary['ProjectCount'] = $projects.Count
            $summary['ResetVerified'] = $true
        }
        else {
        $instancesResponse = Invoke-McpTool -Name 'list-instances'
        $instanceData = $instancesResponse.result.structuredContent.data
        $instances = @($instanceData.instances)
        if ($instances.Count -ne 1) {
            throw "Live project smoke requires exactly one running TIA instance; found $($instances.Count)."
        }

        $processId = [int]$instances[0].processId
        [void](Invoke-McpTool -Name 'connect-instance' -Arguments @{ 'instance-id' = $processId })

        $projectsResponse = Invoke-McpTool -Name 'list-projects'
        $projects = @($projectsResponse.result.structuredContent.data)
        if ($projects.Count -lt 1) {
            throw 'The connected TIA instance did not expose an open project.'
        }

        $devicesResponse = Invoke-McpTool -Name 'list-devices'
        $devices = @($devicesResponse.result.structuredContent.data)
        if ($devices.Count -lt 1) {
            throw 'The open project did not return any devices.'
        }

        $plcsResponse = Invoke-McpTool -Name 'list-plcs'
        $plcs = @($plcsResponse.result.structuredContent.data)
        if ($plcs.Count -lt 1) {
            throw 'Typed PLC discovery did not return a confirmed PLC.'
        }

        $plcNames = @($plcs | ForEach-Object { [string]$_.name } | Where-Object { $_ })
        if (@($plcNames | Select-Object -Unique).Count -ne $plcNames.Count) {
            throw 'PLC discovery returned duplicate names.'
        }

        [void](Invoke-McpTool -Name 'open-plc' -Arguments @{ 'plc-name' = $plcNames[0] })
        $blocksResponse = Invoke-McpTool -Name 'get-blocks' -Arguments @{ 'plc-name' = $plcNames[0] }
        $blocks = @($blocksResponse.result.structuredContent.data)
        foreach ($block in $blocks) {
            if ([string]$block.comment -match '^Siemens\.Engineering\.MultilingualText$') {
                throw 'PLC block comments still expose the Siemens MultilingualText type name.'
            }
        }

        $hmisResponse = Invoke-McpTool -Name 'list-hmis'
        $hmis = @($hmisResponse.result.structuredContent.data)
        if ($hmis.Count -lt 1) {
            throw 'Typed Unified HMI discovery did not return a confirmed HMI.'
        }

        $hmiNames = @($hmis | ForEach-Object { [string]$_.name } | Where-Object { $_ })
        if (@($hmiNames | Select-Object -Unique).Count -ne $hmiNames.Count) {
            throw 'HMI discovery returned duplicate names.'
        }

        [void](Invoke-McpTool -Name 'open-unified-hmi' -Arguments @{ 'hmi-name' = $hmiNames[0] })
        $screensResponse = Invoke-McpTool -Name 'list-hmi-screens' -Arguments @{ 'hmi-name' = $hmiNames[0] }
        $screens = @($screensResponse.result.structuredContent.data)

        if (-not [string]::IsNullOrWhiteSpace($DisposableArchivePath)) {
            $archiveFullPath = [System.IO.Path]::GetFullPath($DisposableArchivePath)
            $archiveDirectory = Split-Path -Parent $archiveFullPath
            New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
            $fixtureProjectPath = Join-Path $archiveDirectory 'phase2-fixture-source.ap21'
            [void](Invoke-McpTool -Name 'create-project' -Arguments @{
                'project-path' = $fixtureProjectPath
                'confirm' = $true
            })
            [void](Invoke-McpTool -Name 'archive-project' -Arguments @{
                'archive-path' = $archiveFullPath
                'mode' = 'Compressed'
                'confirm' = $true
            })
            if (-not (Test-Path -LiteralPath $archiveFullPath -PathType Leaf)) {
                throw "Disposable archive was not produced at '$archiveFullPath'."
            }
            $summary['DisposableArchiveCreated'] = $true
        }

        [void](Invoke-McpTool -Name 'portal-status')
        [void](Invoke-McpTool -Name 'reset-session')

        $summary['InstanceCount'] = $instances.Count
        $summary['ProjectCount'] = $projects.Count
        $summary['DeviceCount'] = $devices.Count
        $summary['PlcCount'] = $plcs.Count
        $summary['BlockCount'] = $blocks.Count
        $summary['HmiCount'] = $hmis.Count
        $summary['ScreenCount'] = $screens.Count
        $summary['ResetVerified'] = $true
        }
    }

    [pscustomobject]$summary
}
finally {
    try {
        $process.StandardInput.Close()
    }
    catch {
    }

    if (-not $process.WaitForExit(5000)) {
        $process.Kill()
        $process.WaitForExit()
    }

    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        throw "MCP process exited with code $($process.ExitCode). stderr: $stderr"
    }

    $process.Dispose()
}
