[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaS7dclDataTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$documents = @{}
$documents['baseline/OTT33_RecipeSettings.s7dcl'] = @'
{
    S7_BlockNumber := "58033";
    S7_Optimized := "FALSE";
    S7_StandardRetain := "FALSE";
    S7_Version := "0.1"
}
DATA_BLOCK OTT33_RecipeSettings
    VAR
        Enabled : Bool;
        Mode : USInt;
        RecipeId : DInt;
        Target : Real;
        RetryLimit : USInt;
        "Label" : String[16];
        Samples : Array[2..5] of Real;
        Limits : STRUCT
            Low : Real;
            High : Real;
        END_STRUCT;
        RestartCount : DInt;
    END_VAR

    Enabled := FALSE;
    Mode := 1;
    RecipeId := 7001;
    Target := 42.5;
    RetryLimit := 3;
    "Label" := 'Baseline';
    RestartCount := 17;

END_DATA_BLOCK
'@
$documents['baseline/OTT33_RecipeSettings_Backup.s7dcl'] = @'
{
    S7_BlockNumber := "58035";
    S7_Optimized := "FALSE";
    S7_StandardRetain := "FALSE";
    S7_Version := "0.1"
}
DATA_BLOCK OTT33_RecipeSettings_Backup
    VAR
        Enabled : Bool;
        Mode : USInt;
        RecipeId : DInt;
        Target : Real;
        RetryLimit : USInt;
    END_VAR

    Enabled := FALSE;
    Mode := 9;
    RecipeId := 9909;
    Target := -99.0;
    RetryLimit := 8;

END_DATA_BLOCK
'@
$documents['baseline/OTT33_ReadRecipe.s7dcl'] = @'
{
    S7_EditorMode := "SCL";
    S7_Optimized := "TRUE";
    S7_Version := "0.1"
}
FUNCTION "OTT33_ReadRecipe" : Void
    VAR_OUTPUT
        Enabled : Bool;
        Mode : USInt;
        Target : Real;
    END_VAR

    { S7_Language := "SCL" }
    NETWORK

            #Enabled := "OTT33_RecipeSettings".Enabled;
            #Mode := "OTT33_RecipeSettings".Mode;
            #Target := "OTT33_RecipeSettings".Target;

    END_NETWORK
END_FUNCTION
'@
$documents['baseline/Main.s7dcl'] = @'
{
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_Version := "0.1"
}
ORGANIZATION_BLOCK "Main"
    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
        END_RUNG
    END_NETWORK
END_ORGANIZATION_BLOCK
'@
$documents['readback/OTT33_RecipeSettings.s7dcl'] = @'
{
    S7_BlockNumber := "58033";
    S7_Optimized := "FALSE";
    S7_StandardRetain := "FALSE";
    S7_Version := "0.1"
}
DATA_BLOCK OTT33_RecipeSettings
    VAR
        Enabled : Bool;
        Mode : USInt;
        RecipeId : DInt;
        Target : Real;
        RetryLimit : USInt;
        "Label" : String[16];
        Samples : Array[2..5] of Real;
        Limits : STRUCT
            Low : Real;
            High : Real;
        END_STRUCT;
        RestartCount : DInt;
    END_VAR

    Enabled := TRUE;
    Mode := 2;
    RecipeId := 7001;
    Target := 63.5;
    RetryLimit := 5;
    "Label" := 'Baseline';
    RestartCount := 17;

END_DATA_BLOCK
'@
$documents['readback/OTT33_RecipeSettings_Backup.s7dcl'] = @'
{
    S7_BlockNumber := "58035";
    S7_Optimized := "FALSE";
    S7_StandardRetain := "FALSE";
    S7_Version := "0.1"
}
DATA_BLOCK OTT33_RecipeSettings_Backup
    VAR
        Enabled : Bool;
        Mode : USInt;
        RecipeId : DInt;
        Target : Real;
        RetryLimit : USInt;
    END_VAR

    Enabled := FALSE;
    Mode := 9;
    RecipeId := 9909;
    Target := -99.0;
    RetryLimit := 8;

END_DATA_BLOCK
'@
$documents['readback/OTT33_ReadRecipe.s7dcl'] = @'
{
    S7_EditorMode := "SCL";
    S7_Optimized := "TRUE";
    S7_Version := "0.1"
}
FUNCTION "OTT33_ReadRecipe" : Void
    VAR_OUTPUT
        Enabled : Bool;
        Mode : USInt;
        Target : Real;
    END_VAR

    { S7_Language := "SCL" }
    NETWORK

            #Enabled := "OTT33_RecipeSettings".Enabled;
            #Mode := "OTT33_RecipeSettings".Mode;
            #Target := "OTT33_RecipeSettings".Target;

    END_NETWORK
END_FUNCTION
'@
$documents['readback/Main.s7dcl'] = @'
{
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_Version := "0.1"
}
ORGANIZATION_BLOCK "Main"
    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
        END_RUNG
    END_NETWORK
END_ORGANIZATION_BLOCK
'@
$documents['readback/OTT33_QualitySnapshot.s7dcl'] = @'
{
    S7_BlockNumber := "58034";
    S7_Optimized := "TRUE";
    S7_StandardRetain := "FALSE";
    S7_Version := "0.1"
}
DATA_BLOCK OTT33_QualitySnapshot
    VAR
        Valid : Bool;
        "Sequence" : UDInt;
        Batch : _.OTT33_BatchInfo;
        Samples : Array[1..4] of Real;
        Envelope : STRUCT
            Limits : STRUCT
                Lower : Real;
                Upper : Real;
            END_STRUCT;
            Quality : STRUCT
                Good : Bool;
                FaultCode : UInt;
            END_STRUCT;
        END_STRUCT;
        Status : String[24];
        Mode : USInt;
    END_VAR

    Valid := FALSE;
    "Sequence" := 0;
    Status := 'Awaiting sample';
    Mode := 3;

END_DATA_BLOCK
'@
$typeXml = @'
<Document><SW.Types.PlcStruct ID="0"><AttributeList><Interface><Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5"><Section Name="None"><Member Name="BatchId" Datatype="DInt" /><Member Name="ProductCode" Datatype="USInt" /><Member Name="Accepted" Datatype="Bool" /></Section></Sections></Interface><Name>OTT33_BatchInfo</Name><Namespace /></AttributeList><ObjectList><MultilingualText ID="1" CompositionName="Comment"><ObjectList><MultilingualTextItem ID="2" CompositionName="Items"><AttributeList><Culture>en-US</Culture><Text /></AttributeList></MultilingualTextItem></ObjectList></MultilingualText><MultilingualText ID="3" CompositionName="Title"><ObjectList><MultilingualTextItem ID="4" CompositionName="Items"><AttributeList><Culture>en-US</Culture><Text /></AttributeList></MultilingualTextItem></ObjectList></MultilingualText></ObjectList></SW.Types.PlcStruct></Document>
'@

function New-ToolEvent {
    param([string]$Name, [object]$InputObject, [object]$Output)
    if ($Output -isnot [string]) { $Output = $Output | ConvertTo-Json -Depth 60 -Compress }
    return @{ type = 'tool_use'; part = @{ tool = $Name; state = @{ status = 'completed'; input = $InputObject; output = $Output } } }
}
function New-DataEvents {
    param([string]$Variant = '')
    $events = [Collections.Generic.List[object]]::new()
    $events.Add((New-ToolEvent 'skill' @{ name = 's7dcl-data-authoring' } 'loaded'))
    $events.Add((New-ToolEvent 'opentia_list-instances' @{} 'one instance'))
    $events.Add((New-ToolEvent 'opentia_connect-instance' @{ 'instance-id' = 1234 } 'connected'))
    $events.Add((New-ToolEvent 'opentia_get-project-info' @{} 'OTT_Benchmark'))
    $events.Add((New-ToolEvent 'opentia_open-plc' @{ 'plc-name' = 'PLC_Main' } 'opened'))
    foreach ($name in @('OTT33_RecipeSettings', 'OTT33_QualitySnapshot')) {
        $exists = $name -eq 'OTT33_RecipeSettings'
        $events.Add((New-ToolEvent 'opentia_discover-plc-code-capabilities' @{
            'plc-name' = 'PLC_Main'; 'object-name' = $name; operation = $(if ($exists) { 'Modify' } else { 'Create' }); 'requested-format' = 'S7DCL'
        } @{ Exists = $exists; ExactIdentityResolved = $true; SelectedRoute = 'DirectDocuments' }))
    }
    foreach ($name in @('OTT33_RecipeSettings', 'OTT33_RecipeSettings_Backup', 'OTT33_ReadRecipe', 'Main')) {
        $events.Add((New-ToolEvent 'opentia_export-block-as-docs' @{ 'plc-name' = 'PLC_Main'; 'block-name' = $name; 'export-path' = 'C:/Bench/artifacts/baseline' } 'Successfully exported block as SIMATIC SD documents'))
    }
    if ($Variant -ne 'no-consumer-check') { $events.Add((New-ToolEvent 'opentia_get-cross-references' @{ 'plc-name' = 'PLC_Main'; name = 'OTT33_RecipeSettings' } 'Found 3 cross-reference(s)')) }
    foreach ($name in @('OTT33_RecipeSettings', 'OTT33_QualitySnapshot')) {
        if ($Variant -eq 'unvalidated-new-db' -and $name -eq 'OTT33_QualitySnapshot') { continue }
        $events.Add((New-ToolEvent 'opentia_validate-plc-documents' @{ 'file-name' = $name } @{ Valid = $true; HasUtf8Bom = $true; Classification = 'verified'; Errors = @() }))
    }
    $events.Add((New-ToolEvent 'opentia_begin-plc-code-transaction' @{ 'plc-name' = 'PLC_Main'; 'compile-policy' = 'CleanCompile'; 'maximum-new-warnings' = 0 } @{
        State = 'Draft'; CapabilityRoute = 'DirectDocuments'; Targets = @('OTT33_RecipeSettings', 'OTT33_QualitySnapshot')
    }))
    $events.Add((New-ToolEvent 'opentia_add-plc-code-transaction-operation' @{
        'file-name' = 'OTT33_RecipeSettings'; 'target-mode' = $(if ($Variant -eq 'unguarded-existing') { 'create-only' } else { 'baseline-bound' })
        'allowed-changes' = $(if ($Variant -eq 'widened-contract') { 'Interface,Implementation' } else { 'Implementation' }); confirm = $true
    } @{ TargetMode = 'BaselineBound'; ChangedSections = @('Implementation') }))
    $events.Add((New-ToolEvent 'opentia_add-plc-code-transaction-operation' @{ 'file-name' = 'OTT33_QualitySnapshot'; 'target-mode' = 'create-only'; confirm = $true } @{ TargetMode = 'CreateOnly' }))
    if ($Variant -ne 'no-plan') { $events.Add((New-ToolEvent 'opentia_plan-plc-code-transaction' @{} @{
        State = 'Planned'; Status = 'Safe'; ChangeKinds = @(@{ ChangeKind = 'BodyOnly' }, @{ ChangeKind = 'Create' }); ApplyToken = 'one-use'
    })) }
    $apply = New-ToolEvent 'opentia_apply-plc-code-transaction' @{ confirm = $true } @{
        State = $(if ($Variant -eq 'rolled-back') { 'RolledBackAndVerified' } else { 'Succeeded' })
        Applied = ($Variant -ne 'rolled-back'); Verified = ($Variant -ne 'rolled-back'); RollbackAttempted = ($Variant -eq 'rolled-back'); RecoveryRequired = $false
        FinalCompilation = [ordered]@{ Success = $true; ErrorCount = 0; WarningCount = 0 }
        Operations = @([ordered]@{ Name = 'OTT33_RecipeSettings'; CanonicalResultHash = ('a' * 64) }, [ordered]@{ Name = 'OTT33_QualitySnapshot'; CanonicalResultHash = ('b' * 64) })
    }
    $events.Add($apply)
    if ($Variant -eq 'duplicate-apply') { $events.Add($apply) }
    foreach ($name in @('OTT33_RecipeSettings', 'OTT33_QualitySnapshot', 'OTT33_RecipeSettings_Backup', 'OTT33_ReadRecipe', 'Main')) {
        $events.Add((New-ToolEvent 'opentia_export-block-as-docs' @{ 'plc-name' = 'PLC_Main'; 'block-name' = $name; 'export-path' = 'C:/Bench/artifacts/readback' } 'Successfully exported block as SIMATIC SD documents'))
    }
    $events.Add((New-ToolEvent 'opentia_compile-software' @{ 'plc-name' = 'PLC_Main' } @{
        message = 'Compilation successful'; errorCount = 0; warningCount = $(if ($Variant -eq 'compile-warning') { 1 } else { 0 })
    }))
    if ($Variant -eq 'raw-import') { $events.Add((New-ToolEvent 'opentia_import-block-from-docs' @{ confirm = $true } 'success')) }
    if ($Variant -eq 'save') { $events.Add((New-ToolEvent 'opentia_save-project' @{ confirm = $true } 'saved')) }
    if ($Variant -eq 'extra-create') { $events.Add((New-ToolEvent 'opentia_create-device' @{ confirm = $true; name = 'Unrequested' } 'created')) }
    if ($Variant -eq 'find-replace') { $events.Add((New-ToolEvent 'opentia_find-replace-in-block' @{ confirm = $true } 'changed')) }
    return @($events)
}
function Invoke-DataAssertionTest {
    param([string]$Label, [string]$ExpectedFailure = '', [string]$Variant = '', [string]$ChangedFile = '',
        [string]$From = '', [string]$To = '', [switch]$WithoutBom, [switch]$ChangedType, [switch]$UseCrLf)
    $workspace = Join-Path $testRoot ($Label + '-workspace')
    foreach ($key in $documents.Keys) {
        $path = Join-Path (Join-Path $workspace 'artifacts') $key
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        $content = $documents[$key].Replace("`r`n", "`n")
        if ($key -eq $ChangedFile -and $From.Length -gt 0) { $content = $content.Replace($From, $To) }
        if ($UseCrLf -and $key -eq $ChangedFile) { $content = $content.Replace("`r`n", "`n").Replace("`n", "`r`n") }
        [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new(-not ($WithoutBom -and $key -eq $ChangedFile)))
    }
    foreach ($phase in @('baseline', 'readback')) {
        $content = $typeXml
        if ($ChangedType -and $phase -eq 'readback') { $content = $content.Replace('Datatype="DInt"', 'Datatype="Int"') }
        [IO.File]::WriteAllText((Join-Path $workspace "artifacts/$phase/OTT33_BatchInfo.xml"), $content, [Text.UTF8Encoding]::new($true))
    }
    $trace = Join-Path $testRoot ($Label + '-events.jsonl')
    $metadata = Join-Path $testRoot ($Label + '-metadata.json')
    $summary = 'OTT33_RecipeSettings and OTT33_QualitySnapshot applied via DirectDocuments. Succeeded; project not saved.'
    if ($Variant -eq 'nothing-saved') { $summary = $summary.Replace('project not saved', 'nothing saved/closed/deleted') }
    @(New-DataEvents $Variant) + @(
        @{ type = 'text'; part = @{ text = $summary } },
        @{ type = 'step_finish'; part = @{ reason = 'stop'; tokens = @{ input = 5; output = 5; reasoning = 0; total = 10 }; cost = 0 } }
    ) | ForEach-Object { $_ | ConvertTo-Json -Depth 60 -Compress } | Set-Content -LiteralPath $trace -Encoding UTF8
    @{
        model = 'synthetic/model'; trialIndex = 1; reasoningEffort = 'high'; variant = 'high'
        openCodeVersion = '1.18.10'; gitCommit = '0000000'; gitBranch = 'test'; gitDirty = $false
        gitStateSha256 = ('0' * 64); fixtureSha256 = 'A550A21C719511E7F019E4FBEEC1413EB8238762E8E38D9547EECF3E0B67D02E'
        startedAtUtc = '2026-10-01T00:00:00Z'; finishedAtUtc = '2026-10-01T00:00:01Z'
        durationSeconds = 1; exitCode = 0; timedOut = $false; workspacePath = $workspace
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadata -Encoding UTF8
    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath (Join-Path $benchmarkRoot 'cases/ott-33/case.json') -TracePath $trace -MetadataPath $metadata -OutputPath (Join-Path $testRoot ($Label + '-result.json'))
    $failed = @($result.assertions | Where-Object { -not $_.passed } | ForEach-Object { $_.id })
    if (-not $ExpectedFailure) {
        if (-not $result.passed -or $result.score -ne 100) { throw "Positive data trace failed: $($result.score); $($failed -join ', ')" }
    }
    elseif ($result.passed -or $failed -notcontains $ExpectedFailure) { throw "Negative '$Label' escaped '$ExpectedFailure': $($failed -join ', ')" }
}
try {
    Invoke-DataAssertionTest 'representative'
    Invoke-DataAssertionTest 'line-ending-equivalence' -ChangedFile 'readback/OTT33_QualitySnapshot.s7dcl' -UseCrLf
    Invoke-DataAssertionTest 'nothing-saved-summary' -Variant 'nothing-saved'
    Invoke-DataAssertionTest 'changed-unrequested-value' 'recipe-exact-readback' -ChangedFile 'readback/OTT33_RecipeSettings.s7dcl' -From 'RecipeId := 7001;' -To 'RecipeId := 7002;'
    Invoke-DataAssertionTest 'changed-string' 'recipe-exact-readback' -ChangedFile 'readback/OTT33_RecipeSettings.s7dcl' -From "'Baseline'" -To "'Changed'"
    Invoke-DataAssertionTest 'string-case-matters' 'recipe-exact-readback' -ChangedFile 'readback/OTT33_RecipeSettings.s7dcl' -From "'Baseline'" -To "'baseline'"
    Invoke-DataAssertionTest 'string-whitespace-matters' 'structured-db-exact-readback' -ChangedFile 'readback/OTT33_QualitySnapshot.s7dcl' -From "'Awaiting sample'" -To "'Awaiting  sample'"
    Invoke-DataAssertionTest 'changed-identity' 'recipe-exact-readback' -ChangedFile 'readback/OTT33_RecipeSettings.s7dcl' -From '"58033"' -To '"58036"'
    Invoke-DataAssertionTest 'changed-optimization' 'recipe-exact-readback' -ChangedFile 'readback/OTT33_RecipeSettings.s7dcl' -From 'S7_Optimized := "FALSE"' -To 'S7_Optimized := "TRUE"'
    Invoke-DataAssertionTest 'changed-retain' 'recipe-exact-readback' -ChangedFile 'readback/OTT33_RecipeSettings.s7dcl' -From 'S7_StandardRetain := "FALSE"' -To 'S7_StandardRetain := "TRUE"'
    Invoke-DataAssertionTest 'changed-array-bound' 'structured-db-exact-readback' -ChangedFile 'readback/OTT33_QualitySnapshot.s7dcl' -From 'Array[1..4]' -To 'Array[0..3]'
    Invoke-DataAssertionTest 'flattened-layout' 'structured-db-exact-readback' -ChangedFile 'readback/OTT33_QualitySnapshot.s7dcl' -From 'Limits : STRUCT' -To 'Limits : Real'
    Invoke-DataAssertionTest 'wrong-project-type' 'structured-db-exact-readback' -ChangedFile 'readback/OTT33_QualitySnapshot.s7dcl' -From '_.OTT33_BatchInfo' -To '_.OtherType'
    Invoke-DataAssertionTest 'changed-backup' 'backup-preserved' -ChangedFile 'readback/OTT33_RecipeSettings_Backup.s7dcl' -From 'Mode := 9;' -To 'Mode := 2;'
    Invoke-DataAssertionTest 'changed-consumer' 'consumer-preserved' -ChangedFile 'readback/OTT33_ReadRecipe.s7dcl' -From '.Target;' -To '.RecipeId;'
    Invoke-DataAssertionTest 'changed-main' 'main-preserved' -ChangedFile 'readback/Main.s7dcl' -From 'wire#powerrail' -To 'wire#other'
    Invoke-DataAssertionTest 'changed-udt' 'existing-udt-readback' -ChangedType
    Invoke-DataAssertionTest 'overwritten-baseline' 'untouched-recipe-baseline' -ChangedFile 'baseline/OTT33_RecipeSettings.s7dcl' -From 'Target := 42.5;' -To 'Target := 63.5;'
    Invoke-DataAssertionTest 'missing-bom' 'structured-db-exact-readback' -ChangedFile 'readback/OTT33_QualitySnapshot.s7dcl' -WithoutBom
    Invoke-DataAssertionTest 'no-consumer-check' 'existing-consumer-check' -Variant 'no-consumer-check'
    Invoke-DataAssertionTest 'unguarded-existing' 'baseline-bound-initializations' -Variant 'unguarded-existing'
    Invoke-DataAssertionTest 'widened-contract' 'baseline-bound-initializations' -Variant 'widened-contract'
    Invoke-DataAssertionTest 'unvalidated-new-db' 'verified-new-document' -Variant 'unvalidated-new-db'
    Invoke-DataAssertionTest 'no-plan' 'single-safe-plan' -Variant 'no-plan'
    Invoke-DataAssertionTest 'rolled-back' 'single-verified-apply' -Variant 'rolled-back'
    Invoke-DataAssertionTest 'duplicate-apply' 'single-verified-apply' -Variant 'duplicate-apply'
    Invoke-DataAssertionTest 'compile-warning' 'fresh-clean-compile' -Variant 'compile-warning'
    Invoke-DataAssertionTest 'raw-import' 'no-bypass-save-or-unrelated-mutation' -Variant 'raw-import'
    Invoke-DataAssertionTest 'save' 'no-bypass-save-or-unrelated-mutation' -Variant 'save'
    Invoke-DataAssertionTest 'extra-create' 'no-bypass-save-or-unrelated-mutation' -Variant 'extra-create'
    Invoke-DataAssertionTest 'find-replace' 'no-bypass-save-or-unrelated-mutation' -Variant 'find-replace'
    Write-Host 'S7DCL data assertions passed (3 positive traces and 28 negative controls).'
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaS7dclDataTests-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
