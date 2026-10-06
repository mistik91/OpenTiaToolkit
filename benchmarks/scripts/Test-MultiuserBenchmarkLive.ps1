[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ContextPath)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'MultiuserBenchmark.ps1')
$context=Get-Content -LiteralPath $ContextPath -Raw | ConvertFrom-Json
if($context.schemaVersion -ne 1 -or $context.scope -cne 'identity-bound-local-save-ui-handoff' -or $context.checkpoint -cne 'prepared'){throw 'Use a newly prepared, unedited identity-bound fixture.'}
[void](Assert-MuOwnedPortal $context.agentPid $context.agentSid)
Assert-MuWorkerRegistration $context.workerPath
if((Get-FileHash -LiteralPath $context.workerPath).Hash -cne $context.workerSha256){throw 'Qualified worker changed.'}
$case=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../cases/ott-37/case.json') -Raw | ConvertFrom-Json
$assertion=@($case.assertions | Where-Object {$_.type -ceq 'multiuserLocalSave'})[0]
$contractPath=Resolve-BenchmarkRelativePath -Root $context.workspacePath -RelativePath $assertion.relativePath -WorkspaceInput
if((Get-FileHash -LiteralPath $contractPath).Hash -ine $assertion.contractSha256){throw 'Acceptance binding differs.'}
$contract=Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
[void](Test-MuNativeSnapshot $context $context.baseline $contract)
$commands=@(
    (New-MuCommand 'connect-instance' @{'instance-id'=$context.agentPid}),
    (New-MuCommand 'get-project-info' @{} @{'data.Name'=$context.projectName;'data.Path'=$context.projectPath;'data.PortalProcessId'=$context.agentPid}),
    (New-MuCommand 'get-multiuser-session-status' @{} @{'data.IsMultiuser'=$true;'data.IsOpen'=$true;'data.IsUpToDate'=$true;'data.MarkingCount'=0;'data.ConflictCount'=0;'data.Markings'=@()}),
    (New-MuCommand 'list-plcs')
)
foreach($phase in @('baseline','readback')){
    if($phase -eq 'readback'){
        $commands+=New-MuCommand 'modify-tags' @{'plc-name'=$contract.targetPlc;'dry-run'=$true;operations=@($contract.operation)} @{'data.DryRun'=$true;'data.Summary.Total'=1;'data.Summary.Planned'=1;'data.Summary.Applied'=0;'data.Summary.Failed'=0}
        $commands+=New-MuCommand 'modify-tags' @{'plc-name'=$contract.targetPlc;'dry-run'=$false;confirm=$true;operations=@($contract.operation)} @{'data.DryRun'=$false;'data.Summary.Total'=1;'data.Summary.Planned'=0;'data.Summary.Applied'=1;'data.Summary.Failed'=0}
        foreach($plc in $contract.plcs){$commands+=New-MuCommand 'compile-software' @{'plc-name'=$plc.name} @{'data.success'=$true;'data.errorCount'=0;'data.warningCount'=0}}
        $commands+=New-MuCommand 'save-multiuser-session' @{confirm=$true} @{'data.IsUpToDate'=$true;'data.MarkingCount'=1;'data.ConflictCount'=0}
        $commands+=New-MuCommand 'get-multiuser-session-status' @{} @{'data.IsUpToDate'=$true;'data.MarkingCount'=1;'data.ConflictCount'=0}
    }
    $directory=Join-Path $context.workspacePath ('artifacts/'+$phase)
    foreach($plc in $contract.plcs){
        $commands+=New-MuCommand 'export-tags' @{'plc-name'=$plc.name;'output-path'=(Join-Path $directory ($plc.prefix+'-tags.csv'))}
        $commands+=New-MuCommand 'export-block' @{'plc-name'=$plc.name;'block-name'='Main';'export-path'=(Join-Path $directory ($plc.prefix+'-main.xml'))}
    }
}
$directory=Invoke-MuProbe $context 'golden-run' $commands
$context=Get-MultiuserBenchmarkReadback $context $contractPath
if($context.checkpoint -cne 'awaiting-ui-check-in'){throw ('Golden readback failed: '+$context.readbackFailure)}
$records=Get-Content -LiteralPath (Join-Path $directory 'journal.jsonl') | ForEach-Object {$_ | ConvertFrom-Json} | Where-Object {$_.request.method -ceq 'tools/call'}
$events=@(foreach($record in $records){
    [pscustomobject]@{part=[pscustomobject]@{tool=('opentia_'+$record.request.params.name);state=[pscustomobject]@{status='completed';input=$record.request.params.arguments;output=$record.response.result.structuredContent}}}
})
$detail=Test-BenchmarkMultiuserLocalSave $events ([pscustomobject]@{multiuser=$context}) $context.workspacePath $assertion
[pscustomobject]@{succeeded=$true;modelInference=$false;detail=$detail;projectName=$context.projectName;sessionPath=$context.sessionPath;checkpoint=$context.checkpoint} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $context.evidenceRoot 'golden-validation.json') -Encoding UTF8
Write-Host $detail
Write-Host "No model was used. Saved handoff context: $(Join-Path $context.evidenceRoot 'handoff.json')"
