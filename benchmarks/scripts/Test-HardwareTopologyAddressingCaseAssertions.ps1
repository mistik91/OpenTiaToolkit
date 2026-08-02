[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaTopologyAddressingTests-" + [Guid]::NewGuid().ToString('N'))
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
    $outputKey = if ($ErrorOutput) { 'error' } else { 'output' }
    $state[$outputKey] = $Output
    return [ordered]@{
        type = 'tool_use'
        part = [ordered]@{ tool = $Tool; state = $state }
    }
}

function Invoke-TransferAssertionTest {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][object[]]$Events,
        [bool]$ExpectedPassed = $true,
        [string]$ExpectedFailedAssertion
    )

    $tracePath = Join-Path $testRoot "$Label-events.jsonl"
    $metadataPath = Join-Path $testRoot "$Label-metadata.json"
    $resultPath = Join-Path $testRoot "$Label-result.json"
    $allEvents = @($Events) + @(
        [ordered]@{ type = 'step_finish'; part = [ordered]@{
                reason = 'stop'
                tokens = [ordered]@{ total = 10; input = 5; output = 5; reasoning = 0 }
                cost = 0
            } }
    )
    $allEvents |
        ForEach-Object { $_ | ConvertTo-Json -Depth 50 -Compress } |
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

    $casePath = Join-Path $benchmarkRoot 'cases\ott-23\case.json'
    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative topology-addressing trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative topology-addressing trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

function New-TransferEvents {
    param(
        [Parameter(Mandatory = $true)][object[]]$CorrectedOperations,
        [switch]$ApplyBeforePreview,
        [switch]$ExtraCorrectedPreview
    )

    $firstStrictOperations = @($CorrectedOperations | ForEach-Object {
            $copy = $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json
            if ([string]$copy.op -eq 'plug-item') {
                $copy.PSObject.Properties.Remove('allowDisplacedPlacement')
            }
            $copy
        })
    $secondStrictOperations = @($CorrectedOperations | ForEach-Object {
            $copy = $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json
            if ([string]$copy.op -eq 'plug-item' -and [string]$copy.itemName -eq 'Pneumatics') {
                $copy.PSObject.Properties.Remove('allowDisplacedPlacement')
            }
            $copy
        })

    $firstStrictFailure = '{"DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"Photoeyes was placed at PackagingBay/Photoeyes instead of requested PackagingBay/Rack_0/Photoeyes. Set allowDisplacedPlacement=true to accept the actual placement."}'
    $secondStrictFailure = '{"DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"Pneumatics was placed at PackagingBay/Pneumatics instead of requested PackagingBay/Rack_0/Pneumatics. Set allowDisplacedPlacement=true to accept the actual placement.","Operations":[{"Op":"set-io-address-start","Data":{"WrittenStartAddress":72}}]}'
    $previewOutput = '{"message":"Hardware patch preflighted and rolled back","DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":false,"Operations":[{"Message":"Created device with item: PackagingBay"},{"Message":"Plugged hardware item: Photoeyes"},{"Message":"Set hardware IO start address.","Data":{"WrittenStartAddress":72}},{"Message":"Plugged hardware item: Pneumatics"},{"Message":"Set hardware IO start address.","Data":{"WrittenStartAddress":140}},{"Message":"Connected node to subnet: CellPN"},{"Message":"Connected IO connector to IO system: CellIO"},{"Message":"Configured PROFINET device identity and addressing"}]}'
    $applyOutput = '{"message":"Hardware patch applied","DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Data":{"WrittenStartAddress":72}},{"Data":{"WrittenStartAddress":140}},{"Data":{"Written":{"Address":"192.168.0.48","PnDeviceName":"packaging-bay","PnDeviceNumber":"48"}}}]}'
    $preview = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = [ordered]@{ operations = $CorrectedOperations }
            'dry-run' = $true
            confirm = $true
        }) -Output $previewOutput
    $apply = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $CorrectedOperations
            'dry-run' = $false
            confirm = $true
        }) -Output $applyOutput

    $mutationEvents = if ($ApplyBeforePreview) {
        @($apply, $preview)
    }
    elseif ($ExtraCorrectedPreview) {
        @($preview, $preview, $apply)
    }
    else {
        @($preview, $apply)
    }

    return @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output '{"deviceName":"PLC_Main","typeIdentifier":"System:Device.S71500"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{} -Output '{"target":{"deviceName":"PLC_Main"},"connectedSubnet":"CellPN","ioSystemName":"CellIO"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output '{"Target":{"DeviceName":"PLC_Main"},"Attributes":[{"Name":"StartAddress","Value":"40"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]}'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 155-6AU02-0BN0'} -Output 'OrderNumber:6ES7 155-6AU02-0BN0/V6.4'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 131-6BH01-0BA0'} -Output 'OrderNumber:6ES7 131-6BH01-0BA0/V0.0'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 132-6BH01-0BA0'} -Output 'OrderNumber:6ES7 132-6BH01-0BA0/V0.0'),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
                operations = $firstStrictOperations
                'dry-run' = $true
                confirm = $true
            }) -Output $firstStrictFailure -ErrorOutput),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
                operations = $secondStrictOperations
                'dry-run' = $true
                confirm = $true
            }) -Output $secondStrictFailure -ErrorOutput)
    ) + $mutationEvents + @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{'device-name' = 'PackagingBay'} -Output '{"deviceName":"PackagingBay","typeIdentifier":"System:Device.ET200SP","items":[{"name":"Photoeyes","typeIdentifier":"OrderNumber:6ES7 131-6BH01-0BA0/V0.0","positionNumber":3},{"name":"Pneumatics","typeIdentifier":"OrderNumber:6ES7 132-6BH01-0BA0/V0.0","positionNumber":5}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{'device-name' = 'PackagingBay'} -Output '{"target":{"deviceName":"PackagingBay"},"connectedSubnet":"CellPN","connectedIoSystemName":"CellIO","Address":"192.168.0.48","PnDeviceName":"packaging-bay","PnDeviceNumber":"48"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{'device-name' = 'PackagingBay'} -Output '[{"Target":{"DeviceName":"PackagingBay","ItemPath":["Photoeyes","Photoeyes"]},"Attributes":[{"Name":"StartAddress","Value":"72"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"DeviceName":"PackagingBay","ItemPath":["Pneumatics","Pneumatics"]},"Attributes":[{"Name":"StartAddress","Value":"140"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]}]'),
        (New-ToolEvent -Tool 'opentia_compile-software' -InputObject @{'plc-name' = 'PLC_Main'} -Output '{"success":true,"ErrorCount":0,"WarningCount":0}')
    )
}

try {
    $correctedOperations = @(
        [ordered]@{
            op = 'create-device-with-item'
            resultId = 'packaging-station'
            typeIdentifier = 'OrderNumber:6ES7 155-6AU02-0BN0/V6.4'
            deviceName = 'PackagingBay'
            itemName = 'PackagingBay'
        },
        [ordered]@{
            op = 'plug-item'
            resultId = 'photoeyes'
            targetRef = 'packaging-station'
            target = [ordered]@{ itemPath = @('Rack_0') }
            typeIdentifier = 'OrderNumber:6ES7 131-6BH01-0BA0/V0.0'
            itemName = 'Photoeyes'
            positionNumber = 3
            allowDisplacedPlacement = $true
        },
        [ordered]@{
            op = 'set-io-address-start'
            targetRef = 'photoeyes'
            target = [ordered]@{ itemPath = @('Photoeyes') }
            addressIndex = 0
            startAddress = 72
        },
        [ordered]@{
            op = 'plug-item'
            resultId = 'pneumatics'
            targetRef = 'packaging-station'
            target = [ordered]@{ itemPath = @('Rack_0') }
            typeIdentifier = 'OrderNumber:6ES7 132-6BH01-0BA0/V0.0'
            itemName = 'Pneumatics'
            positionNumber = 5
            allowDisplacedPlacement = $true
        },
        [ordered]@{
            op = 'set-io-address-start'
            targetRef = 'pneumatics'
            target = [ordered]@{ itemPath = @('Pneumatics') }
            addressIndex = 0
            startAddress = 140
        },
        [ordered]@{
            op = 'connect-node-to-subnet'
            targetRef = 'packaging-station'
            target = [ordered]@{ itemPath = @('PackagingBay', 'PROFINET interface') }
            nodeIndex = 0
            subnetName = 'CellPN'
        },
        [ordered]@{
            op = 'connect-io-connector'
            targetRef = 'packaging-station'
            target = [ordered]@{ itemPath = @('PackagingBay', 'PROFINET interface') }
            connectorIndex = 0
            ioSystemName = 'CellIO'
        },
        [ordered]@{
            op = 'configure-profinet-device'
            targetRef = 'packaging-station'
            target = [ordered]@{ itemPath = @('PackagingBay', 'PROFINET interface') }
            ipAddress = '192.168.0.48'
            pnDeviceName = 'packaging-bay'
            pnDeviceNumber = 48
        }
    )

    Invoke-TransferAssertionTest -Label 'representative' `
        -Events (New-TransferEvents -CorrectedOperations $correctedOperations)

    $wrongChild = @($correctedOperations | ForEach-Object {
            $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        })
    $wrongChild[2].target.itemPath = @('WrongChild')
    Invoke-TransferAssertionTest -Label 'wrong-generated-child' `
        -Events (New-TransferEvents -CorrectedOperations $wrongChild) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'exact-corrected-operation-set'

    $wrongAddress = @($correctedOperations | ForEach-Object {
            $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        })
    $wrongAddress[4].startAddress = 142
    Invoke-TransferAssertionTest -Label 'wrong-output-address' `
        -Events (New-TransferEvents -CorrectedOperations $wrongAddress) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'exact-corrected-operation-set'

    Invoke-TransferAssertionTest -Label 'apply-before-preview' `
        -Events (New-TransferEvents -CorrectedOperations $correctedOperations -ApplyBeforePreview) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'preview-before-apply-sequence'

    Invoke-TransferAssertionTest -Label 'extra-corrected-preview' `
        -Events (New-TransferEvents -CorrectedOperations $correctedOperations -ExtraCorrectedPreview) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'exact-corrected-operation-set'

    Write-Host 'Hardware topology-addressing case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_TOPOLOGY_ADDRESSING_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaTopologyAddressingTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
