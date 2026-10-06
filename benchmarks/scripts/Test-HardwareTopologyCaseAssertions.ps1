[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaTopologyCaseTests-" + [Guid]::NewGuid().ToString('N'))
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

function Invoke-TopologyAssertionTest {
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
        ForEach-Object { $_ | ConvertTo-Json -Depth 40 -Compress } |
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

    $casePath = Join-Path $benchmarkRoot 'cases\ott-22\case.json'
    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative topology trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative topology trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

function New-TopologyEvents {
    param(
        [Parameter(Mandatory = $true)][object[]]$CorrectedOperations,
        [switch]$ApplyBeforePreview
    )

    $firstStrictOperations = @($CorrectedOperations | ForEach-Object {
            $copy = $_ | ConvertTo-Json -Depth 20 | ConvertFrom-Json
            if ([string]$copy.op -eq 'plug-item') {
                $copy.PSObject.Properties.Remove('allowDisplacedPlacement')
            }
            $copy
        })
    $secondStrictOperations = @($CorrectedOperations | ForEach-Object {
            $copy = $_ | ConvertTo-Json -Depth 20 | ConvertFrom-Json
            if ([string]$copy.op -eq 'plug-item' -and [string]$copy.itemName -eq 'ValveOutputs') {
                $copy.PSObject.Properties.Remove('allowDisplacedPlacement')
            }
            $copy
        })
    $firstStrictFailure = '{"DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"GuardInputs was placed at RemoteCell/GuardInputs instead of requested RemoteCell/Rack_0/GuardInputs. Set allowDisplacedPlacement=true to accept the actual placement."}'
    $secondStrictFailure = '{"DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"ValveOutputs was placed at RemoteCell/ValveOutputs instead of requested RemoteCell/Rack_0/ValveOutputs. Set allowDisplacedPlacement=true to accept the actual placement."}'
    $previewOutput = '{"message":"Hardware patch preflighted and rolled back","DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":false,"Operations":[{"Message":"Created device with item: RemoteCell"},{"Message":"Plugged hardware item: GuardInputs"},{"Message":"Plugged hardware item: ValveOutputs"},{"Message":"Connected node to subnet: CellPN"},{"Message":"Connected IO connector to IO system: CellIO"},{"Message":"Configured PROFINET device identity and addressing"}]}'
    $applyOutput = '{"message":"Hardware patch applied","DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Data":{"Written":{"Address":"192.168.0.37","PnDeviceName":"remote-cell","PnDeviceNumber":"37"}}}]}'
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

    $mutationEvents = if ($ApplyBeforePreview) { @($apply, $preview) } else { @($preview, $apply) }
    return @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output '{"deviceName":"PLC_Main","typeIdentifier":"System:Device.S71500"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{} -Output '{"target":{"deviceName":"PLC_Main"},"connectedSubnet":"CellPN","ioSystemName":"CellIO"}'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 155-6AU02-0BN0'} -Output 'OrderNumber:6ES7 155-6AU02-0BN0/V6.4'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 131-6BF01-0BA0'} -Output 'OrderNumber:6ES7 131-6BF01-0BA0/V0.0'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 132-6BF01-0BA0'} -Output 'OrderNumber:6ES7 132-6BF01-0BA0/V0.0'),
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
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{'device-name' = 'RemoteCell'} -Output '{"deviceName":"RemoteCell","typeIdentifier":"System:Device.ET200SP","items":[{"name":"RemoteCell","typeIdentifier":"OrderNumber:6ES7 155-6AU02-0BN0/V6.4"},{"name":"GuardInputs","typeIdentifier":"OrderNumber:6ES7 131-6BF01-0BA0/V0.0","positionNumber":1},{"name":"ValveOutputs","typeIdentifier":"OrderNumber:6ES7 132-6BF01-0BA0/V0.0","positionNumber":2}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{} -Output '{"target":{"deviceName":"RemoteCell"},"connectedSubnet":"CellPN","connectedIoSystemName":"CellIO","Address":"192.168.0.37","PnDeviceName":"remote-cell","PnDeviceNumber":"37"}'),
        (New-ToolEvent -Tool 'opentia_compile-software' -InputObject @{'plc-name' = 'PLC_Main'} -Output '{"success":true,"ErrorCount":0,"WarningCount":0}')
    )
}

try {
    $correctedOperations = @(
        [ordered]@{
            op = 'create-device-with-item'
            resultId = 'new-station'
            typeIdentifier = 'OrderNumber:6ES7 155-6AU02-0BN0/V6.4'
            deviceName = 'RemoteCell'
            itemName = 'RemoteCell'
        },
        [ordered]@{
            op = 'plug-item'
            resultId = 'new-inputs'
            targetRef = 'new-station'
            target = [ordered]@{ itemPath = @('Rack_0') }
            typeIdentifier = 'OrderNumber:6ES7 131-6BF01-0BA0/V0.0'
            itemName = 'GuardInputs'
            positionNumber = 1
            allowDisplacedPlacement = $true
        },
        [ordered]@{
            op = 'plug-item'
            resultId = 'new-outputs'
            targetRef = 'new-station'
            target = [ordered]@{ itemPath = @('Rack_0') }
            typeIdentifier = 'OrderNumber:6ES7 132-6BF01-0BA0/V0.0'
            itemName = 'ValveOutputs'
            positionNumber = 2
            allowDisplacedPlacement = $true
        },
        [ordered]@{
            op = 'connect-node-to-subnet'
            targetRef = 'new-station'
            target = [ordered]@{ itemPath = @('RemoteCell', 'PROFINET interface') }
            nodeIndex = 0
            subnetName = 'CellPN'
        },
        [ordered]@{
            op = 'connect-io-connector'
            targetRef = 'new-station'
            target = [ordered]@{ itemPath = @('RemoteCell', 'PROFINET interface') }
            connectorIndex = 0
            ioSystemName = 'CellIO'
        },
        [ordered]@{
            op = 'configure-profinet-device'
            targetRef = 'new-station'
            target = [ordered]@{ itemPath = @('RemoteCell', 'PROFINET interface') }
            ipAddress = '192.168.0.37'
            pnDeviceName = 'remote-cell'
            pnDeviceNumber = 37
        }
    )

    Invoke-TopologyAssertionTest -Label 'representative' -Events (New-TopologyEvents -CorrectedOperations $correctedOperations)

    $wrongVersion = @($correctedOperations | ForEach-Object {
            $_ | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        })
    $wrongVersion[0].typeIdentifier = 'OrderNumber:6ES7 155-6AU02-0BN0/V6.3'
    Invoke-TopologyAssertionTest -Label 'wrong-version' `
        -Events (New-TopologyEvents -CorrectedOperations $wrongVersion) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'exact-corrected-operation-set'

    Invoke-TopologyAssertionTest -Label 'apply-before-preview' `
        -Events (New-TopologyEvents -CorrectedOperations $correctedOperations -ApplyBeforePreview) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'preview-before-apply-sequence'

    Write-Host 'Hardware topology case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_TOPOLOGY_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaTopologyCaseTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
