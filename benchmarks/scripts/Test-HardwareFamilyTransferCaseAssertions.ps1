[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaHardwareFamilyTransferTests-" + [Guid]::NewGuid().ToString('N'))
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

function Invoke-FamilyTransferAssertionTest {
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
        -CasePath (Join-Path $benchmarkRoot 'cases\ott-24\case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative ET 200MP trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative ET 200MP trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

function New-FamilyTransferEvents {
    param(
        [Parameter(Mandatory = $true)][object[]]$CorrectedOperations,
        [switch]$ApplyBeforePreview
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
            if ([string]$copy.op -eq 'plug-item' -and [string]$copy.itemName -eq 'RejectOutputs') {
                $copy.PSObject.Properties.Remove('allowDisplacedPlacement')
            }
            $copy
        })

    $preview = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = [ordered]@{ operations = $CorrectedOperations }
            'dry-run' = $true
            confirm = $true
        }) -Output '{"message":"Hardware patch preflighted and rolled back","DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":false,"Operations":[{"Data":{"WrittenStartAddress":96}},{"Data":{"WrittenStartAddress":152}},{"Message":"Connected node to subnet: CellPN"},{"Message":"Connected IO connector to IO system: CellIO"},{"Message":"Configured PROFINET device identity and addressing"}]}'
    $apply = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $CorrectedOperations
            'dry-run' = $false
            confirm = $true
        }) -Output '{"message":"Hardware patch applied","DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Data":{"WrittenStartAddress":96}},{"Data":{"WrittenStartAddress":152}},{"Data":{"Written":{"Address":"192.168.0.56","PnDeviceName":"quality-cell","PnDeviceNumber":"56"}}}]}'

    $mutations = if ($ApplyBeforePreview) { @($apply, $preview) } else { @($preview, $apply) }

    return @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output '{"deviceName":"PLC_Main","typeIdentifier":"System:Device.S71500"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{} -Output '{"target":{"deviceName":"PLC_Main"},"connectedSubnet":"CellPN","ioSystemName":"CellIO"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output '{"Target":{"DeviceName":"PLC_Main"},"Attributes":[{"Name":"StartAddress","Value":"40"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]}'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 155-5AA01-0AB0'} -Output 'OrderNumber:6ES7 155-5AA01-0AB0/V4.1'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 521-1BH00-0AB0'} -Output 'OrderNumber:6ES7 521-1BH00-0AB0/V2.2'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 522-1BH01-0AB0'} -Output 'OrderNumber:6ES7 522-1BH01-0AB0/V1.1'),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
                    operations = $firstStrictOperations; 'dry-run' = $true; confirm = $true
                }) -Output '{"DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"InspectionInputs was placed at QualityCell/InspectionInputs instead of requested QualityCell/Rail_0/InspectionInputs. Set allowDisplacedPlacement=true to accept the actual placement."}' -ErrorOutput),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
                    operations = $secondStrictOperations; 'dry-run' = $true; confirm = $true
                }) -Output '{"DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"RejectOutputs was placed at QualityCell/RejectOutputs instead of requested QualityCell/Rail_0/RejectOutputs. Set allowDisplacedPlacement=true to accept the actual placement.","Operations":[{"Data":{"WrittenStartAddress":96}}]}' -ErrorOutput)
    ) + $mutations + @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{'device-name' = 'QualityCell'} -Output '{"deviceName":"QualityCell","typeIdentifier":"System:Device.S71500","items":[{"name":"QualityCell","typeIdentifier":"OrderNumber:6ES7 155-5AA01-0AB0/V4.1"},{"name":"InspectionInputs","positionNumber":2},{"name":"RejectOutputs","positionNumber":4}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{'device-name' = 'QualityCell'} -Output '{"target":{"deviceName":"QualityCell"},"connectedSubnet":"CellPN","connectedIoSystemName":"CellIO","Address":"192.168.0.56","PnDeviceName":"quality-cell","PnDeviceNumber":"56"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{'device-name' = 'QualityCell'} -Output '[{"Target":{"DeviceName":"QualityCell","ItemPath":["InspectionInputs","InspectionInputs"]},"Attributes":[{"Name":"StartAddress","Value":"96"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"DeviceName":"QualityCell","ItemPath":["RejectOutputs","RejectOutputs"]},"Attributes":[{"Name":"StartAddress","Value":"152"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]}]'),
        (New-ToolEvent -Tool 'opentia_compile-software' -InputObject @{'plc-name' = 'PLC_Main'} -Output '{"success":true,"ErrorCount":0,"WarningCount":0}')
    )
}

try {
    $operations = @(
        [ordered]@{ op = 'create-device-with-item'; resultId = 'quality-station'; typeIdentifier = 'OrderNumber:6ES7 155-5AA01-0AB0/V4.1'; deviceName = 'QualityCell'; itemName = 'QualityCell' },
        [ordered]@{ op = 'plug-item'; resultId = 'inspection-inputs'; targetRef = 'quality-station'; target = [ordered]@{ itemPath = @('Rail_0') }; typeIdentifier = 'OrderNumber:6ES7 521-1BH00-0AB0/V2.2'; itemName = 'InspectionInputs'; positionNumber = 2; allowDisplacedPlacement = $true },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'inspection-inputs'; target = [ordered]@{ itemPath = @('InspectionInputs') }; addressIndex = 0; startAddress = 96 },
        [ordered]@{ op = 'plug-item'; resultId = 'reject-outputs'; targetRef = 'quality-station'; target = [ordered]@{ itemPath = @('Rail_0') }; typeIdentifier = 'OrderNumber:6ES7 522-1BH01-0AB0/V1.1'; itemName = 'RejectOutputs'; positionNumber = 4; allowDisplacedPlacement = $true },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'reject-outputs'; target = [ordered]@{ itemPath = @('RejectOutputs') }; addressIndex = 0; startAddress = 152 },
        [ordered]@{ op = 'connect-node-to-subnet'; targetRef = 'quality-station'; target = [ordered]@{ itemPath = @('QualityCell', 'PROFINET interface') }; nodeIndex = 0; subnetName = 'CellPN' },
        [ordered]@{ op = 'connect-io-connector'; targetRef = 'quality-station'; target = [ordered]@{ itemPath = @('QualityCell', 'PROFINET interface') }; connectorIndex = 0; ioSystemName = 'CellIO' },
        [ordered]@{ op = 'configure-profinet-device'; targetRef = 'quality-station'; target = [ordered]@{ itemPath = @('QualityCell', 'PROFINET interface') }; ipAddress = '192.168.0.56'; pnDeviceName = 'quality-cell'; pnDeviceNumber = 56 }
    )

    Invoke-FamilyTransferAssertionTest -Label 'representative' -Events (New-FamilyTransferEvents -CorrectedOperations $operations)

    $guessedGuard = @($operations | ForEach-Object { $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json })
    Add-Member -InputObject $guessedGuard[2] -NotePropertyName expectedStartAddress -NotePropertyValue -1
    Add-Member -InputObject $guessedGuard[4] -NotePropertyName expectedStartAddress -NotePropertyValue -1
    Invoke-FamilyTransferAssertionTest -Label 'guessed-generated-address-guards' `
        -Events (New-FamilyTransferEvents -CorrectedOperations $guessedGuard) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'preview-before-apply-without-guessed-guards'

    Invoke-FamilyTransferAssertionTest -Label 'apply-before-preview' `
        -Events (New-FamilyTransferEvents -CorrectedOperations $operations -ApplyBeforePreview) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'preview-before-apply-without-guessed-guards'

    Write-Host 'Hardware family-transfer case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_HARDWARE_FAMILY_TRANSFER_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaHardwareFamilyTransferTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
