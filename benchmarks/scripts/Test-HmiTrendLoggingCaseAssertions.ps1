[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaHmiTrendTests-" + [Guid]::NewGuid().ToString('N'))
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
    $state[$(if ($ErrorOutput) { 'error' } else { 'output' })] = $Output
    return [ordered]@{
        type = 'tool_use'
        part = [ordered]@{ tool = $Tool; state = $state }
    }
}

function Invoke-TrendAssertionTest {
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
        [ordered]@{ type = 'text'; part = [ordered]@{ text = 'Trend logging reconciliation complete without saving.' } },
        [ordered]@{ type = 'step_finish'; part = [ordered]@{
                reason = 'stop'
                tokens = [ordered]@{ total = 10; input = 5; output = 5; reasoning = 0 }
                cost = 0
            } }
    ) | ForEach-Object { $_ | ConvertTo-Json -Depth 60 -Compress } |
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
        fixtureSha256 = '7078FA79558948F375229C94AB69FF00D171646CAE874D51DA9B8453D35C82EB'
        startedAtUtc = '2026-09-05T00:00:00.0000000Z'
        finishedAtUtc = '2026-09-05T00:00:01.0000000Z'
        durationSeconds = 1
        exitCode = 0
        timedOut = $false
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath (Join-Path $benchmarkRoot 'cases\ott-29\case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative Unified trend trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative Unified trend trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

function New-TagEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$DataType,
        [Parameter(Mandatory = $true)][int]$LoggingTagCount
    )

    return New-ToolEvent -Tool 'opentia_list-hmi-tags' -InputObject @{
        'hmi-name' = 'HMI_RT_1'
        'root-path' = $Path
        'include-details' = $true
        'include-logging-counts' = $true
    } -Output ('[{"Path":"' + $Path + '","Name":"' + $Path + '","DataType":"' +
        $DataType + '","TagTableName":"OTT28 Fixture","LoggingTagCount":' + $LoggingTagCount + '}]')
}

function New-TrendEvents {
    param(
        [switch]$TargetedTagReads,
        [switch]$ApplyBeforePreview,
        [switch]$WrongCycle,
        [switch]$OmitMixedSafety,
        [switch]$ApplyMixedSafetyBatch,
        [switch]$OmitFinalTags,
        [switch]$OmitFinalAssociations,
        [switch]$OmitProcessLogGuard,
        [switch]$OmitRetiredAbsence,
        [switch]$UseSingleItemCommand,
        [switch]$SaveProject
    )

    $cycle = if ($WrongCycle) { 'T10s' } else { 'T5s' }
    $processConfigs = @(
        [ordered]@{
            tagPath = 'MixerSpeed'; dataLogName = 'MixerProcessLog'; cyclic = $true; cycle = $cycle
            logMaxSize = 4096; durationDays = 60; segmentMaxSize = 512
        },
        [ordered]@{
            tagPath = 'BatchNumber'; dataLogName = 'MixerProcessLog'; cyclic = $false
            logMaxSize = 4096; durationDays = 60; segmentMaxSize = 512
        }
    )
    $mixedLogs = @(
        [ordered]@{ dataLogName = 'MixerLegacyLog' },
        [ordered]@{ dataLogName = 'MixerBatchRetireLog' }
    )
    $retiredLogs = @(
        [ordered]@{ dataLogName = 'MixerLegacyLog' },
        [ordered]@{ dataLogName = 'MixerBatchRetireLog' },
        [ordered]@{ dataLogName = 'MixerProtectedLog' }
    )
    $statusRemoval = @([ordered]@{ tagPath = 'MixerAlarmBits'; dataLogName = 'MixerProtectedLog' })
    $finalAssociations = @(
        [ordered]@{ tagPath = 'MixerSpeed'; dataLogName = 'MixerProcessLog' },
        [ordered]@{ tagPath = 'BatchNumber'; dataLogName = 'MixerProcessLog' }
    )

    $events = [System.Collections.Generic.List[object]]::new()
    $events.Add((New-ToolEvent -Tool 'opentia_open-unified-hmi' -InputObject @{
                'hmi-name' = 'HMI_RT_1'
            } -Output '{"opened":true,"hmiName":"HMI_RT_1"}'))

    if ($TargetedTagReads) {
        $events.Add((New-TagEvent -Path 'BatchNumber' -DataType 'DInt' -LoggingTagCount 0))
        $events.Add((New-TagEvent -Path 'MixerAlarmBits' -DataType 'Int' -LoggingTagCount 1))
        $events.Add((New-TagEvent -Path 'MixerSpeed' -DataType 'Real' -LoggingTagCount 1))
    }
    else {
        $events.Add((New-ToolEvent -Tool 'opentia_list-hmi-tags' -InputObject @{
                    'hmi-name' = 'HMI_RT_1'; 'allow-full-scan' = $true; 'max-results' = 20
                    'max-depth' = 2; 'include-details' = $true; 'include-logging-counts' = $true
                } -Output '[{"Path":"BatchNumber","DataType":"DInt","TagTableName":"OTT28 Fixture","LoggingTagCount":0},{"Path":"MixerAlarmBits","DataType":"Int","TagTableName":"OTT28 Fixture","LoggingTagCount":1},{"Path":"MixerSpeed","DataType":"Real","TagTableName":"OTT28 Fixture","LoggingTagCount":1}]'))
    }

    $mixedPreview = New-ToolEvent -Tool 'opentia_delete-hmi-data-logs' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; configs = $mixedLogs; 'dry-run' = $true
    } -Output '{"success":false,"message":"0 applied, 1 planned, 1 blocked, 0 not found, 0 failed","data":{"DryRun":true,"RequestedCount":2,"PlannedCount":1,"BlockedCount":1,"NotFoundCount":0,"FailedCount":0,"Results":[{"DataLogName":"MixerLegacyLog","Status":"Blocked","References":[{"TagPath":"MixerSpeed","LoggingTagName":"MixerSpeed","DataLogName":"MixerLegacyLog"}]},{"DataLogName":"MixerBatchRetireLog","Status":"Planned"}]}}' -ErrorOutput
    if (-not $OmitMixedSafety) {
        $events.Add($mixedPreview)
    }
    if ($ApplyMixedSafetyBatch) {
        $events.Add((New-ToolEvent -Tool 'opentia_delete-hmi-data-logs' -InputObject @{
                    'hmi-name' = 'HMI_RT_1'; configs = $mixedLogs; 'dry-run' = $false; confirm = $true
                } -Output '{"DryRun":false,"RequestedCount":2,"AppliedCount":1,"BlockedCount":1}'))
    }

    $createPreview = New-ToolEvent -Tool 'opentia_create-hmi-trends' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; configs = $processConfigs; 'dry-run' = $true
    } -Output '{"Operation":"create-hmi-trends","DryRun":true,"RequestedCount":2,"AppliedCount":0,"PlannedCount":2,"BlockedCount":0,"NotFoundCount":0,"FailedCount":0,"Results":[{"TagPath":"MixerSpeed","DataLogName":"MixerProcessLog","Status":"Planned"},{"TagPath":"BatchNumber","DataLogName":"MixerProcessLog","Status":"Planned"}]}'
    $createApply = New-ToolEvent -Tool 'opentia_create-hmi-trends' -InputObject @{
        'hmi-name' = 'HMI_RT_1'; configs = $processConfigs; 'dry-run' = $false; confirm = $true
    } -Output ('{"Operation":"create-hmi-trends","DryRun":false,"RequestedCount":2,"AppliedCount":2,"PlannedCount":0,"BlockedCount":0,"NotFoundCount":0,"FailedCount":0,"Results":[' +
        '{"TagPath":"MixerSpeed","DataLogName":"MixerProcessLog","Status":"Applied","Trend":{"TagPath":"MixerSpeed","DataLogName":"MixerProcessLog","LoggingMode":"Cyclic","Cycle":"' + $cycle + '","LogMaxSize":4096,"DurationDays":60,"SegmentMaxSize":512,"DataLogCreated":true,"LoggingTagCreated":false}},' +
        '{"TagPath":"BatchNumber","DataLogName":"MixerProcessLog","Status":"Applied","Trend":{"TagPath":"BatchNumber","DataLogName":"MixerProcessLog","LoggingMode":"OnChange","Cycle":null,"LogMaxSize":4096,"DurationDays":60,"SegmentMaxSize":512,"DataLogCreated":false,"LoggingTagCreated":true}}]}')
    if ($ApplyBeforePreview) {
        $events.Add($createApply)
        $events.Add($createPreview)
    }
    else {
        $events.Add($createPreview)
        $events.Add($createApply)
    }

    if ($UseSingleItemCommand) {
        $events.Add((New-ToolEvent -Tool 'opentia_create-hmi-trend' -InputObject @{
                    'hmi-name' = 'HMI_RT_1'; 'tag-path' = 'MixerSpeed'; 'datalog-name' = 'MixerProcessLog'; confirm = $true
                } -Output '{"configured":true}'))
    }

    $events.Add((New-ToolEvent -Tool 'opentia_remove-hmi-logging-tags' -InputObject @{
                'hmi-name' = 'HMI_RT_1'; configs = $statusRemoval; 'dry-run' = $true
            } -Output '{"Operation":"remove-hmi-logging-tags","DryRun":true,"RequestedCount":1,"AppliedCount":0,"PlannedCount":1,"BlockedCount":0,"NotFoundCount":0,"FailedCount":0,"Results":[{"TagPath":"MixerAlarmBits","DataLogName":"MixerProtectedLog","LoggingTagName":"MixerAlarmBits","Status":"Planned"}]}'))
    $events.Add((New-ToolEvent -Tool 'opentia_remove-hmi-logging-tags' -InputObject @{
                'hmi-name' = 'HMI_RT_1'; configs = $statusRemoval; 'dry-run' = $false; confirm = $true
            } -Output '{"Operation":"remove-hmi-logging-tags","DryRun":false,"RequestedCount":1,"AppliedCount":1,"PlannedCount":0,"BlockedCount":0,"NotFoundCount":0,"FailedCount":0,"Results":[{"TagPath":"MixerAlarmBits","DataLogName":"MixerProtectedLog","LoggingTagName":"MixerAlarmBits","Status":"Applied"}]}'))

    $events.Add((New-ToolEvent -Tool 'opentia_delete-hmi-data-logs' -InputObject @{
                'hmi-name' = 'HMI_RT_1'; configs = $retiredLogs; 'dry-run' = $true
            } -Output '{"Operation":"delete-hmi-data-logs","DryRun":true,"RequestedCount":3,"AppliedCount":0,"PlannedCount":3,"BlockedCount":0,"NotFoundCount":0,"FailedCount":0,"Results":[{"DataLogName":"MixerLegacyLog","Status":"Planned"},{"DataLogName":"MixerBatchRetireLog","Status":"Planned"},{"DataLogName":"MixerProtectedLog","Status":"Planned"}]}'))
    $events.Add((New-ToolEvent -Tool 'opentia_delete-hmi-data-logs' -InputObject @{
                'hmi-name' = 'HMI_RT_1'; configs = $retiredLogs; 'dry-run' = $false; confirm = $true
            } -Output '{"Operation":"delete-hmi-data-logs","DryRun":false,"RequestedCount":3,"AppliedCount":3,"PlannedCount":0,"BlockedCount":0,"NotFoundCount":0,"FailedCount":0,"Results":[{"DataLogName":"MixerLegacyLog","Status":"Applied"},{"DataLogName":"MixerBatchRetireLog","Status":"Applied"},{"DataLogName":"MixerProtectedLog","Status":"Applied"}]}'))

    if (-not $OmitFinalTags) {
        if ($TargetedTagReads) {
            $events.Add((New-TagEvent -Path 'BatchNumber' -DataType 'DInt' -LoggingTagCount 1))
            $events.Add((New-TagEvent -Path 'MixerAlarmBits' -DataType 'Int' -LoggingTagCount 0))
            $events.Add((New-TagEvent -Path 'MixerSpeed' -DataType 'Real' -LoggingTagCount 1))
        }
        else {
            $events.Add((New-ToolEvent -Tool 'opentia_list-hmi-tags' -InputObject @{
                        'hmi-name' = 'HMI_RT_1'; 'allow-full-scan' = $true; 'max-results' = 20
                        'max-depth' = 2; 'include-details' = $true; 'include-logging-counts' = $true
                    } -Output '[{"Path":"BatchNumber","DataType":"DInt","TagTableName":"OTT28 Fixture","LoggingTagCount":1},{"Path":"MixerAlarmBits","DataType":"Int","TagTableName":"OTT28 Fixture","LoggingTagCount":0},{"Path":"MixerSpeed","DataType":"Real","TagTableName":"OTT28 Fixture","LoggingTagCount":1}]'))
        }
    }

    if (-not $OmitFinalAssociations) {
        $events.Add((New-ToolEvent -Tool 'opentia_remove-hmi-logging-tags' -InputObject @{
                    'hmi-name' = 'HMI_RT_1'; configs = $finalAssociations; 'dry-run' = $true
                } -Output '{"Operation":"remove-hmi-logging-tags","DryRun":true,"RequestedCount":2,"AppliedCount":0,"PlannedCount":2,"BlockedCount":0,"NotFoundCount":0,"FailedCount":0,"Results":[{"TagPath":"MixerSpeed","DataLogName":"MixerProcessLog","LoggingTagName":"MixerSpeed","Status":"Planned"},{"TagPath":"BatchNumber","DataLogName":"MixerProcessLog","LoggingTagName":"BatchNumber","Status":"Planned"}]}'))
    }
    if (-not $OmitProcessLogGuard) {
        $events.Add((New-ToolEvent -Tool 'opentia_delete-hmi-data-logs' -InputObject @{
                    'hmi-name' = 'HMI_RT_1'; configs = @([ordered]@{ dataLogName = 'MixerProcessLog' }); 'dry-run' = $true
                } -Output '{"success":false,"message":"0 applied, 0 planned, 1 blocked, 0 not found, 0 failed","data":{"DryRun":true,"RequestedCount":1,"BlockedCount":1,"Results":[{"DataLogName":"MixerProcessLog","Status":"Blocked","References":[{"TagPath":"MixerSpeed","DataLogName":"MixerProcessLog"},{"TagPath":"BatchNumber","DataLogName":"MixerProcessLog"}]}]}}' -ErrorOutput))
    }
    if (-not $OmitRetiredAbsence) {
        $events.Add((New-ToolEvent -Tool 'opentia_delete-hmi-data-logs' -InputObject @{
                    'hmi-name' = 'HMI_RT_1'; configs = $retiredLogs; 'dry-run' = $true
                } -Output '{"success":false,"message":"0 applied, 0 planned, 0 blocked, 3 not found, 0 failed","data":{"DryRun":true,"RequestedCount":3,"PlannedCount":0,"BlockedCount":0,"NotFoundCount":3,"FailedCount":0,"Results":[{"DataLogName":"MixerLegacyLog","Status":"NotFound"},{"DataLogName":"MixerBatchRetireLog","Status":"NotFound"},{"DataLogName":"MixerProtectedLog","Status":"NotFound"}]}}' -ErrorOutput))
    }
    if ($SaveProject) {
        $events.Add((New-ToolEvent -Tool 'opentia_save-project' -InputObject @{ confirm = $true } -Output '{"saved":true}'))
    }

    return @($events)
}

try {
    Invoke-TrendAssertionTest -Label 'representative' -Events (New-TrendEvents)
    Invoke-TrendAssertionTest -Label 'targeted-tag-reads' -Events (New-TrendEvents -TargetedTagReads)

    Invoke-TrendAssertionTest -Label 'missing-mixed-safety' -Events (New-TrendEvents -OmitMixedSafety) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'mixed-retirement-safety-input'
    Invoke-TrendAssertionTest -Label 'wrong-cycle' -Events (New-TrendEvents -WrongCycle) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'shared-process-log-preview-input'
    Invoke-TrendAssertionTest -Label 'apply-before-preview' -Events (New-TrendEvents -ApplyBeforePreview) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'reconciliation-sequence'
    Invoke-TrendAssertionTest -Label 'mixed-batch-applied' -Events (New-TrendEvents -ApplyMixedSafetyBatch) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'mixed-batch-never-applied'
    Invoke-TrendAssertionTest -Label 'missing-final-tags' -Events (New-TrendEvents -OmitFinalTags) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'baseline-and-final-tag-evidence'
    Invoke-TrendAssertionTest -Label 'missing-final-associations' -Events (New-TrendEvents -OmitFinalAssociations) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'final-process-associations-input'
    Invoke-TrendAssertionTest -Label 'missing-process-log-guard' -Events (New-TrendEvents -OmitProcessLogGuard) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'active-process-log-guard-input'
    Invoke-TrendAssertionTest -Label 'missing-retired-absence' -Events (New-TrendEvents -OmitRetiredAbsence) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'retired-archive-preview-and-absence-inputs'
    Invoke-TrendAssertionTest -Label 'single-item-command' -Events (New-TrendEvents -UseSingleItemCommand) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-out-of-domain-or-single-item-mutation'
    Invoke-TrendAssertionTest -Label 'project-saved' -Events (New-TrendEvents -SaveProject) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-out-of-domain-or-single-item-mutation'

    Write-Host 'Unified HMI trend-logging case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_HMI_TREND_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaHmiTrendTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
