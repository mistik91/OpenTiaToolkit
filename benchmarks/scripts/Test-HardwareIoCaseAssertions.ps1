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
        [Parameter(Mandatory = $true)][object[]]$Events,
        [bool]$ExpectedPassed = $true,
        [string]$ExpectedFailedAssertion
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
    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } | ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "$CaseId representative trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } | ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "$CaseId negative trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
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

    $outputBaselineMap = '{"Target":{"DeviceName":"PLC_Main","ItemPath":["DQ_Candidate","DQ_Candidate"]},"Index":0,"Attributes":[{"Name":"IoType","Value":"Output"},{"Name":"StartAddress","Value":"2"}]}'
    $outputRelocatedMap = '{"Target":{"DeviceName":"PLC_Main","ItemPath":["DQ_Candidate","DQ_Candidate"]},"Index":0,"Attributes":[{"Name":"IoType","Value":"Output"},{"Name":"StartAddress","Value":"130"}]}'
    $outputOperation = [ordered]@{ operations = @([ordered]@{
                op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DQ_Candidate', 'DQ_Candidate') }
                addressIndex = 0; startAddress = 130; expectedStartAddress = 2
            }) }
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-13' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output 'DQ_Candidate'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $outputBaselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $outputOperation; 'dry-run' = $true }) -Output '{"DryRun":true,"Applied":false,"RolledBack":true,"Operations":[{"Data":{"IoType":"Output","BeforeStartAddress":2,"WrittenStartAddress":130}}]}'),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $outputOperation; 'dry-run' = $false; confirm = $true }) -Output '{"DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Data":{"IoType":"Output","BeforeStartAddress":2,"WrittenStartAddress":130}}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $outputRelocatedMap),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Applied DQ_Candidate output relocation to 130 and verified it by fresh readback.' } }
    )

    $mixedOperations = [ordered]@{ operations = @(
            [ordered]@{
                op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DI_Candidate', 'DI_Candidate') }
                addressIndex = 0; startAddress = 66; expectedStartAddress = 2
            },
            [ordered]@{
                op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DQ_Candidate', 'DQ_Candidate') }
                addressIndex = 0; startAddress = 128; expectedStartAddress = 2
            }
        ) }
    $mixedBaselineMap = 'DI_Candidate DQ_Candidate DQ_Occupied {"Name":"IoType","Value":"Input"} {"Name":"IoType","Value":"Output"} {"Name":"StartAddress","Value":"2"}'
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-17' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output 'DI_Candidate DQ_Candidate DQ_Occupied'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $mixedBaselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $mixedOperations; 'dry-run' = $true }) -Output 'This address is already being used. Next free address: 130.' -ErrorOutput),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $mixedBaselineMap),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'DQ_Candidate collided with DQ_Occupied at output bytes 128-129; next free address 130. The transaction rolled back, so both DI_Candidate and DQ_Candidate remained unchanged.' } }
    )
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-17' -ExpectedPassed $false -ExpectedFailedAssertion 'accurate-output-range-final' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output 'DI_Candidate DQ_Candidate DQ_Occupied'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $mixedBaselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $mixedOperations; 'dry-run' = $true }) -Output 'This address is already being used. Next free address: 130.' -ErrorOutput),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $mixedBaselineMap),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'The collision rolled back both DI_Candidate and DQ_Candidate. DQ_Occupied covers output bytes 128-143, so the next free output byte is 144; Siemens also reported next free address 130.' } }
    )

    # Keep StartAddress before IoType to cover the attribute ordering that exposed
    # the original scorer bug in live trial 2. Assertions must be order-independent.
    $mixedModuleBaselineMap = '[{"Target":{"DeviceName":"PLC_Main","ItemPath":["DIDQ_Candidate","DIDQ_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"0"},{"Name":"IoType","Value":"Input"}]},{"Target":{"DeviceName":"PLC_Main","ItemPath":["DIDQ_Candidate","DIDQ_Candidate"]},"Index":1,"Attributes":[{"Name":"StartAddress","Value":"0"},{"Name":"IoType","Value":"Output"}]}]'
    $mixedModuleRelocatedMap = '[{"Target":{"DeviceName":"PLC_Main","ItemPath":["DIDQ_Candidate","DIDQ_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"0"},{"Name":"IoType","Value":"Input"}]},{"Target":{"DeviceName":"PLC_Main","ItemPath":["DIDQ_Candidate","DIDQ_Candidate"]},"Index":1,"Attributes":[{"Name":"StartAddress","Value":"132"},{"Name":"IoType","Value":"Output"}]}]'
    $mixedModuleOperation = [ordered]@{ operations = @([ordered]@{
                op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DIDQ_Candidate', 'DIDQ_Candidate') }
                addressIndex = 1; startAddress = 132; expectedStartAddress = 0
            }) }
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-12' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output 'DIDQ_Candidate'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $mixedModuleBaselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $mixedModuleOperation; 'dry-run' = $true }) -Output '{"DryRun":true,"Applied":false,"RolledBack":true,"Operations":[{"Data":{"IoType":"Output","BeforeStartAddress":0,"WrittenStartAddress":132}}]}'),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $mixedModuleOperation; 'dry-run' = $false; confirm = $true }) -Output '{"DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Data":{"IoType":"Output","BeforeStartAddress":0,"WrittenStartAddress":132}}]}'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $mixedModuleRelocatedMap),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Applied DIDQ_Candidate output relocation to 132 and verified it by fresh readback; its input remained unchanged at byte 0.' } }
    )
    $wrongMixedModuleOperation = [ordered]@{ operations = @([ordered]@{
                op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DIDQ_Candidate', 'DIDQ_Candidate') }
                addressIndex = 0; startAddress = 132; expectedStartAddress = 0
            }) }
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-12' -ExpectedPassed $false -ExpectedFailedAssertion 'no-index-zero-mutation' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output 'DIDQ_Candidate'),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $mixedModuleBaselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $wrongMixedModuleOperation; 'dry-run' = $true }) -Output '{"DryRun":true,"Applied":false,"RolledBack":true,"Operations":[{"Data":{"IoType":"Input","BeforeStartAddress":0,"WrittenStartAddress":132}}]}'),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Incorrectly selected input index 0.' } }
    )

    $dualSpaceTree = 'DI_Occupied PositionNumber 2 DI_Candidate PositionNumber 3 DQ_Occupied PositionNumber 4 DQ_Candidate PositionNumber 5 DIDQ_Candidate PositionNumber 6'
    $dualSpaceBaselineMap = '[{"Target":{"ItemPath":["DI_Occupied","DI_Occupied"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"64"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DI_Candidate","DI_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"2"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DQ_Occupied","DQ_Occupied"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"128"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DQ_Candidate","DQ_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"2"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DIDQ_Candidate","DIDQ_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"0"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DIDQ_Candidate","DIDQ_Candidate"]},"Index":1,"Attributes":[{"Name":"StartAddress","Value":"0"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]}]'
    $dualSpaceFinalMap = '[{"Target":{"ItemPath":["DI_Occupied","DI_Occupied"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"64"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DI_Candidate","DI_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"66"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DQ_Occupied","DQ_Occupied"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"128"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DQ_Candidate","DQ_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"130"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DIDQ_Candidate","DIDQ_Candidate"]},"Index":0,"Attributes":[{"Name":"StartAddress","Value":"68"},{"Name":"IoType","Value":"Input"},{"Name":"Length","Value":"16"}]},{"Target":{"ItemPath":["DIDQ_Candidate","DIDQ_Candidate"]},"Index":1,"Attributes":[{"Name":"StartAddress","Value":"132"},{"Name":"IoType","Value":"Output"},{"Name":"Length","Value":"16"}]}]'
    $dualSpaceOperations = [ordered]@{ operations = @(
            [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DI_Candidate', 'DI_Candidate') }; addressIndex = 0; startAddress = 66; expectedStartAddress = 2 },
            [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DIDQ_Candidate', 'DIDQ_Candidate') }; addressIndex = 0; startAddress = 68; expectedStartAddress = 0 },
            [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DQ_Candidate', 'DQ_Candidate') }; addressIndex = 0; startAddress = 130; expectedStartAddress = 2 },
            [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DIDQ_Candidate', 'DIDQ_Candidate') }; addressIndex = 1; startAddress = 132; expectedStartAddress = 0 }
        ) }
    $dualSpacePreview = '{"DryRun":true,"Applied":false,"RolledBack":true,"Operations":[{"Data":{"AddressIndex":0,"IoType":"Input","BeforeStartAddress":2,"WrittenStartAddress":66}},{"Data":{"AddressIndex":0,"IoType":"Input","BeforeStartAddress":0,"WrittenStartAddress":68}},{"Data":{"AddressIndex":0,"IoType":"Output","BeforeStartAddress":2,"WrittenStartAddress":130}},{"Data":{"AddressIndex":1,"IoType":"Output","BeforeStartAddress":0,"WrittenStartAddress":132}}]}'
    $dualSpaceApply = '{"DryRun":false,"Applied":true,"RolledBack":false,"Operations":[{"Data":{"AddressIndex":0,"IoType":"Input","BeforeStartAddress":2,"WrittenStartAddress":66}},{"Data":{"AddressIndex":0,"IoType":"Input","BeforeStartAddress":0,"WrittenStartAddress":68}},{"Data":{"AddressIndex":0,"IoType":"Output","BeforeStartAddress":2,"WrittenStartAddress":130}},{"Data":{"AddressIndex":1,"IoType":"Output","BeforeStartAddress":0,"WrittenStartAddress":132}}]}'
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-19' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output $dualSpaceTree),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $dualSpaceBaselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $dualSpaceOperations; 'dry-run' = $true }) -Output $dualSpacePreview),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $dualSpaceOperations; 'dry-run' = $false; confirm = $true }) -Output $dualSpaceApply),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $dualSpaceFinalMap),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'One atomic patch preserved DI_Occupied Input at 64 and moved DI_Candidate Input to 66; DIDQ_Candidate Input index 0 is 68. It preserved DQ_Occupied Output at 128 and moved DQ_Candidate Output to 130; DIDQ_Candidate Output index 1 is 132.' } }
    )

    $byteConfusionOperations = [ordered]@{ operations = @(
            [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DI_Candidate', 'DI_Candidate') }; addressIndex = 0; startAddress = 80; expectedStartAddress = 2 },
            [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DIDQ_Candidate', 'DIDQ_Candidate') }; addressIndex = 0; startAddress = 96; expectedStartAddress = 0 },
            [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DQ_Candidate', 'DQ_Candidate') }; addressIndex = 0; startAddress = 144; expectedStartAddress = 2 },
            [ordered]@{ op = 'set-io-address-start'; target = [ordered]@{ deviceName = 'PLC_Main'; itemPath = @('DIDQ_Candidate', 'DIDQ_Candidate') }; addressIndex = 1; startAddress = 160; expectedStartAddress = 0 }
        ) }
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-19' -ExpectedPassed $false -ExpectedFailedAssertion 'one-four-operation-plan' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output $dualSpaceTree),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $dualSpaceBaselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $byteConfusionOperations; 'dry-run' = $true }) -Output 'Incorrect 16-byte allocation preview.'),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Incorrectly treated Length 16 as bytes.' } }
    )

    $inputHalf = [ordered]@{ operations = [ordered]@{ operations = @($dualSpaceOperations.operations[0], $dualSpaceOperations.operations[1]) } }
    $outputHalf = [ordered]@{ operations = [ordered]@{ operations = @($dualSpaceOperations.operations[2], $dualSpaceOperations.operations[3]) } }
    Invoke-HardwareCaseAssertionTest -CaseId 'ott-19' -ExpectedPassed $false -ExpectedFailedAssertion 'exactly-two-patch-calls' -Events @(
        (New-ToolEvent -Tool 'opentia_get-hardware-tree' -InputObject @{} -Output $dualSpaceTree),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $dualSpaceBaselineMap),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $inputHalf.operations; 'dry-run' = $true }) -Output $dualSpacePreview),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $inputHalf.operations; 'dry-run' = $false; confirm = $true }) -Output $dualSpaceApply),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $outputHalf.operations; 'dry-run' = $true }) -Output $dualSpacePreview),
        (New-ToolEvent -Tool 'opentia_apply-hardware-patch' -InputObject ([ordered]@{ operations = $outputHalf.operations; 'dry-run' = $false; confirm = $true }) -Output $dualSpaceApply),
        (New-ToolEvent -Tool 'opentia_get-hardware-io-addresses' -InputObject @{} -Output $dualSpaceFinalMap),
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Split the work into two transactions.' } }
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
