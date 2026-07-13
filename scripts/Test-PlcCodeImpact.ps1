[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$WorkerPath,

    [string]$AssetPath = '',

    [ValidateRange(10, 300)]
    [int]$TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$resolvedWorker = (Resolve-Path -LiteralPath $WorkerPath).Path
if ([string]::IsNullOrWhiteSpace($AssetPath)) {
    $AssetPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'skills\s7dcl-lad-authoring\assets\Codex_LAD_Probe.s7dcl'
}
$resolvedAsset = (Resolve-Path -LiteralPath $AssetPath).Path
$testRoot = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'OpenTiaToolkit') ('PlcImpact-' + [Guid]::NewGuid().ToString('N'))
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
$startInfo.EnvironmentVariables['OPEN_TIA_MCP_ALLOWED_ROOTS'] = ((Split-Path -Parent $PSScriptRoot) + ';' + (Join-Path ([IO.Path]::GetTempPath()) 'OpenTiaToolkit'))

$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (-not $process.Start()) { throw "Failed to start MCP executable '$resolvedExecutable'." }
$stderrTask = $process.StandardError.ReadToEndAsync()
$requestId = 0
$transactionIds = [Collections.Generic.List[string]]::new()
$probeName = 'Codex_Impact_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$plcName = ''

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
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress -Depth 30))
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

try {
    [void](Invoke-Request -Method 'initialize' -Params ([ordered]@{
        protocolVersion = '2024-11-05'
        capabilities = @{}
        clientInfo = [ordered]@{ name = 'OpenTiaToolkit PLC impact live test'; version = '1.0' }
    }))
    $process.StandardInput.WriteLine((([ordered]@{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} }) | ConvertTo-Json -Compress))
    $process.StandardInput.Flush()

    $tools = @((Invoke-Request -Method 'tools/list' -Params @{}).result.tools.name)
    foreach ($required in @('get-plc-code-impact', 'approve-plc-code-impact', 'discover-plc-code-capabilities')) {
        if ($tools -notcontains $required) { throw "MCP surface is missing '$required'." }
    }

    $instances = @((Get-Data (Invoke-Tool -Name 'list-instances')).instances)
    if ($instances.Count -ne 1) { throw "Expected exactly one live TIA instance; found $($instances.Count)." }
    [void](Invoke-Tool -Name 'connect-instance' -Arguments @{ 'instance-id' = [int]$instances[0].processId })
    $projects = @(Get-Data (Invoke-Tool -Name 'list-projects'))
    if ($projects.Count -lt 1) { throw 'The connected TIA instance has no open project.' }
    $plcs = @(Get-Data (Invoke-Tool -Name 'list-plcs'))
    if ($plcs.Count -lt 1) { throw 'The open project has no discoverable PLC.' }
    $plcName = [string]$plcs[0].name
    [void](Invoke-Tool -Name 'open-plc' -Arguments @{ 'plc-name' = $plcName })

    $source = [IO.File]::ReadAllText($resolvedAsset)
    $source = $source.Replace('Codex_LAD_Probe', $probeName).Replace('59991', '63991')
    $createPath = Join-Path $testRoot 'create'
    $modifyPath = Join-Path $testRoot 'modify'
    New-Item -ItemType Directory -Force -Path $createPath, $modifyPath | Out-Null
    $utf8Bom = [Text.UTF8Encoding]::new($true)
    [IO.File]::WriteAllText((Join-Path $createPath ($probeName + '.s7dcl')), $source, $utf8Bom)
    $modified = $source.Replace('        InB : Bool;', "        InB : Bool;`r`n        ImpactReset : Bool;")
    [IO.File]::WriteAllText((Join-Path $modifyPath ($probeName + '.s7dcl')), $modified, $utf8Bom)

    $createCapability = Get-Data (Invoke-Tool -Name 'discover-plc-code-capabilities' -Arguments @{
        'plc-name' = $plcName; 'object-kind' = 'Block'; 'object-name' = $probeName; 'group-path' = ''
        'operation' = 'Create'; 'block-type' = 'FC'; 'requested-language' = 'LAD'
    })
    if ([string]$createCapability.SelectedRoute -ne 'DirectDocuments') { throw "Expected DirectDocuments create route; got $($createCapability.SelectedRoute)." }
    $createTransaction = Get-Data (Invoke-Tool -Name 'begin-plc-code-transaction' -Arguments @{
        'plc-name' = $plcName; 'description' = 'Live Step 6 safe create'; 'capability-id' = [string]$createCapability.CapabilityId
    })
    $transactionIds.Add([string]$createTransaction.TransactionId)
    [void](Invoke-Tool -Name 'add-plc-code-transaction-operation' -Arguments @{
        'transaction-id' = [string]$createTransaction.TransactionId; 'import-directory' = $createPath; 'file-name' = $probeName
        'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true
    })
    $createPlan = Get-Data (Invoke-Tool -Name 'plan-plc-code-transaction' -Arguments @{ 'transaction-id' = [string]$createTransaction.TransactionId })
    if ([string]$createPlan.State -ne 'Planned' -or [string]$createPlan.ImpactAnalysis.Status -ne 'Safe' -or [string]::IsNullOrWhiteSpace([string]$createPlan.ApplyToken)) {
        throw "Create impact was not Safe and directly applicable: $($createPlan | ConvertTo-Json -Compress -Depth 30)"
    }
    $createResult = Get-Data (Invoke-Tool -Name 'apply-plc-code-transaction' -Arguments @{
        'transaction-id' = [string]$createPlan.TransactionId; 'apply-token' = [string]$createPlan.ApplyToken; 'confirm' = $true
    })
    if ($createResult.Applied -ne $true -or $createResult.Verified -ne $true) { throw 'Safe create transaction was not verified.' }

    $modifyCapability = Get-Data (Invoke-Tool -Name 'discover-plc-code-capabilities' -Arguments @{
        'plc-name' = $plcName; 'object-kind' = 'Block'; 'object-name' = $probeName; 'group-path' = ''
        'operation' = 'Modify'; 'block-type' = 'FC'; 'requested-language' = 'LAD'
    })
    $modifyTransaction = Get-Data (Invoke-Tool -Name 'begin-plc-code-transaction' -Arguments @{
        'plc-name' = $plcName; 'description' = 'Live Step 6 interface review'; 'capability-id' = [string]$modifyCapability.CapabilityId
    })
    $transactionIds.Add([string]$modifyTransaction.TransactionId)
    [void](Invoke-Tool -Name 'add-plc-code-transaction-operation' -Arguments @{
        'transaction-id' = [string]$modifyTransaction.TransactionId; 'import-directory' = $modifyPath; 'file-name' = $probeName
        'group-path' = ''; 'import-option' = 'Override'; 'confirm' = $true
    })
    $review = Get-Data (Invoke-Tool -Name 'plan-plc-code-transaction' -Arguments @{ 'transaction-id' = [string]$modifyTransaction.TransactionId })
    if ([string]$review.State -ne 'ImpactReviewRequired' -or [string]$review.ImpactAnalysis.Targets[0].ChangeKind -ne 'InterfaceChange' -or -not [string]::IsNullOrWhiteSpace([string]$review.ApplyToken)) {
        throw "Interface change did not require impact review: $($review | ConvertTo-Json -Compress -Depth 30)"
    }
    $impact = Get-Data (Invoke-Tool -Name 'get-plc-code-impact' -Arguments @{ 'transaction-id' = [string]$review.TransactionId })
    [void](Invoke-Tool -Name 'approve-plc-code-impact' -Arguments @{
        'transaction-id' = [string]$review.TransactionId; 'impact-fingerprint' = 'incorrect'; 'confirm' = $true
    } -ExpectError)
    $acceptUnknown = [string]$impact.Status -eq 'Unknown'
    $approved = Get-Data (Invoke-Tool -Name 'approve-plc-code-impact' -Arguments @{
        'transaction-id' = [string]$review.TransactionId; 'impact-fingerprint' = [string]$impact.Fingerprint
        'accept-unknown' = $acceptUnknown; 'confirm' = $true
    })
    if ([string]$approved.State -ne 'Planned' -or $approved.ImpactAcknowledged -ne $true -or [string]::IsNullOrWhiteSpace([string]$approved.ApplyToken)) {
        throw 'Reviewed impact did not transition to a fingerprint-bound Planned state.'
    }
    $modifyResult = Get-Data (Invoke-Tool -Name 'apply-plc-code-transaction' -Arguments @{
        'transaction-id' = [string]$approved.TransactionId; 'apply-token' = [string]$approved.ApplyToken; 'confirm' = $true
    })
    if ($modifyResult.Applied -ne $true -or $modifyResult.Verified -ne $true) { throw 'Reviewed interface transaction was not verified.' }
    $compile = Get-Data (Invoke-Tool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcName })
    if ([int]$compile.ErrorCount -ne 0) { throw "Final live compile has $($compile.ErrorCount) error(s)." }

    [void](Invoke-Tool -Name 'delete-blocks' -Arguments @{ 'plc-name' = $plcName; 'filter' = ('^' + [Regex]::Escape($probeName) + '$'); 'confirm' = $true })
    $cleanupCompile = Get-Data (Invoke-Tool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcName })
    $remainingProbe = @(Get-Data (Invoke-Tool -Name 'search-blocks' -Arguments @{ 'plc-name' = $plcName; 'pattern' = ('^' + [Regex]::Escape($probeName) + '$'); 'search-in' = 'name' }))
    if ($remainingProbe.Count -ne 0 -or [int]$cleanupCompile.ErrorCount -ne 0) { throw 'Live impact probe cleanup could not be proven.' }
    foreach ($transactionId in @($transactionIds)) {
        [void](Invoke-Tool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId; 'confirm' = $true })
    }
    $remainingTransactions = @(Get-Data (Invoke-Tool -Name 'list-plc-code-transactions') | Where-Object { $transactionIds -contains [string]$_.TransactionId })
    if ($remainingTransactions.Count -ne 0) { throw 'Live impact transactions remain after cleanup.' }
    $transactionIds.Clear()

    [pscustomobject]@{
        PlcName = $plcName
        ProbeName = $probeName
        CreateImpact = [string]$createPlan.ImpactAnalysis.Status
        ModifyImpact = [string]$impact.Status
        ModifyChangeKind = [string]$impact.Targets[0].ChangeKind
        PlcConsumerCount = @($impact.Targets[0].Consumers).Count
        PlcConsumers = @($impact.Targets[0].Consumers | ForEach-Object { ([string]$_.Name + '|' + [string]$_.Path + '|' + [string]$_.Usage + '|' + [string]$_.Detail) }) -join '; '
        UnknownExplicitlyAccepted = $acceptUnknown
        CreateVerified = [bool]$createResult.Verified
        ModifyVerified = [bool]$modifyResult.Verified
        FinalCompileErrors = [int]$compile.ErrorCount
        CleanupCompileErrors = [int]$cleanupCompile.ErrorCount
        CleanupVerified = $true
    }
}
finally {
    try {
        if (-not [string]::IsNullOrWhiteSpace($plcName)) {
            [void](Invoke-Tool -Name 'delete-blocks' -Arguments @{ 'plc-name' = $plcName; 'filter' = ('^' + [Regex]::Escape($probeName) + '$'); 'confirm' = $true })
            [void](Invoke-Tool -Name 'compile-software' -Arguments @{ 'plc-name' = $plcName })
        }
    } catch {}
    foreach ($transactionId in $transactionIds) {
        try { [void](Invoke-Tool -Name 'discard-plc-code-transaction' -Arguments @{ 'transaction-id' = $transactionId; 'confirm' = $true }) } catch {}
    }
    try { $process.StandardInput.Close() } catch {}
    if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { Write-Warning "MCP process exited with $($process.ExitCode): $stderr" }
    $process.Dispose()
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
