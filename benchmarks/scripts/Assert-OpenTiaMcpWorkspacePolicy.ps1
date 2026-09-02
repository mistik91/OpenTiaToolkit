[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$McpPath,

    [Parameter(Mandatory = $true)]
    [string]$WorkspacePath,

    [Parameter(Mandatory = $true)]
    [string]$AllowedRoots,

    [ValidateRange(5, 120)]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedMcp = (Resolve-Path -LiteralPath $McpPath).Path
$resolvedWorkspace = (Resolve-Path -LiteralPath $WorkspacePath).Path.TrimEnd([char[]]'\/')
$allowedRootList = @($AllowedRoots -split ';' | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    } | ForEach-Object {
        [System.IO.Path]::GetFullPath($_).TrimEnd([char[]]'\/')
    })
if (-not @($allowedRootList | Where-Object {
            [string]::Equals($_, $resolvedWorkspace, [System.StringComparison]::OrdinalIgnoreCase)
        })) {
    throw "Benchmark MCP allowed roots do not contain the exact workspace '$resolvedWorkspace'."
}

$probePath = Join-Path $resolvedWorkspace '.opentia-mcp-policy-probe.xml'
$probeContent = '<Document />'
[System.IO.File]::WriteAllText($probePath, $probeContent, [System.Text.UTF8Encoding]::new($false))

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $resolvedMcp
$startInfo.WorkingDirectory = Split-Path -Parent $resolvedMcp
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.EnvironmentVariables['OPEN_TIA_MCP_ALLOWED_ROOTS'] = $AllowedRoots
$startInfo.EnvironmentVariables['OPEN_TIA_MCP_LOG_LEVEL'] = 'Error'

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$processStarted = $false
$stderrTask = $null

function Stop-PolicyProbeProcessTree {
    param([Parameter(Mandatory = $true)][int]$RootProcessId)

    $processes = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId)
    $owned = [System.Collections.Generic.HashSet[int]]::new()
    [void]$owned.Add($RootProcessId)
    do {
        $added = $false
        foreach ($candidate in $processes) {
            if ($owned.Contains([int]$candidate.ParentProcessId) -and
                -not $owned.Contains([int]$candidate.ProcessId)) {
                [void]$owned.Add([int]$candidate.ProcessId)
                $added = $true
            }
        }
    } while ($added)

    foreach ($processId in @($owned) | Sort-Object -Descending) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
}

try {
    if (-not $process.Start()) {
        throw "Failed to start benchmark MCP policy probe '$resolvedMcp'."
    }
    $processStarted = $true
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $request = [ordered]@{
        jsonrpc = '2.0'
        id      = 1
        method  = 'tools/call'
        params  = [ordered]@{
            name      = 'validate-plc-code-artifact'
            arguments = [ordered]@{
                'code-format' = 'simaticml'
                'input-path'  = $probePath
            }
            '_meta'   = [ordered]@{
                'io.modelcontextprotocol/protocolVersion' = '2026-07-28'
                'io.modelcontextprotocol/clientInfo'      = [ordered]@{
                    name    = 'OpenTia benchmark workspace policy preflight'
                    version = '1.0'
                }
                'io.modelcontextprotocol/clientCapabilities' = @{}
            }
        }
    }
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress -Depth 30))
    $process.StandardInput.Flush()

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $response = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        $remainingMilliseconds = [Math]::Max(
            1,
            [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds
        )
        $readTask = $process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remainingMilliseconds)) {
            break
        }
        $line = $readTask.Result
        if ($null -eq $line) {
            break
        }
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        try {
            $candidateResponse = $line | ConvertFrom-Json
        }
        catch {
            continue
        }
        if ($null -ne $candidateResponse.id -and [int]$candidateResponse.id -eq 1) {
            $response = $candidateResponse
            break
        }
    }
    if ($null -eq $response) {
        throw "Timed out waiting for the benchmark MCP workspace policy preflight after $TimeoutSeconds second(s)."
    }

    $protocolError = $response.PSObject.Properties['error']
    if ($null -ne $protocolError -and $null -ne $protocolError.Value) {
        throw "Benchmark MCP workspace policy preflight returned a protocol error: $($protocolError.Value | ConvertTo-Json -Compress -Depth 20)"
    }
    if ($null -eq $response.result) {
        throw 'Benchmark MCP workspace policy preflight returned no result.'
    }

    $structuredProperty = $response.result.PSObject.Properties['structuredContent']
    $structured = if ($null -ne $structuredProperty) {
        $structuredProperty.Value
    }
    else {
        $null
    }
    if ($null -eq $structured) {
        $textBlock = @($response.result.content | Where-Object { $_.type -eq 'text' } |
                Select-Object -First 1)
        if ($textBlock.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$textBlock[0].text)) {
            throw 'Benchmark MCP workspace policy preflight returned no structured or text result.'
        }
        $structured = [string]$textBlock[0].text | ConvertFrom-Json
    }

    $serializedResult = $structured | ConvertTo-Json -Compress -Depth 30
    $errorCode = if ($null -ne $structured.PSObject.Properties['error'] -and
        $null -ne $structured.error) {
        [string]$structured.error.code
    }
    else {
        ''
    }
    if ($errorCode -ceq 'PolicyDenied' -or
        $serializedResult -match '(?i)MCP file policy rejected') {
        throw "Benchmark MCP rejected its own workspace probe '$probePath': $serializedResult"
    }

    [pscustomobject][ordered]@{
        status        = 'passed'
        workspacePath = $resolvedWorkspace
        allowedRoots  = $allowedRootList
        probePath     = $probePath
    }
}
finally {
    if ($processStarted -and $process.HasExited -eq $false) {
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(5000)) {
            Stop-PolicyProbeProcessTree -RootProcessId $process.Id
            [void]$process.WaitForExit(5000)
        }
    }
    $process.Dispose()
    if (Test-Path -LiteralPath $probePath -PathType Leaf) {
        Remove-Item -LiteralPath $probePath -Force
    }
}
