[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenTiaVciSyncTests-" + [Guid]::NewGuid().ToString('N'))
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

function New-StatusEvent {
    param(
        [Parameter(Mandatory = $true)][string]$BlockName,
        [Parameter(Mandatory = $true)][ValidateSet('Equal', 'Unequal')][string]$Status
    )

    return New-ToolEvent -Tool 'opentia_get-vci-block-status' -InputObject @{
        'workspace-name' = 'OTT31_Workspace'
        'plc-name' = 'PLC_Main'
        'block-name' = $BlockName
        'block-type' = 'FC'
    } -Output ('{"ObjectName":"' + $BlockName + '","ObjectType":"FC","Success":true,"Status":"' + $Status + '"}')
}

function New-MappingEvent {
    $output = '{"message":"Found 2 VCI mapping(s).","data":{"items":[' +
        '{"WorkspaceName":"OTT31_Workspace","ObjectName":"OTT31_ProjectSource","ObjectType":"FC","FileNameWithoutExtension":"OTT31_ProjectSource","FileFormat":"SCL","Status":"Equal"},' +
        '{"WorkspaceName":"OTT31_Workspace","ObjectName":"OTT31_WorkspaceTarget","ObjectType":"FC","FileNameWithoutExtension":"OTT31_WorkspaceTarget","FileFormat":"SCL","Status":"Equal"}' +
        '],"totalCount":2}}'
    return New-ToolEvent -Tool 'opentia_list-vci-mappings' -InputObject @{
        'workspace-name' = 'OTT31_Workspace'; 'page-size' = 100
    } -Output $output
}

function New-FormatEvent {
    param([Parameter(Mandatory = $true)][string]$BlockName)
    return New-ToolEvent -Tool 'opentia_get-vci-block-formats' -InputObject @{
        'plc-name' = 'PLC_Main'; 'block-name' = $BlockName; 'block-type' = 'FC'
    } -Output '{"message":"Found 3 supported VCI format(s).","data":["SCL","SimaticML","SimaticSD"]}'
}

function New-ExportMappingEvent {
    param([Parameter(Mandatory = $true)][string]$BlockName)
    return New-ToolEvent -Tool 'opentia_export-vci-block' -InputObject @{
        'workspace-name' = 'OTT31_Workspace'; 'plc-name' = 'PLC_Main'; 'block-name' = $BlockName
        'block-type' = 'FC'; 'relative-directory' = '.'; 'file-name' = $BlockName; 'file-format' = 'SCL'
    } -Output ('{"message":"Exported and mapped VCI block: ' + $BlockName + '","data":{"ObjectName":"' +
        $BlockName + '","MappingCreated":true,"FileFormat":"SCL","Status":"Equal"}}')
}

function New-VciEvents {
    param(
        [switch]$FormatBeforeWorkspace,
        [switch]$OmitValidation,
        [switch]$ValidateBaselineInstead,
        [switch]$WrongProjectDirection,
        [switch]$BroadSync,
        [switch]$MapDistractor,
        [switch]$SaveProject
    )

    $events = [System.Collections.Generic.List[object]]::new()
    $events.Add((New-ToolEvent -Tool 'skill' -InputObject @{ name = 'vci-sync-workflow' } -Output 'loaded'))
    $events.Add((New-ToolEvent -Tool 'skill' -InputObject @{ name = 'plc-code-engineering' } -Output 'loaded'))
    $events.Add((New-ToolEvent -Tool 'opentia_list-instances' -InputObject @{} -Output '{"count":1,"processId":1234}'))
    $events.Add((New-ToolEvent -Tool 'opentia_connect-instance' -InputObject @{ 'process-id' = 1234 } -Output '{"connected":true}'))
    $events.Add((New-ToolEvent -Tool 'opentia_get-project-info' -InputObject @{} -Output '{"Name":"OTT_Benchmark"}'))
    $events.Add((New-ToolEvent -Tool 'opentia_list-plcs' -InputObject @{} -Output '[{"name":"PLC_Main"}]'))
    $events.Add((New-ToolEvent -Tool 'opentia_list-vci-workspaces' -InputObject @{ 'page-size' = 100 } -Output '{"message":"Found 0 VCI workspace(s).","data":{"items":[],"totalCount":0}}'))

    if ($FormatBeforeWorkspace) {
        $events.Add((New-FormatEvent -BlockName 'OTT31_ProjectSource'))
        $events.Add((New-FormatEvent -BlockName 'OTT31_WorkspaceTarget'))
    }

    $events.Add((New-ToolEvent -Tool 'opentia_create-vci-workspace' -InputObject @{
                'workspace-name' = 'OTT31_Workspace'; 'root-path' = 'C:\Bench\artifacts\vci-repo'
                'workspace-language' = 'en-US'; confirm = $true
            } -Output '{"message":"Created VCI workspace: OTT31_Workspace","data":{"Name":"OTT31_Workspace","MappingCount":0}}'))

    if (-not $FormatBeforeWorkspace) {
        $events.Add((New-FormatEvent -BlockName 'OTT31_ProjectSource'))
        $events.Add((New-FormatEvent -BlockName 'OTT31_WorkspaceTarget'))
    }

    $events.Add((New-ExportMappingEvent -BlockName 'OTT31_ProjectSource'))
    $events.Add((New-ExportMappingEvent -BlockName 'OTT31_WorkspaceTarget'))
    if ($MapDistractor) {
        $events.Add((New-ExportMappingEvent -BlockName 'OTT31_Distractor'))
    }
    $events.Add((New-MappingEvent))
    $events.Add((New-ToolEvent -Tool 'opentia_discover-plc-code-capabilities' -InputObject @{
                'plc-name' = 'PLC_Main'; 'object-name' = 'OTT31_WorkspaceTarget'; 'object-kind' = 'Block'
                'block-type' = 'FC'; operation = 'Modify'; 'requested-format' = 'VCI'
                'requested-language' = 'SCL'; 'prefer-source-control' = $true
            } -Output '{"Target":{"ExactIdentityResolved":true},"Vci":{"WorkspaceName":"OTT31_Workspace","ExactMappingFound":true,"MappingFormat":"SCL","MappingStatus":"Equal"},"SelectedRoute":"VCI"}'))

    $events.Add((New-StatusEvent -BlockName 'OTT31_ProjectSource' -Status 'Unequal'))
    $events.Add((New-StatusEvent -BlockName 'OTT31_WorkspaceTarget' -Status 'Unequal'))
    if (-not $OmitValidation) {
        $validationPath = if ($ValidateBaselineInstead) {
            'C:\Bench\artifacts\baseline\OTT31_WorkspaceTarget.scl'
        }
        else {
            'C:\Bench\artifacts\vci-repo\OTT31_WorkspaceTarget.scl'
        }
        $events.Add((New-ToolEvent -Tool 'opentia_validate-plc-code-artifact' -InputObject @{
                    'input-path' = $validationPath; 'code-format' = 'SCL'
                } -Output '{"Valid":true,"Format":"SCL","Classification":"verified","HasUtf8Bom":true,"DeclaredObjects":[{"Name":"OTT31_WorkspaceTarget","ObjectType":"FC"}],"Errors":[],"Warnings":[]}'))
    }

    $projectDirection = if ($WrongProjectDirection) { 'workspace-to-project' } else { 'project-to-workspace' }
    $events.Add((New-ToolEvent -Tool 'opentia_sync-vci-block' -InputObject @{
                'workspace-name' = 'OTT31_Workspace'; 'plc-name' = 'PLC_Main'; 'block-name' = 'OTT31_ProjectSource'
                'block-type' = 'FC'; direction = $projectDirection; 'create-mapping' = $false; confirm = $true
            } -Output '{"message":"Synchronized.","data":{"ObjectName":"OTT31_ProjectSource","Success":true,"MappingCreated":false,"Status":"Equal"}}'))
    $events.Add((New-StatusEvent -BlockName 'OTT31_ProjectSource' -Status 'Equal'))

    $events.Add((New-ToolEvent -Tool 'opentia_sync-vci-block' -InputObject @{
                'workspace-name' = 'OTT31_Workspace'; 'plc-name' = 'PLC_Main'; 'block-name' = 'OTT31_WorkspaceTarget'
                'block-type' = 'FC'; direction = 'workspace-to-project'; 'create-mapping' = $false; confirm = $true
            } -Output '{"message":"Synchronized.","data":{"ObjectName":"OTT31_WorkspaceTarget","Success":true,"MappingCreated":false,"Status":"Equal"}}'))
    $events.Add((New-StatusEvent -BlockName 'OTT31_WorkspaceTarget' -Status 'Equal'))
    $events.Add((New-ToolEvent -Tool 'opentia_compile-software' -InputObject @{
                'plc-name' = 'PLC_Main'
            } -Output '{"message":"Compilation successful: 0 errors, 0 warnings","errorCount":0,"warningCount":0}'))
    $events.Add((New-ToolEvent -Tool 'opentia_export-block-as-source' -InputObject @{
                'plc-name' = 'PLC_Main'; 'block-name' = 'OTT31_WorkspaceTarget'
                'output-path' = 'C:\Bench\artifacts\readback\OTT31_WorkspaceTarget.scl'
            } -Output '{"Success":true,"SourceName":"OTT31_WorkspaceTarget","OutputPath":"OTT31_WorkspaceTarget.scl"}'))
    $events.Add((New-ToolEvent -Tool 'opentia_export-block-as-source' -InputObject @{
                'plc-name' = 'PLC_Main'; 'block-name' = 'OTT31_Distractor'
                'output-path' = 'C:\Bench\artifacts\readback\OTT31_Distractor.scl'
            } -Output '{"Success":true,"SourceName":"OTT31_Distractor","OutputPath":"OTT31_Distractor.scl"}'))
    $events.Add((New-MappingEvent))

    if ($BroadSync) {
        $events.Add((New-ToolEvent -Tool 'opentia_sync-vci-blocks' -InputObject @{
                    'workspace-name' = 'OTT31_Workspace'; 'plc-name' = 'PLC_Main'; direction = 'project-to-workspace'
                } -Output '{"success":true}'))
    }
    if ($SaveProject) {
        $events.Add((New-ToolEvent -Tool 'opentia_save-project' -InputObject @{ confirm = $true } -Output '{"saved":true}'))
    }
    return @($events)
}

function Get-SclSource {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][int]$Constant)
    return @"
FUNCTION "$Name" : Void
{ S7_Optimized_Access := 'TRUE' }
VERSION : 0.1
   VAR_INPUT
      Value : DInt;
   END_VAR
   VAR_OUTPUT
      Result : DInt;
   END_VAR
BEGIN
    #Result := #Value + $Constant;
END_FUNCTION
"@
}

function Invoke-VciAssertionTest {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][object[]]$Events,
        [int]$BaselineConstant = 20,
        [int]$TargetReadbackConstant = 25,
        [bool]$ExpectedPassed = $true,
        [string]$ExpectedFailedAssertion
    )

    $workspacePath = Join-Path $testRoot "$Label-workspace"
    $vciDirectory = Join-Path $workspacePath 'artifacts\vci-repo'
    $baselineDirectory = Join-Path $workspacePath 'artifacts\baseline'
    $readbackDirectory = Join-Path $workspacePath 'artifacts\readback'
    New-Item -ItemType Directory -Path $vciDirectory, $baselineDirectory, $readbackDirectory -Force | Out-Null
    $utf8Bom = [System.Text.UTF8Encoding]::new($true)
    [System.IO.File]::WriteAllText((Join-Path $vciDirectory 'OTT31_ProjectSource.scl'), (Get-SclSource -Name 'OTT31_ProjectSource' -Constant 10), $utf8Bom)
    [System.IO.File]::WriteAllText((Join-Path $vciDirectory 'OTT31_WorkspaceTarget.scl'), (Get-SclSource -Name 'OTT31_WorkspaceTarget' -Constant 25), $utf8Bom)
    [System.IO.File]::WriteAllText((Join-Path $baselineDirectory 'OTT31_WorkspaceTarget.scl'), (Get-SclSource -Name 'OTT31_WorkspaceTarget' -Constant $BaselineConstant), $utf8Bom)
    [System.IO.File]::WriteAllText((Join-Path $readbackDirectory 'OTT31_WorkspaceTarget.scl'), (Get-SclSource -Name 'OTT31_WorkspaceTarget' -Constant $TargetReadbackConstant), $utf8Bom)
    [System.IO.File]::WriteAllText((Join-Path $readbackDirectory 'OTT31_Distractor.scl'), (Get-SclSource -Name 'OTT31_Distractor' -Constant 30), $utf8Bom)

    $tracePath = Join-Path $testRoot "$Label-events.jsonl"
    $metadataPath = Join-Path $testRoot "$Label-metadata.json"
    $resultPath = Join-Path $testRoot "$Label-result.json"
    @($Events) + @(
        [ordered]@{ type = 'text'; part = [ordered]@{
                text = 'VCI reconciliation completed for OTT31_ProjectSource project-to-workspace and OTT31_WorkspaceTarget workspace-to-project. OTT31_Distractor stayed at 30; final mappings are Equal on the selected VCI route; compile was 0 errors and 0 warnings; project not saved.'
            } },
        [ordered]@{ type = 'step_finish'; part = [ordered]@{
                reason = 'stop'; tokens = [ordered]@{ total = 10; input = 5; output = 5; reasoning = 0 }; cost = 0
            } }
    ) | ForEach-Object { $_ | ConvertTo-Json -Depth 60 -Compress } |
        Set-Content -LiteralPath $tracePath -Encoding UTF8

    [ordered]@{
        model = 'synthetic/model'; trialIndex = 1; reasoningEffort = 'high'; variant = 'high'
        openCodeVersion = '1.18.10'; gitCommit = '0000000'; gitBranch = 'test'; gitDirty = $false
        gitStateSha256 = ('0' * 64); fixtureSha256 = 'B058929F708DB5A5781428238D04B627C4611A0F15C40CE46D4CCC742E76C773'
        startedAtUtc = '2026-09-30T00:00:00.0000000Z'; finishedAtUtc = '2026-09-30T00:00:01.0000000Z'
        durationSeconds = 1; exitCode = 0; timedOut = $false; workspacePath = $workspacePath
    } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $result = & (Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1') `
        -CasePath (Join-Path $benchmarkRoot 'cases\ott-31\case.json') `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath

    if ($ExpectedPassed -and (-not [bool]$result.passed -or [double]$result.score -ne 100)) {
        $failed = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { "$($_.id) [$($_.details)]" }) -join ', '
        throw "Representative VCI trace failed: score=$($result.score); assertions=$failed"
    }
    if (-not $ExpectedPassed) {
        $failedIds = @($result.assertions | Where-Object { -not [bool]$_.passed } |
                ForEach-Object { [string]$_.id })
        if ([bool]$result.passed -or $failedIds -notcontains $ExpectedFailedAssertion) {
            throw "Negative VCI trace was not rejected by '$ExpectedFailedAssertion': passed=$($result.passed); failed=$($failedIds -join ', ')"
        }
    }
}

try {
    Invoke-VciAssertionTest -Label 'representative' -Events (New-VciEvents)
    Invoke-VciAssertionTest -Label 'format-before-workspace' -Events (New-VciEvents -FormatBeforeWorkspace) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'workspace-before-format-discovery'
    Invoke-VciAssertionTest -Label 'missing-validation' -Events (New-VciEvents -OmitValidation) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'workspace-target-validated'
    Invoke-VciAssertionTest -Label 'baseline-validated-instead' -Events (New-VciEvents -ValidateBaselineInstead) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'mapped-target-validation-bound-to-import'
    Invoke-VciAssertionTest -Label 'wrong-project-direction' -Events (New-VciEvents -WrongProjectDirection) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'exact-project-to-workspace-sync'
    Invoke-VciAssertionTest -Label 'broad-sync' -Events (New-VciEvents -BroadSync) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-broad-bypass-or-save'
    Invoke-VciAssertionTest -Label 'mapped-distractor' -Events (New-VciEvents -MapDistractor) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-distractor-mapping'
    Invoke-VciAssertionTest -Label 'stale-baseline' -Events (New-VciEvents) -BaselineConstant 25 `
        -ExpectedPassed $false -ExpectedFailedAssertion 'workspace-target-baseline-retained'
    Invoke-VciAssertionTest -Label 'stale-readback' -Events (New-VciEvents) -TargetReadbackConstant 20 `
        -ExpectedPassed $false -ExpectedFailedAssertion 'workspace-target-project-readback'
    Invoke-VciAssertionTest -Label 'project-saved' -Events (New-VciEvents -SaveProject) `
        -ExpectedPassed $false -ExpectedFailedAssertion 'no-broad-bypass-or-save'

    Write-Host 'VCI synchronization case assertion tests passed.'
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
    if ($env:OTT_KEEP_VCI_TEST_ARTIFACTS -ne '1' -and
        $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTest).StartsWith('OpenTiaVciSyncTests-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force -ErrorAction SilentlyContinue
    }
}
