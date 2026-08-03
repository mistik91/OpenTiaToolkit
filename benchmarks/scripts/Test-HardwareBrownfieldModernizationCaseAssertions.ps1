[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaHardwareBrownfieldTests-" + [Guid]::NewGuid().ToString('N'))
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

function Invoke-BrownfieldAssertionTest {
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
        -CasePath (Join-Path $benchmarkRoot 'cases\ott-25\case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative brownfield trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative brownfield trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

function New-BrownfieldEvents {
    param(
        [Parameter(Mandatory = $true)][object[]]$CorrectedOperations,
        [switch]$ApplyBeforePreview,
        [switch]$OmitDestructiveApplyConfirmation
    )

    $strictOperations = @($CorrectedOperations | ForEach-Object {
            $copy = $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json
            if ([string]$copy.op -eq 'plug-item') {
                $copy.PSObject.Properties.Remove('allowDisplacedPlacement')
            }
            $copy
        })

    $preview = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
            operations = $CorrectedOperations
            'dry-run' = $true
            'confirm-destructive' = $true
        }) -Output '{"message":"Hardware patch preflighted and rolled back","DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":false,"Operations":[{"Message":"Deleted hardware item."},{"Data":{"BeforeStartAddress":72,"WrittenStartAddress":104}},{"Data":{"Written":{"Address":"192.168.0.67","PnDeviceName":"packaging-cell","PnDeviceNumber":67}}},{"Message":"Plugged hardware item: ModernOutputs"},{"Data":{"WrittenStartAddress":160}}]}'

    $applyInput = [ordered]@{
        operations = [ordered]@{ operations = $CorrectedOperations }
        'dry-run' = $false
        confirm = $true
    }
    if (-not $OmitDestructiveApplyConfirmation) {
        $applyInput['confirm-destructive'] = $true
    }
    $apply = New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject $applyInput `
        -Output '{"message":"Hardware patch applied","DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Message":"Deleted hardware item."},{"Data":{"BeforeStartAddress":72,"WrittenStartAddress":104}},{"Data":{"Written":{"Address":"192.168.0.67","PnDeviceName":"packaging-cell","PnDeviceNumber":67}}},{"Message":"Plugged hardware item: ModernOutputs"},{"Data":{"WrittenStartAddress":160}}]}'

    $mutations = if ($ApplyBeforePreview) { @($apply, $preview) } else { @($preview, $apply) }

    return @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output '{"deviceName":"LegacyCell","items":[{"name":"LegacyInputs","positionNumber":2},{"name":"LegacyOutputs","typeIdentifier":"OrderNumber:6ES7 522-1BH10-0AA0/V1.1","positionNumber":4}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{} -Output '{"target":{"deviceName":"LegacyCell"},"connectedSubnet":"CellPN","connectedIoSystemName":"CellIO","Address":"192.168.0.57","PnDeviceName":"legacy-cell","PnDeviceNumber":"57"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output '[{"Target":{"DeviceName":"LegacyCell","ItemPath":["LegacyInputs","LegacyInputs"]},"Attributes":[{"Name":"StartAddress","Value":"72"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"DeviceName":"LegacyCell","ItemPath":["LegacyOutputs","LegacyOutputs"]},"Attributes":[{"Name":"StartAddress","Value":"140"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]}]'),
        (New-ToolEvent -Tool 'opentia_find-hardware-catalog-items' -InputObject @{'search-text' = '6ES7 522-1BH01-0AB0'} -Output 'OrderNumber:6ES7 522-1BH01-0AB0/V1.1'),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{
                    operations = $strictOperations
                    'dry-run' = $true
                    'confirm-destructive' = $true
                }) -Output '{"DryRun":true,"Applied":false,"RolledBack":true,"SafetyBlocked":true,"SafetyMessage":"ModernOutputs was placed at LegacyCell/ModernOutputs instead of requested LegacyCell/Rail_0/ModernOutputs. Set allowDisplacedPlacement=true to accept the actual placement."}' -ErrorOutput)
    ) + $mutations + @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{'device-name' = 'LegacyCell'} -Output '{"deviceName":"LegacyCell","items":[{"name":"LegacyInputs","positionNumber":2},{"name":"ModernOutputs","typeIdentifier":"OrderNumber:6ES7 522-1BH01-0AB0/V1.1","positionNumber":4}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-networks' -InputObject @{'device-name' = 'LegacyCell'} -Output '{"target":{"deviceName":"LegacyCell"},"connectedSubnet":"CellPN","connectedIoSystemName":"CellIO","Address":"192.168.0.67","PnDeviceName":"packaging-cell","PnDeviceNumber":"67"}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{'device-name' = 'LegacyCell'} -Output '[{"Target":{"DeviceName":"LegacyCell","ItemPath":["LegacyInputs","LegacyInputs"]},"Attributes":[{"Name":"StartAddress","Value":"104"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"DeviceName":"LegacyCell","ItemPath":["ModernOutputs","ModernOutputs"]},"Attributes":[{"Name":"StartAddress","Value":"160"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]}]'),
        (New-ToolEvent -Tool 'opentia_compile-software' -InputObject @{'plc-name' = 'PLC_Main'} -Output '{"success":true,"ErrorCount":0,"WarningCount":0}')
    )
}

try {
    $operations = @(
        [ordered]@{ op = 'delete-item'; target = [ordered]@{ deviceName = 'LegacyCell'; itemPath = @('LegacyOutputs'); expectedTypeIdentifier = 'OrderNumber:6ES7 522-1BH10-0AA0/V1.1' } },
        [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'LegacyCell'; itemPath = @('LegacyInputs', 'LegacyInputs') }; addressIndex = 0; startAddress = 104; expectedStartAddress = 72 },
        [ordered]@{ op = 'configure-profinet-device'; target = [ordered]@{ deviceName = 'LegacyCell'; itemPath = @('LegacyCell', 'PROFINET interface') }; ipAddress = '192.168.0.67'; expectedIpAddress = '192.168.0.57'; pnDeviceName = 'packaging-cell'; expectedPnDeviceName = 'legacy-cell'; pnDeviceNumber = 67; expectedPnDeviceNumber = 57 },
        [ordered]@{ op = 'plug-item'; resultId = 'modern-output'; target = [ordered]@{ deviceName = 'LegacyCell'; itemPath = @('Rail_0') }; typeIdentifier = 'OrderNumber:6ES7 522-1BH01-0AB0/V1.1'; itemName = 'ModernOutputs'; positionNumber = 4; allowDisplacedPlacement = $true },
        [ordered]@{ op = 'set-io-address-start'; targetRef = 'modern-output'; target = [ordered]@{ itemPath = @('ModernOutputs') }; addressIndex = 0; startAddress = 160 }
    )

    Invoke-BrownfieldAssertionTest -Label 'representative' -Events (New-BrownfieldEvents -CorrectedOperations $operations)

    $unguarded = @($operations | ForEach-Object { $_ | ConvertTo-Json -Depth 30 | ConvertFrom-Json })
    $unguarded[1].PSObject.Properties.Remove('expectedStartAddress')
    Invoke-BrownfieldAssertionTest -Label 'unguarded-existing-input' `
        -Events (New-BrownfieldEvents -CorrectedOperations $unguarded) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'exact-corrected-operation-set'

    Invoke-BrownfieldAssertionTest -Label 'apply-before-preview' `
        -Events (New-BrownfieldEvents -CorrectedOperations $operations -ApplyBeforePreview) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'strict-preview-apply-sequence'

    Invoke-BrownfieldAssertionTest -Label 'missing-destructive-confirmation' `
        -Events (New-BrownfieldEvents -CorrectedOperations $operations -OmitDestructiveApplyConfirmation) `
        -ExpectedPassed $false `
        -ExpectedFailedAssertion 'corrected-destructive-apply-succeeded'

    Write-Host 'Hardware brownfield-modernization case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_HARDWARE_BROWNFIELD_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaHardwareBrownfieldTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
