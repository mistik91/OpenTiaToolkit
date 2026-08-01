[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaHardwareCaseTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

function New-ToolEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$InputObject,
        [Parameter(Mandatory = $true)][string]$Output,
        [switch]$ErrorOutput
    )

    $state = [ordered]@{ status = if ($ErrorOutput) { 'error' } else { 'completed' }; input = $InputObject }
    $outputKey = if ($ErrorOutput) { 'error' } else { 'output' }
    [void]($state[$outputKey] = $Output)
    return [ordered]@{
        type = 'tool_use'
        part = [ordered]@{ tool = $Tool; state = $state }
    }
}

function Invoke-HardwareCaseAssertionTest {
    param(
        [Parameter(Mandatory = $true)][string]$CaseId,
        [Parameter(Mandatory = $true)][object[]]$Events
    )

    $caseRoot = Join-Path (Join-Path $benchmarkRoot 'cases') $CaseId
    $tracePath = Join-Path $testRoot "$CaseId-events.jsonl"
    $metadataPath = Join-Path $testRoot "$CaseId-metadata.json"
    $resultPath = Join-Path $testRoot "$CaseId-result.json"

    $allEvents = @($Events) + @(
        [ordered]@{ type = 'step_finish'; part = [ordered]@{
                reason = 'stop'; tokens = [ordered]@{ total = 10; input = 5; output = 5; reasoning = 0 }; cost = 0
            } }
    )
    $allEvents |
        ForEach-Object { $_ | ConvertTo-Json -Depth 30 -Compress } |
        Set-Content -LiteralPath $tracePath -Encoding UTF8

    [ordered]@{
        model = 'synthetic/model'; trialIndex = 1; reasoningEffort = 'max'; variant = 'max'
        openCodeVersion = '1.18.10'; gitCommit = '0000000'; gitBranch = 'test'; gitDirty = $false
        gitStateSha256 = ('0' * 64); fixtureSha256 = ('1' * 64)
        startedAtUtc = '2026-01-01T00:00:00.0000000Z'; finishedAtUtc = '2026-01-01T00:00:01.0000000Z'
        durationSeconds = 1; exitCode = 0; timedOut = $false
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath (Join-Path $caseRoot 'case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath
    if (-not [bool]$result.passed -or [double]$result.score -ne 100) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } | ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "$CaseId representative trace failed: score=$($result.score); assertions=$failed"
    }
}

try {
    $originalOperation = [ordered]@{ operations = @([ordered]@{
                op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DI_Candidate', 'DI_Candidate') }
                addressIndex = 0; startAddress = 64; expectedStartAddress = 2
            }) }
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-14' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output 'DI_Candidate DI_Occupied'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output 'DI_Candidate DI_Occupied StartAddress 2 64'),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $originalOperation; 'dry-run' = $true }) -Output 'This address is already being used. Next free address: 66.' -ErrorOutput),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Collision with DI_Occupied; next free address 66. No mutation applied.' } }
    )

    $baselineMap = '{"Target":{"DeviceName":"PLC_Main","ItemPath":["DI_Candidate","DI_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"2"}]}'
    $relocatedMap = '{"Target":{"DeviceName":"PLC_Main","ItemPath":["DI_Candidate","DI_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"66"}]}'
    $candidateOperation = [ordered]@{ operations = [ordered]@{ operations = @([ordered]@{
                    op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DI_Candidate', 'DI_Candidate') }
                    addressIndex = 0; startAddress = 66; expectedStartAddress = 2
                }) } }
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-15' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output 'DI_Candidate'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $baselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $candidateOperation.operations; 'dry-run' = $true }) -Output '{"DryRun":true,"Applied":false,"RolledBack":true,"Operations":[{"Data":{"BeforeStartAddress":2,"WrittenStartAddress":66}}]}'),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $candidateOperation.operations; 'dry-run' = $false; confirm = $true }) -Output '{"DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Data":{"BeforeStartAddress":2,"WrittenStartAddress":66}}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $relocatedMap),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Applied DI_Candidate at 66 and verified it by fresh readback.' } }
    )

    $reverseOperation = [ordered]@{ operations = @([ordered]@{
                op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DI_Occupied', 'DI_Occupied') }
                addressIndex = 0; startAddress = 2; expectedStartAddress = 64
            }) }
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-16' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output 'DI_Occupied DI_Candidate'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output 'DI_Occupied DI_Candidate StartAddress 64 2'),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $reverseOperation; 'dry-run' = $true }) -Output 'This address is already being used. Next free address: 4.' -ErrorOutput),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Collision with DI_Candidate; next free address 4. No mutation applied.' } }
    )

    Write-Host 'Hardware I/O case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaHardwareCaseTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
