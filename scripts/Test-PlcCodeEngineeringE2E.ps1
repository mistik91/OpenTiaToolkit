[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$WorkerPath,

    [int]$TiaProcessId = 0,
    [string]$PlcName = '',
    [string]$InstanceName = '',
    [string]$OutputPath = '',

    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 240,

    [switch]$SynchronizeSimulatorBaseline,
    [switch]$SkipBehavior
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$resolvedWorker = (Resolve-Path -LiteralPath $WorkerPath).Path
$counterAsset = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'skills\s7dcl-lad-authoring\assets\Codex_LAD_Counter_Probe.s7dcl')).Path
$behaviorRoot = Join-Path $repoRoot 'skills\plc-behavior-verification\assets'
$behaviorPackage = (Resolve-Path -LiteralPath (Join-Path $behaviorRoot 'Codex_Behavior_Package.json')).Path
$behaviorVector = (Resolve-Path -LiteralPath (Join-Path $behaviorRoot 'Codex_Behavior_Vector.json')).Path
$runId = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$testRoot = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'OpenTiaToolkit') ('PlcCodeE2E-' + $runId)
$probeName = 'Codex_E2E_Probe_' + $runId
$productName = 'Codex_E2E_Product_' + $runId
$modifyProbeName = 'Codex_E2E_Modify_' + $runId
$createdNames = [Collections.Generic.List[string]]::new()
$transactionIds = [Collections.Generic.List[string]]::new()
$selectedPlc = ''
$behaviorNames = @('Codex_Behavior_Function', 'Codex_Behavior_IO', 'Codex_Behavior_Cycle')
$behaviorWasClear = $false
$cleanupVerified = $false
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $resolvedExecutable
$startInfo.WorkingDirectory = Split-Path -Parent $resolvedExecutable
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.EnvironmentVariables['OPEN_TIA_MCP_LOG_LEVEL'] = 'None'
$startInfo.EnvironmentVariables['OPEN_TIA_WORKER_V21_EXE'] = $resolvedWorker
$startInfo.EnvironmentVariables['OPEN_TIA_WORKER_EXE'] = $resolvedWorker
$startInfo.EnvironmentVariables['OPEN_TIA_MCP_ALLOWED_ROOTS'] = ($repoRoot + ';' + (Join-Path ([IO.Path]::GetTempPath()) 'OpenTiaToolkit'))

$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (-not $process.Start()) { throw "Failed to start MCP executable '$resolvedExecutable'." }
$stderrTask = $process.StandardError.ReadToEndAsync()
$requestId = 0

function Read-Response {
    param([int]$ExpectedId)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = [Math]::Max(1, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        $read = $process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait($remaining)) { break }
        $line = $read.Result
        if ($null -eq $line) { break }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $response = $line | ConvertFrom-Json
        if ($null -ne $response.id -and [int]$response.id -eq $ExpectedId) { return $response }
    }
    throw "Timed out waiting for MCP response $ExpectedId."
}

function Invoke-Request {
    param([string]$Method, [object]$Params)
    $script:requestId++
    $request = [ordered]@{ jsonrpc = '2.0'; id = $script:requestId; method = $Method; params = $Params }
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress -Depth 40))
    $process.StandardInput.Flush()
    return Read-Response -ExpectedId $script:requestId
}

function Invoke-Tool {
    param([string]$Name, [hashtable]$Arguments = @{}, [switch]$ExpectError)
    $response = Invoke-Request -Method 'tools/call' -Params ([ordered]@{ name = $Name; arguments = $Arguments })
    $isError = $null -ne $response.result.PSObject.Properties['isError'] -and $response.result.isError -eq $true
    if ($ExpectError -and -not $isError) { throw "MCP tool '$Name' was expected to fail." }
    if (-not $ExpectError -and $isError) {
        $message = @($response.result.content | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text }) -join [Environment]::NewLine
        throw "MCP tool '$Name' failed: $message"
    }
    return $response
}

function Get-Data {
    param([object]$Response)
    return $Response.result.structuredContent.data
}

function Discover-BlockCapability {
    param([string]$Name, [string]$Operation, [string]$Type, [string]$Language, [string]$Format)
    return Get-Data (Invoke-Tool -Name 'discover-plc-code-capabilities' -Arguments @{
        'plc-name' = $selectedPlc; 'object-kind' = 'Block'; 'object-name' = $Name; 'group-path' = ''
        'operation' = $Operation; 'block-type' = $Type; 'requested-language' = $Language; 'requested-format' = $Format
    })
}

function Invoke-Promotion {
    param([string]$EvidenceId, [object]$Capability, [string]$ArtifactPath, [string]$BaselineDirectory = '')
    $query = Get-Data (Invoke-Tool -Name 'query-plc-code-evidence' -Arguments @{
        'capability-ids' = [string]$Capability.CapabilityId; 'code-format' = 's7dcl'; 'input-path' = $ArtifactPath
    })
    if ($query.Qualified -ne $true -or @($query.Matches.EvidenceId) -notcontains $EvidenceId) {
        throw "Exact evidence '$EvidenceId' was not reusable for the production artifact."
    }
    $transaction = Get-Data (Invoke-Tool -Name 'begin-promoted-plc-code-transaction' -Arguments @{
        'plc-name' = $selectedPlc; 'evidence-id' = $EvidenceId; 'capability-ids' = [string]$Capability.CapabilityId
        'code-format' = 's7dcl'; 'input-path' = $ArtifactPath; 'description' = 'Repeatable PLC code engineering E2E'
        'save-decision' = 'Defer'; 'source-control-decision' = 'None'
    })
    $transactionIds.Add([string]$transaction.TransactionId)
    $arguments = @{
        'transaction-id' = [string]$transaction.TransactionId; 'import-directory' = (Split-Path -Parent $ArtifactPath)
        'file-name' = [IO.Path]::GetFileNameWithoutExtension($ArtifactPath); 'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true
    }
    if ([string]::IsNullOrWhiteSpace($BaselineDirectory)) {
        $arguments['target-mode'] = 'create-only'
    } else {
        $arguments['target-mode'] = 'baseline-bound'
        $arguments['baseline-directory'] = $BaselineDirectory
        $arguments['baseline-file-name'] = $productName
        $arguments['allowed-changes'] = 'Implementation'
    }
    [void](Invoke-Tool -Name 'add-plc-code-transaction-operation' -Arguments $arguments)
    $plan = Get-Data (Invoke-Tool -Name 'plan-plc-code-transaction' -Arguments @{ 'transaction-id' = [string]$transaction.TransactionId })
    if ([string]$plan.State -ne 'Planned' -or [string]$plan.ImpactAnalysis.Status -ne 'Safe' -or [string]::IsNullOrWhiteSpace([string]$plan.ApplyToken)) {
        throw "Promotion plan was not Safe: $($plan | ConvertTo-Json -Compress -Depth 40)"
    }
    $apply = Get-Data (Invoke-Tool -Name 'apply-plc-code-transaction' -Arguments @{
        'transaction-id' = [string]$plan.TransactionId; 'apply-token' = [string]$plan.ApplyToken; 'confirm' = $true
    })
    if ($apply.Applied -ne $true -or $apply.Verified -ne $true -or [int]$apply.Transaction.FinalCompilation.ErrorCount -ne 0) {
        throw 'Evidence-bound production apply was not canonically verified with a clean compile.'
    }
    return $apply
}

try {
    [void](Invoke-Request -Method 'initialize' -Params ([ordered]@{
        protocolVersion = '2024-11-05'; capabilities = @{}
        clientInfo = [ordered]@{ name = 'OpenTiaToolkit PLC code engineering E2E'; version = '1.0' }
    }))
    $process.StandardInput.WriteLine((([ordered]@{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} }) | ConvertTo-Json -Compress))
    $process.StandardInput.Flush()

    $required = @(
        'discover-plc-code-capabilities', 'run-plc-code-probe', 'query-plc-code-evidence', 'begin-promoted-plc-code-transaction',
        'add-plc-code-transaction-operation', 'plan-plc-code-transaction', 'apply-plc-code-transaction', 'export-block-as-docs',
        'compile-software', 'get-blocks', 'delete-blocks', 'discard-plc-code-transaction'
    )
    if (-not $SkipBehavior) { $required += @('discover-plc-simulator-capabilities', 'run-plc-code-behavior-probe') }
    if ($SynchronizeSimulatorBaseline) { $required += 'download-software' }
    $tools = @((Invoke-Request -Method 'tools/list' -Params @{}).result.tools.name)
    foreach ($name in $required) { if ($tools -notcontains $name) { throw "MCP surface is missing '$name'." } }

    $instances = @((Get-Data (Invoke-Tool -Name 'list-instances')).instances)
    if ($TiaProcessId -gt 0) {
        $instance = @($instances | Where-Object { [int]$_.processId -eq $TiaProcessId })
        if ($instance.Count -ne 1) { throw "TIA process $TiaProcessId was not found exactly once." }
    } else {
        if ($instances.Count -ne 1) { throw "Expected exactly one live TIA instance; found $($instances.Count). Supply -TiaProcessId." }
        $TiaProcessId = [int]$instances[0].processId
    }
    [void](Invoke-Tool -Name 'connect-instance' -Arguments @{ 'instance-id' = $TiaProcessId })
    $projects = @(Get-Data (Invoke-Tool -Name 'list-projects'))
    if ($projects.Count -lt 1) { throw 'The selected TIA instance has no open project.' }
    $plcs = @(Get-Data (Invoke-Tool -Name 'list-plcs'))
    if ([string]::IsNullOrWhiteSpace($PlcName)) {
        if ($plcs.Count -ne 1) { throw "Expected one PLC; found $($plcs.Count). Supply -PlcName." }
        $selectedPlc = [string]$plcs[0].name
    } else {
        $matchingPlcs = @($plcs | Where-Object { [string]$_.name -eq $PlcName })
        if ($matchingPlcs.Count -ne 1) { throw "PLC '$PlcName' was not found exactly once." }
        $selectedPlc = $PlcName
    }
    [void](Invoke-Tool -Name 'open-plc' -Arguments @{ 'plc-name' = $selectedPlc })

    $stale = @((Get-Data (Invoke-Tool -Name 'get-blocks' -Arguments @{ 'plc-name' = $selectedPlc; 'filter' = '^Codex_(E2E|Behavior)_'; 'page-size' = 100 })).items)
    if ($stale.Count -ne 0) { throw 'Refusing to start over stale or colliding E2E blocks: ' + (($stale.Name) -join ', ') }
    $reserved = @($probeName, $productName, $modifyProbeName) + $behaviorNames
    $collisionPattern = '^(' + (($reserved | ForEach-Object { [Regex]::Escape($_) }) -join '|') + ')$'
    $collisions = @((Get-Data (Invoke-Tool -Name 'get-blocks' -Arguments @{ 'plc-name' = $selectedPlc; 'filter' = $collisionPattern; 'page-size' = 100 })).items)
    if ($collisions.Count -ne 0) { throw 'E2E reserved block names already exist: ' + (($collisions.Name) -join ', ') }
    $behaviorWasClear = $true
    foreach ($name in $reserved) { $createdNames.Add($name) }

    $utf8Bom = [Text.UTF8Encoding]::new($true)
    $source = [IO.File]::ReadAllText($counterAsset).TrimStart([char]0xFEFF)
    $source = [Regex]::Replace($source, '(?im)^\s*S7_BlockNumber\s*:=\s*"[^"]+";\s*\r?\n', '')
    $probeDirectory = Join-Path $testRoot 'qualification'
    $productDirectory = Join-Path $testRoot 'production'
    New-Item -ItemType Directory -Force -Path $probeDirectory, $productDirectory | Out-Null
    $probePath = Join-Path $probeDirectory ($probeName + '.s7dcl')
    $productPath = Join-Path $productDirectory ($productName + '.s7dcl')
    [IO.File]::WriteAllText($probePath, $source.Replace('Codex_LAD_Counter_Probe', $probeName), $utf8Bom)
    [IO.File]::WriteAllText($productPath, $source.Replace('Codex_LAD_Counter_Probe', $productName), $utf8Bom)

    $probeCapability = Discover-BlockCapability -Name $probeName -Operation 'Create' -Type 'FB' -Language 'LAD' -Format 'S7DCL'
    $qualification = Get-Data (Invoke-Tool -Name 'run-plc-code-probe' -Arguments @{
        'plc-name' = $selectedPlc; 'capability-ids' = [string]$probeCapability.CapabilityId; 'code-format' = 's7dcl'
        'input-path' = $probePath; 'force' = $true; 'confirm' = $true
    })
    if ([string]$qualification.Evidence.Status -ne 'Verified' -or $qualification.CleanupProven -ne $true) {
        throw 'Disposable create qualification or cleanup was not verified.'
    }
    $createEvidenceId = [string]$qualification.Evidence.EvidenceId
    $productCapability = Discover-BlockCapability -Name $productName -Operation 'Create' -Type 'FB' -Language 'LAD' -Format 'S7DCL'
    $createApply = Invoke-Promotion -EvidenceId $createEvidenceId -Capability $productCapability -ArtifactPath $productPath

    $baselineDirectory = Join-Path $testRoot 'baseline'
    $candidateDirectory = Join-Path $testRoot 'candidate'
    $modifyProbeDirectory = Join-Path $testRoot 'modify-qualification'
    New-Item -ItemType Directory -Force -Path $baselineDirectory, $candidateDirectory, $modifyProbeDirectory | Out-Null
    [void](Invoke-Tool -Name 'export-block-as-docs' -Arguments @{ 'plc-name' = $selectedPlc; 'block-name' = $productName; 'export-path' = $baselineDirectory })
    $baselinePath = Join-Path $baselineDirectory ($productName + '.s7dcl')
    if (-not (Test-Path -LiteralPath $baselinePath)) { throw 'The promoted production block baseline export was not created.' }
    $baselineText = [IO.File]::ReadAllText($baselinePath).TrimStart([char]0xFEFF)
    $modifiedText = [Regex]::Replace($baselineText, 'CU\s*:=\s*#CountUp', 'CU := (#CountUp AND TRUE)', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($modifiedText -eq $baselineText) { throw 'The exported counter implementation did not contain the expected CTU input.' }
    $candidatePath = Join-Path $candidateDirectory ($productName + '.s7dcl')
    $modifyProbePath = Join-Path $modifyProbeDirectory ($modifyProbeName + '.s7dcl')
    [IO.File]::WriteAllText($candidatePath, $modifiedText, $utf8Bom)
    $modifyProbeText = $modifiedText.Replace($productName, $modifyProbeName)
    $modifyProbeText = [Regex]::Replace($modifyProbeText, '(?im)^\s*S7_BlockNumber\s*:=\s*"[^"]+";\s*\r?\n', '')
    [IO.File]::WriteAllText($modifyProbePath, $modifyProbeText, $utf8Bom)

    $modifyProbeCapability = Discover-BlockCapability -Name $modifyProbeName -Operation 'Create' -Type 'FB' -Language 'LAD' -Format 'S7DCL'
    $modifyQualification = Get-Data (Invoke-Tool -Name 'run-plc-code-probe' -Arguments @{
        'plc-name' = $selectedPlc; 'capability-ids' = [string]$modifyProbeCapability.CapabilityId; 'code-format' = 's7dcl'
        'input-path' = $modifyProbePath; 'force' = $true; 'confirm' = $true
    })
    if ([string]$modifyQualification.Evidence.Status -ne 'Verified' -or $modifyQualification.CleanupProven -ne $true) {
        throw 'Disposable modification-shape qualification or cleanup was not verified.'
    }
    $modifyCapability = Discover-BlockCapability -Name $productName -Operation 'Modify' -Type 'FB' -Language 'LAD' -Format 'S7DCL'
    $modifyApply = Invoke-Promotion -EvidenceId ([string]$modifyQualification.Evidence.EvidenceId) -Capability $modifyCapability -ArtifactPath $candidatePath -BaselineDirectory $baselineDirectory

    $behaviorEvidenceId = ''
    $behaviorAssertions = 0
    if (-not $SkipBehavior) {
        if ($SynchronizeSimulatorBaseline) {
            $baselineDownload = Get-Data (Invoke-Tool -Name 'download-software' -Arguments @{
                'plc-name' = $selectedPlc; 'to-simulator' = $true; 'dry-run' = $false; 'only-changes' = $true; 'confirm' = $true
            })
            if ($baselineDownload.Success -ne $true) { throw 'Explicit PLCSIM baseline synchronization failed.' }
        }
        $simulator = Get-Data (Invoke-Tool -Name 'discover-plc-simulator-capabilities' -Arguments @{
            'plc-name' = $selectedPlc; 'instance-name' = $InstanceName; 'refresh-tags' = $true
        })
        if ($simulator.Available -ne $true -or $null -eq $simulator.SelectedInstance) { throw 'No compatible powered PLCSIM Advanced instance was selected.' }
        $functionCapability = Discover-BlockCapability -Name 'Codex_Behavior_Function' -Operation 'Create' -Type 'FC' -Language 'SCL' -Format 'SCL'
        $dbCapability = Discover-BlockCapability -Name 'Codex_Behavior_IO' -Operation 'Create' -Type 'GlobalDB' -Language '' -Format 'S7DCL'
        $cycleCapability = Discover-BlockCapability -Name 'Codex_Behavior_Cycle' -Operation 'Create' -Type 'OB' -Language 'SCL' -Format 'SCL'
        $behavior = Get-Data (Invoke-Tool -Name 'run-plc-code-behavior-probe' -Arguments @{
            'plc-name' = $selectedPlc
            'capability-ids' = (@([string]$functionCapability.CapabilityId, [string]$dbCapability.CapabilityId, [string]$cycleCapability.CapabilityId) -join ',')
            'package-path' = $behaviorPackage; 'vector-path' = $behaviorVector; 'instance-name' = [string]$simulator.SelectedInstance.Name
            'only-changes' = $true; 'force' = $true; 'confirm' = $true
        })
        if ([string]$behavior.Evidence.Status -ne 'Verified' -or $behavior.Evidence.CleanupProven -ne $true -or $behavior.Evidence.Behavior.SimulatorCleanupProven -ne $true -or $behavior.Evidence.Behavior.Execution.Passed -ne $true) {
            throw 'PLCSIM behavioral qualification or cleanup was not fully verified.'
        }
        $behaviorEvidenceId = [string]$behavior.Evidence.EvidenceId
        $behaviorAssertions = @($behavior.Evidence.Behavior.Execution.Steps.Assertions).Count
    }

    [void](Invoke-Tool -Name 'delete-blocks' -Arguments @{ 'plc-name' = $selectedPlc; 'filter' = ('^' + [Regex]::Escape($productName) + '$'); 'confirm' = $true })
    $compile = Get-Data (Invoke-Tool -Name 'compile-software' -Arguments @{ 'plc-name' = $selectedPlc })
    $remaining = @((Get-Data (Invoke-Tool -Name 'get-blocks' -Arguments @{ 'plc-name' = $selectedPlc; 'filter' = $collisionPattern; 'page-size' = 100 })).items)
    if ($remaining.Count -ne 0 -or [int]$compile.ErrorCount -ne 0 -or [int]$compile.WarningCount -ne 0) {
        throw 'Final E2E cleanup did not restore a zero-residue, clean-compiling project.'
    }
    foreach ($transactionId in @($transactionIds)) {
        [void](Invoke-Tool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId; 'confirm' = $true })
    }
    $transactionIds.Clear()
    $cleanupVerified = $true

    $result = [ordered]@{
        schemaVersion = '1.0'; runId = $runId; tiaProcessId = $TiaProcessId; plcName = $selectedPlc
        createEvidenceId = $createEvidenceId; createTransactionId = [string]$createApply.TransactionId; createVerified = [bool]$createApply.Verified
        modifyEvidenceId = [string]$modifyQualification.Evidence.EvidenceId; modifyTransactionId = [string]$modifyApply.TransactionId; modifyVerified = [bool]$modifyApply.Verified
        behaviorEvidenceId = $behaviorEvidenceId; behaviorAssertions = $behaviorAssertions
        finalCompileErrors = [int]$compile.ErrorCount; finalCompileWarnings = [int]$compile.WarningCount; remainingReservedBlocks = $remaining.Count
        cleanupVerified = $cleanupVerified; workerPath = $resolvedWorker
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedOutput)) | Out-Null
        [IO.File]::WriteAllText($resolvedOutput, ($result | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    }
    [pscustomobject]$result
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($selectedPlc) -and $behaviorWasClear) {
        try {
            $cleanupPattern = '^(' + (($createdNames | ForEach-Object { [Regex]::Escape($_) }) -join '|') + ')$'
            [void](Invoke-Tool -Name 'delete-blocks' -Arguments @{ 'plc-name' = $selectedPlc; 'filter' = $cleanupPattern; 'confirm' = $true })
            [void](Invoke-Tool -Name 'compile-software' -Arguments @{ 'plc-name' = $selectedPlc })
        } catch {}
    }
    foreach ($transactionId in @($transactionIds)) {
        try { [void](Invoke-Tool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId; 'confirm' = $true }) } catch {}
    }
    try { $process.StandardInput.Close() } catch {}
    if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { Write-Warning "MCP process exited with $($process.ExitCode): $stderr" }
    $process.Dispose()
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
