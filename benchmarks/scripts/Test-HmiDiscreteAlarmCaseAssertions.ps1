[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaHmiAlarmTests-" + [Guid]::NewGuid().ToString('N'))
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

function Copy-Events {
    param([Parameter(Mandatory = $true)][object[]]$Events)
    return @($Events | ForEach-Object { $_ | ConvertTo-Json -Depth 50 | ConvertFrom-Json })
}

function Invoke-AlarmAssertionTest {
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
        fixtureSha256 = '316B538B73F1DF41FBC07DBD0DA5963F039E552568EB1984604FE854DBE9BD52'
        startedAtUtc = '2026-09-04T08:00:00.0000000Z'
        finishedAtUtc = '2026-09-04T08:00:01.0000000Z'
        durationSeconds = 1
        exitCode = 0
        timedOut = $false
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath (Join-Path $benchmarkRoot 'cases\ott-28\case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative Unified alarm trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative Unified alarm trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

function New-AlarmEvents {
    param(
        [switch]$ApplyBeforePreview,
        [switch]$SplitReconciliationBatch,
        [switch]$OmitRetirementNotFoundRead,
        [switch]$OmitNearNameRead,
        [switch]$AllowReferencedDelete,
        [switch]$SaveProject,
        [switch]$OmitInternalTagDiscovery,
        [switch]$TargetedInternalTagDiscovery,
        [switch]$IncompleteTargetedInternalTagDiscovery,
        [switch]$WrongComputedAllocation
    )

    $baselineOverloadHash = '9eedff2c8971afc5c2c87259c9bf801cd8bcbed1b7fdf3e11a5ddda3054c641a'
    $finalOverloadHash = 'dfc7126ba2e9948c3d76373a1e77c575d7dc3e9989de56a9767f1fb7e8f7f629'
    $speedHighHash = '0f12d0427b2f66a88fa16cdf673e192c7e6fbd13ad515314794daa684cb53322'
    $legacyHash = 'f01f6f6649ff1a83369281154deebed14914bae2a35eb033b75dfda74c1278f8'
    $nearNameHash = '2eaba142159d40a68202a0e7939374a74495c34cade687565d18b1bff566925c'
    $computedId = if ($WrongComputedAllocation) { 2404 } else { 2402 }
    $computedBit = if ($WrongComputedAllocation) { 6 } else { 5 }
    $documentPath = 'C:\OTT\Bench\synthetic\ott-28-alarm-change.json'

    $open = New-ToolEvent -Tool 'opentia_open-unified-hmi' -InputObject @{
        'hmi-name' = 'HMI_RT_1'
    } -Output '{"opened":true,"hmiName":"HMI_RT_1"}'
    $tags = New-ToolEvent -Tool 'opentia_list-hmi-tags' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'allow-full-scan' = $true; 'max-results' = 50; 'max-depth' = 5; 'include-details' = $true
    } -Output '[{"Name":"BatchNumber","DataType":"DInt","TagTableName":"OTT28 Fixture"},{"Name":"MixerAlarmBits","DataType":"Int","TagTableName":"OTT28 Fixture"},{"Name":"MixerSpeed","DataType":"Real","TagTableName":"OTT28 Fixture"}]'
    $targetedTags = @(
        (New-ToolEvent -Tool 'opentia_list-hmi-tags' -InputObject @{
                'hmi-name' = 'HMI_RT_1'; 'root-path' = 'MixerAlarmBits'
            } -Output '[{"Name":"MixerAlarmBits","DataType":"Int","TagTableName":"OTT28 Fixture"}]'),
        (New-ToolEvent -Tool 'opentia_list-hmi-tags' -InputObject @{
                'hmi-name' = 'HMI_RT_1'; 'root-path' = 'MixerSpeed'
            } -Output '[{"Name":"MixerSpeed","DataType":"Real","TagTableName":"OTT28 Fixture"}]')
    )
    if (-not $IncompleteTargetedInternalTagDiscovery) {
        $targetedTags += New-ToolEvent -Tool 'opentia_list-hmi-tags' -InputObject @{
            'hmi-name' = 'HMI_RT_1'; 'root-path' = 'BatchNumber'
        } -Output '[{"Name":"BatchNumber","DataType":"DInt","TagTableName":"OTT28 Fixture"}]'
    }
    $baselineList = New-ToolEvent -Tool 'opentia_list-unified-discrete-alarms' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'include-validation' = $true
    } -Output '[{"Name":"Mixer_Bit_Distractor","Id":2403,"Validation":[]},{"Name":"Mixer_Id_Distractor","Id":2401,"Validation":[]},{"Name":"Mixer_Overload","Id":2400,"ContentHash":"9eedff2c8971afc5c2c87259c9bf801cd8bcbed1b7fdf3e11a5ddda3054c641a","Validation":[]},{"Name":"Mixer_Trip_Legacy","Id":2399,"ContentHash":"f01f6f6649ff1a83369281154deebed14914bae2a35eb033b75dfda74c1278f8","Validation":[]},{"Name":"Mixer_Trip_Legacy_Display","Id":2398,"ContentHash":"2eaba142159d40a68202a0e7939374a74495c34cade687565d18b1bff566925c","Validation":[]}]'
    $baselineOverload = New-ToolEvent -Tool 'opentia_get-unified-discrete-alarm' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'alarm-name' = 'Mixer_Overload'; 'include-validation' = $true
    } -Output ('{"Name":"Mixer_Overload","Id":2400,"AlarmClass":"Warning","Priority":8,"Origin":"Mixer","Area":"Process","RaisedStateTag":"MixerAlarmBits","RaisedStateTagBitNumber":0,"AlarmParameterTags":["MixerSpeed","BatchNumber"],"Validation":[],"ContentHash":"' + $baselineOverloadHash + '"}')

    $previewInput = [ordered]@{
        'hmi-name' = 'HMI_RT_1'; 'input-path' = $documentPath; 'dry-run' = $true; 'fail-on-warning' = $true
    }
    $applyInput = [ordered]@{
        'hmi-name' = 'HMI_RT_1'; 'input-path' = $documentPath; 'dry-run' = $false; apply = $true; confirm = $true; 'fail-on-warning' = $true
    }
    $previewOutput = '{"DryRun":true,"Success":true,"Message":"Discrete-alarm dry-run completed and the transaction was rolled back.","Alarms":[{"Name":"Mixer_Overload","Status":"Updated","ProposedAfter":{"Name":"Mixer_Overload","AlarmClass":"Critical","Priority":14,"Origin":"Mixer","Area":"Process","RaisedStateTag":"MixerAlarmBits","RaisedStateTagBitNumber":0,"AlarmParameterTags":["MixerSpeed","BatchNumber"],"EventText":"Mixer overload critical at speed {0} during batch {1}.","InfoText":"Stop the mixer, inspect the drive, and record the batch before restart.","ContentHash":"' + $finalOverloadHash + '"},"Warnings":[]},{"Name":"Mixer_SpeedHigh","Status":"Created","ProposedAfter":{"Name":"Mixer_SpeedHigh","Id":' + $computedId + ',"AlarmClass":"Critical","Priority":14,"Origin":"Mixer","Area":"Process","RaisedStateTag":"MixerAlarmBits","RaisedStateTagBitNumber":' + $computedBit + ',"AlarmParameterTags":["MixerSpeed","BatchNumber"],"EventText":"Mixer speed high at {0} rpm during batch {1}.","InfoText":"Reduce the mixer speed and inspect the drive.","ContentHash":"' + $speedHighHash + '"},"Warnings":[]}],"Transaction":{"Committed":false,"RolledBack":true}}'
    $applyOutput = '{"DryRun":false,"Success":true,"Message":"Discrete alarms were applied atomically. The open TIA project now contains unsaved changes.","Alarms":[{"Name":"Mixer_Overload","Status":"Updated","Persisted":{"ContentHash":"' + $finalOverloadHash + '"}},{"Name":"Mixer_SpeedHigh","Status":"Created","Persisted":{"Id":' + $computedId + ',"RaisedStateTagBitNumber":' + $computedBit + ',"ContentHash":"' + $speedHighHash + '"}}],"Transaction":{"Committed":true,"RolledBack":false}}'
    $preview = New-ToolEvent -Tool 'opentia_apply-unified-discrete-alarms' -InputObject $previewInput -Output $previewOutput
    $apply = New-ToolEvent -Tool 'opentia_apply-unified-discrete-alarms' -InputObject $applyInput -Output $applyOutput

    $reconciliationEvents = if ($SplitReconciliationBatch) {
        @($preview, (Copy-Events -Events @($preview))[0], $apply, (Copy-Events -Events @($apply))[0])
    }
    elseif ($ApplyBeforePreview) {
        @($apply, $preview)
    }
    else {
        @($preview, $apply)
    }

    $finalOverload = New-ToolEvent -Tool 'opentia_get-unified-discrete-alarm' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'alarm-name' = 'Mixer_Overload'; 'include-validation' = $true
    } -Output ('{"Name":"Mixer_Overload","Id":2400,"AlarmClass":"Critical","Priority":14,"Origin":"Mixer","Area":"Process","RaisedStateTag":"MixerAlarmBits","RaisedStateTagBitNumber":0,"AlarmParameterTags":["MixerSpeed","BatchNumber"],"EventText":"Mixer overload critical at speed {0} during batch {1}.","InfoText":"Stop the mixer, inspect the drive, and record the batch before restart.","Validation":[],"ContentHash":"' + $finalOverloadHash + '"}')
    $speedHigh = New-ToolEvent -Tool 'opentia_get-unified-discrete-alarm' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'alarm-name' = 'Mixer_SpeedHigh'; 'include-validation' = $true
    } -Output ('{"Name":"Mixer_SpeedHigh","Id":' + $computedId + ',"AlarmClass":"Critical","Priority":14,"Origin":"Mixer","Area":"Process","RaisedStateTag":"MixerAlarmBits","RaisedStateTagBitNumber":' + $computedBit + ',"AlarmParameterTags":["MixerSpeed","BatchNumber"],"EventText":"Mixer speed high at {0} rpm during batch {1}.","InfoText":"Reduce the mixer speed and inspect the drive.","Validation":[],"ContentHash":"' + $speedHighHash + '"}')
    $legacy = New-ToolEvent -Tool 'opentia_get-unified-discrete-alarm' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'alarm-name' = 'Mixer_Trip_Legacy'; 'include-validation' = $true
    } -Output ('{"Name":"Mixer_Trip_Legacy","Id":2399,"Validation":[],"ContentHash":"' + $legacyHash + '"}')

    $deletePreviewInput = [ordered]@{
        'hmi-name' = 'HMI_RT_1'; 'alarm-name' = 'Mixer_Trip_Legacy'; 'expected-hash' = $legacyHash; 'dry-run' = $true
    }
    $deleteApplyInput = [ordered]@{
        'hmi-name' = 'HMI_RT_1'; 'alarm-name' = 'Mixer_Trip_Legacy'; 'expected-hash' = $legacyHash; 'dry-run' = $false; confirm = $true
    }
    if ($AllowReferencedDelete) {
        $deleteApplyInput['allow-referenced-delete'] = $true
    }
    $deletePreview = New-ToolEvent -Tool 'opentia_delete-unified-discrete-alarm' -InputObject $deletePreviewInput -Output '{"DryRun":true,"Success":true,"Deleted":false,"Message":"Delete dry-run completed and was rolled back.","CrossReferences":[],"Transaction":{"Committed":false,"RolledBack":true}}'
    $deleteApply = New-ToolEvent -Tool 'opentia_delete-unified-discrete-alarm' -InputObject $deleteApplyInput -Output '{"DryRun":false,"Success":true,"Deleted":true,"Message":"The exact discrete alarm was deleted. The open TIA project now contains unsaved changes.","CrossReferences":[],"Transaction":{"Committed":true,"RolledBack":false}}'
    $legacyNotFound = New-ToolEvent -Tool 'opentia_get-unified-discrete-alarm' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'alarm-name' = 'Mixer_Trip_Legacy'; 'include-validation' = $true
    } -Output 'Unified discrete alarm not found: Mixer_Trip_Legacy' -ErrorOutput
    $nearName = New-ToolEvent -Tool 'opentia_get-unified-discrete-alarm' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'alarm-name' = 'Mixer_Trip_Legacy_Display'; 'include-validation' = $true
    } -Output ('{"Name":"Mixer_Trip_Legacy_Display","Id":2398,"Validation":[],"ContentHash":"' + $nearNameHash + '"}')
    $finalList = New-ToolEvent -Tool 'opentia_list-unified-discrete-alarms' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; 'include-validation' = $true
    } -Output '[{"Name":"Mixer_Bit_Distractor","Id":2403,"Validation":[]},{"Name":"Mixer_Id_Distractor","Id":2401,"Validation":[]},{"Name":"Mixer_Overload","ContentHash":"dfc7126ba2e9948c3d76373a1e77c575d7dc3e9989de56a9767f1fb7e8f7f629","Validation":[]},{"Name":"Mixer_SpeedHigh","Id":2402,"ContentHash":"0f12d0427b2f66a88fa16cdf673e192c7e6fbd13ad515314794daa684cb53322","Validation":[]},{"Name":"Mixer_Trip_Legacy_Display","Id":2398,"ContentHash":"2eaba142159d40a68202a0e7939374a74495c34cade687565d18b1bff566925c","Validation":[]}]'

    $verificationEvents = @($finalOverload, $speedHigh, $legacy, $deletePreview, $deleteApply)
    if (-not $OmitRetirementNotFoundRead) { $verificationEvents += $legacyNotFound }
    if (-not $OmitNearNameRead) { $verificationEvents += $nearName }
    if ($SaveProject) {
        $verificationEvents += New-ToolEvent -Tool 'opentia_save-project' -InputObject @{ confirm = $true } -Output '{"saved":true}'
    }
    $verificationEvents += $finalList

    $discoveryEvents = @($open)
    if (-not $OmitInternalTagDiscovery) {
        $discoveryEvents += if ($TargetedInternalTagDiscovery) { $targetedTags } else { $tags }
    }
    return $discoveryEvents + @($baselineList, $baselineOverload) + $reconciliationEvents + $verificationEvents
}

try {
    Invoke-AlarmAssertionTest -Label 'representative' -Events (New-AlarmEvents)

    Invoke-AlarmAssertionTest -Label 'targeted-internal-tag-discovery' `
        -Events (New-AlarmEvents -TargetedInternalTagDiscovery)

    Invoke-AlarmAssertionTest -Label 'incomplete-targeted-internal-tag-discovery' `
        -Events (New-AlarmEvents -TargetedInternalTagDiscovery -IncompleteTargetedInternalTagDiscovery) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'internal-hmi-tag-discovery'

    Invoke-AlarmAssertionTest -Label 'apply-before-preview' -Events (New-AlarmEvents -ApplyBeforePreview) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'preview-apply-readback-retire-sequence'

    Invoke-AlarmAssertionTest -Label 'split-reconciliation-batch' -Events (New-AlarmEvents -SplitReconciliationBatch) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'atomic-reconciliation-preview'

    Invoke-AlarmAssertionTest -Label 'wrong-computed-allocation' -Events (New-AlarmEvents -WrongComputedAllocation) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'atomic-reconciliation-preview'

    Invoke-AlarmAssertionTest -Label 'missing-internal-tag-discovery' -Events (New-AlarmEvents -OmitInternalTagDiscovery) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'internal-hmi-tag-discovery'

    Invoke-AlarmAssertionTest -Label 'missing-retirement-not-found-read' -Events (New-AlarmEvents -OmitRetirementNotFoundRead) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'retirement-fresh-exact-read'

    Invoke-AlarmAssertionTest -Label 'missing-near-name-read' -Events (New-AlarmEvents -OmitNearNameRead) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'near-name-distractor-preserved'

    Invoke-AlarmAssertionTest -Label 'referenced-delete-override' -Events (New-AlarmEvents -AllowReferencedDelete) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-referenced-delete-override'

    Invoke-AlarmAssertionTest -Label 'project-saved' -Events (New-AlarmEvents -SaveProject) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'project-not-saved'

    Write-Host 'Unified HMI discrete-alarm case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_HMI_ALARM_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaHmiAlarmTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
