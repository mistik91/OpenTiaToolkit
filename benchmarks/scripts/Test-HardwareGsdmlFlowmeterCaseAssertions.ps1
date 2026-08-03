[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaHardwareGsdmlTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

function New-ToolEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$InputObject,
        [Parameter(Mandatory = $true)][string]$Output,
        [switch]$ErrorOutput
    )

    $state = [ordered]@{
        status = if ($ErrorOutput) { 'error' } else { 'completed' }
        input = $InputObject
    }
    $state[ $(if ($ErrorOutput) { 'error' } else { 'output' }) ] = $Output
    return [ordered]@{
        type = 'tool_use'
        part = [ordered]@{ tool = $Tool; state = $state }
    }
}

function Invoke-GsdmlAssertionTest {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][object[]]$Events,
        [bool]$ExpectedPassed = $true,
        [string]$ExpectedFailedAssertion
    )

    $tracePath = Join-Path $testRoot "$Label-events.jsonl"
    $metadataPath = Join-Path $testRoot "$Label-metadata.json"
    $resultPath = Join-Path $testRoot "$Label-result.json"
    @($Events) + @(
        [ordered]@{ type = 'step_finish'; part = [ordered]@{
                reason = 'stop'
                tokens = [ordered]@{ total = 10; input = 5; output = 5; reasoning = 0 }
                cost = 0
            } }
    ) | ForEach-Object { $_ | ConvertTo-Json -Depth 50 -Compress } |
        Set-Content -LiteralPath $tracePath -Encoding UTF8

    [ordered]@{
        model = 'synthetic/model'
        trialIndex = 1
        reasoningEffort = 'max'
        variant = 'max'
        openCodeVersion = '1.18.10'
        gitCommit = '0000000'
        gitBranch = 'test'
        gitDirty = $false
        gitStateSha256 = ('0' * 64)
        fixtureSha256 = ('1' * 64)
        startedAtUtc = '2026-01-01T00:00:00.0000000Z'
        finishedAtUtc = '2026-01-01T00:00:01.0000000Z'
        durationSeconds = 1
        exitCode = 0
        timedOut = $false
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath (Join-Path $benchmarkRoot 'cases\ott-26\case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative GSDML trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative GSDML trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

function Copy-Operations {
    param([Parameter(Mandatory = $true)][object[]]$Operations)
    return @($Operations | ForEach-Object { $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json })
}

function New-GsdmlEvents {
    param(
        [Parameter(Mandatory = $true)][object[]]$CorrectedOperations,
        [switch]$ApplyBeforePreview,
        [switch]$PrematureBothDisplaced,
        [switch]$OmitGsdReadback
    )

    $strictOperations = Copy-Operations -Operations $CorrectedOperations
    $controlCorrectedOperations = Copy-Operations -Operations $CorrectedOperations
    foreach ($operation in $strictOperations) {
        if ([string]$operation.op -eq 'plug-item') {
            $operation.PSObject.Properties.Remove('allowDisplacedPlacement')
        }
    }
    foreach ($operation in $controlCorrectedOperations) {
        if ([string]$operation.op -eq 'plug-item' -and [string]$operation.itemName -eq 'TotalizerMode') {
            $operation.PSObject.Properties.Remove('allowDisplacedPlacement')
        }
    }

    $controlFailure = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $strictOperations
            'dry-run' = $true
        }) -Output '{"DryRun":true,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"TotalizerControl was placed at FT_101/TotalizerControl instead of requested FT_101/Totalizer_1/TotalizerControl. Set allowDisplacedPlacement=true to accept the actual placement."}' -ErrorOutput
    $modeFailure = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $controlCorrectedOperations
            'dry-run' = $true
        }) -Output '{"DryRun":true,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"TotalizerMode was placed at FT_101/TotalizerMode instead of requested FT_101/Totalizer_1/TotalizerMode. Set allowDisplacedPlacement=true to accept the actual placement."}' -ErrorOutput
    $preview = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $CorrectedOperations
            'dry-run' = $true
        }) -Output '{"message":"Hardware patch preflighted and rolled back","DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":false,"Operations":[{"Message":"Connected node to subnet: CellPN"},{"Message":"Connected IO connector to IO system: CellIO"},{"Data":{"BeforeHex":"80 DF","WrittenHex":"80 AF"}},{"Data":{"WrittenStartAddress":176}},{"Data":{"WrittenStartAddress":176}},{"Data":{"WrittenStartAddress":177}}]}'
    $apply = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = [ordered]@{ operations = $CorrectedOperations }
            'dry-run' = $false
            confirm = $true
        }) -Output '{"message":"Hardware patch applied","DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Message":"Connected node to subnet: CellPN"},{"Message":"Connected IO connector to IO system: CellIO"},{"Data":{"Written":{"Address":"192.168.0.61","PnDeviceName":"feed-flow-101","PnDeviceNumber":61}}},{"Data":{"BeforeHex":"80 DF","WrittenHex":"80 AF"}},{"Data":{"WrittenStartAddress":176}},{"Data":{"WrittenStartAddress":176}},{"Data":{"WrittenStartAddress":177}}]}'

    $placementEvents = if ($PrematureBothDisplaced) { @($controlFailure) } else { @($controlFailure, $modeFailure) }
    $mutationEvents = if ($ApplyBeforePreview) { @($apply, $preview) } else { @($preview, $apply) }
    $readbackEvents = @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{'device-name' = 'FT_101'} -Output '{"deviceName":"FT_101","typeIdentifier":"GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/DAP/DAP 1","items":[{"name":"Volume flow_1","positionNumber":1},{"name":"Totalizer_1","positionNumber":11},{"name":"TotalizerControl","positionNumber":1},{"name":"TotalizerMode","positionNumber":2}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{'device-name' = 'FT_101'} -Output '{"deviceName":"FT_101","connectedSubnet":"CellPN","connectedIoSystemName":"CellIO","Address":"192.168.0.61","PnDeviceName":"feed-flow-101","PnDeviceNumber":"61"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{'device-name' = 'FT_101'} -Output '[{"Target":{"DeviceName":"FT_101","ItemPath":["Volume flow_1","Volume flow"]},"Attributes":[{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"40"},{"Name":"StartAddress","Value":"176"}]},{"Target":{"DeviceName":"FT_101","ItemPath":["TotalizerControl"]},"Attributes":[{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"8"},{"Name":"StartAddress","Value":"176"}]},{"Target":{"DeviceName":"FT_101","ItemPath":["TotalizerMode"]},"Attributes":[{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"8"},{"Name":"StartAddress","Value":"177"}]}]')
    )
    if (-not $OmitGsdReadback) {
        $readbackEvents += New-ToolEvent -Tool 'opentia_get-gsd-parameter-data' -InputObject @{
            'device-name' = 'FT_101'; 'item-path' = 'FT_101/Promag 100 PNIO'; 'dataset-number' = 32752; 'byte-offset' = 45; length = 2
        } -Output '{"Target":{"DeviceName":"FT_101","ItemPath":["FT_101","Promag 100 PNIO"]},"DatasetNumber":32752,"ByteOffset":45,"Length":2,"DataHex":"80 AF"}'
    }
    $readbackEvents += New-ToolEvent -Tool 'opentia_compile-software' -InputObject @{'plc-name' = 'PLC_Main'} -Output '{"success":true,"ErrorCount":0,"WarningCount":0}'

    return @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output '{"deviceName":"PLC_Main"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{} -Output '{"deviceName":"PLC_Main","subnet":"CellPN","ioSystem":"CellIO"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output '[]'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = 'Promag 100'} -Output 'GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/DAP/DAP 1 GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/M/50 GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/SM/51 GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/SM/58'),
        (New-ToolEvent -Tool 'opentia_can-plug-hardware-item' -InputObject @{ 'item-name' = 'TotalizerControl'; 'position-number' = 1 } -Output '{"ItemName":"TotalizerControl","PositionNumber":1,"CanPlug":true}'),
        (New-ToolEvent -Tool 'opentia_can-plug-hardware-item' -InputObject @{ 'item-name' = 'TotalizerMode'; 'position-number' = 2 } -Output '{"ItemName":"TotalizerMode","PositionNumber":2,"CanPlug":true}')
    ) + $placementEvents + $mutationEvents + $readbackEvents
}

try {
    $operations = @(
        [ordered]@{ op = 'create-device-with-item'; resultId = 'flowmeter'; typeIdentifier = 'GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/DAP/DAP 1'; deviceName = 'FT_101'; itemName = 'FT_101' },
        [ordered]@{ op = 'plug-item'; resultId = 'totalizer-control'; targetRef = 'flowmeter'; target = [ordered]@{ itemPath = @('Totalizer_1') }; typeIdentifier = 'GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/SM/51'; itemName = 'TotalizerControl'; positionNumber = 1; allowDisplacedPlacement = $true },
        [ordered]@{ op = 'plug-item'; resultId = 'totalizer-mode'; targetRef = 'flowmeter'; target = [ordered]@{ itemPath = @('Totalizer_1') }; typeIdentifier = 'GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/SM/58'; itemName = 'TotalizerMode'; positionNumber = 2; allowDisplacedPlacement = $true },
        [ordered]@{ op = 'connect-node-to-subnet'; targetRef = 'flowmeter'; target = [ordered]@{ itemPath = @('FT_101', 'PN-IO') }; nodeIndex = 0; subnetName = 'CellPN' },
        [ordered]@{ op = 'connect-io-connector'; targetRef = 'flowmeter'; target = [ordered]@{ itemPath = @('FT_101', 'PN-IO') }; connectorIndex = 0; ioSystemName = 'CellIO' },
        [ordered]@{ op = 'configure-profinet-device'; targetRef = 'flowmeter'; target = [ordered]@{ itemPath = @('FT_101', 'PN-IO') }; ipAddress = '192.168.0.61'; pnDeviceName = 'feed-flow-101'; pnDeviceNumber = 61 },
        [ordered]@{ op = 'set-gsd-parameter-data'; targetRef = 'flowmeter'; target = [ordered]@{ itemPath = @('FT_101', 'Promag 100 PNIO') }; datasetNumber = 32752; byteOffset = 45; dataHex = '80 AF'; expectedDataHex = '80 DF' },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'flowmeter'; target = [ordered]@{ itemPath = @('Volume flow_1', 'Volume flow') }; addressIndex = 0; startAddress = 176 },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'totalizer-control'; addressIndex = 0; startAddress = 176 },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'totalizer-mode'; addressIndex = 0; startAddress = 177 }
    )

    Invoke-GsdmlAssertionTest -Label 'representative' -Events (New-GsdmlEvents -CorrectedOperations $operations)

    $unguarded = Copy-Operations -Operations $operations
    @($unguarded | Where-Object op -eq 'set-gsd-parameter-data')[0].PSObject.Properties.Remove('expectedDataHex')
    Invoke-GsdmlAssertionTest -Label 'unguarded-gsd-write' `
        -Events (New-GsdmlEvents -CorrectedOperations $unguarded) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'exact-corrected-operation-set'

    Invoke-GsdmlAssertionTest -Label 'apply-before-preview' `
        -Events (New-GsdmlEvents -CorrectedOperations $operations -ApplyBeforePreview) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'strict-preview-apply-readback-sequence'

    Invoke-GsdmlAssertionTest -Label 'premature-sibling-displacement' `
        -Events (New-GsdmlEvents -CorrectedOperations $operations -PrematureBothDisplaced) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'two-stage-strict-placement-recovery'

    Invoke-GsdmlAssertionTest -Label 'missing-gsd-readback' `
        -Events (New-GsdmlEvents -CorrectedOperations $operations -OmitGsdReadback) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'gsd-parameter-readback'

    Write-Host 'Hardware GSDML flowmeter case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_HARDWARE_GSDML_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaHardwareGsdmlTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
