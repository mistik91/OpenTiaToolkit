[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [switch]$LiveProject,

    [string]$DisposableArchivePath = '',

    [switch]$DisposableProjectOnly,

    [string]$PlcCodeAssetsRoot = '',

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
    if ($tools.Count -lt 145) {
        throw "Expected the shared command surface, but tools/list returned only $($tools.Count) tools."
    }

    foreach ($requiredTool in @('list-instances', 'connect-instance', 'list-projects', 'list-devices', 'reset-session', 'validate-blueprint', 'plan-blueprint', 'apply-blueprint', 'validate-plc-documents', 'plan-block-document-import', 'apply-block-document-import', 'begin-plc-code-transaction', 'add-plc-code-transaction-operation', 'plan-plc-code-transaction', 'apply-plc-code-transaction', 'get-plc-code-transaction', 'list-plc-code-transactions', 'recover-plc-code-transaction', 'discard-plc-code-transaction', 'describe-api', 'diff-api-profile', 'get-hardware-drive-parameters', 'list-unified-alarms')) {
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

        if (-not [string]::IsNullOrWhiteSpace($PlcCodeAssetsRoot)) {
            $assetRoot = [System.IO.Path]::GetFullPath($PlcCodeAssetsRoot)
            $ladDirectory = Join-Path $assetRoot 's7dcl-lad-authoring\assets'
            $dataDirectory = Join-Path $assetRoot 's7dcl-data-authoring\assets'
            $sclPath = Join-Path $assetRoot 'scl-source-authoring\assets\Codex_SCL_Probe.scl'
            $documentCases = @(
                @{ Directory = $ladDirectory; Name = 'Codex_LAD_Probe'; Type = 'FC'; Number = '59991' },
                @{ Directory = $ladDirectory; Name = 'Codex_LAD_FB_Probe'; Type = 'FB'; Number = '59992' },
                @{ Directory = $ladDirectory; Name = 'Codex_Mixed_Probe'; Type = 'FB'; Number = '59993' },
                @{ Directory = $dataDirectory; Name = 'Codex_Data_Probe'; Type = 'GlobalDB'; Number = '59994' }
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

            $beginTransaction = Invoke-McpTool -Name 'begin-plc-code-transaction' -Arguments @{ 'plc-name' = $plcNames[0]; 'description' = 'MCP live multi-block transaction' }
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
            $beginFailureTransaction = Invoke-McpTool -Name 'begin-plc-code-transaction' -Arguments @{ 'plc-name' = $plcNames[0]; 'description' = 'MCP live forced rollback transaction' }
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
            $beginRecoveryTransaction = Invoke-McpTool -Name 'begin-plc-code-transaction' -Arguments @{ 'plc-name' = $plcNames[0]; 'description' = 'MCP live interrupted transaction recovery' }
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

            $sourcesResponse = Invoke-McpTool -Name 'list-external-sources' -Arguments @{ 'plc-name' = $plcNames[0] }
            $sourceNames = @($sourcesResponse.result.structuredContent.data | ForEach-Object { [string]$_.name })
            if (-not ($sourceNames -contains 'Codex_SCL_Probe')) {
                [void](Invoke-McpTool -Name 'import-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-path' = $sclPath; 'dry-run' = $true })
                [void](Invoke-McpTool -Name 'import-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-path' = $sclPath; 'dry-run' = $false; 'confirm' = $true })
            }
            [void](Invoke-McpTool -Name 'generate-blocks-from-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-name' = 'Codex_SCL_Probe'; 'dry-run' = $true })
            [void](Invoke-McpTool -Name 'generate-blocks-from-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-name' = 'Codex_SCL_Probe'; 'dry-run' = $false; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })

            foreach ($name in @('Codex_LAD_Probe', 'Codex_LAD_FB_Probe', 'Codex_Mixed_Probe', 'Codex_Data_Probe', 'Codex_SCL_Probe')) {
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
                'filter' = '^Codex_(LAD_Probe|LAD_FB_Probe|Mixed_Probe|Data_Probe|SCL_Probe|TX_A|TX_B)$'
                'confirm' = $true
            })
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            [void](Invoke-McpTool -Name 'delete-external-source' -Arguments @{ 'plc-name' = $plcNames[0]; 'source-name' = 'Codex_SCL_Probe'; 'confirm' = $true })
            [void](Invoke-McpTool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId; 'confirm' = $true })
            $remainingBlocksResponse = Invoke-McpTool -Name 'search-blocks' -Arguments @{ 'plc-name' = $plcNames[0]; 'pattern' = 'Codex_'; 'search-in' = 'name' }
            if (@($remainingBlocksResponse.result.structuredContent.data).Count -ne 0) { throw 'Disposable Codex PLC blocks remain after cleanup.' }
            $remainingSourcesResponse = Invoke-McpTool -Name 'list-external-sources' -Arguments @{ 'plc-name' = $plcNames[0] }
            if (@($remainingSourcesResponse.result.structuredContent.data | Where-Object { [string]$_.name -like 'Codex_*' }).Count -ne 0) { throw 'Disposable Codex PLC external sources remain after cleanup.' }
            [void](Invoke-McpTool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcNames[0] })
            $summary['PlcCodeWorkflowVerified'] = $true
            $summary['PlcCodeBlocksCreatedAndRemoved'] = 8
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
