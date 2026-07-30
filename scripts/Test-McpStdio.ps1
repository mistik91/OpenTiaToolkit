[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [string]$CommandCatalogExecutablePath = '',

    [string]$WorkerExecutablePath = '',

    [switch]$LiveProject,

    [ValidateRange(0, [int]::MaxValue)]
    [int]$LiveInstanceId = 0,

    [string]$DisposableArchivePath = '',

    [switch]$DisposableProjectOnly,

    [string]$PlcCodeAssetsRoot = '',

    [ValidateRange(5, 300)]
    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$executableDirectory = Split-Path -Parent $resolvedExecutable
$payloadRoot = Split-Path -Parent $executableDirectory

function Assert-StableMcpSdkAssemblies {
    $observed = @()
    foreach ($assemblyFileName in @('ModelContextProtocol.dll', 'ModelContextProtocol.Core.dll')) {
        $assemblyPath = Join-Path $executableDirectory $assemblyFileName
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "The MCP payload is missing required stable SDK assembly '$assemblyPath'."
        }

        $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion
        if ($assemblyVersion -ne '2.0.0.0' -or
            [string]::IsNullOrWhiteSpace($productVersion) -or
            $productVersion -notmatch '^2\.0\.0(?:\+.*)?$' -or
            $productVersion -match '(?i)preview|prerelease|alpha|beta|rc') {
            throw "MCP SDK assembly '$assemblyFileName' is not stable 2.0.0. AssemblyVersion='$assemblyVersion', ProductVersion='$productVersion'."
        }

        $observed += "$assemblyFileName=$productVersion"
    }

    return $observed
}

$mcpSdkAssemblies = @(Assert-StableMcpSdkAssemblies)

$stagedWorker = ''
if (-not [string]::IsNullOrWhiteSpace($WorkerExecutablePath)) {
    $stagedWorker = (Resolve-Path -LiteralPath $WorkerExecutablePath).Path
}
else {
    foreach ($candidate in @(
        (Join-Path $payloadRoot 'worker-v21\OpenTiaToolkit.Worker.V21.exe'),
        (Join-Path $executableDirectory 'worker-v21\OpenTiaToolkit.Worker.V21.exe')
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $stagedWorker = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $resolvedExecutable
$startInfo.WorkingDirectory = $executableDirectory
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$stderrMarker = 'Ignoring invalid OPEN_TIA_MCP_LOG_LEVEL value'
$startInfo.EnvironmentVariables['OPEN_TIA_MCP_LOG_LEVEL'] = 'InvalidForStdioSeparationTest'

# A staged MCP payload must exercise the worker shipped beside it instead of
# inheriting a developer-machine OPEN_TIA_WORKER_EXE value.
if (-not [string]::IsNullOrWhiteSpace($stagedWorker)) {
    $startInfo.EnvironmentVariables['OPEN_TIA_WORKER_EXE'] = $stagedWorker
}

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (-not $process.Start()) {
    throw "Failed to start MCP executable '$resolvedExecutable'."
}

$requestId = 0
$script:requestEra = 'Modern'
$stderrTask = $process.StandardError.ReadToEndAsync()
$ownedWorkerPids = @()

function Read-McpResponse {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ExpectedId,

        [switch]$AllowError
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
            if (-not $AllowError -and $null -ne $errorProperty -and $null -ne $errorProperty.Value) {
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
        [object]$Params,

        [ValidateSet('Modern', 'Legacy')]
        [string]$Era = $script:requestEra,

        [switch]$AllowError
    )

    $requestParams = [ordered]@{}
    if ($Params -is [System.Collections.IDictionary]) {
        foreach ($key in $Params.Keys) {
            $requestParams[[string]$key] = $Params[$key]
        }
    }
    else {
        foreach ($property in $Params.PSObject.Properties) {
            $requestParams[$property.Name] = $property.Value
        }
    }
    if ($Era -eq 'Modern') {
        $requestParams['_meta'] = [ordered]@{
            'io.modelcontextprotocol/protocolVersion' = '2026-07-28'
            'io.modelcontextprotocol/clientInfo' = [ordered]@{
                name = 'OpenTiaToolkit raw stdio smoke'
                version = '2.0'
            }
            'io.modelcontextprotocol/clientCapabilities' = @{}
        }
    }

    $script:requestId++
    $request = [ordered]@{
        jsonrpc = '2.0'
        id = $script:requestId
        method = $Method
        params = $requestParams
    }
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress -Depth 30))
    $process.StandardInput.Flush()
    return Read-McpResponse -ExpectedId $script:requestId -AllowError:$AllowError
}

function Invoke-McpRequestExpectProtocolError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [object]$Params,

        [Parameter(Mandatory = $true)]
        [int]$ExpectedCode
    )

    $response = Invoke-McpRequest -Method $Method -Params $Params -AllowError
    $errorProperty = $response.PSObject.Properties['error']
    if ($null -eq $errorProperty -or $null -eq $errorProperty.Value) {
        throw "MCP request '$Method' was expected to return JSON-RPC error $ExpectedCode, but it succeeded."
    }
    if ([int]$errorProperty.Value.code -ne $ExpectedCode) {
        throw "MCP request '$Method' returned JSON-RPC error $($errorProperty.Value.code), expected ${ExpectedCode}: $($errorProperty.Value | ConvertTo-Json -Compress -Depth 20)"
    }

    return $response
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

function Invoke-McpToolExpectError {
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
    if ($null -eq $isErrorProperty -or $isErrorProperty.Value -ne $true) {
        throw "MCP tool '$Name' was expected to fail, but it succeeded."
    }
    return $response
}

function Assert-ToolResultShape {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Response,

        [Parameter(Mandatory = $true)]
        [bool]$ExpectedIsError,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Modern', 'Legacy')]
        [string]$Era
    )

    $result = $Response.result
    if ($null -eq $result) {
        throw 'tools/call response did not contain a result.'
    }
    if ($null -eq $result.PSObject.Properties['structuredContent']) {
        throw 'tools/call response did not contain structuredContent.'
    }
    $isErrorProperty = $result.PSObject.Properties['isError']
    if ($null -eq $isErrorProperty -or [bool]$isErrorProperty.Value -ne $ExpectedIsError) {
        throw "tools/call isError was '$($isErrorProperty.Value)', expected '$ExpectedIsError'."
    }
    $textBlocks = @($result.content | Where-Object { $_.type -eq 'text' })
    if ($textBlocks.Count -lt 1 -or [string]::IsNullOrWhiteSpace([string]$textBlocks[0].text)) {
        throw 'tools/call response did not contain text fallback content.'
    }
    $fallback = $textBlocks[0].text | ConvertFrom-Json
    if ([string]$fallback.correlationId -ne [string]$result.structuredContent.correlationId -or
        [bool]$fallback.success -ne [bool]$result.structuredContent.success -or
        [string]$fallback.message -ne [string]$result.structuredContent.message) {
        throw 'tools/call text fallback did not match structuredContent.'
    }

    $resultTypeProperty = $result.PSObject.Properties['resultType']
    if ($Era -eq 'Modern') {
        if ($null -eq $resultTypeProperty -or [string]$resultTypeProperty.Value -ne 'complete') {
            throw 'Modern tools/call response did not contain resultType=complete.'
        }
    }
    elseif ($null -ne $resultTypeProperty) {
        throw 'Legacy tools/call response unexpectedly contained resultType.'
    }
}

function Resolve-CommandCatalogExecutable {
    if (-not [string]::IsNullOrWhiteSpace($CommandCatalogExecutablePath)) {
        return (Resolve-Path -LiteralPath $CommandCatalogExecutablePath).Path
    }

    $stagedCli = Join-Path $payloadRoot 'cli\OpenTiaToolkit.CLI.exe'
    if (Test-Path -LiteralPath $stagedCli -PathType Leaf) {
        return $stagedCli
    }

    $current = Split-Path -Parent $resolvedExecutable
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath (Join-Path $current 'OpenTiaToolkit.sln') -PathType Leaf) {
            $configuration = Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $resolvedExecutable))
            $developerCli = Join-Path $current "src\OpenTiaToolkit.CLI\bin\$configuration\net48\OpenTiaToolkit.CLI.exe"
            if (Test-Path -LiteralPath $developerCli -PathType Leaf) {
                return $developerCli
            }
            break
        }

        $parent = Split-Path -Parent $current
        if ([string]::Equals($parent, $current, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $current = $parent
    }

    throw 'Unable to locate the matching OpenTiaToolkit.CLI executable required for runtime command-catalog parity. Pass -CommandCatalogExecutablePath explicitly.'
}

function Get-RuntimeCommandCatalog {
    $catalogExecutable = Resolve-CommandCatalogExecutable
    $catalogStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $catalogStartInfo.FileName = $catalogExecutable
    $catalogStartInfo.Arguments = '--command-catalog'
    $catalogStartInfo.WorkingDirectory = Split-Path -Parent $catalogExecutable
    $catalogStartInfo.UseShellExecute = $false
    $catalogStartInfo.CreateNoWindow = $true
    $catalogStartInfo.RedirectStandardOutput = $true
    $catalogStartInfo.RedirectStandardError = $true
    if (Test-Path -LiteralPath $stagedWorker -PathType Leaf) {
        $catalogStartInfo.EnvironmentVariables['OPEN_TIA_WORKER_EXE'] = $stagedWorker
    }

    $catalogProcess = [System.Diagnostics.Process]::new()
    $catalogProcess.StartInfo = $catalogStartInfo
    if (-not $catalogProcess.Start()) {
        throw "Failed to start command-catalog executable '$catalogExecutable'."
    }
    try {
        $stdout = $catalogProcess.StandardOutput.ReadToEnd()
        $stderr = $catalogProcess.StandardError.ReadToEnd()
        if (-not $catalogProcess.WaitForExit($TimeoutSeconds * 1000)) {
            $catalogProcess.Kill()
            $catalogProcess.WaitForExit()
            throw "Timed out reading runtime command catalog from '$catalogExecutable'."
        }
        if ($catalogProcess.ExitCode -ne 0) {
            throw "Runtime command catalog failed with exit code $($catalogProcess.ExitCode): $stderr"
        }
        return [ordered]@{
            Executable = $catalogExecutable
            Catalog = ($stdout | ConvertFrom-Json)
        }
    }
    finally {
        $catalogProcess.Dispose()
    }
}

function Assert-ToolCatalogParity {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Tools,

        [Parameter(Mandatory = $true)]
        [object]$RuntimeCatalog
    )

    $duplicateNames = @($Tools | Group-Object { ([string]$_.name).ToLowerInvariant() } | Where-Object { $_.Count -ne 1 })
    if ($duplicateNames.Count -gt 0) {
        throw "tools/list returned duplicate tool names: $($duplicateNames.Name -join ', ')"
    }

    foreach ($tool in $Tools) {
        if ([string]::IsNullOrWhiteSpace([string]$tool.name)) {
            throw 'tools/list returned a tool without a name.'
        }
        if ($null -eq $tool.inputSchema -or [string]$tool.inputSchema.type -ne 'object') {
            throw "Tool '$($tool.name)' did not expose an object inputSchema."
        }
        if ($tool.inputSchema.PSObject.Properties['additionalProperties'].Value -ne $false) {
            throw "Tool '$($tool.name)' did not preserve additionalProperties=false."
        }
        foreach ($property in $tool.inputSchema.properties.PSObject.Properties) {
            if ([string]::IsNullOrWhiteSpace([string]$property.Value.type)) {
                throw "Tool '$($tool.name)' parameter '$($property.Name)' did not declare a JSON Schema type."
            }
            if ([string]$property.Value.type -eq 'array' -and
                [string]::IsNullOrWhiteSpace([string]$property.Value.items.type)) {
                throw "Tool '$($tool.name)' array parameter '$($property.Name)' did not declare items.type."
            }
        }
    }

    $infrastructureTools = @('operation-status', 'cancel-operation', 'reset-session')
    $expectedNames = @($RuntimeCatalog.commands.name) + $infrastructureTools
    $observedNames = @($Tools.name)
    $difference = @(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $observedNames)
    if ($difference.Count -gt 0) {
        throw "MCP/runtime command-catalog drift: $($difference | ConvertTo-Json -Compress -Depth 10)"
    }
    if ([int]$RuntimeCatalog.commandCount -ne @($RuntimeCatalog.commands).Count) {
        throw "Runtime command catalog count $($RuntimeCatalog.commandCount) did not match its command array."
    }
    if ($Tools.Count -ne ([int]$RuntimeCatalog.commandCount + $infrastructureTools.Count)) {
        throw "MCP tool count $($Tools.Count) did not equal runtime commands $($RuntimeCatalog.commandCount) plus $($infrastructureTools.Count) infrastructure tools."
    }
}

try {
    $runtimeCatalogResult = Get-RuntimeCommandCatalog
    $runtimeCatalog = $runtimeCatalogResult.Catalog

    $discover = Invoke-McpRequest -Method 'server/discover' -Params @{}
    if (-not (@($discover.result.supportedVersions) -contains '2026-07-28')) {
        throw "server/discover did not advertise MCP 2026-07-28: $($discover.result.supportedVersions -join ', ')"
    }
    if ($null -eq $discover.result.capabilities.tools) {
        throw 'server/discover did not advertise the tools capability.'
    }
    $discoverServerInfo = $discover.result._meta.PSObject.Properties['io.modelcontextprotocol/serverInfo'].Value
    if ([string]::IsNullOrWhiteSpace([string]$discoverServerInfo.name)) {
        throw 'server/discover did not return server identity metadata.'
    }
    if ([string]$discover.result.resultType -ne 'complete') {
        throw 'server/discover did not return resultType=complete.'
    }
    if ($null -eq $discover.result.PSObject.Properties['ttlMs'] -or
        $null -eq $discover.result.PSObject.Properties['cacheScope']) {
        throw 'server/discover did not return the required cache hints.'
    }

    $modernToolsResponse = Invoke-McpRequest -Method 'tools/list' -Params @{}
    $modernTools = @($modernToolsResponse.result.tools)
    if ([string]$modernToolsResponse.result.resultType -ne 'complete') {
        throw 'Modern tools/list did not return resultType=complete.'
    }
    if ($null -eq $modernToolsResponse.result.PSObject.Properties['ttlMs'] -or
        $null -eq $modernToolsResponse.result.PSObject.Properties['cacheScope']) {
        throw 'Modern tools/list did not return the required cache hints.'
    }
    Assert-ToolCatalogParity -Tools $modernTools -RuntimeCatalog $runtimeCatalog

    $modernStatus = Invoke-McpTool -Name 'operation-status'
    Assert-ToolResultShape -Response $modernStatus -ExpectedIsError $false -Era Modern
    $modernPortalStatus = Invoke-McpTool -Name 'portal-status'
    Assert-ToolResultShape -Response $modernPortalStatus -ExpectedIsError $false -Era Modern

    $modernCommandFailure = Invoke-McpToolExpectError -Name 'connect-instance'
    Assert-ToolResultShape -Response $modernCommandFailure -ExpectedIsError $true -Era Modern
    if ([string]$modernCommandFailure.result.structuredContent.error.code -ne 'ValidationFailed') {
        throw "Missing connect-instance arguments returned '$($modernCommandFailure.result.structuredContent.error.code)', expected ValidationFailed."
    }

    $policyFailure = Invoke-McpToolExpectError -Name 'validate-blueprint' -Arguments @{
        # Deliberately vary casing to prove the SDK v2 argument adapter
        # preserves the command surface's case-insensitive lookup behavior.
        'BLUEPRINT-PATH' = 'C:\Windows\win.ini'
    }
    Assert-ToolResultShape -Response $policyFailure -ExpectedIsError $true -Era Modern
    if ([string]$policyFailure.result.structuredContent.error.code -ne 'PolicyDenied') {
        throw "Outside-root path returned '$($policyFailure.result.structuredContent.error.code)', expected PolicyDenied."
    }

    $deniedProjectPath = Join-Path (
        Join-Path ([System.IO.Path]::GetTempPath()) 'OpenTiaToolkit\McpStdioPolicy') (
            'Denied-' + [Guid]::NewGuid().ToString('N') + '.ap21')
    $mutationFailure = Invoke-McpToolExpectError -Name 'create-project' -Arguments @{
        'project-path' = $deniedProjectPath
    }
    Assert-ToolResultShape -Response $mutationFailure -ExpectedIsError $true -Era Modern
    if ([string]$mutationFailure.result.structuredContent.error.code -ne 'PolicyDenied') {
        throw "Unconfirmed mutation returned '$($mutationFailure.result.structuredContent.error.code)', expected PolicyDenied."
    }
    if (Test-Path -LiteralPath $deniedProjectPath) {
        throw "Unconfirmed mutation created '$deniedProjectPath'."
    }

    $unknownTool = Invoke-McpRequestExpectProtocolError -Method 'tools/call' -Params ([ordered]@{
        name = 'tool-that-does-not-exist'
        arguments = @{}
    }) -ExpectedCode -32602
    $malformedToolCall = Invoke-McpRequestExpectProtocolError -Method 'tools/call' -Params ([ordered]@{
        name = ' '
        arguments = @{}
    }) -ExpectedCode -32602

    $script:requestEra = 'Legacy'
    $initialize = Invoke-McpRequest -Method 'initialize' -Params ([ordered]@{
        protocolVersion = '2025-11-25'
        capabilities = @{}
        clientInfo = [ordered]@{ name = 'OpenTiaToolkit legacy stdio smoke'; version = '2.0' }
    })
    if ([string]::IsNullOrWhiteSpace([string]$initialize.result.serverInfo.name)) {
        throw 'MCP initialize response did not contain serverInfo.name.'
    }
    if ([string]$initialize.result.protocolVersion -ne '2025-11-25') {
        throw "Legacy initialize negotiated '$($initialize.result.protocolVersion)', expected 2025-11-25."
    }
    if ($null -ne $initialize.result.PSObject.Properties['resultType']) {
        throw 'Legacy initialize unexpectedly returned resultType.'
    }

    $initialized = [ordered]@{
        jsonrpc = '2.0'
        method = 'notifications/initialized'
        params = @{}
    }
    $process.StandardInput.WriteLine(($initialized | ConvertTo-Json -Compress -Depth 10))
    $process.StandardInput.Flush()

    $legacyToolsResponse = Invoke-McpRequest -Method 'tools/list' -Params @{}
    $legacyTools = @($legacyToolsResponse.result.tools)
    Assert-ToolCatalogParity -Tools $legacyTools -RuntimeCatalog $runtimeCatalog
    foreach ($modernOnlyProperty in @('resultType', 'ttlMs', 'cacheScope')) {
        if ($null -ne $legacyToolsResponse.result.PSObject.Properties[$modernOnlyProperty]) {
            throw "Legacy tools/list unexpectedly returned '$modernOnlyProperty'."
        }
    }

    $catalogDifference = @(Compare-Object -ReferenceObject @($modernTools.name) -DifferenceObject @($legacyTools.name))
    if ($catalogDifference.Count -gt 0) {
        throw "Modern and legacy tools/list results drifted: $($catalogDifference | ConvertTo-Json -Compress -Depth 10)"
    }
    $legacyStatus = Invoke-McpTool -Name 'operation-status'
    Assert-ToolResultShape -Response $legacyStatus -ExpectedIsError $false -Era Legacy

    # Once initialize pins this stdio connection to a legacy revision, every
    # remaining request on the same connection must stay in that era.
    $script:requestEra = 'Legacy'

    $summary = [ordered]@{
        Executable = $resolvedExecutable
        CommandCatalogExecutable = [string]$runtimeCatalogResult.Executable
        Server = [string]$discoverServerInfo.name
        ModernProtocolVersion = '2026-07-28'
        LegacyProtocolVersion = [string]$initialize.result.protocolVersion
        RuntimeCommandCount = [int]$runtimeCatalog.commandCount
        InfrastructureToolCount = 3
        ToolCount = $modernTools.Count
        McpSdkAssemblies = $mcpSdkAssemblies
        WorkerExecutable = $stagedWorker
        ModernSuccessfulToolCalls = 2
        ModernToolExecutionErrors = 3
        ModernProtocolErrors = 2
        LegacySuccessfulToolCalls = 1
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
            $fixtureProjectName = 'phase3-mcp-fixture-' + [Guid]::NewGuid().ToString('N')
            $fixtureProjectPath = Join-Path $archiveDirectory ($fixtureProjectName + '.ap21')
            $blueprintPath = Join-Path $archiveDirectory ($fixtureProjectName + '.blueprint.json')
            [ordered]@{
                schemaVersion = '1.0'
                name = 'Packaged MCP disposable project bootstrap'
                requiredWorker = 'V21'
                steps = @(
                    [ordered]@{ id = 'open'; command = 'open-tia-portal'; arguments = @{ 'with-ui' = $false } }
                    [ordered]@{ id = 'create'; command = 'create-project'; arguments = @{ 'project-path' = $fixtureProjectPath } }
                )
            } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $blueprintPath -Encoding UTF8
            [void](Invoke-McpTool -Name 'validate-blueprint' -Arguments @{ 'blueprint-path' = $blueprintPath })
            $planResponse = Invoke-McpTool -Name 'plan-blueprint' -Arguments @{ 'blueprint-path' = $blueprintPath }
            $applyToken = [string]$planResponse.result.structuredContent.data.ApplyToken
            if ([string]::IsNullOrWhiteSpace($applyToken)) { throw 'Blueprint plan did not return an apply token.' }
            [void](Invoke-McpTool -Name 'apply-blueprint' -Arguments @{ 'apply-token' = $applyToken; 'confirm' = $true })
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
            [void](Invoke-McpTool -Name 'describe-api')
            [void](Invoke-McpTool -Name 'diff-api-profile')
            [void](Invoke-McpTool -Name 'reset-session')
            Remove-Item -LiteralPath $blueprintPath -Force -ErrorAction SilentlyContinue
            $summary['DisposableArchiveCreated'] = $true
            $summary['BlueprintApplied'] = $true
            $summary['ProjectCount'] = $projects.Count
            $summary['ResetVerified'] = $true
        }
        else {
        $instancesResponse = Invoke-McpTool -Name 'list-instances'
        $instanceData = $instancesResponse.result.structuredContent.data
        $instances = @($instanceData.instances)
        if ($instances.Count -lt 1) {
            throw 'Live project smoke did not find a running TIA instance.'
        }

        $selectedInstances = @(
            if ($LiveInstanceId -gt 0) {
                $instances | Where-Object { [int]$_.processId -eq $LiveInstanceId }
            }
            else {
                $instances
            }
        )
        if ($selectedInstances.Count -ne 1) {
            throw "Live project smoke requires one selected TIA instance. Found $($instances.Count); pass -LiveInstanceId using this discovery result: $($instances | ConvertTo-Json -Compress -Depth 20)"
        }

        $processId = [int]$selectedInstances[0].processId
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

        if (-not [string]::IsNullOrWhiteSpace($PlcCodeAssetsRoot)) {
            $assetRoot = [System.IO.Path]::GetFullPath($PlcCodeAssetsRoot)
            $ladDirectory = Join-Path $assetRoot 's7dcl-lad-authoring\assets'
            $dataDirectory = Join-Path $assetRoot 's7dcl-data-authoring\assets'
            $sclDirectory = Join-Path $assetRoot 'scl-source-authoring\assets'
            $sclCases = @('Codex_SCL_Probe', 'Codex_SCL_Stateful_Probe', 'Codex_SCL_Type_Probe')
            $qualificationRoot = Join-Path (Join-Path ([System.IO.Path]::GetTempPath()) 'OpenTiaToolkit') ('Qualification-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Force -Path $qualificationRoot | Out-Null
            $qualificationName = 'Codex_Qualification_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
            $qualificationPath = Join-Path $qualificationRoot ($qualificationName + '.s7dcl')
            $qualificationText = [IO.File]::ReadAllText((Join-Path $ladDirectory 'Codex_LAD_Probe.s7dcl')).Replace('Codex_LAD_Probe', $qualificationName).Replace('59991', '65010')
            [IO.File]::WriteAllText($qualificationPath, $qualificationText, [Text.UTF8Encoding]::new($true))
            [void](Invoke-McpTool -Name 'validate-plc-code-artifact' -Arguments @{ 'code-format' = 's7dcl'; 'input-path' = $qualificationPath })
            $qualificationCapability = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Block'; 'object-name' = $qualificationName; 'group-path' = ''; 'operation' = 'Create'; 'block-type' = 'FC'; 'requested-language' = 'LAD' }).result.structuredContent.data
            if ([string]::IsNullOrWhiteSpace([string]$qualificationCapability.Environment.TiaVersion) -or [string]::IsNullOrWhiteSpace([string]$qualificationCapability.Environment.TiaBuild)) {
                throw "Qualification capability did not capture exact TIA version/build evidence: $($qualificationCapability.Environment | ConvertTo-Json -Compress -Depth 10)"
            }
            $qualificationRun = (Invoke-McpTool -Name 'run-plc-code-probe' -Arguments @{ 'plc-name' = $plcNames[0]; 'capability-ids' = [string]$qualificationCapability.CapabilityId; 'code-format' = 's7dcl'; 'input-path' = $qualificationPath; 'group-path' = ''; 'force' = $true; 'confirm' = $true }).result.structuredContent.data
            if ([string]$qualificationRun.Evidence.Status -ne 'Verified' -or $qualificationRun.ProbeApplied -ne $true -or $qualificationRun.CleanupProven -ne $true -or $qualificationRun.Evidence.CleanupProven -ne $true) {
                throw "Disposable qualification probe was not fully verified and cleaned: $($qualificationRun | ConvertTo-Json -Compress -Depth 30)"
            }
            $qualificationEvidence = (Invoke-McpTool -Name 'get-plc-code-evidence' -Arguments @{ 'evidence-id' = [string]$qualificationRun.Evidence.EvidenceId }).result.structuredContent.data
            if ([string]$qualificationEvidence.Status -ne 'Verified') { throw 'Persisted qualification evidence was not Verified.' }
            $qualificationQuery = (Invoke-McpTool -Name 'query-plc-code-evidence' -Arguments @{ 'capability-ids' = [string]$qualificationCapability.CapabilityId; 'code-format' = 's7dcl'; 'input-path' = $qualificationPath }).result.structuredContent.data
            if ($qualificationQuery.Qualified -ne $true -or @($qualificationQuery.Matches).Count -lt 1) { throw 'Exact compatible qualification evidence was not queryable.' }
            $qualificationRecords = @((Invoke-McpTool -Name 'list-plc-code-evidence' -Arguments @{ 'status' = 'Verified'; 'code-format' = 'S7DCL-LAD' }).result.structuredContent.data)
            if (@($qualificationRecords | Where-Object { [string]$_.EvidenceId -eq [string]$qualificationRun.Evidence.EvidenceId }).Count -ne 1) { throw 'Verified qualification evidence was not listed.' }
            $qualificationBlockSearch = Invoke-McpTool -Name 'search-blocks' -Arguments @{ 'plc-name' = $plcNames[0]; 'pattern' = ('^' + [Regex]::Escape($qualificationName) + '$'); 'search-in' = 'name' }
            if (@($qualificationBlockSearch.result.structuredContent.data).Count -ne 0) { throw 'Disposable qualification block remains after runner cleanup.' }
            $qualificationTransactions = @((Invoke-McpTool -Name 'list-plc-code-transactions').result.structuredContent.data)
            if (@($qualificationTransactions | Where-Object { [string]$_.TransactionId -eq [string]$qualificationRun.Evidence.TransactionId }).Count -ne 0) { throw 'Disposable qualification transaction package remains after proven cleanup.' }
            Remove-Item -LiteralPath $qualificationRoot -Recurse -Force
            $summary['QualificationProbeVerified'] = $true
            $summary['QualificationEvidenceId'] = [string]$qualificationRun.Evidence.EvidenceId
            $summary['QualificationCleanupVerified'] = $true
            $documentCases = @(
                @{ Directory = $ladDirectory; Name = 'Codex_LAD_Branch_Timer_Probe'; Type = 'FB'; Number = '59988' },
                @{ Directory = $ladDirectory; Name = 'Codex_LAD_Analog_Edge_Probe'; Type = 'FC'; Number = '59989' },
                @{ Directory = $ladDirectory; Name = 'Codex_LAD_Probe'; Type = 'FC'; Number = '59991' },
                @{ Directory = $ladDirectory; Name = 'Codex_LAD_FB_Probe'; Type = 'FB'; Number = '59992' },
                @{ Directory = $ladDirectory; Name = 'Codex_Mixed_Probe'; Type = 'FB'; Number = '59993' },
                @{ Directory = $dataDirectory; Name = 'Codex_Data_Probe'; Type = 'GlobalDB'; Number = '59994' },
                @{ Directory = $dataDirectory; Name = 'Codex_Data_Struct_Probe'; Type = 'GlobalDB'; Number = '59987' }
            )

            foreach ($case in $documentCases) {
                [void](Invoke-McpTool -Name 'validate-plc-documents' -Arguments @{
                    'import-directory' = $case.Directory
                    'file-name' = $case.Name
                })
                $planResponse = Invoke-McpTool -Name 'plan-block-document-import' -Arguments @{
                    'plc-name' = $plcNames[0]
                    'import-directory' = $case.Directory
                    'file-name' = $case.Name
                    'import-option' = 'Override'
                }
                $token = [string]$planResponse.result.structuredContent.data.ApplyToken
                if ([string]::IsNullOrWhiteSpace($token)) { throw "Document import plan for '$($case.Name)' did not return an apply token." }
                $target = $planResponse.result.structuredContent.data.Target
                if ([string]$target.Name -ne $case.Name -or [string]$target.BlockType -ne $case.Type -or [string]$target.Number -ne $case.Number -or -not [string]::IsNullOrEmpty([string]$target.GroupPath)) {
                    throw "Document import plan did not bind the expected exact root target for '$($case.Name)': $($target | ConvertTo-Json -Compress -Depth 10)"
                }
                [void](Invoke-McpTool -Name 'apply-block-document-import' -Arguments @{ 'apply-token' = $token; 'confirm' = $true })
            }

            $productionQualificationCapability = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Block'; 'object-name' = 'Codex_LAD_Probe'; 'group-path' = ''; 'operation' = 'Modify'; 'block-type' = 'FC'; 'requested-language' = 'LAD' }).result.structuredContent.data
            $productionQualificationQuery = (Invoke-McpTool -Name 'query-plc-code-evidence' -Arguments @{ 'capability-ids' = [string]$productionQualificationCapability.CapabilityId; 'code-format' = 's7dcl'; 'input-path' = (Join-Path $ladDirectory 'Codex_LAD_Probe.s7dcl') }).result.structuredContent.data
            if ($productionQualificationQuery.Qualified -ne $true -or @($productionQualificationQuery.Matches | Where-Object { [string]$_.EvidenceId -eq [string]$qualificationRun.Evidence.EvidenceId }).Count -ne 1) {
                throw 'Disposable qualification evidence did not match the same production code shape under a different exact block identity.'
            }
            $summary['ProductionIdentityEvidenceMatchVerified'] = $true

            $adversarialRoot = Join-Path (Join-Path ([System.IO.Path]::GetTempPath()) 'OpenTiaToolkit') ('PlcDocuments-' + [Guid]::NewGuid().ToString('N'))
            $staleRoot = Join-Path $adversarialRoot 'stale'
            $brokenRoot = Join-Path $adversarialRoot 'compile-failure'
            New-Item -ItemType Directory -Force -Path $staleRoot, $brokenRoot | Out-Null
            $validLadPath = Join-Path $ladDirectory 'Codex_LAD_Probe.s7dcl'
            $utf8Bom = [Text.UTF8Encoding]::new($true)

            $stalePlanResponse = Invoke-McpTool -Name 'plan-block-document-import' -Arguments @{
                'plc-name' = $plcNames[0]
                'import-directory' = $ladDirectory
                'file-name' = 'Codex_LAD_Probe'
                'group-path' = ''
                'import-option' = 'Override'
            }
            $staleToken = [string]$stalePlanResponse.result.structuredContent.data.ApplyToken
            $staleText = [IO.File]::ReadAllText($validLadPath).Replace('in2 := #ValueB', 'in2 := 123')
            [IO.File]::WriteAllText((Join-Path $staleRoot 'Codex_LAD_Probe.s7dcl'), $staleText, $utf8Bom)
            [void](Invoke-McpTool -Name 'import-block-from-docs' -Arguments @{ 'plc-name' = $plcNames[0]; 'import-directory' = $staleRoot; 'file-name' = 'Codex_LAD_Probe'; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            $staleResponse = Invoke-McpToolExpectError -Name 'apply-block-document-import' -Arguments @{ 'apply-token' = $staleToken; 'confirm' = $true }
            $staleErrorText = @($staleResponse.result.content | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text }) -join [Environment]::NewLine
            if ($staleErrorText -notmatch 'stale') { throw "Stale document apply did not report stale state: $staleErrorText" }
            [void](Invoke-McpTool -Name 'import-block-from-docs' -Arguments @{ 'plc-name' = $plcNames[0]; 'import-directory' = $ladDirectory; 'file-name' = 'Codex_LAD_Probe'; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })

            $brokenText = [IO.File]::ReadAllText($validLadPath).Replace('Contact( #InA )', 'Contact( "Codex_Missing_Tag" )')
            [IO.File]::WriteAllText((Join-Path $brokenRoot 'Codex_LAD_Probe.s7dcl'), $brokenText, $utf8Bom)
            [void](Invoke-McpTool -Name 'validate-plc-documents' -Arguments @{ 'import-directory' = $brokenRoot; 'file-name' = 'Codex_LAD_Probe' })
            $rollbackPlanResponse = Invoke-McpTool -Name 'plan-block-document-import' -Arguments @{
                'plc-name' = $plcNames[0]
                'import-directory' = $brokenRoot
                'file-name' = 'Codex_LAD_Probe'
                'group-path' = ''
                'import-option' = 'Override'
            }
            $rollbackToken = [string]$rollbackPlanResponse.result.structuredContent.data.ApplyToken
            $rollbackResponse = Invoke-McpToolExpectError -Name 'apply-block-document-import' -Arguments @{ 'apply-token' = $rollbackToken; 'confirm' = $true }
            $rollbackData = $rollbackResponse.result.structuredContent.data
            if ($rollbackData.RolledBack -ne $true -or $rollbackData.RestoredHashMatches -ne $true) {
                throw "Compile-failure rollback was not proven: $($rollbackData | ConvertTo-Json -Compress -Depth 20)"
            }
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })

            $transactionRoot = Join-Path $adversarialRoot 'transaction'
            $transactionSuccessRoot = Join-Path $transactionRoot 'success'
            $transactionOverrideRoot = Join-Path $transactionRoot 'override'
            $transactionBrokenRoot = Join-Path $transactionRoot 'broken'
            $transactionRecoveryRoot = Join-Path $transactionRoot 'recovery'
            New-Item -ItemType Directory -Force -Path $transactionSuccessRoot, $transactionOverrideRoot, $transactionBrokenRoot, $transactionRecoveryRoot | Out-Null
            $transactionA = [IO.File]::ReadAllText($validLadPath).Replace('Codex_LAD_Probe', 'Codex_TX_A').Replace('59991', '63001')
            $transactionFbPath = Join-Path $ladDirectory 'Codex_LAD_FB_Probe.s7dcl'
            $transactionB = [IO.File]::ReadAllText($transactionFbPath).Replace('Codex_LAD_FB_Probe', 'Codex_TX_B').Replace('59992', '63002')
            [IO.File]::WriteAllText((Join-Path $transactionSuccessRoot 'Codex_TX_A.s7dcl'), $transactionA, $utf8Bom)
            [IO.File]::WriteAllText((Join-Path $transactionSuccessRoot 'Codex_TX_B.s7dcl'), $transactionB, $utf8Bom)

            $transactionCapabilityA = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Block'; 'object-name' = 'Codex_TX_A'; 'group-path' = ''; 'operation' = 'Create'; 'block-type' = 'FC'; 'requested-language' = 'LAD' }).result.structuredContent.data
            $transactionCapabilityB = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Block'; 'object-name' = 'Codex_TX_B'; 'group-path' = ''; 'operation' = 'Create'; 'block-type' = 'FB'; 'requested-language' = 'LAD' }).result.structuredContent.data
            $beginTransaction = Invoke-McpTool -Name 'begin-plc-code-transaction' -Arguments @{ 'plc-name' = $plcNames[0]; 'description' = 'MCP live multi-block transaction'; 'capability-ids' = ([string]$transactionCapabilityA.CapabilityId + ',' + [string]$transactionCapabilityB.CapabilityId) }
            $transactionId = [string]$beginTransaction.result.structuredContent.data.TransactionId
            [void](Invoke-McpTool -Name 'add-plc-code-transaction-operation' -Arguments @{ 'transaction-id' = $transactionId; 'import-directory' = $transactionSuccessRoot; 'file-name' = 'Codex_TX_A'; 'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'add-plc-code-transaction-operation' -Arguments @{ 'transaction-id' = $transactionId; 'import-directory' = $transactionSuccessRoot; 'file-name' = 'Codex_TX_B'; 'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true })
            $transactionPlanResponse = Invoke-McpTool -Name 'plan-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId }
            $transactionPlan = $transactionPlanResponse.result.structuredContent.data
            if ([string]$transactionPlan.State -ne 'Planned' -or @($transactionPlan.Operations).Count -ne 2 -or @($transactionPlan.Operations | Where-Object { [string]$_.Action -ne 'Create' }).Count -ne 0) {
                throw "Multi-block transaction plan was not exact: $($transactionPlan | ConvertTo-Json -Compress -Depth 20)"
            }
            $transactionApplyResponse = Invoke-McpTool -Name 'apply-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId; 'apply-token' = [string]$transactionPlan.ApplyToken; 'confirm' = $true }
            $transactionApply = $transactionApplyResponse.result.structuredContent.data
            if ($transactionApply.Applied -ne $true -or $transactionApply.Verified -ne $true -or [string]$transactionApply.State -ne 'Succeeded' -or @($transactionApply.Transaction.Operations | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.CanonicalResultHash) }).Count -ne 0) {
                throw "Multi-block transaction success was not proven: $($transactionApply | ConvertTo-Json -Compress -Depth 20)"
            }
            $persistedTransaction = (Invoke-McpTool -Name 'get-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId }).result.structuredContent.data
            if ([string]$persistedTransaction.State -ne 'Succeeded') { throw 'Successful transaction manifest did not persist.' }
            $listedTransactions = @((Invoke-McpTool -Name 'list-plc-code-transactions').result.structuredContent.data)
            if (@($listedTransactions | Where-Object { [string]$_.TransactionId -eq $transactionId }).Count -ne 1) { throw 'Successful transaction was not discoverable through list-plc-code-transactions.' }

            $overrideA = $transactionA.Replace('in2 := #ValueB', 'in2 := 456')
            $brokenTransaction = $transactionA.Replace('Codex_TX_A', 'Codex_TX_Bad').Replace('63001', '63003').Replace('Contact( #InA )', 'Contact( "Codex_TX_Missing_Tag" )')
            [IO.File]::WriteAllText((Join-Path $transactionOverrideRoot 'Codex_TX_A.s7dcl'), $overrideA, $utf8Bom)
            [IO.File]::WriteAllText((Join-Path $transactionBrokenRoot 'Codex_TX_Bad.s7dcl'), $brokenTransaction, $utf8Bom)
            $failureCapabilityA = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Block'; 'object-name' = 'Codex_TX_A'; 'group-path' = ''; 'operation' = 'Modify'; 'block-type' = 'FC'; 'requested-language' = 'LAD' }).result.structuredContent.data
            $failureCapabilityBad = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Block'; 'object-name' = 'Codex_TX_Bad'; 'group-path' = ''; 'operation' = 'Create'; 'block-type' = 'FC'; 'requested-language' = 'LAD' }).result.structuredContent.data
            $beginFailureTransaction = Invoke-McpTool -Name 'begin-plc-code-transaction' -Arguments @{ 'plc-name' = $plcNames[0]; 'description' = 'MCP live forced rollback transaction'; 'capability-ids' = ([string]$failureCapabilityA.CapabilityId + ',' + [string]$failureCapabilityBad.CapabilityId) }
            $failureTransactionId = [string]$beginFailureTransaction.result.structuredContent.data.TransactionId
            [void](Invoke-McpTool -Name 'add-plc-code-transaction-operation' -Arguments @{ 'transaction-id' = $failureTransactionId; 'import-directory' = $transactionOverrideRoot; 'file-name' = 'Codex_TX_A'; 'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'add-plc-code-transaction-operation' -Arguments @{ 'transaction-id' = $failureTransactionId; 'import-directory' = $transactionBrokenRoot; 'file-name' = 'Codex_TX_Bad'; 'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true })
            $failureTransactionPlan = (Invoke-McpTool -Name 'plan-plc-code-transaction' -Arguments @{ 'transaction-id' = $failureTransactionId }).result.structuredContent.data
            $failureTransactionResponse = Invoke-McpToolExpectError -Name 'apply-plc-code-transaction' -Arguments @{ 'transaction-id' = $failureTransactionId; 'apply-token' = [string]$failureTransactionPlan.ApplyToken; 'confirm' = $true }
            $failureTransaction = $failureTransactionResponse.result.structuredContent.data
            if ($failureTransaction.RolledBackAndVerified -ne $true -or $failureTransaction.RecoveryRequired -eq $true -or [string]$failureTransaction.State -ne 'RolledBackAndVerified' -or @($failureTransaction.Transaction.Operations | Where-Object { $_.RestorationVerified -ne $true }).Count -ne 0) {
                throw "Multi-block transaction rollback was not proven: $($failureTransaction | ConvertTo-Json -Compress -Depth 30)"
            }
            $badBlockSearch = Invoke-McpTool -Name 'search-blocks' -Arguments @{ 'plc-name' = $plcNames[0]; 'pattern' = '^Codex_TX_Bad$'; 'search-in' = 'name' }
            if (@($badBlockSearch.result.structuredContent.data).Count -ne 0) { throw 'Failed transaction left its newly created block behind.' }
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            [void](Invoke-McpTool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $failureTransactionId; 'confirm' = $true })

            $recoveryOverride = $transactionA.Replace('in2 := #ValueB', 'in2 := 789')
            $recoveryNew = $transactionA.Replace('Codex_TX_A', 'Codex_TX_Recover').Replace('63001', '63004')
            [IO.File]::WriteAllText((Join-Path $transactionRecoveryRoot 'Codex_TX_A.s7dcl'), $recoveryOverride, $utf8Bom)
            [IO.File]::WriteAllText((Join-Path $transactionRecoveryRoot 'Codex_TX_Recover.s7dcl'), $recoveryNew, $utf8Bom)
            $recoveryCapabilityA = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Block'; 'object-name' = 'Codex_TX_A'; 'group-path' = ''; 'operation' = 'Modify'; 'block-type' = 'FC'; 'requested-language' = 'LAD' }).result.structuredContent.data
            $recoveryCapabilityNew = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Block'; 'object-name' = 'Codex_TX_Recover'; 'group-path' = ''; 'operation' = 'Create'; 'block-type' = 'FC'; 'requested-language' = 'LAD' }).result.structuredContent.data
            $beginRecoveryTransaction = Invoke-McpTool -Name 'begin-plc-code-transaction' -Arguments @{ 'plc-name' = $plcNames[0]; 'description' = 'MCP live interrupted transaction recovery'; 'capability-ids' = ([string]$recoveryCapabilityA.CapabilityId + ',' + [string]$recoveryCapabilityNew.CapabilityId) }
            $recoveryTransactionId = [string]$beginRecoveryTransaction.result.structuredContent.data.TransactionId
            [void](Invoke-McpTool -Name 'add-plc-code-transaction-operation' -Arguments @{ 'transaction-id' = $recoveryTransactionId; 'import-directory' = $transactionRecoveryRoot; 'file-name' = 'Codex_TX_A'; 'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'add-plc-code-transaction-operation' -Arguments @{ 'transaction-id' = $recoveryTransactionId; 'import-directory' = $transactionRecoveryRoot; 'file-name' = 'Codex_TX_Recover'; 'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'plan-plc-code-transaction' -Arguments @{ 'transaction-id' = $recoveryTransactionId })
            [void](Invoke-McpTool -Name 'import-block-from-docs' -Arguments @{ 'plc-name' = $plcNames[0]; 'import-directory' = $transactionRecoveryRoot; 'file-name' = 'Codex_TX_A'; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'import-block-from-docs' -Arguments @{ 'plc-name' = $plcNames[0]; 'import-directory' = $transactionRecoveryRoot; 'file-name' = 'Codex_TX_Recover'; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            $recoveryManifestPath = Join-Path (Join-Path (Join-Path ([System.IO.Path]::GetTempPath()) 'OpenTiaToolkit\plc-code-transactions') $recoveryTransactionId) 'manifest.json'
            $recoveryManifest = [IO.File]::ReadAllText($recoveryManifestPath) | ConvertFrom-Json
            $recoveryManifest.State = 'Applying'
            $recoveryManifest.ApplyToken = $null
            [IO.File]::WriteAllText($recoveryManifestPath, ($recoveryManifest | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
            [void](Invoke-McpTool -Name 'reset-session')
            [void](Invoke-McpTool -Name 'connect-instance' -Arguments @{ 'instance-id' = $processId })
            $recoveryResponse = Invoke-McpTool -Name 'recover-plc-code-transaction' -Arguments @{ 'transaction-id' = $recoveryTransactionId; 'confirm' = $true }
            $recoveryResult = $recoveryResponse.result.structuredContent.data
            if ($recoveryResult.RolledBackAndVerified -ne $true -or [string]$recoveryResult.State -ne 'RolledBackAndVerified') {
                throw "Interrupted transaction recovery was not proven: $($recoveryResult | ConvertTo-Json -Compress -Depth 30)"
            }
            $recoveryBlockSearch = Invoke-McpTool -Name 'search-blocks' -Arguments @{ 'plc-name' = $plcNames[0]; 'pattern' = '^Codex_TX_Recover$'; 'search-in' = 'name' }
            if (@($recoveryBlockSearch.result.structuredContent.data).Count -ne 0) { throw 'Interrupted transaction recovery left its newly created block behind.' }
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            [void](Invoke-McpTool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $recoveryTransactionId; 'confirm' = $true })
            Remove-Item -LiteralPath $adversarialRoot -Recurse -Force

            foreach ($sclCase in $sclCases) {
                $sclPath = Join-Path $sclDirectory ($sclCase + '.scl')
                [void](Invoke-McpTool -Name 'validate-plc-code-artifact' -Arguments @{ 'code-format' = 'scl'; 'input-path' = $sclPath })
                $sourcesResponse = Invoke-McpTool -Name 'list-external-sources' -Arguments @{ 'plc-name' = $plcNames[0] }
                $sourceNames = @($sourcesResponse.result.structuredContent.data | ForEach-Object { [string]$_.name })
                if (-not ($sourceNames -contains $sclCase)) {
                    [void](Invoke-McpTool -Name 'import-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-path' = $sclPath; 'dry-run' = $true })
                    [void](Invoke-McpTool -Name 'import-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-path' = $sclPath; 'dry-run' = $false; 'confirm' = $true })
                }
                [void](Invoke-McpTool -Name 'generate-blocks-from-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-name' = $sclCase; 'dry-run' = $true })
                [void](Invoke-McpTool -Name 'generate-blocks-from-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-name' = $sclCase; 'dry-run' = $false; 'confirm' = $true })
            }
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })

            foreach ($name in @('Codex_LAD_Branch_Timer_Probe', 'Codex_LAD_Analog_Edge_Probe', 'Codex_LAD_Probe', 'Codex_LAD_FB_Probe', 'Codex_Mixed_Probe', 'Codex_Data_Probe', 'Codex_Data_Struct_Probe', 'Codex_SCL_Probe', 'Codex_SCL_Stateful_Probe', 'Codex_SCL_Type_Probe')) {
                [void](Invoke-McpTool -Name 'get-block-info' -Arguments @{ 'plc-name' = $plcNames[0]; 'block-name' = $name })
            }

            $xmlRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('OpenTiaToolkit-SimaticML-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Force -Path $xmlRoot | Out-Null
            $xmlPath = Join-Path $xmlRoot 'Codex_LAD_Probe.xml'
            [void](Invoke-McpTool -Name 'export-block' -Arguments @{ 'plc-name' = $plcNames[0]; 'block-name' = 'Codex_LAD_Probe'; 'export-path' = $xmlPath })
            [void](Invoke-McpTool -Name 'delete-blocks' -Arguments @{ 'plc-name' = $plcNames[0]; 'filter' = '^Codex_LAD_Probe$'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'import-block' -Arguments @{ 'plc-name' = $plcNames[0]; 'import-path' = $xmlPath; 'import-option' = 'Override'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            [void](Invoke-McpTool -Name 'get-block-info' -Arguments @{ 'plc-name' = $plcNames[0]; 'block-name' = 'Codex_LAD_Probe' })
            Remove-Item -LiteralPath $xmlRoot -Recurse -Force

            [void](Invoke-McpTool -Name 'delete-blocks' -Arguments @{
                'plc-name' = $plcNames[0]
                'filter' = '^Codex_(LAD_Branch_Timer_Probe|LAD_Analog_Edge_Probe|LAD_Probe|LAD_FB_Probe|Mixed_Probe|Data_Probe|Data_Struct_Probe|SCL_Probe|SCL_Stateful_Probe|SCL_Type_Probe|TX_A|TX_B)$'
                'confirm' = $true
            })
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            $typeDeleteCapability = (Invoke-McpTool -Name 'discover-plc-code-capabilities' -Arguments @{ 'plc-name' = $plcNames[0]; 'object-kind' = 'Type'; 'object-name' = 'Codex_SCL_Type_Probe_Record'; 'group-path' = ''; 'operation' = 'Modify'; 'block-type' = 'TYPE'; 'requested-language' = 'SCL' }).result.structuredContent.data
            $beginTypeDelete = Invoke-McpTool -Name 'begin-plc-code-transaction' -Arguments @{ 'plc-name' = $plcNames[0]; 'description' = 'MCP live source-generated type cleanup'; 'capability-id' = [string]$typeDeleteCapability.CapabilityId }
            $typeDeleteId = [string]$beginTypeDelete.result.structuredContent.data.TransactionId
            [void](Invoke-McpTool -Name 'add-delete-plc-type-transaction-operation' -Arguments @{ 'transaction-id' = $typeDeleteId; 'type-name' = 'Codex_SCL_Type_Probe_Record'; 'group-path' = ''; 'confirm' = $true })
            $typeDeletePlan = (Invoke-McpTool -Name 'plan-plc-code-transaction' -Arguments @{ 'transaction-id' = $typeDeleteId }).result.structuredContent.data
            if ([string]$typeDeletePlan.State -eq 'ImpactReviewRequired') {
                $typeDeleteImpact = (Invoke-McpTool -Name 'get-plc-code-impact' -Arguments @{ 'transaction-id' = $typeDeleteId }).result.structuredContent.data
                $typeDeletePlan = (Invoke-McpTool -Name 'approve-plc-code-impact' -Arguments @{ 'transaction-id' = $typeDeleteId; 'impact-fingerprint' = [string]$typeDeleteImpact.Fingerprint; 'accept-unknown' = ([string]$typeDeleteImpact.Status -eq 'Unknown'); 'confirm' = $true }).result.structuredContent.data
            }
            [void](Invoke-McpTool -Name 'apply-plc-code-transaction' -Arguments @{ 'transaction-id' = $typeDeleteId; 'apply-token' = [string]$typeDeletePlan.ApplyToken; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $typeDeleteId; 'confirm' = $true })
            foreach ($sclCase in $sclCases) {
                [void](Invoke-McpTool -Name 'delete-external-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-name' = $sclCase; 'confirm' = $true })
            }
            [void](Invoke-McpTool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId; 'confirm' = $true })
            $remainingBlocksResponse = Invoke-McpTool -Name 'search-blocks' -Arguments @{ 'plc-name' = $plcNames[0]; 'pattern' = 'Codex_'; 'search-in' = 'name' }
            if (@($remainingBlocksResponse.result.structuredContent.data).Count -ne 0) { throw 'Disposable Codex PLC blocks remain after cleanup.' }
            $remainingSourcesResponse = Invoke-McpTool -Name 'list-external-sources' -Arguments @{ 'plc-name' = $plcNames[0] }
            if (@($remainingSourcesResponse.result.structuredContent.data | Where-Object { [string]$_.name -like 'Codex_*' }).Count -ne 0) { throw 'Disposable Codex PLC external sources remain after cleanup.' }
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            $summary['PlcCodeWorkflowVerified'] = $true
            $summary['PlcCodeBlocksCreatedAndRemoved'] = 12
            $summary['SimaticMlFallbackVerified'] = $true
            $summary['StaleDocumentStateRejected'] = $true
            $summary['CompileFailureRollbackHashVerified'] = $true
            $summary['MultiBlockTransactionVerified'] = $true
            $summary['MultiBlockTransactionRollbackVerified'] = $true
            $summary['InterruptedTransactionRecoveryVerified'] = $true
            $summary['CleanupVerified'] = $true
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
        $summary['SelectedInstanceId'] = $processId
        $summary['Projects'] = @($projects | ForEach-Object {
            if ($_.PSObject.Properties['path']) { [string]$_.path }
            elseif ($_.PSObject.Properties['name']) { [string]$_.name }
            else { [string]$_ }
        })
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
        $ownedWorkerPids = @(
            Get-CimInstance Win32_Process -Filter "ParentProcessId = $($process.Id)" -ErrorAction Stop |
                Where-Object { $_.Name -like 'OpenTiaToolkit.Worker.*' } |
                ForEach-Object { [int]$_.ProcessId }
        )
    }
    catch {
        $ownedWorkerPids = @()
    }

    try {
        $process.StandardInput.Close()
    }
    catch {
    }

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
    if ($forcedShutdown) {
        throw "MCP process did not exit within five seconds after stdin closed and required forced termination. stderr: $stderr"
    }
    if ($exitCode -ne 0) {
        throw "MCP process exited with code $exitCode. stderr: $stderr"
    }
    if (-not $stderr.Contains($stderrMarker)) {
        throw "MCP stderr separation marker was not observed. stderr: $stderr"
    }
    if ($orphanedWorkers.Count -gt 0) {
        throw "MCP shutdown left worker process(es) running: $($orphanedWorkers -join ', ')."
    }
}
