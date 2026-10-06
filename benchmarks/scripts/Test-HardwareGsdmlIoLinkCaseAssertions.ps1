[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaHardwareGsdmlIoLinkTests-" + [Guid]::NewGuid().ToString('N'))
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

function Copy-Operations {
    param([Parameter(Mandatory = $true)][object[]]$Operations)
    return @($Operations | ForEach-Object { $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json })
}

function Invoke-IoLinkAssertionTest {
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
        reasoningEffort = 'high'
        variant = 'high'
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
        -CasePath (Join-Path $benchmarkRoot 'cases\ott-27\case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative IFM GSDML trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative IFM GSDML trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

function New-IoLinkEvents {
    param(
        [Parameter(Mandatory = $true)][object[]]$CorrectedOperations,
        [switch]$ApplyBeforePreview,
        [switch]$PrematureBothDisplaced,
        [switch]$OmitGsdReadback,
        [switch]$ImpossibleCapabilityProbe
    )

    $strictOperations = Copy-Operations -Operations $CorrectedOperations
    $flowCorrectedOperations = Copy-Operations -Operations $CorrectedOperations
    foreach ($operation in $strictOperations) {
        if ([string]$operation.op -eq 'plug-item') {
            $operation.PSObject.Properties.Remove('allowDisplacedPlacement')
        }
    }
    foreach ($operation in $flowCorrectedOperations) {
        if ([string]$operation.op -eq 'plug-item' -and [string]$operation.itemName -eq 'LevelPort') {
            $operation.PSObject.Properties.Remove('allowDisplacedPlacement')
        }
    }

    $flowFailure = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $strictOperations
            'dry-run' = $true
        }) -Output '{"DryRun":true,"SafetyMessage":"FlowPort was placed at IOL_Master_01/FlowPort instead of requested IOL_Master_01/4 Ports_1/FlowPort. Set allowDisplacedPlacement=true to accept the actual placement."}' -ErrorOutput
    $levelFailure = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $flowCorrectedOperations
            'dry-run' = $true
        }) -Output '{"DryRun":true,"SafetyMessage":"LevelPort was placed at IOL_Master_01/LevelPort instead of requested IOL_Master_01/4 Ports_1/LevelPort. Set allowDisplacedPlacement=true to accept the actual placement."}' -ErrorOutput
    $preview = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $CorrectedOperations
            'dry-run' = $true
        }) -Output '{"message":"Hardware patch preflighted and rolled back","DryRun":true,"Applied":false,"RolledBack":true,"Operations":[{"Message":"Connected node to subnet: CellPN"},{"Message":"Connected IO connector to IO system: CellIO"},{"Data":{"BeforeHex":"17","WrittenHex":"07"}},{"Data":{"WrittenStartAddress":192}},{"Data":{"WrittenStartAddress":192}},{"Data":{"WrittenStartAddress":220}},{"Data":{"WrittenStartAddress":193}},{"Data":{"WrittenStartAddress":225}}]}'
    $apply = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = [ordered]@{ operations = $CorrectedOperations }
            'dry-run' = $false
            confirm = $true
        }) -Output '{"message":"Hardware patch applied","DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Message":"Connected node to subnet: CellPN"},{"Message":"Connected IO connector to IO system: CellIO"},{"Data":{"Written":{"Address":"192.168.0.62","PnDeviceName":"iol-master-01","PnDeviceNumber":62}}},{"Data":{"BeforeHex":"17","WrittenHex":"07"}},{"Data":{"WrittenStartAddress":192}},{"Data":{"WrittenStartAddress":192}},{"Data":{"WrittenStartAddress":220}},{"Data":{"WrittenStartAddress":193}},{"Data":{"WrittenStartAddress":225}}]}'

    $placementEvents = if ($PrematureBothDisplaced) { @($flowFailure) } else { @($flowFailure, $levelFailure) }
    $mutationEvents = if ($ApplyBeforePreview) { @($apply, $preview) } else { @($preview, $apply) }
    $capabilityEvents = if ($ImpossibleCapabilityProbe) {
        @(
            (New-ToolEvent -Tool 'opentia_can-plug-hardware-item' -InputObject @{
                    'device-name' = 'IOL_Master_01'; 'item-path' = '4 Ports_1';
                    'type-identifier' = 'GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/SM/IDS IO-Link 4I_2O';
                    'item-name' = 'FlowPort'; 'position-number' = 2
                } -Output '{"message":"Device not found: IOL_Master_01"}' -ErrorOutput)
        )
    }
    else {
        @()
    }

    $readbackEvents = @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{'device-name' = 'IOL_Master_01'} -Output '{"deviceName":"IOL_Master_01","typeIdentifier":"GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/DAP/DIM_AL1400","items":[{"name":"4 Ports_1","positionNumber":1},{"name":"IO-Link Master Proxy 4P EnMo","positionNumber":0},{"name":"FlowPort","positionNumber":2},{"name":"LevelPort","positionNumber":3}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{'device-name' = 'IOL_Master_01'} -Output '{"deviceName":"IOL_Master_01","connectedSubnet":"CellPN","connectedIoSystemName":"CellIO","Address":"192.168.0.62","PnDeviceName":"iol-master-01","PnDeviceNumber":"62"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{'device-name' = 'IOL_Master_01'} -Output '[{"Target":{"ItemPath":["IO-Link Master Proxy 4P EnMo"]},"Attributes":[{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"224"},{"Name":"StartAddress","Value":"192"}]},{"Target":{"ItemPath":["IO-Link Master Proxy 4P EnMo"]},"Attributes":[{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"8"},{"Name":"StartAddress","Value":"192"}]},{"Target":{"ItemPath":["FlowPort"]},"Attributes":[{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"40"},{"Name":"StartAddress","Value":"220"}]},{"Target":{"ItemPath":["FlowPort"]},"Attributes":[{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"},{"Name":"StartAddress","Value":"193"}]},{"Target":{"ItemPath":["LevelPort"]},"Attributes":[{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"40"},{"Name":"StartAddress","Value":"225"}]}]')
    )
    if (-not $OmitGsdReadback) {
        $readbackEvents += New-ToolEvent -Tool 'opentia_get-gsd-parameter-data' -InputObject @{
            'device-name' = 'IOL_Master_01'; 'item-path' = 'FlowPort'; 'dataset-number' = 47360; 'byte-offset' = 3; length = 1
        } -Output '{"Target":{"DeviceName":"IOL_Master_01","ItemPath":["FlowPort"]},"DatasetNumber":47360,"ByteOffset":3,"Length":1,"DataHex":"07"}'
    }
    $readbackEvents += New-ToolEvent -Tool 'opentia_compile-software' -InputObject @{'plc-name' = 'PLC_Main'} -Output '{"success":true,"ErrorCount":0,"WarningCount":0}'

    return @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output '{"deviceName":"PLC_Main"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{} -Output '{"deviceName":"PLC_Main","subnet":"CellPN","ioSystem":"CellIO"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output '[]'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = 'AL1400'} -Output 'GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/DAP/DIM_AL1400'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = 'IO-Link 4I'} -Output 'GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/SM/IDS IO-Link 4I_2O GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/SM/IDS IO-Link 4I GSD:GSDML-V2.34-IFM-AL1300-20181105.XML/SM/IDS_1 Port x IO-Link 4I')
    ) + $capabilityEvents + $placementEvents + $mutationEvents + $readbackEvents
}

try {
    $operations = @(
        [ordered]@{ op = 'create-device-with-item'; resultId = 'iol-master'; typeIdentifier = 'GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/DAP/DIM_AL1400'; deviceName = 'IOL_Master_01'; itemName = 'IOL_Master_01' },
        [ordered]@{ op = 'plug-item'; resultId = 'flow-port'; targetRef = 'iol-master'; target = [ordered]@{ itemPath = @('4 Ports_1') }; typeIdentifier = 'GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/SM/IDS IO-Link 4I_2O'; itemName = 'FlowPort'; positionNumber = 2; allowDisplacedPlacement = $true },
        [ordered]@{ op = 'plug-item'; resultId = 'level-port'; targetRef = 'iol-master'; target = [ordered]@{ itemPath = @('4 Ports_1') }; typeIdentifier = 'GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/SM/IDS IO-Link 4I'; itemName = 'LevelPort'; positionNumber = 3; allowDisplacedPlacement = $true },
        [ordered]@{ op = 'connect-node-to-subnet'; targetRef = 'iol-master'; target = [ordered]@{ itemPath = @('IOL_Master_01', 'PN-IO') }; nodeIndex = 0; subnetName = 'CellPN' },
        [ordered]@{ op = 'connect-io-connector'; targetRef = 'iol-master'; target = [ordered]@{ itemPath = @('IOL_Master_01', 'PN-IO') }; connectorIndex = 0; ioSystemName = 'CellIO' },
        [ordered]@{ op = 'configure-profinet-device'; targetRef = 'iol-master'; target = [ordered]@{ itemPath = @('IOL_Master_01', 'PN-IO') }; ipAddress = '192.168.0.62'; pnDeviceName = 'iol-master-01'; pnDeviceNumber = 62 },
        [ordered]@{ op = 'set-gsd-parameter-data'; targetRef = 'flow-port'; datasetNumber = 47360; byteOffset = 3; dataHex = '07'; expectedDataHex = '17' },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'iol-master'; target = [ordered]@{ itemPath = @('IO-Link Master Proxy 4P EnMo') }; addressIndex = 0; startAddress = 192 },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'iol-master'; target = [ordered]@{ itemPath = @('IO-Link Master Proxy 4P EnMo') }; addressIndex = 1; startAddress = 192 },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'flow-port'; addressIndex = 0; startAddress = 220 },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'flow-port'; addressIndex = 1; startAddress = 193 },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'level-port'; addressIndex = 0; startAddress = 225 }
    )

    Invoke-IoLinkAssertionTest -Label 'representative' -Events (New-IoLinkEvents -CorrectedOperations $operations)

    Invoke-IoLinkAssertionTest -Label 'impossible-generated-parent-capability-probe' `
        -Events (New-IoLinkEvents -CorrectedOperations $operations -ImpossibleCapabilityProbe) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-generated-parent-capability-probe'

    $legacyPackage = Copy-Operations -Operations $operations
    @($legacyPackage | Where-Object {
            $null -ne $_.PSObject.Properties['itemName'] -and [string]$_.itemName -eq 'LevelPort'
        })[0].typeIdentifier = 'GSD:GSDML-V2.34-IFM-AL1300-20181105.XML/SM/IDS_1 Port x IO-Link 4I'
    Invoke-IoLinkAssertionTest -Label 'mixed-gsdml-version' -Events (New-IoLinkEvents -CorrectedOperations $legacyPackage) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'exact-corrected-operation-set'

    $unguarded = Copy-Operations -Operations $operations
    @($unguarded | Where-Object op -eq 'set-gsd-parameter-data')[0].PSObject.Properties.Remove('expectedDataHex')
    Invoke-IoLinkAssertionTest -Label 'unguarded-gsd-write' -Events (New-IoLinkEvents -CorrectedOperations $unguarded) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'exact-corrected-operation-set'

    $missingProxyOutput = Copy-Operations -Operations $operations
    $missingProxyOutput = @($missingProxyOutput | Where-Object {
            -not ([string]$_.op -eq 'set-io-address-start' -and [string]$_.targetRef -eq 'iol-master' -and [int]$_.addressIndex -eq 1)
        })
    Invoke-IoLinkAssertionTest -Label 'missing-generated-proxy-output-address' -Events (New-IoLinkEvents -CorrectedOperations $missingProxyOutput) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'exact-corrected-operation-set'

    Invoke-IoLinkAssertionTest -Label 'apply-before-preview' -Events (New-IoLinkEvents -CorrectedOperations $operations -ApplyBeforePreview) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'strict-preview-apply-readback-sequence'

    Invoke-IoLinkAssertionTest -Label 'premature-sibling-displacement' -Events (New-IoLinkEvents -CorrectedOperations $operations -PrematureBothDisplaced) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'two-stage-strict-placement-recovery'

    Invoke-IoLinkAssertionTest -Label 'missing-gsd-readback' -Events (New-IoLinkEvents -CorrectedOperations $operations -OmitGsdReadback) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'gsd-parameter-readback'

    Write-Host 'Hardware IFM GSDML IO-Link case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_HARDWARE_GSDML_IOLINK_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaHardwareGsdmlIoLinkTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
