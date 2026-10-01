[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('OpenTiaLibraryTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

function New-ToolEvent {
    param([string]$Tool, [object]$InputObject, [string]$Output)
    return [ordered]@{ type = 'tool_use'; part = [ordered]@{
        tool = $Tool; state = [ordered]@{ status = 'completed'; input = $InputObject; output = $Output }
    } }
}

function New-CopyEvent {
    param([bool]$DryRun, [string]$Master = 'OTT32_Standards/Approved/PLC/Offset', [string]$Group = 'OTT32_Reuse')
    $inputObject = [ordered]@{
        'library-scope' = 'project'; 'master-copy-path' = $Master; 'plc-name' = 'PLC_Main'
        'target-group-path' = $Group; 'dry-run' = $DryRun
    }
    if (-not $DryRun) { $inputObject.confirm = $true }
    $output = [ordered]@{
        Success = $true; DryRun = $DryRun; Applied = (-not $DryRun)
        Message = if ($DryRun) { 'Dry-run successful.' } else { 'Copied master copy.' }
        CreatedName = if ($DryRun) { $null } else { 'OTT32_ApprovedOffset' }
        CreatedType = if ($DryRun) { $null } else { 'FC' }
    } | ConvertTo-Json -Depth 10 -Compress
    return New-ToolEvent 'opentia_copy-from-master-copy' $inputObject $output
}

function New-LibraryEvents {
    param([switch]$OmitPreview, [switch]$ReverseCopyOrder, [switch]$WrongMaster, [switch]$WrongGroup,
        [switch]$DuplicateCopy, [switch]$SaveProject, [switch]$ImportType)
    $events = [System.Collections.Generic.List[object]]::new()
    $events.Add((New-ToolEvent 'skill' @{ name = 'library-workflow' } 'loaded'))
    $events.Add((New-ToolEvent 'opentia_list-instances' @{} '{"count":1,"processId":1234}'))
    $events.Add((New-ToolEvent 'opentia_connect-instance' @{ 'instance-id' = 1234 } '{"connected":true}'))
    $events.Add((New-ToolEvent 'opentia_get-project-info' @{} '{"Name":"OTT_Benchmark"}'))
    $events.Add((New-ToolEvent 'opentia_open-plc' @{ 'plc-name' = 'PLC_Main' } '{"success":true}'))
    foreach ($command in @('list-master-copies', 'list-library-types', 'get-library-type-versions')) {
        $inputObject = @{ 'library-scope' = 'project' }
        if ($command -eq 'get-library-type-versions') { $inputObject['type-path'] = 'UDT/MyANALOG_LEVEL' }
        $events.Add((New-ToolEvent "opentia_$command" $inputObject '{"success":true,"data":{"OutputPath":"artifact.json"}}'))
    }
    $events.Add((New-ToolEvent 'opentia_check-library-updates' @{
        'library-scope' = 'project'; 'output-path' = 'C:/Bench/artifacts/updates-before.json'
    } 'Library update check found 1 message(s).'))
    $events.Add((New-ToolEvent 'opentia_get-software-tree' @{
        'plc-name' = 'PLC_Main'; 'output-path' = 'C:/Bench/artifacts/tree-before.json'
    } 'Software tree retrieved successfully.'))
    $master = if ($WrongMaster) { 'OTT32_Standards/Legacy/PLC/Offset' } else { 'OTT32_Standards/Approved/PLC/Offset' }
    $group = if ($WrongGroup) { '' } else { 'OTT32_Reuse' }
    if ($ReverseCopyOrder) { $events.Add((New-CopyEvent $false $master $group)) }
    if (-not $OmitPreview) { $events.Add((New-CopyEvent $true $master $group)) }
    if (-not $ReverseCopyOrder) { $events.Add((New-CopyEvent $false $master $group)) }
    if ($DuplicateCopy) { $events.Add((New-CopyEvent $false $master $group)) }
    $events.Add((New-ToolEvent 'opentia_get-software-tree' @{
        'plc-name' = 'PLC_Main'; 'output-path' = 'C:/Bench/artifacts/tree-after.json'
    } 'Software tree retrieved successfully.'))
    $events.Add((New-ToolEvent 'opentia_compile-software' @{ 'plc-name' = 'PLC_Main' } '{"message":"Compilation successful: 0 errors, 1 warnings","errorCount":0,"warningCount":1}'))
    $events.Add((New-ToolEvent 'opentia_export-block' @{
        'plc-name' = 'PLC_Main'; 'block-name' = 'OTT32_ApprovedOffset'; 'export-path' = 'C:/Bench/artifacts/readback/OTT32_ApprovedOffset.xml'
    } 'Successfully exported block: OTT32_ApprovedOffset'))
    $events.Add((New-ToolEvent 'opentia_check-library-updates' @{
        'library-scope' = 'project'; 'output-path' = 'C:/Bench/artifacts/updates-after.json'
    } 'Library update check found 1 message(s).'))
    if ($SaveProject) { $events.Add((New-ToolEvent 'opentia_save-project' @{ confirm = $true } '{"saved":true}')) }
    if ($ImportType) { $events.Add((New-ToolEvent 'opentia_import-type' @{ confirm = $true } '{"success":true}')) }
    return @($events)
}

function Get-BlockXml {
    param([string]$Name, [int]$Number, [int]$Constant, [switch]$Main)
    if ($Main) { return '<Document><SW.Blocks.OB><Name>Main</Name><Number>1</Number><ProgrammingLanguage>LAD</ProgrammingLanguage></SW.Blocks.OB></Document>' }
    return @"
<Document><SW.Blocks.FC><Name>$Name</Name><Number>$Number</Number><ProgrammingLanguage>SCL</ProgrammingLanguage>
<Member Name="Value" Datatype="DInt" /><Member Name="Result" Datatype="DInt" />
<Component Name="Result" /><Token Text=":=" /><Component Name="Value" /><Token Text="+" />
<ConstantValue>$Constant</ConstantValue></SW.Blocks.FC></Document>
"@
}

function Invoke-LibraryAssertionTest {
    param([string]$Label, [object[]]$Events, [string]$ExpectedFailedAssertion,
        [int]$ApprovedConstant = 10, [int]$LegacyConstant = 20, [int]$ExistingConstant = 30,
        [string]$ResultGroup = 'OTT32_Reuse', [string]$CurrentVersion = '0.0.3',
        [bool]$DefaultIsDefault = $true, [switch]$BaselineAlreadyContainsPayload, [switch]$ZeroWarningCompile,
        [int]$CompileErrors = 0, [int]$CompileWarnings = 1, [switch]$MarkdownSummary)
    $workspace = Join-Path $testRoot "$Label-workspace"
    $artifacts = Join-Path $workspace 'artifacts'
    $readback = Join-Path $artifacts 'readback'
    New-Item -ItemType Directory -Path $readback -Force | Out-Null
    @('Approved/PLC', 'Approved/HMI', 'Legacy/PLC') | ForEach-Object {
        @{ name = 'Offset'; path = "OTT32_Standards/$_/Offset" }
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifacts 'masters.json') -Encoding UTF8
    @(@{ name = 'MyANALOG_LEVEL'; path = 'UDT/MyANALOG_LEVEL'; versionCount = 2; status = 'NonDefaultVersionInstantiation' }) |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifacts 'types.json') -Encoding UTF8
    @(
        [ordered]@{ typeName = 'MyANALOG_LEVEL'; versionNumber = '0.0.3'; isDefault = $false; state = 'Committed' },
        [ordered]@{ typeName = 'MyANALOG_LEVEL'; versionNumber = '0.0.4'; isDefault = $DefaultIsDefault; state = 'Committed' }
    ) | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifacts 'versions.json') -Encoding UTF8
    foreach ($phase in @('before', 'after')) {
        $version = if ($phase -eq 'before') { '0.0.3' } else { $CurrentVersion }
        [ordered]@{
            success = $true; libraryScope = 'project'; messages = @(@{ messageParts = @{
                LibraryTypeName = 'MyANALOG_LEVEL'; LibraryVersionNumber = '0.0.4'; UpToDate = 'Out-of-date'
                PathToInstance = 'PLC_Main/PLC data types/MyANALOG_LEVEL'; CurrentVersionNumber = $version
            } })
        } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifacts "updates-$phase.json") -Encoding UTF8
        $children = @()
        if ($phase -eq 'after' -or $BaselineAlreadyContainsPayload) {
            $children = @(@{ name = 'OTT32_ApprovedOffset'; path = "PLC_Main/Blocks/$ResultGroup/OTT32_ApprovedOffset"; children = @() })
        }
        @{ tree = @{ children = @(
            @{ name = 'Main'; path = 'PLC_Main/Blocks/Main'; children = @() },
            @{ name = 'OTT32_ApprovedOffset_Legacy'; path = 'PLC_Main/Blocks/OTT32_ApprovedOffset_Legacy'; children = @() },
            @{ name = 'OTT32_ExistingOffset'; path = 'PLC_Main/Blocks/OTT32_ExistingOffset'; children = @() },
            [ordered]@{ name = 'OTT32_Reuse'; path = 'PLC_Main/Blocks/OTT32_Reuse'; children = $children }
        ) } } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $artifacts "tree-$phase.json") -Encoding UTF8
    }
    Get-BlockXml 'OTT32_ApprovedOffset' 1 $ApprovedConstant | Set-Content -LiteralPath (Join-Path $readback 'OTT32_ApprovedOffset.xml') -Encoding UTF8
    Get-BlockXml 'OTT32_ApprovedOffset_Legacy' 2 $LegacyConstant | Set-Content -LiteralPath (Join-Path $readback 'OTT32_ApprovedOffset_Legacy.xml') -Encoding UTF8
    Get-BlockXml 'OTT32_ExistingOffset' 3 $ExistingConstant | Set-Content -LiteralPath (Join-Path $readback 'OTT32_ExistingOffset.xml') -Encoding UTF8
    Get-BlockXml -Main | Set-Content -LiteralPath (Join-Path $readback 'Main.xml') -Encoding UTF8
    '<Document><SW.Types.PlcStruct><Name>MyANALOG_LEVEL</Name></SW.Types.PlcStruct></Document>' |
        Set-Content -LiteralPath (Join-Path $readback 'MyANALOG_LEVEL.xml') -Encoding UTF8
    if ($ZeroWarningCompile -or $CompileErrors -ne 0 -or $CompileWarnings -ne 1) {
        if ($ZeroWarningCompile) { $CompileWarnings = 0 }
        foreach ($event in $Events) {
            if ($event.part.tool -eq 'opentia_compile-software') {
                $event.part.state.output = (@{ message = "Compilation successful: $CompileErrors errors, $CompileWarnings warnings"
                    errorCount = $CompileErrors; warningCount = $CompileWarnings } | ConvertTo-Json -Compress)
            }
        }
    }
    $tracePath = Join-Path $testRoot "$Label-events.jsonl"
    $metadataPath = Join-Path $testRoot "$Label-metadata.json"
    $summaryText = 'Copied OTT32_Standards/Approved/PLC/Offset as OTT32_ApprovedOffset into OTT32_Reuse. MyANALOG_LEVEL retained 0.0.3, default 0.0.4, outdated and not upgraded. Existing objects preserved; project not saved.'
    if ($MarkdownSummary) { $summaryText = $summaryText.Replace('not saved', '**not** saved') }
    @($Events) + @(
        @{ type = 'text'; part = @{ text = $summaryText } },
        @{ type = 'step_finish'; part = @{ reason = 'stop'; tokens = @{ total = 10; input = 5; output = 5; reasoning = 0 }; cost = 0 } }
    ) | ForEach-Object { $_ | ConvertTo-Json -Depth 60 -Compress } | Set-Content -LiteralPath $tracePath -Encoding UTF8
    @{
        model = 'synthetic/model'; trialIndex = 1; reasoningEffort = 'high'; variant = 'high'
        openCodeVersion = '1.18.10'; gitCommit = '0000000'; gitBranch = 'test'; gitDirty = $false
        gitStateSha256 = ('0' * 64); fixtureSha256 = 'B749366ECD7CFD0564FEC06881526373BF0DBE14F9BE9ECCD1043D0071166749'
        startedAtUtc = '2026-10-01T00:00:00Z'; finishedAtUtc = '2026-10-01T00:00:01Z'
        durationSeconds = 1; exitCode = 0; timedOut = $false; workspacePath = $workspace
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') -CasePath (Join-Path $benchmarkRoot 'cases\ott-32\case.json') `
        -TracePath $tracePath -MetadataPath $metadataPath -OutputPath (Join-Path $testRoot "$Label-result.json")
    $failed = @($result.assertions | Where-Object { -not [bool]$_.passed })
    if ([string]::IsNullOrWhiteSpace($ExpectedFailedAssertion)) {
        if (-not [bool]$result.passed -or [double]$result.score -ne 100) {
            throw "Representative library trace failed ($Label): score=$($result.score); failed=$($failed | ConvertTo-Json -Depth 10 -Compress)"
        }
    }
    elseif ([bool]$result.passed -or @($failed | ForEach-Object { [string]$_.id }) -notcontains $ExpectedFailedAssertion) {
        throw "Negative library trace was not rejected by '$ExpectedFailedAssertion': $Label; score=$($result.score)"
    }
}

try {
    Invoke-LibraryAssertionTest 'representative' (New-LibraryEvents)
    Invoke-LibraryAssertionTest 'incremental-zero-warnings' (New-LibraryEvents) -ZeroWarningCompile
    Invoke-LibraryAssertionTest 'markdown-summary' (New-LibraryEvents) -MarkdownSummary
    Invoke-LibraryAssertionTest 'missing-preview' (New-LibraryEvents -OmitPreview) 'approved-copy-preview'
    Invoke-LibraryAssertionTest 'apply-before-preview' (New-LibraryEvents -ReverseCopyOrder) 'reviewed-preview-before-apply'
    Invoke-LibraryAssertionTest 'wrong-master' (New-LibraryEvents -WrongMaster) 'approved-copy-apply'
    Invoke-LibraryAssertionTest 'wrong-destination' (New-LibraryEvents -WrongGroup) 'approved-copy-apply'
    Invoke-LibraryAssertionTest 'repeated-copy' (New-LibraryEvents -DuplicateCopy) 'only-reviewed-copy-pair'
    Invoke-LibraryAssertionTest 'legacy-payload' (New-LibraryEvents) 'approved-payload-xml-readback' -ApprovedConstant 20
    Invoke-LibraryAssertionTest 'wrong-group-readback' (New-LibraryEvents) 'exact-grouped-result' -ResultGroup 'Other'
    Invoke-LibraryAssertionTest 'preexisting-payload' (New-LibraryEvents) 'baseline-empty-destination' -BaselineAlreadyContainsPayload
    Invoke-LibraryAssertionTest 'instance-upgraded' (New-LibraryEvents) 'outdated-instance-retained' -CurrentVersion '0.0.4'
    Invoke-LibraryAssertionTest 'wrong-default' (New-LibraryEvents) 'committed-default-version-evidence' -DefaultIsDefault $false
    Invoke-LibraryAssertionTest 'legacy-changed' (New-LibraryEvents) 'legacy-preserved' -LegacyConstant 10
    Invoke-LibraryAssertionTest 'existing-changed' (New-LibraryEvents) 'existing-preserved' -ExistingConstant 20
    Invoke-LibraryAssertionTest 'unauthorized-type-import' (New-LibraryEvents -ImportType) 'no-other-mutations-or-save'
    Invoke-LibraryAssertionTest 'project-saved' (New-LibraryEvents -SaveProject) 'no-other-mutations-or-save'
    Invoke-LibraryAssertionTest 'compile-error' (New-LibraryEvents) 'error-free-compile' -CompileErrors 1
    Invoke-LibraryAssertionTest 'additional-compile-warnings' (New-LibraryEvents) 'error-free-compile' -CompileWarnings 10
    Write-Host 'Library workflow assertion tests passed (3 positive traces and 16 negative controls).'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaLibraryTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
