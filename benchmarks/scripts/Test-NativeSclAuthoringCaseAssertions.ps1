[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaNativeSclTests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

$representativeSource = @'
TYPE "OTT30_RecipeWindow"
VERSION : 0.1
    STRUCT
        Target : Array[0..3] of Real;
        Tolerance : Array[0..3] of Real;
        Enabled : Array[0..3] of Bool;
        HoldTime : Time;
    END_STRUCT;
END_TYPE

FUNCTION_BLOCK "OTT30_RecipeWindowMonitor"
{ S7_Optimized_Access := 'TRUE' }
VERSION : 0.1
    VAR_INPUT
        Start : Bool;
        Reset : Bool;
        Actual : Array[0..3] of Real;
        Recipe : "OTT30_RecipeWindow";
    END_VAR
    VAR_OUTPUT
        Active : Bool;
        Ready : Bool;
        FirstOutsideIndex : Int;
        State : USInt;
    END_VAR
    VAR
        StartEdge : R_TRIG;
        ReadyTimer : TON_TIME;
        InternalState : USInt;
    END_VAR
    VAR_TEMP
        Index : Int;
        AnyEnabled : Bool;
        AllInTolerance : Bool;
        Difference : Real;
        EffectiveTolerance : Real;
    END_VAR
BEGIN
    #StartEdge(CLK := #Start);
    #AnyEnabled := FALSE;
    #AllInTolerance := TRUE;
    #FirstOutsideIndex := -1;
    FOR #Index := 0 TO 3 DO
        IF #Recipe.Enabled[#Index] THEN
            #AnyEnabled := TRUE;
            #EffectiveTolerance := ABS(#Recipe.Tolerance[#Index]);
            #Difference := ABS(#Actual[#Index] - #Recipe.Target[#Index]);
            IF #Difference > #EffectiveTolerance THEN
                #AllInTolerance := FALSE;
                IF #FirstOutsideIndex = -1 THEN
                    #FirstOutsideIndex := #Index;
                END_IF;
            END_IF;
        END_IF;
    END_FOR;
    #ReadyTimer.TON(IN := (#InternalState = 1) AND #AnyEnabled AND #AllInTolerance, PT := #Recipe.HoldTime);
    IF #Reset THEN
        #InternalState := 0;
    ELSIF #StartEdge.Q THEN
        #InternalState := 1;
    ELSE
        CASE #InternalState OF
            0: #InternalState := 0;
            1: IF #ReadyTimer.Q THEN #InternalState := 2; END_IF;
            2: IF NOT #AllInTolerance THEN #InternalState := 1; END_IF;
        ELSE
            #InternalState := 0;
        END_CASE;
    END_IF;
    #State := #InternalState;
    #Active := #InternalState <> 0;
    #Ready := #InternalState = 2;
END_FUNCTION_BLOCK
'@

$equivalentSource = $representativeSource
$equivalentSource = $equivalentSource.Replace(
    '        EffectiveTolerance : Real;',
    "        EffectiveTolerance : Real;`r`n        IsMonitoring : Bool;`r`n        IsReady : Bool;"
)
$equivalentSource = $equivalentSource.Replace(
    '            #EffectiveTolerance := ABS(#Recipe.Tolerance[#Index]);',
    "            #EffectiveTolerance := #Recipe.Tolerance[#Index];`r`n            IF #EffectiveTolerance < 0.0 THEN`r`n                #EffectiveTolerance := -#EffectiveTolerance;`r`n            END_IF;"
)
$equivalentSource = $equivalentSource.Replace(
    '            #Difference := ABS(#Actual[#Index] - #Recipe.Target[#Index]);',
    ''
)
$equivalentSource = $equivalentSource.Replace(
    '            IF #Difference > #EffectiveTolerance THEN',
    '            IF (#Actual[#Index] < (#Recipe.Target[#Index] - #EffectiveTolerance)) OR (#Actual[#Index] > (#Recipe.Target[#Index] + #EffectiveTolerance)) THEN'
)
$equivalentSource = $equivalentSource.Replace(
    "                IF #FirstOutsideIndex = -1 THEN`r`n                    #FirstOutsideIndex := #Index;`r`n                END_IF;",
    "                #FirstOutsideIndex := #Index;`r`n                EXIT;"
)
$equivalentSource = $equivalentSource.Replace(
    "    #Active := #InternalState <> 0;`r`n    #Ready := #InternalState = 2;",
    "    #IsMonitoring := #InternalState = 1;`r`n    #IsReady := #InternalState = 2;`r`n    #Active := #IsMonitoring OR #IsReady;`r`n    #Ready := (#InternalState = 2);"
)
$equivalentSource = $equivalentSource.Replace('#ReadyTimer.TON(', '#ReadyTimer(')

$directOutputStateSource = $representativeSource
$directOutputStateSource = $directOutputStateSource.Replace('        InternalState : USInt;', '')
$directOutputStateSource = $directOutputStateSource.Replace(
    '            #Difference := ABS(#Actual[#Index] - #Recipe.Target[#Index]);',
    '            #Difference := #Actual[#Index] - #Recipe.Target[#Index];'
)
$directOutputStateSource = $directOutputStateSource.Replace(
    '            IF #Difference > #EffectiveTolerance THEN',
    '            IF ABS(#Difference) > #EffectiveTolerance THEN'
)
$directOutputStateSource = $directOutputStateSource.Replace('#InternalState', '#State')
$directOutputStateSource = $directOutputStateSource.Replace('    #State := #State;', '')

$limitAndOutputCaseSource = $representativeSource
$limitAndOutputCaseSource = $limitAndOutputCaseSource.Replace(
    '        Difference : Real;',
    "        Difference : Real;`r`n        LowerLimit : Real;`r`n        UpperLimit : Real;"
)

$parenthesizedActiveSource = $representativeSource.Replace(
    '#Active := #InternalState <> 0;',
    '#Active := (#InternalState <> 0);'
)
$conjunctiveFirstOutsideSource = $representativeSource.Replace(
    "            IF #Difference > #EffectiveTolerance THEN`r`n                #AllInTolerance := FALSE;`r`n                IF #FirstOutsideIndex = -1 THEN`r`n                    #FirstOutsideIndex := #Index;`r`n                END_IF;`r`n            END_IF;",
    "            IF (#Difference > #EffectiveTolerance) AND (#FirstOutsideIndex = -1) THEN`r`n                #AllInTolerance := FALSE;`r`n                #FirstOutsideIndex := #Index;`r`n            END_IF;"
)
$typedLiteralSource = $representativeSource
$typedLiteralSource = $typedLiteralSource.Replace('#FirstOutsideIndex = -1', '#FirstOutsideIndex < 0')
$typedLiteralSource = $typedLiteralSource.Replace('#InternalState := 0;', '#InternalState := USINT#0;')
$typedLiteralSource = $typedLiteralSource.Replace('#InternalState := 1;', '#InternalState := USINT#1;')
$typedLiteralSource = $typedLiteralSource.Replace('#InternalState := 2;', '#InternalState := USINT#2;')
$typedLiteralSource = $typedLiteralSource.Replace('#InternalState = 1', '#InternalState = USINT#1')
$typedLiteralSource = $typedLiteralSource.Replace('#InternalState = 2', '#InternalState = USINT#2')
$typedLiteralSource = $typedLiteralSource.Replace('#InternalState <> 0', '#InternalState <> USINT#0')
$typedLiteralSource = $typedLiteralSource.Replace('            0:', '            USINT#0:')
$typedLiteralSource = $typedLiteralSource.Replace('            1:', '            USINT#1:')
$typedLiteralSource = $typedLiteralSource.Replace('            2:', '            USINT#2:')
$monitoringOnlyActiveSource = $representativeSource.Replace(
    '#Active := #InternalState <> 0;',
    '#Active := (#InternalState = 1);'
)
$limitAndOutputCaseSource = $limitAndOutputCaseSource.Replace(
    "            #Difference := ABS(#Actual[#Index] - #Recipe.Target[#Index]);`r`n            IF #Difference > #EffectiveTolerance THEN",
    "            #LowerLimit := #Recipe.Target[#Index] - #EffectiveTolerance;`r`n            #UpperLimit := #Recipe.Target[#Index] + #EffectiveTolerance;`r`n            IF (#Actual[#Index] < #LowerLimit) OR (#Actual[#Index] > #UpperLimit) THEN"
)
$limitAndOutputCaseSource = $limitAndOutputCaseSource.Replace(
    "    #State := #InternalState;`r`n    #Active := #InternalState <> 0;`r`n    #Ready := #InternalState = 2;",
    "    CASE #InternalState OF`r`n        2:`r`n            #Active := TRUE;`r`n            #Ready := TRUE;`r`n        1:`r`n            #Active := TRUE;`r`n            #Ready := FALSE;`r`n        ELSE`r`n            #Active := FALSE;`r`n            #Ready := FALSE;`r`n    END_CASE;`r`n    #State := #InternalState;"
)

function New-ToolEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Tool,
        [Parameter(Mandatory = $true)][object]$InputObject,
        [Parameter(Mandatory = $true)][string]$Output
    )

    return [ordered]@{
        type = 'tool_use'
        part = [ordered]@{
            tool = $Tool
            state = [ordered]@{ status = 'completed'; input = $InputObject; output = $Output }
        }
    }
}

function New-CapabilityEvent {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Type', 'Block')][string]$ObjectKind,
        [Parameter(Mandatory = $true)][ValidateSet('TYPE', 'FB')][string]$BlockType,
        [Parameter(Mandatory = $true)][string]$ObjectName,
        [Parameter(Mandatory = $true)][ValidateSet('Create', 'Modify')][string]$Operation,
        [switch]$OmitBlockType,
        [switch]$OmitRequestedLanguage
    )

    $exists = if ($Operation -eq 'Modify') { 'true' } else { 'false' }
    $sclObserved = if ($ObjectKind -eq 'Block' -and $Operation -eq 'Modify') { 'true' } else { 'false' }
    $inputObject = [ordered]@{
            'plc-name' = 'PLC_Main'
            'object-name' = $ObjectName
            'object-kind' = $ObjectKind
            operation = $Operation
            'requested-format' = 'SCL'
        }
    if (-not $OmitBlockType) { $inputObject['block-type'] = $BlockType }
    if (-not $OmitRequestedLanguage) { $inputObject['requested-language'] = 'SCL' }
    return New-ToolEvent -Tool 'opentia_discover-plc-code-capabilities' -InputObject $inputObject -Output ('{"Target":{"Exists":' + $exists + ',"ExactIdentityResolved":true,' +
        '"ObjectKind":"' + $ObjectKind + '","ObjectName":"' + $ObjectName + '",' +
        '"ObjectType":"' + $BlockType + '"},"ImportExport":{"SclSourceRouteObserved":' +
        $sclObserved + '},"SelectedRoute":"DirectScl"}')
}

function New-SclEvents {
    param(
        [switch]$MissingTypeCapability,
        [switch]$DuplicateApply,
        [switch]$LowLevelImport,
        [switch]$SaveProject,
        [switch]$AlternativeContext,
        [switch]$DiscardFailedDraft,
        [switch]$DiscardSuccessful,
        [switch]$OmitTypeBlockType,
        [switch]$OmitTypeRequestedLanguage,
        [switch]$OmitWarningBudget
    )

    $events = [System.Collections.Generic.List[object]]::new()
    $events.Add((New-ToolEvent -Tool 'skill' -InputObject @{ name = 'plc-code-engineering' } -Output 'loaded'))
    $events.Add((New-ToolEvent -Tool 'skill' -InputObject @{ name = 'scl-source-authoring' } -Output 'loaded'))
    $events.Add((New-ToolEvent -Tool 'opentia_list-instances' -InputObject @{} -Output '{"count":1,"processId":13352}'))
    $events.Add((New-ToolEvent -Tool 'opentia_connect-instance' -InputObject @{ 'instance-id' = 13352 } -Output '{"connected":true}'))
    if ($AlternativeContext) {
        $events.Add((New-ToolEvent -Tool 'opentia_get-project-info' -InputObject @{} -Output '{"Name":"OTT_Benchmark"}'))
        $events.Add((New-ToolEvent -Tool 'opentia_list-plcs' -InputObject @{} -Output '{"Name":"PLC_Main"}'))
    }
    else {
        $events.Add((New-ToolEvent -Tool 'opentia_list-projects' -InputObject @{} -Output '{"Name":"OTT_Benchmark"}'))
        $events.Add((New-ToolEvent -Tool 'opentia_open-plc' -InputObject @{ 'plc-name' = 'PLC_Main' } -Output '{"opened":true}'))
    }
    if (-not $MissingTypeCapability) {
        $events.Add((New-CapabilityEvent -ObjectKind Type -BlockType TYPE -ObjectName 'OTT30_RecipeWindow' -Operation Create `
                    -OmitBlockType:$OmitTypeBlockType -OmitRequestedLanguage:$OmitTypeRequestedLanguage))
    }
    $events.Add((New-CapabilityEvent -ObjectKind Block -BlockType FB -ObjectName 'OTT30_RecipeWindowMonitor' -Operation Create))
    $events.Add((New-ToolEvent -Tool 'opentia_validate-plc-code-artifact' -InputObject @{
                'input-path' = 'C:\OTT\Bench\synthetic\workspace\artifacts\OTT30_RecipeWindowMonitor.scl'
                'code-format' = 'scl'
            } -Output '{"Valid":true,"Format":"SCL","Classification":"verified","HasUtf8Bom":true,"DeclaredObjects":[{"Name":"OTT30_RecipeWindow","ObjectType":"TYPE"},{"Name":"OTT30_RecipeWindowMonitor","ObjectType":"FB"}],"Errors":[],"Warnings":[]}'))
    if ($DiscardFailedDraft) {
        $events.Add((New-ToolEvent -Tool 'opentia_discard-plc-code-transaction' -InputObject @{ 'transaction-id' = 'failed-draft'; confirm = $true } -Output '{"Discarded":true,"PreviousState":"Draft"}'))
    }
    $beginInput = [ordered]@{
        'plc-name' = 'PLC_Main'
        'capability-ids' = 'type-id,fb-id'
        'compile-policy' = 'CleanCompile'
    }
    if (-not $OmitWarningBudget) { $beginInput['maximum-new-warnings'] = 0 }
    $maximumNewWarnings = if ($OmitWarningBudget) { 'null' } else { '0' }
    $events.Add((New-ToolEvent -Tool 'opentia_begin-plc-code-transaction' -InputObject $beginInput `
            -Output ('{"State":"Draft","CapabilityRoute":"DirectScl","CapabilityBindings":[{"ObjectName":"OTT30_RecipeWindow"},{"ObjectName":"OTT30_RecipeWindowMonitor"}],"CompilePolicy":"CleanCompile","MaximumNewWarnings":' + $maximumNewWarnings + '}')))
    $events.Add((New-ToolEvent -Tool 'opentia_add-scl-source-transaction-operation' -InputObject @{
                'transaction-id' = 'tx'; 'source-path' = 'C:\OTT\Bench\synthetic\workspace\artifacts\OTT30_RecipeWindowMonitor.scl'; confirm = $true
            } -Output '{"Kind":"GenerateSclSource","SourceName":"OTT30_RecipeWindowMonitor","Message":"Added guarded SCL source generation for 2 declared object(s)."}'))
    $events.Add((New-ToolEvent -Tool 'opentia_plan-plc-code-transaction' -InputObject @{ 'transaction-id' = 'tx' } -Output '{"State":"Planned","ApplyToken":"token","BaselineCompilation":{"ErrorCount":0,"WarningCount":0},"ImpactAnalysis":{"Status":"Safe"},"TargetSnapshots":[{"Target":{"Name":"OTT30_RecipeWindow","BlockType":"TYPE"},"Action":"Create","BaselineHash":"missing"},{"Target":{"Name":"OTT30_RecipeWindowMonitor","BlockType":"FB"},"Action":"Create","BaselineHash":"missing"}]}'))
    $apply = New-ToolEvent -Tool 'opentia_apply-plc-code-transaction' -InputObject @{
        'transaction-id' = 'tx'; 'apply-token' = 'token'; confirm = $true
    } -Output '{"State":"Succeeded","Applied":true,"Verified":true,"RollbackAttempted":false,"RecoveryRequired":false,"FinalCompilation":{"Success":true,"ErrorCount":0,"WarningCount":0},"TargetSnapshots":[{"Target":{"Name":"OTT30_RecipeWindow","BlockType":"TYPE"},"CanonicalResultHash":"846160c6f0eaf012a83eb211cb476c202625eb1c72c9585dba7216d0b1059d36"},{"Target":{"Name":"OTT30_RecipeWindowMonitor","BlockType":"FB"},"CanonicalResultHash":"596958bf182b01ec03b74d3399bd98ec8a098dace1b5dac8cddb8107be7158d4"}]}'
    $events.Add($apply)
    if ($DuplicateApply) { $events.Add($apply) }
    if ($DiscardSuccessful) {
        $events.Add((New-ToolEvent -Tool 'opentia_discard-plc-code-transaction' -InputObject @{ 'transaction-id' = 'tx'; confirm = $true } -Output '{"Discarded":true,"PreviousState":"Succeeded"}'))
    }
    if ($LowLevelImport) {
        $events.Add((New-ToolEvent -Tool 'opentia_import-source' -InputObject @{ path = 'candidate.scl' } -Output '{"imported":true}'))
    }
    $events.Add((New-ToolEvent -Tool 'opentia_get-block-info' -InputObject @{
                'plc-name' = 'PLC_Main'; 'block-name' = 'OTT30_RecipeWindowMonitor'
            } -Output '{"Name":"OTT30_RecipeWindowMonitor","Type":"FB","ProgrammingLanguage":"SCL"}'))
    $events.Add((New-ToolEvent -Tool 'opentia_export-block-as-source' -InputObject @{
                'plc-name' = 'PLC_Main'; 'block-name' = 'OTT30_RecipeWindowMonitor'; 'output-path' = 'artifacts/readback'
            } -Output '{"Success":true,"OutputPath":"artifacts/readback/OTT30_RecipeWindowMonitor.scl"}'))
    $events.Add((New-ToolEvent -Tool 'opentia_export-type' -InputObject @{
                'plc-name' = 'PLC_Main'; 'type-name' = 'OTT30_RecipeWindow'; 'export-path' = 'artifacts/readback/OTT30_RecipeWindow.xml'
            } -Output '{"message":"Successfully exported type","exportPath":"artifacts/readback/OTT30_RecipeWindow.xml"}'))
    $events.Add((New-CapabilityEvent -ObjectKind Type -BlockType TYPE -ObjectName 'OTT30_RecipeWindow' -Operation Modify `
                -OmitBlockType:$OmitTypeBlockType -OmitRequestedLanguage:$OmitTypeRequestedLanguage))
    $events.Add((New-CapabilityEvent -ObjectKind Block -BlockType FB -ObjectName 'OTT30_RecipeWindowMonitor' -Operation Modify))
    $events.Add((New-ToolEvent -Tool 'opentia_compile-software' -InputObject @{ 'plc-name' = 'PLC_Main' } -Output '{"message":"Compilation successful: 0 errors, 0 warnings","errorCount":0,"warningCount":0}'))
    if ($SaveProject) {
        $events.Add((New-ToolEvent -Tool 'opentia_save-project' -InputObject @{ confirm = $true } -Output '{"saved":true}'))
    }
    return @($events)
}

function Invoke-SclAssertionTest {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][object[]]$Events,
        [string]$Source = $representativeSource,
        [string]$FinalText = 'Created OTT30_RecipeWindow and OTT30_RecipeWindowMonitor through DirectScl. Transaction Succeeded with canonical verification and a final compile of 0 errors and 0 warnings. Project was not saved.',
        [switch]$WithoutBom,
        [bool]$ExpectedPassed = $true,
        [string]$ExpectedFailedAssertion
    )

    $workspacePath = Join-Path $testRoot "$Label-workspace"
    $artifactDirectory = Join-Path $workspacePath 'artifacts'
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $artifactDirectory 'OTT30_RecipeWindowMonitor.scl'),
        $Source,
        [System.Text.UTF8Encoding]::new(-not $WithoutBom)
    )
    $tracePath = Join-Path $testRoot "$Label-events.jsonl"
    $metadataPath = Join-Path $testRoot "$Label-metadata.json"
    $resultPath = Join-Path $testRoot "$Label-result.json"
    @($Events) + @(
        [ordered]@{ type = 'text'; part = [ordered]@{ text = $FinalText } },
        [ordered]@{ type = 'step_finish'; part = [ordered]@{
                reason = 'stop'; tokens = [ordered]@{ total = 10; input = 5; output = 5; reasoning = 0 }; cost = 0
            } }
    ) | ForEach-Object { $_ | ConvertTo-Json -Depth 60 -Compress } |
        Set-Content -LiteralPath $tracePath -Encoding UTF8

    [ordered]@{
        model = 'synthetic/model'; trialIndex = 1; reasoningEffort = 'high'; variant = 'high'
        openCodeVersion = '1.18.10'; gitCommit = '0000000'; gitBranch = 'test'; gitDirty = $false
        gitStateSha256 = ('0' * 64); fixtureSha256 = '1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74'
        startedAtUtc = '2026-09-05T00:00:00.0000000Z'; finishedAtUtc = '2026-09-05T00:00:01.0000000Z'
        durationSeconds = 1; exitCode = 0; timedOut = $false; workspacePath = $workspacePath
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath (Join-Path $benchmarkRoot 'cases\ott-30\case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative native-SCL trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative native-SCL trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

try {
    Invoke-SclAssertionTest -Label 'representative' -Events (New-SclEvents)
    Invoke-SclAssertionTest -Label 'equivalent-implementation' -Events (New-SclEvents -AlternativeContext -DiscardFailedDraft -OmitTypeBlockType -OmitTypeRequestedLanguage) `
        -Source $equivalentSource
    Invoke-SclAssertionTest -Label 'direct-output-state' -Events (New-SclEvents) -Source $directOutputStateSource
    Invoke-SclAssertionTest -Label 'limit-temporaries-output-case' -Events (New-SclEvents) -Source $limitAndOutputCaseSource
    Invoke-SclAssertionTest -Label 'parenthesized-active' -Events (New-SclEvents) -Source $parenthesizedActiveSource
    Invoke-SclAssertionTest -Label 'conjunctive-first-outside-guard' -Events (New-SclEvents) -Source $conjunctiveFirstOutsideSource
    Invoke-SclAssertionTest -Label 'typed-state-literals' -Events (New-SclEvents) -Source $typedLiteralSource
    Invoke-SclAssertionTest -Label 'nothing-saved-summary' -Events (New-SclEvents) `
        -FinalText 'Created OTT30_RecipeWindow and OTT30_RecipeWindowMonitor through DirectScl. Transaction Succeeded with canonical verification and a final compile of 0 errors and 0 warnings. Nothing saved.'
    Invoke-SclAssertionTest -Label 'active-only-while-monitoring' -Events (New-SclEvents) `
        -Source $monitoringOnlyActiveSource -ExpectedPassed $false -ExpectedFailedAssertion 'native-scl-artifact'
    Invoke-SclAssertionTest -Label 'missing-bom' -Events (New-SclEvents) -WithoutBom `
        -ExpectedPassed $false -ExpectedFailedAssertion 'native-scl-artifact'
    Invoke-SclAssertionTest -Label 'missing-absolute-tolerance' -Events (New-SclEvents) `
        -Source ($representativeSource.Replace('ABS(#Recipe.Tolerance[#Index])', '#Recipe.Tolerance[#Index]')) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'native-scl-artifact'
    Invoke-SclAssertionTest -Label 'missing-type-capability' -Events (New-SclEvents -MissingTypeCapability) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'type-create-capability'
    Invoke-SclAssertionTest -Label 'missing-warning-budget' -Events (New-SclEvents -OmitWarningBudget) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'two-target-clean-transaction'
    Invoke-SclAssertionTest -Label 'duplicate-apply' -Events (New-SclEvents -DuplicateApply) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'single-verified-apply'
    Invoke-SclAssertionTest -Label 'lower-level-import' -Events (New-SclEvents -LowLevelImport) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-bypass-probe-save-or-destructive-cleanup'
    Invoke-SclAssertionTest -Label 'project-saved' -Events (New-SclEvents -SaveProject) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-bypass-probe-save-or-destructive-cleanup'
    Invoke-SclAssertionTest -Label 'successful-transaction-discarded' -Events (New-SclEvents -DiscardSuccessful) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-bypass-probe-save-or-destructive-cleanup'

    Write-Host 'Native SCL authoring case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_NATIVE_SCL_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaNativeSclTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
