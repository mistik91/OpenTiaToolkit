[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$CommandCatalogExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$WorkerExecutablePath,

    [ValidateRange(0, [int]::MaxValue)]
    [int]$InstanceId = 0,

    [string]$ReportPath = '.\artifacts\validation\mcp-live-readonly.json',

    [ValidateRange(5, 300)]
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$resolvedCatalogExecutable = (Resolve-Path -LiteralPath $CommandCatalogExecutablePath).Path
$resolvedWorker = (Resolve-Path -LiteralPath $WorkerExecutablePath).Path
$resolvedReport = [IO.Path]::GetFullPath($ReportPath)
$reportDirectory = Split-Path -Parent $resolvedReport
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

function Get-Sha256Text {
    param([Parameter(Mandatory = $true)][string]$Text)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-StableSdkAssemblies {
    $directory = Split-Path -Parent $resolvedExecutable
    $result = @()
    foreach ($name in @('ModelContextProtocol.dll', 'ModelContextProtocol.Core.dll')) {
        $path = Join-Path $directory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing MCP SDK assembly '$path'."
        }

        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString()
        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion
        if ($assemblyVersion -ne '2.0.0.0' -or $productVersion -notmatch '^2\.0\.0(?:\+.*)?$') {
            throw "MCP SDK assembly '$name' is not stable 2.0.0: AssemblyVersion='$assemblyVersion', ProductVersion='$productVersion'."
        }

        $result += [ordered]@{
            name = $name
            assemblyVersion = $assemblyVersion
            productVersion = $productVersion
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

    return $result
}

function Get-RuntimeCatalog {
    $oldWorker = $env:OPEN_TIA_WORKER_EXE
    $hadWorker = Test-Path Env:\OPEN_TIA_WORKER_EXE
    try {
        $env:OPEN_TIA_WORKER_EXE = $resolvedWorker
        $json = (& $resolvedCatalogExecutable --command-catalog | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "Runtime command catalog failed with exit code $LASTEXITCODE."
        }
        return $json | ConvertFrom-Json
    }
    finally {
        if ($hadWorker) { $env:OPEN_TIA_WORKER_EXE = $oldWorker }
        else { Remove-Item Env:\OPEN_TIA_WORKER_EXE -ErrorAction SilentlyContinue }
    }
}

$catalog = Get-RuntimeCatalog
$sdkAssemblies = @(Get-StableSdkAssemblies)
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $resolvedExecutable
$startInfo.WorkingDirectory = Split-Path -Parent $resolvedExecutable
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.EnvironmentVariables['OPEN_TIA_WORKER_EXE'] = $resolvedWorker
$startInfo.EnvironmentVariables['OPEN_TIA_MCP_LOG_LEVEL'] = 'Error'

$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (-not $process.Start()) {
    throw "Failed to start MCP executable '$resolvedExecutable'."
}

$stderrTask = $process.StandardError.ReadToEndAsync()
$requestId = 0
$coverage = @{}
$negativeChecks = @()
$requiredFailures = @()
$projectBeforeReset = $null
$projectAfterReset = $null
$toolsHashBeforeReset = $null
$toolsHashAfterReset = $null
$selectedInstanceId = 0
$artifactRoot = $null
$artifactEvidence = @()
$failure = $null
$ownedWorkerPids = @()

function Read-Response {
    param([Parameter(Mandatory = $true)][int]$ExpectedId)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = [Math]::Max(1, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        $readTask = $process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remaining)) { break }
        $line = $readTask.Result
        if ($null -eq $line) { break }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        $response = $line | ConvertFrom-Json
        if ($null -ne $response.id -and [int]$response.id -eq $ExpectedId) {
            return $response
        }
    }

    throw "Timed out waiting for MCP response id $ExpectedId."
}

function Invoke-Request {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][object]$Params
    )

    $requestParams = [ordered]@{}
    if ($Params -is [Collections.IDictionary]) {
        foreach ($key in $Params.Keys) { $requestParams[[string]$key] = $Params[$key] }
    }
    else {
        foreach ($property in $Params.PSObject.Properties) {
            $requestParams[$property.Name] = $property.Value
        }
    }
    $requestParams['_meta'] = [ordered]@{
        'io.modelcontextprotocol/protocolVersion' = '2026-07-28'
        'io.modelcontextprotocol/clientInfo' = [ordered]@{
            name = 'OpenTiaToolkit read-only live coverage'
            version = '2.0'
        }
        'io.modelcontextprotocol/clientCapabilities' = @{}
    }

    $script:requestId++
    $request = [ordered]@{
        jsonrpc = '2.0'
        id = $script:requestId
        method = $Method
        params = $requestParams
    }
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress -Depth 40))
    $process.StandardInput.Flush()
    return Read-Response -ExpectedId $script:requestId
}

function Initialize-Coverage {
    param([Parameter(Mandatory = $true)][object[]]$Tools)

    $catalogByName = @{}
    foreach ($command in @($catalog.commands)) {
        $catalogByName[[string]$command.name] = $command
    }
    foreach ($tool in $Tools) {
        $name = [string]$tool.name
        $command = $catalogByName[$name]
        $coverage[$name] = [ordered]@{
            name = $name
            category = if ($null -ne $command) { [string]$command.category } else { 'MCP Infrastructure' }
            risk = if ($null -ne $command) { [string]$command.risk } else { 'Infrastructure' }
            schemaValidated = $true
            attemptedLive = $false
            disposition = 'SchemaValidatedOnly'
            reason = $null
            elapsedMs = $null
            success = $null
            errorCode = $null
            message = $null
        }
    }
}

function Set-CoverageSkip {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Disposition,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    if ($coverage.ContainsKey($Name) -and -not $coverage[$Name].attemptedLive) {
        $coverage[$Name].disposition = $Disposition
        $coverage[$Name].reason = $Reason
    }
}

function Invoke-ToolAttempt {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [hashtable]$Arguments = @{},
        [switch]$Required
    )

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-Request -Method 'tools/call' -Params ([ordered]@{
        name = $Name
        arguments = $Arguments
    })
    $watch.Stop()

    $protocolError = $response.PSObject.Properties['error']
    if ($null -ne $protocolError -and $null -ne $protocolError.Value) {
        throw "Tool '$Name' returned JSON-RPC error: $($protocolError.Value | ConvertTo-Json -Compress -Depth 20)"
    }

    $result = $response.result
    $isError = $null -ne $result.PSObject.Properties['isError'] -and [bool]$result.isError
    $structured = $result.structuredContent
    $entry = $coverage[$Name]
    $entry.attemptedLive = $true
    $entry.disposition = if ($isError) { 'LiveToolError' } else { 'LiveSuccess' }
    $entry.reason = if ($isError) { 'The command was safely invoked but reported that it was unavailable or inapplicable in the open project.' } else { $null }
    $entry.elapsedMs = $watch.ElapsedMilliseconds
    $entry.success = -not $isError
    $entry.errorCode = if ($isError -and $null -ne $structured.error) { [string]$structured.error.code } else { $null }
    $entry.message = [string]$structured.message

    if ($Required -and $isError) {
        $script:requiredFailures += "$Name`: $($entry.errorCode) $($entry.message)"
        throw "Required live MCP tool '$Name' failed: $($entry.errorCode) $($entry.message)"
    }
    return $response
}

function Invoke-NegativeToolCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [hashtable]$Arguments = @{},
        [Parameter(Mandatory = $true)][string]$ExpectedErrorCode,
        [Parameter(Mandatory = $true)][string]$CheckName
    )

    $response = Invoke-Request -Method 'tools/call' -Params ([ordered]@{
        name = $Name
        arguments = $Arguments
    })
    $result = $response.result
    $actual = if ($null -ne $result.structuredContent.error) {
        [string]$result.structuredContent.error.code
    }
    else {
        ''
    }
    if (-not [bool]$result.isError -or $actual -ne $ExpectedErrorCode) {
        throw "Negative check '$CheckName' expected tool error '$ExpectedErrorCode', observed '$actual'."
    }
    $script:negativeChecks += [ordered]@{
        check = $CheckName
        status = 'Passed'
        errorCode = $actual
    }
}

function Invoke-NegativeProtocolCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$ExpectedCode,
        [Parameter(Mandatory = $true)][string]$CheckName
    )

    $response = Invoke-Request -Method 'tools/call' -Params ([ordered]@{
        name = $Name
        arguments = @{}
    })
    $error = $response.PSObject.Properties['error']
    if ($null -eq $error -or $null -eq $error.Value -or [int]$error.Value.code -ne $ExpectedCode) {
        throw "Negative check '$CheckName' expected JSON-RPC error $ExpectedCode."
    }
    $script:negativeChecks += [ordered]@{
        check = $CheckName
        status = 'Passed'
        jsonRpcCode = [int]$error.Value.code
    }
}

function Get-Data {
    param([Parameter(Mandatory = $true)][object]$Response)
    return $Response.result.structuredContent.data
}

try {
    $discover = Invoke-Request -Method 'server/discover' -Params @{}
    if (-not (@($discover.result.supportedVersions) -contains '2026-07-28') -or
        [string]$discover.result.resultType -ne 'complete') {
        throw 'Modern server/discover did not prove MCP 2026-07-28 support.'
    }

    $toolsBefore = Invoke-Request -Method 'tools/list' -Params @{}
    $tools = @($toolsBefore.result.tools)
    if ([string]$toolsBefore.result.resultType -ne 'complete') {
        throw 'Modern tools/list did not return resultType=complete.'
    }
    if ($tools.Count -ne ([int]$catalog.commandCount + 3)) {
        throw "MCP tools/list count $($tools.Count) does not equal runtime catalog $($catalog.commandCount) plus three infrastructure tools."
    }
    $expectedNames = @($catalog.commands.name) + @('operation-status', 'cancel-operation', 'reset-session')
    $difference = @(Compare-Object -ReferenceObject $expectedNames -DifferenceObject @($tools.name))
    if ($difference.Count -gt 0) {
        throw "MCP/runtime tool drift: $($difference | ConvertTo-Json -Compress -Depth 10)"
    }
    foreach ($tool in $tools) {
        if ([string]$tool.inputSchema.type -ne 'object' -or
            $tool.inputSchema.additionalProperties -ne $false) {
            throw "Tool '$($tool.name)' has an invalid root input schema."
        }
        foreach ($property in $tool.inputSchema.properties.PSObject.Properties) {
            if ([string]$property.Value.type -eq 'array' -and
                [string]::IsNullOrWhiteSpace([string]$property.Value.items.type)) {
                throw "Tool '$($tool.name)' array '$($property.Name)' has no items.type."
            }
        }
    }
    Initialize-Coverage -Tools $tools
    $toolsHashBeforeReset = Get-Sha256Text -Text (
        $toolsBefore.result.tools | ConvertTo-Json -Compress -Depth 40)

    [void](Invoke-ToolAttempt -Name 'operation-status' -Required)
    [void](Invoke-ToolAttempt -Name 'portal-status' -Required)
    [void](Invoke-ToolAttempt -Name 'version' -Required)
    [void](Invoke-ToolAttempt -Name 'capabilities' -Required)
    [void](Invoke-ToolAttempt -Name 'doctor' -Required)
    [void](Invoke-ToolAttempt -Name 'describe-api' -Required)
    [void](Invoke-ToolAttempt -Name 'diff-api-profile' -Required)
    [void](Invoke-ToolAttempt -Name 'describe-plc-code-formats' -Required)

    $instancesResponse = Invoke-ToolAttempt -Name 'list-instances' -Required
    $instances = @((Get-Data $instancesResponse).instances)
    if ($instances.Count -lt 1) { throw 'No shareable TIA Portal instance was discovered.' }
    $selected = @(
        if ($InstanceId -gt 0) {
            $instances | Where-Object { [int]$_.processId -eq $InstanceId }
        }
        else {
            $instances
        }
    )
    if ($selected.Count -ne 1) {
        throw "Expected one selected TIA instance; pass -InstanceId using: $($instances | ConvertTo-Json -Compress -Depth 20)"
    }
    $selectedInstanceId = [int]$selected[0].processId
    [void](Invoke-ToolAttempt -Name 'connect-instance' -Arguments @{ 'instance-id' = $selectedInstanceId } -Required)

    $projectsResponse = Invoke-ToolAttempt -Name 'list-projects' -Required
    $projects = @(Get-Data $projectsResponse)
    if ($projects.Count -lt 1) { throw 'The selected instance has no open project.' }
    $projectBeforeReset = $projects | ConvertTo-Json -Compress -Depth 30
    [void](Invoke-ToolAttempt -Name 'get-project-info' -Required)
    [void](Invoke-ToolAttempt -Name 'get-project-tree' -Required)

    $devicesResponse = Invoke-ToolAttempt -Name 'list-devices' -Required
    $devices = @(Get-Data $devicesResponse)
    if ($devices.Count -lt 1) { throw 'The open project returned no devices.' }
    $deviceName = [string]$devices[0].name
    if (-not [string]::IsNullOrWhiteSpace($deviceName)) {
        [void](Invoke-ToolAttempt -Name 'get-device-info' -Arguments @{ 'device-name' = $deviceName } -Required)
        [void](Invoke-ToolAttempt -Name 'get-hardware-item-capabilities' -Arguments @{
            'device-name' = $deviceName
        })
    }

    $plcsResponse = Invoke-ToolAttempt -Name 'list-plcs' -Required
    $plcs = @(Get-Data $plcsResponse)
    if ($plcs.Count -lt 1) { throw 'The open project returned no PLC.' }
    $plcName = [string]$plcs[0].name
    [void](Invoke-ToolAttempt -Name 'open-plc' -Arguments @{ 'plc-name' = $plcName } -Required)
    [void](Invoke-ToolAttempt -Name 'get-software-tree' -Arguments @{ 'plc-name' = $plcName } -Required)
    $blocksResponse = Invoke-ToolAttempt -Name 'get-blocks' -Arguments @{ 'plc-name' = $plcName } -Required
    $blocks = @(Get-Data $blocksResponse)
    if ($blocks.Count -lt 1) { throw 'The selected PLC returned no blocks.' }
    $blockName = [string]$blocks[0].name
    [void](Invoke-ToolAttempt -Name 'get-block-info' -Arguments @{
        'plc-name' = $plcName
        'block-name' = $blockName
    } -Required)
    [void](Invoke-ToolAttempt -Name 'search-blocks' -Arguments @{
        'plc-name' = $plcName
        pattern = '.*'
        'search-in' = 'name'
    } -Required)

    foreach ($call in @(
        @{ Name = 'list-external-sources'; Arguments = @{ 'plc-name' = $plcName } },
        @{ Name = 'list-technology-objects'; Arguments = @{ 'plc-name' = $plcName } },
        @{ Name = 'list-watch-tables'; Arguments = @{ 'plc-name' = $plcName } },
        @{ Name = 'list-plc-alarm-text-lists'; Arguments = @{ 'plc-name' = $plcName } },
        @{ Name = 'get-cross-references'; Arguments = @{ 'plc-name' = $plcName; name = $blockName } },
        @{ Name = 'discover-plc-code-capabilities'; Arguments = @{ 'plc-name' = $plcName; 'object-name' = $blockName } },
        @{ Name = 'discover-plc-simulator-capabilities'; Arguments = @{ 'plc-name' = $plcName } },
        @{ Name = 'get-online-status'; Arguments = @{ 'plc-name' = $plcName } },
        @{ Name = 'compare-software'; Arguments = @{ 'plc-name' = $plcName } },
        @{ Name = 'get-vci-block-formats'; Arguments = @{ 'plc-name' = $plcName; 'block-name' = $blockName } },
        @{ Name = 'audit-hardware-tag-mappings'; Arguments = @{ 'plc-name' = $plcName } },
        @{ Name = 'export-tags'; Arguments = @{ 'plc-name' = $plcName } }
    )) {
        [void](Invoke-ToolAttempt -Name $call.Name -Arguments $call.Arguments)
    }

    $hmisResponse = Invoke-ToolAttempt -Name 'list-hmis' -Required
    $hmis = @(Get-Data $hmisResponse)
    if ($hmis.Count -lt 1) { throw 'The open project returned no Unified HMI.' }
    $hmiName = [string]$hmis[0].name
    [void](Invoke-ToolAttempt -Name 'open-unified-hmi' -Arguments @{ 'hmi-name' = $hmiName } -Required)
    $screensResponse = Invoke-ToolAttempt -Name 'list-hmi-screens' -Arguments @{ 'hmi-name' = $hmiName } -Required
    $screens = @(Get-Data $screensResponse)
    if ($screens.Count -lt 1) { throw 'The selected Unified HMI returned no screens.' }
    $screenPath = if ($screens[0].PSObject.Properties['path']) {
        [string]$screens[0].path
    }
    else {
        [string]$screens[0].name
    }
    [void](Invoke-ToolAttempt -Name 'list-hmi-tags' -Arguments @{
        'hmi-name' = $hmiName
        'allow-full-scan' = $true
        'max-results' = 200
        'max-depth' = 4
        'include-details' = $true
        'include-logging-counts' = $false
    } -Required)
    foreach ($name in @(
        'get-hmi-screen-info',
        'get-hmi-screen-summary',
        'get-hmi-screen-json',
        'inspect-hmi-screen-json-capabilities'
    )) {
        [void](Invoke-ToolAttempt -Name $name -Arguments @{
            'hmi-name' = $hmiName
            'screen-path' = $screenPath
        })
    }

    $artifactRoot = Join-Path (
        Join-Path ([IO.Path]::GetTempPath()) 'OpenTiaToolkit') (
            'McpReadOnlyCoverage-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
    $blockExportPath = Join-Path $artifactRoot 'representative-block.xml'
    [void](Invoke-ToolAttempt -Name 'export-block' -Arguments @{
        'plc-name' = $plcName
        'block-name' = $blockName
        'export-path' = $blockExportPath
    })
    $hmiExportPath = Join-Path $artifactRoot 'representative-hmi.json'
    [void](Invoke-ToolAttempt -Name 'export-hmi-json' -Arguments @{
        'hmi-name' = $hmiName
        'output-path' = $hmiExportPath
    })
    if (Test-Path -LiteralPath $hmiExportPath -PathType Leaf) {
        [void](Invoke-ToolAttempt -Name 'audit-hmi-label-proximity' -Arguments @{
            'input-path' = $hmiExportPath
        })
        [void](Invoke-ToolAttempt -Name 'diff-hmi-json' -Arguments @{
            'base-path' = $hmiExportPath
            'modified-path' = $hmiExportPath
            'patch-path' = (Join-Path $artifactRoot 'empty-hmi-diff.json')
        })
    }
    $artifactEvidence = @(
        Get-ChildItem -LiteralPath $artifactRoot -File -ErrorAction SilentlyContinue |
            ForEach-Object {
                [ordered]@{
                    path = $_.FullName
                    bytes = $_.Length
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
    )

    foreach ($name in @(
        'hmi-status',
        'list-hmi-item-types',
        'list-project-languages',
        'inspect-project-security',
        'get-multiuser-status',
        'get-multiuser-session-status',
        'list-multiuser-servers',
        'list-global-libraries',
        'list-library-types',
        'list-master-copies',
        'check-library-updates',
        'list-vci-workspaces',
        'find-hardware-catalog-items',
        'get-hardware-tree',
        'get-hardware-networks',
        'get-hardware-io-addresses',
        'get-hardware-drive-telegrams',
        'get-hardware-drive-parameters',
        'list-unified-alarms',
        'list-unified-text-lists',
        'get-unified-runtime-settings',
        'get-plc-alarm-text-list-details',
        'get-technology-object-details',
        'get-watch-table-details',
        'validate-safety-configuration',
        'list-plc-code-transactions',
        'list-plc-code-evidence'
    )) {
        [void](Invoke-ToolAttempt -Name $name)
    }

    $vciWorkspaces = $null
    if ($coverage['list-vci-workspaces'].success -eq $true) {
        $vciWorkspacesResponse = Invoke-Request -Method 'tools/call' -Params ([ordered]@{
            name = 'list-vci-workspaces'
            arguments = @{}
        })
        $vciWorkspaces = @($vciWorkspacesResponse.result.structuredContent.data)
    }
    if (@($vciWorkspaces).Count -gt 0) {
        $workspaceName = [string]$vciWorkspaces[0].name
        [void](Invoke-ToolAttempt -Name 'get-vci-workspace' -Arguments @{ 'workspace-name' = $workspaceName })
        [void](Invoke-ToolAttempt -Name 'list-vci-mappings' -Arguments @{ 'workspace-name' = $workspaceName })
    }

    $deniedProjectPath = Join-Path $artifactRoot 'must-not-be-created.ap21'
    Invoke-NegativeToolCheck -Name 'create-project' -Arguments @{
        'project-path' = $deniedProjectPath
    } -ExpectedErrorCode 'PolicyDenied' -CheckName 'unconfirmed-mutation'
    if (Test-Path -LiteralPath $deniedProjectPath) {
        throw "Unconfirmed mutation created '$deniedProjectPath'."
    }
    Invoke-NegativeToolCheck -Name 'validate-blueprint' -Arguments @{
        'blueprint-path' = 'C:\Windows\win.ini'
    } -ExpectedErrorCode 'PolicyDenied' -CheckName 'outside-allowed-root'
    Invoke-NegativeToolCheck -Name 'get-block-info' -Arguments @{
        'plc-name' = $plcName
        'block-name' = '__OpenTiaToolkit_Nonexistent_Block__'
    } -ExpectedErrorCode 'NotFound' -CheckName 'nonexistent-project-object'
    Invoke-NegativeProtocolCheck -Name '__tool_does_not_exist__' -ExpectedCode -32602 -CheckName 'unknown-tool'
    Invoke-NegativeProtocolCheck -Name ' ' -ExpectedCode -32602 -CheckName 'blank-tool-name'

    [void](Invoke-ToolAttempt -Name 'cancel-operation' -Required)
    [void](Invoke-ToolAttempt -Name 'reset-session' -Required)
    [void](Invoke-ToolAttempt -Name 'connect-instance' -Arguments @{ 'instance-id' = $selectedInstanceId } -Required)
    $projectsAfterResponse = Invoke-ToolAttempt -Name 'list-projects' -Required
    $projectAfterReset = @(Get-Data $projectsAfterResponse) | ConvertTo-Json -Compress -Depth 30
    if ($projectAfterReset -ne $projectBeforeReset) {
        throw "Project identity changed across reset/reconnect. Before='$projectBeforeReset' After='$projectAfterReset'."
    }
    $toolsAfter = Invoke-Request -Method 'tools/list' -Params @{}
    $toolsHashAfterReset = Get-Sha256Text -Text (
        $toolsAfter.result.tools | ConvertTo-Json -Compress -Depth 40)
    if ($toolsHashAfterReset -ne $toolsHashBeforeReset) {
        throw 'tools/list changed across reset and reconnect.'
    }

    foreach ($command in @($catalog.commands)) {
        $name = [string]$command.name
        if ($coverage[$name].attemptedLive) { continue }

        if ([string]$command.risk -ne 'ReadOnly') {
            Set-CoverageSkip -Name $name -Disposition 'SkippedByRisk' `
                -Reason "Catalog risk is '$($command.risk)'; the original project was read-only."
            continue
        }

        if ($name -in @(
            'compile-software',
            'open-project',
            'open-tia-portal',
            'connect-portal',
            'begin-plc-code-transaction',
            'begin-promoted-plc-code-transaction',
            'approve-plc-code-impact',
            'discard-plc-code-transaction',
            'plan-plc-code-transaction',
            'recover-plc-code-transaction',
            'close-global-library'
        )) {
            Set-CoverageSkip -Name $name -Disposition 'SkippedForOriginalProjectSafety' `
                -Reason 'The command can compile, launch/open state, or alter retained transaction/session state despite its read-only catalog classification.'
            continue
        }

        $requiredParameters = @($command.parameters | Where-Object { $_.required } | ForEach-Object { $_.name })
        if ($requiredParameters.Count -gt 0) {
            Set-CoverageSkip -Name $name -Disposition 'SkippedMissingPrerequisite' `
                -Reason "No verified applicable identifier/input was discovered for required parameter(s): $($requiredParameters -join ', ')."
        }
        else {
            Set-CoverageSkip -Name $name -Disposition 'SkippedNotApplicable' `
                -Reason 'The capability was not applicable to the open project or required a subsystem state that was not safely established.'
        }
    }
}
catch {
    $failure = $_
}
finally {
    try {
        $ownedWorkerPids = @(
            Get-CimInstance Win32_Process -Filter "ParentProcessId = $($process.Id)" -ErrorAction Stop |
                Where-Object { $_.Name -like 'OpenTiaToolkit.Worker.*' } |
                ForEach-Object { [int]$_.ProcessId }
        )
    }
    catch {
        $ownedWorkerPids = @()
    }

    try { $process.StandardInput.Close() } catch {}
    $forcedShutdown = $false
    if (-not $process.WaitForExit(5000)) {
        $forcedShutdown = $true
        $process.Kill()
        $process.WaitForExit()
    }
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
    $process.Dispose()

    $orphanedWorkers = @(
        $ownedWorkerPids |
            Where-Object { $null -ne (Get-Process -Id $_ -ErrorAction SilentlyContinue) }
    )
    if ($forcedShutdown -and $null -eq $failure) {
        $failure = [Management.Automation.ErrorRecord]::new(
            [TimeoutException]::new('MCP did not exit within five seconds after stdin closed.'),
            'McpForcedShutdown',
            [Management.Automation.ErrorCategory]::OperationTimeout,
            $resolvedExecutable)
    }
    elseif ($exitCode -ne 0 -and $null -eq $failure) {
        $failure = [Management.Automation.ErrorRecord]::new(
            [InvalidOperationException]::new("MCP exited with code $exitCode. stderr: $stderr"),
            'McpExitCode',
            [Management.Automation.ErrorCategory]::InvalidResult,
            $resolvedExecutable)
    }
    elseif ($orphanedWorkers.Count -gt 0 -and $null -eq $failure) {
        $failure = [Management.Automation.ErrorRecord]::new(
            [InvalidOperationException]::new("MCP left worker process(es) running: $($orphanedWorkers -join ', ')."),
            'McpOrphanWorker',
            [Management.Automation.ErrorCategory]::ResourceBusy,
            $resolvedWorker)
    }
}

$matrix = @($coverage.Values | Sort-Object name)
$report = [ordered]@{
    status = if ($null -eq $failure -and $requiredFailures.Count -eq 0) { 'Passed' } else { 'Failed' }
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    protocolVersion = '2026-07-28'
    executable = [ordered]@{
        path = $resolvedExecutable
        sha256 = (Get-FileHash -LiteralPath $resolvedExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    worker = [ordered]@{
        path = $resolvedWorker
        sha256 = (Get-FileHash -LiteralPath $resolvedWorker -Algorithm SHA256).Hash.ToLowerInvariant()
        lastWriteTimeUtc = (Get-Item -LiteralPath $resolvedWorker).LastWriteTimeUtc.ToString('O')
    }
    sdkAssemblies = $sdkAssemblies
    runtimeCommandCount = [int]$catalog.commandCount
    infrastructureToolCount = 3
    toolCount = $matrix.Count
    selectedInstanceId = $selectedInstanceId
    projectIdentityBeforeReset = $projectBeforeReset
    projectIdentityAfterReset = $projectAfterReset
    safeTempArtifactRoot = $artifactRoot
    safeTempArtifacts = $artifactEvidence
    toolsHashBeforeReset = $toolsHashBeforeReset
    toolsHashAfterReset = $toolsHashAfterReset
    negativeChecks = $negativeChecks
    summary = [ordered]@{
        schemaValidated = @($matrix | Where-Object { $_.schemaValidated }).Count
        liveSuccess = @($matrix | Where-Object { $_.disposition -eq 'LiveSuccess' }).Count
        liveToolError = @($matrix | Where-Object { $_.disposition -eq 'LiveToolError' }).Count
        skippedByRisk = @($matrix | Where-Object { $_.disposition -eq 'SkippedByRisk' }).Count
        skippedForOriginalProjectSafety = @($matrix | Where-Object { $_.disposition -eq 'SkippedForOriginalProjectSafety' }).Count
        skippedMissingPrerequisite = @($matrix | Where-Object { $_.disposition -eq 'SkippedMissingPrerequisite' }).Count
        skippedNotApplicable = @($matrix | Where-Object { $_.disposition -eq 'SkippedNotApplicable' }).Count
    }
    coverage = $matrix
    requiredFailures = $requiredFailures
    failure = if ($null -ne $failure) { $failure.Exception.Message } else { $null }
}
[IO.File]::WriteAllText(
    $resolvedReport,
    ($report | ConvertTo-Json -Depth 50),
    [Text.UTF8Encoding]::new($false))

if ($null -ne $failure) { throw $failure }

[pscustomobject]@{
    Status = $report.status
    ReportPath = $resolvedReport
    ProtocolVersion = $report.protocolVersion
    RuntimeCommandCount = $report.runtimeCommandCount
    ToolCount = $report.toolCount
    SelectedInstanceId = $report.selectedInstanceId
    SchemaValidated = $report.summary.schemaValidated
    LiveSuccess = $report.summary.liveSuccess
    LiveToolError = $report.summary.liveToolError
    SkippedByRisk = $report.summary.skippedByRisk
    SkippedForOriginalProjectSafety = $report.summary.skippedForOriginalProjectSafety
    SkippedMissingPrerequisite = $report.summary.skippedMissingPrerequisite
    SkippedNotApplicable = $report.summary.skippedNotApplicable
    NegativeChecks = @($report.negativeChecks).Count
    ToolsStableAcrossReset = $report.toolsHashBeforeReset -eq $report.toolsHashAfterReset
    ProjectStableAcrossReset = $report.projectIdentityBeforeReset -eq $report.projectIdentityAfterReset
}
