[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArchivePath,
    [Parameter(Mandatory = $true)][string]$RestoreRoot,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$VectorPath,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [Parameter(Mandatory = $true)][string]$McpPath,
    [Parameter(Mandatory = $true)][string]$WorkerPath,
    [string]$PlcName = 'PLC_Main',
    [string]$InstanceName = 'OTT35_Bench',
    [ValidateSet('Verified', 'Failed', 'Rejected')][string]$ExpectedStatus = 'Verified',
    [switch]$MismatchedDocumentRoute,
    [switch]$AcceptUnknownImpact,
    [ValidateRange(60, 900)][int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($MismatchedDocumentRoute -ne ($ExpectedStatus -eq 'Rejected')) { throw 'Route rejection control requires ExpectedStatus=Rejected and MismatchedDocumentRoute together.' }
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$vector = (Resolve-Path -LiteralPath $VectorPath).Path
$restore = [IO.Path]::GetFullPath($RestoreRoot)
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $restore) { throw 'RestoreRoot must be new; never reuse an uninspected probe project.' }
if (Test-Path -LiteralPath $output) { throw 'OutputRoot must be new; evidence is append-only.' }
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
New-Item -ItemType Directory -Path $output | Out-Null
$journal = Join-Path $output 'mcp.jsonl'
$utf8 = [Text.UTF8Encoding]::new($false)
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = (Resolve-Path -LiteralPath $McpPath).Path
$info.WorkingDirectory = Split-Path -Parent $info.FileName
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardInput = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.EnvironmentVariables['OPEN_TIA_WORKER_V21_EXE'] = (Resolve-Path -LiteralPath $WorkerPath).Path
$info.EnvironmentVariables['OPEN_TIA_MCP_LOG_LEVEL'] = 'None'
$info.EnvironmentVariables['OPEN_TIA_MCP_ALLOWED_ROOTS'] = (@(
    $restore, $output, (Split-Path -Parent $archive), (Split-Path -Parent $package), (Split-Path -Parent $vector),
    (Join-Path ([IO.Path]::GetTempPath()) 'OpenTiaToolkit')
) | Select-Object -Unique) -join ';'
$process = [Diagnostics.Process]::new()
$process.StartInfo = $info
[void]$process.Start()
$stderr = $process.StandardError.ReadToEndAsync()
$requestId = 0
$projectOpen = $false
$retainSession = $false

function Invoke-Request {
    param([string]$Method, [object]$Parameters)
    $script:requestId++
    $request = @{jsonrpc = '2.0'; id = $script:requestId; method = $Method; params = $Parameters}
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress -Depth 50))
    $process.StandardInput.Flush()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $read = $process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait([Math]::Max(1, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds))) {
            $script:retainSession = $true
            throw "MCP response timed out; operation outcome is unknown. Inspect the retained session before retrying."
        }
        if ($null -eq $read.Result) { throw 'MCP exited before returning its response.' }
        $response = $read.Result | ConvertFrom-Json
        if ($null -ne $response.PSObject.Properties['id'] -and [int]$response.id -eq $script:requestId) {
            [IO.File]::AppendAllText($journal, ((@{request = $request; response = $response} | ConvertTo-Json -Depth 100 -Compress) + "`n"), $utf8)
            return $response
        }
    }
    throw 'MCP response deadline expired.'
}

function Invoke-Tool {
    param([string]$Name, [hashtable]$Arguments = @{}, [switch]$AllowFailure)
    Write-Host ("{0:o} {1}" -f [DateTime]::UtcNow, $Name)
    $response = Invoke-Request 'tools/call' @{name = $Name; arguments = $Arguments}
    if ($null -ne $response.PSObject.Properties['error']) { throw ($response.error | ConvertTo-Json -Compress) }
    $result = $response.result.structuredContent
    $script:lastToolEnvelope = $result
    if ($result.success -ne $true -and -not $AllowFailure) { throw "$Name failed: $($result.message)" }
    return $result.data
}

try {
    [void](Invoke-Request 'initialize' @{protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{name = 'OTT35 independent behavior qualification'; version = '1.0'}})
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
    $process.StandardInput.Flush()
    [void](Invoke-Tool 'list-instances')
    # Start our own portal; never close, import into, or download from the user's project.
    [void](Invoke-Tool 'open-tia-portal' @{'with-ui' = $false})
    [void](Invoke-Tool 'retrieve-project' @{'archive-path' = $archive; 'target-directory' = $restore; confirm = $true})
    $projectOpen = $true
    $identity = Invoke-Tool 'get-project-info'
    $projectPath = [IO.Path]::GetFullPath([string]$identity.Path).Replace('\\', '\')
    if (-not $projectPath.StartsWith(($restore.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase) -or -not $identity.IsToolkitOwned) {
        throw 'The restored project did not prove independent toolkit ownership.'
    }
    [void](Invoke-Tool 'open-plc' @{'plc-name' = $PlcName})
    $baseline = Invoke-Tool 'get-blocks' @{'plc-name' = $PlcName; 'page-size' = 1000}
    $baselineSources = Invoke-Tool 'list-external-sources' @{'plc-name' = $PlcName; 'page-size' = 1000}
    $compile = Invoke-Tool 'compile-software' @{'plc-name' = $PlcName}
    if ($compile.errorCount -ne 0 -or $compile.warningCount -ne 0) { throw 'Baseline compile is not clean.' }
    [void](Invoke-Tool 'export-block-as-docs' @{'plc-name' = $PlcName; 'block-name' = 'Main'; 'export-path' = (Join-Path $output 'before')})
    $simulator = Invoke-Tool 'discover-plc-simulator-capabilities' @{'plc-name' = $PlcName; 'instance-name' = $InstanceName; 'refresh-tags' = $true}
    if ($simulator.SelectedInstance.ControllerName -cne $PlcName -or $simulator.SelectedInstance.ArticleNumber -cne '6ES7 511-1AL03-0AB0') { throw 'Unexpected simulator controller or article identity.' }
    [void](Invoke-Tool 'validate-plc-behavior-vector' @{'vector-path' = $vector})
    [void](Invoke-Tool 'validate-plc-behavior-package' @{'package-path' = $package})
    $manifest = Get-Content -LiteralPath $package -Raw | ConvertFrom-Json
    $ids = [Collections.Generic.List[string]]::new()
    $transactionsBefore = Invoke-Tool 'list-plc-code-transactions'
    foreach ($artifact in $manifest.artifacts) {
        $artifactPath = Join-Path (Split-Path -Parent $package) ([string]$artifact.inputPath)
        $validation = Invoke-Tool 'validate-plc-code-artifact' @{'code-format' = $artifact.codeFormat; 'input-path' = $artifactPath}
        if (-not $validation.Valid -or -not $validation.HasUtf8Bom) { throw 'A code artifact is invalid or lacks UTF-8 BOM.' }
        foreach ($declaration in $validation.DeclaredObjects) {
            $arguments = @{'plc-name' = $PlcName; 'object-name' = $declaration.Name; 'object-kind' = 'Block'; operation = 'Create'; 'block-type' = $declaration.ObjectType; 'requested-format' = $artifact.codeFormat; 'group-path' = $artifact.groupPath}
            if ($artifact.codeFormat -eq 'scl') { $arguments['requested-language'] = 'SCL' }
            if ($MismatchedDocumentRoute -and $artifact.codeFormat -eq 's7dcl') {
                $arguments['requested-format'] = 'scl'
                $arguments['requested-language'] = 'SCL'
            }
            $capability = Invoke-Tool 'discover-plc-code-capabilities' $arguments
            if ($capability.SelectedRoute -eq 'Blocked') { throw 'A probe declaration is blocked.' }
            $ids.Add([string]$capability.CapabilityId)
        }
    }
    $arguments = @{'plc-name' = $PlcName; 'capability-ids' = ($ids -join ','); 'package-path' = $package; 'vector-path' = $vector; 'instance-name' = $InstanceName}
    [void](Invoke-Tool 'query-plc-behavior-evidence' $arguments -AllowFailure:$MismatchedDocumentRoute)
    if ($MismatchedDocumentRoute -and ($lastToolEnvelope.success -or $lastToolEnvelope.message -notmatch 'DirectDocuments')) { throw 'Mismatched query route was not rejected before execution.' }
    $arguments['compile-policy'] = 'CleanCompile'
    $arguments['maximum-new-warnings'] = 0
    $arguments['only-changes'] = $true
    $arguments['force'] = $true
    $arguments['confirm'] = $true
    $arguments['accept-unknown-impact'] = [bool]$AcceptUnknownImpact
    # Do not dispose a potentially mutated project if the tool throws, returns an
    # incomplete envelope, or recovery itself fails before proving both cleanups.
    $retainSession = $true
    $probe = Invoke-Tool 'run-plc-code-behavior-probe' $arguments -AllowFailure
    [IO.File]::WriteAllText((Join-Path $output 'probe.json'), ($probe | ConvertTo-Json -Depth 100), $utf8)
    if ($MismatchedDocumentRoute) {
        if ($lastToolEnvelope.success -or $lastToolEnvelope.message -notmatch 'DirectDocuments') { throw 'Mismatched probe route was not rejected before execution.' }
        $transactionsAfter = Invoke-Tool 'list-plc-code-transactions'
        if (($transactionsBefore | ConvertTo-Json -Depth 100 -Compress) -cne ($transactionsAfter | ConvertTo-Json -Depth 100 -Compress)) { throw 'Route rejection created or changed a transaction.' }
        $retainSession = $false
    }
    else {
        if ($null -eq $probe -or $null -eq $probe.PSObject.Properties['Evidence']) { throw 'Probe returned no evidence record.' }
        if ($probe.Evidence.Status -eq 'Quarantined') {
            $recovered = Invoke-Tool 'recover-plc-code-behavior-probe' @{'evidence-id' = $probe.Evidence.EvidenceId; 'only-changes' = $true; confirm = $true} -AllowFailure
            $retainSession = -not ($recovered.Evidence.CleanupProven -and $recovered.Evidence.Behavior.SimulatorCleanupProven)
            if ($retainSession) { throw 'Recovery is unproven; keep this session for incident handling.' }
            throw 'A quarantined probe required recovery; qualification is blocked, never passed.'
        }
        $retainSession = -not ($probe.Evidence.CleanupProven -and $probe.Evidence.Behavior.SimulatorCleanupProven)
        if ($probe.Evidence.Status -ne $ExpectedStatus -or -not $probe.Evidence.CleanupProven -or -not $probe.Evidence.Behavior.SimulatorCleanupProven) { throw "Unexpected probe status or missing cleanup proof: $($probe.Evidence.Status) $($probe.Evidence.Failure)" }
        if ($ExpectedStatus -eq 'Verified' -and (-not $probe.Evidence.Behavior.Execution.Passed -or -not $probe.Evidence.Behavior.Execution.ResetApplied -or -not $probe.Evidence.Behavior.Execution.CleanupApplied)) { throw 'Execution proof is incomplete.' }
    }
    $final = Invoke-Tool 'get-blocks' @{'plc-name' = $PlcName; 'page-size' = 1000}
    $finalSources = Invoke-Tool 'list-external-sources' @{'plc-name' = $PlcName; 'page-size' = 1000}
    if (($baseline.items | ConvertTo-Json -Depth 30 -Compress) -cne ($final.items | ConvertTo-Json -Depth 30 -Compress) -or
        ($baselineSources | ConvertTo-Json -Depth 30 -Compress) -cne ($finalSources | ConvertTo-Json -Depth 30 -Compress)) { throw 'Offline block/source inventories changed.' }
    [void](Invoke-Tool 'export-block-as-docs' @{'plc-name' = $PlcName; 'block-name' = 'Main'; 'export-path' = (Join-Path $output 'after')})
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $output 'before') -File) {
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path (Join-Path $output 'after') $file.Name)).Hash) { throw 'Original Main export changed.' }
    }
    $compile = Invoke-Tool 'compile-software' @{'plc-name' = $PlcName}
    if ($compile.errorCount -ne 0 -or $compile.warningCount -ne 0) { throw 'Cleanup compile is not clean.' }
    if (-not $MismatchedDocumentRoute) {
        $query = Invoke-Tool 'query-plc-behavior-evidence' @{'plc-name' = $PlcName; 'capability-ids' = ($ids -join ','); 'package-path' = $package; 'vector-path' = $vector; 'instance-name' = $InstanceName}
        $matchingIds = @($query.Matches | ForEach-Object { $_.EvidenceId })
        if (($ExpectedStatus -eq 'Verified' -and -not $query.Qualified) -or ($ExpectedStatus -eq 'Failed' -and $matchingIds -contains $probe.Evidence.EvidenceId)) { throw 'Exact evidence reuse classification is wrong.' }
        Write-Host "PASS: $ExpectedStatus; evidence $($probe.Evidence.EvidenceId); cleanup and independent inventories verified."
    }
    else { Write-Host 'PASS: capability route rejected before transaction creation; independent inventories and Main hash unchanged.' }
}
finally {
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveHash) { Write-Error 'Immutable archive hash changed.' }
    if ($retainSession) {
        Write-Host "INCIDENT: retained MCP PID $($process.Id). No further probes or disposal. Journal: $journal"
        # Keep ownership explicit while the operator inspects the incident, rather than
        # closing a portal with an unknown in-flight mutation or unproven cleanup.
        while (-not $process.HasExited) { Start-Sleep -Seconds 5 }
    }
    else {
        try { if ($projectOpen) { [void](Invoke-Tool 'close-project' @{save = $false; confirm = $true}) } }
        finally {
            if (-not $process.HasExited) { $process.StandardInput.Close(); [void]$process.WaitForExit(15000) }
            if ($stderr.IsCompleted) { [IO.File]::WriteAllText((Join-Path $output 'stderr.log'), $stderr.Result, $utf8) }
            $process.Dispose()
        }
    }
}
