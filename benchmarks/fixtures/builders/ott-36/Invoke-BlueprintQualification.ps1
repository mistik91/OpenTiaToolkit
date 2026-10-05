[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [Parameter(Mandatory=$true)][string]$RestoreRoot,
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [Parameter(Mandatory=$true)][string]$McpPath,
    [Parameter(Mandatory=$true)][string]$WorkerPath,
    [switch]$BaselineOnly,
    [switch]$CheckContinuation,
    [ValidateRange(30,600)][int]$TimeoutSeconds=180
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($BaselineOnly -and $CheckContinuation){throw 'Select either baseline health or continuation qualification, not both.'}
$archive=(Resolve-Path -LiteralPath $ArchivePath).Path
$worker=Get-Item -LiteralPath (Resolve-Path -LiteralPath $WorkerPath).Path
$workerHash=[Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($worker.FullName)))
$workerDate=$worker.LastWriteTimeUtc.ToString('yyyy/MM/dd HH:mm:ss.fff')
$registrations=@(Get-ChildItem -LiteralPath 'HKLM:/SOFTWARE/Siemens/Automation/Openness/21.0/Whitelist/OpenTiaToolkit.Worker.V21.exe' | Get-ItemProperty)
if(@($registrations | Where-Object {$_.Path -eq $worker.FullName -and $_.DateModified -eq $workerDate -and $_.FileHash -eq $workerHash}).Count -eq 0){throw 'Exact worker path/date/hash is not registered with Siemens V21. Register it before live qualification.'}
$restore=[IO.Path]::GetFullPath($RestoreRoot)
$output=[IO.Path]::GetFullPath($OutputRoot)
if((Test-Path -LiteralPath $restore) -or (Test-Path -LiteralPath $output)){throw 'Qualification roots must be new; evidence is append-only.'}
$archiveHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if($archiveHash -cne '1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74'){throw 'Unexpected baseline archive.'}
New-Item -ItemType Directory -Path $output | Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$journal=Join-Path $output 'mcp.jsonl'
$info=[Diagnostics.ProcessStartInfo]::new()
$info.FileName=(Resolve-Path -LiteralPath $McpPath).Path
$info.WorkingDirectory=Split-Path -Parent $info.FileName
$info.UseShellExecute=$false
$info.CreateNoWindow=$true
$info.RedirectStandardInput=$true
$info.RedirectStandardOutput=$true
$info.RedirectStandardError=$true
$info.EnvironmentVariables['OPEN_TIA_WORKER_V21_EXE']=$worker.FullName
$info.EnvironmentVariables['OPEN_TIA_MCP_LOG_LEVEL']='None'
$info.EnvironmentVariables['OPEN_TIA_MCP_ALLOWED_ROOTS']=(@($restore,$output,(Split-Path -Parent $archive),(Join-Path ([IO.Path]::GetTempPath()) 'OpenTiaToolkit')) | Select-Object -Unique) -join ';'
$process=[Diagnostics.Process]::new()
$process.StartInfo=$info
[void]$process.Start()
$stderr=$process.StandardError.ReadToEndAsync()
$requestId=0
$retainSession=$false
$projectOpen=$false
$portalOpen=$false
$tokens=[Collections.Generic.List[string]]::new()

function Write-Json {
    param([string]$Path,[object]$Value)
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 100),$utf8)
}
function Invoke-Request {
    param([string]$Method,[object]$Parameters)
    $script:requestId++
    $request=@{jsonrpc='2.0';id=$script:requestId;method=$Method;params=$Parameters}
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Depth 50 -Compress))
    $process.StandardInput.Flush()
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while([DateTime]::UtcNow -lt $deadline){
        $read=$process.StandardOutput.ReadLineAsync()
        if(-not $read.Wait([Math]::Max(1,[int]($deadline-[DateTime]::UtcNow).TotalMilliseconds))){
            $script:retainSession=$true
            throw "MCP timeout; session retained at PID $($process.Id). Do not retry an unknown operation."
        }
        if($null -eq $read.Result){throw 'MCP exited before responding.'}
        $response=$read.Result | ConvertFrom-Json
        if($null -ne $response.PSObject.Properties['id'] -and [int]$response.id -eq $script:requestId){
            $serialized=(@{request=$request;response=$response} | ConvertTo-Json -Depth 100 -Compress)
            # Tokens are used only in memory. Persist a deterministic opaque marker, never the token itself.
            if($Method -eq 'tools/call' -and $Parameters.name -eq 'plan-blueprint' -and $response.result.structuredContent.success){
                $script:tokens.Add([string]$response.result.structuredContent.data.applyToken)
            }
            foreach($token in $tokens){
                $hash=[BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($token))).Replace('-','').ToLowerInvariant()
                $serialized=$serialized.Replace($token,"[TOKEN:$hash]")
            }
            [IO.File]::AppendAllText($journal,$serialized+"`n",$utf8)
            return $response
        }
    }
    throw 'Response deadline expired.'
}
function Invoke-Tool {
    param([string]$Name,[hashtable]$Arguments=@{},[switch]$AllowFailure)
    Write-Host ("{0:o} {1}" -f [DateTime]::UtcNow,$Name)
    $response=Invoke-Request 'tools/call' @{name=$Name;arguments=$Arguments}
    if($null -ne $response.PSObject.Properties['error']){throw ($response.error | ConvertTo-Json -Compress)}
    $result=$response.result.structuredContent
    if($result.success -ne $true -and -not $AllowFailure){throw "$Name failed: $($result.message)"}
    return $result
}
function New-TagArguments {
    param([string]$Name,[string]$Address,[string]$Comment)
    return @{'plc-name'='PLC_Main';'dry-run'=$true;operations=@(@{
        action='update';match=@{tagTable='Default tag table';name=$Name;logicalAddress=$Address};set=@{comment=$Comment}
    })}
}
function Get-State {
    param([string]$Phase)
    $folder=Join-Path $output $Phase
    New-Item -ItemType Directory -Path $folder | Out-Null
    [void](Invoke-Tool 'export-tags' @{'plc-name'='PLC_Main';'output-path'=(Join-Path $folder 'tags.csv')})
    $snapshot=Invoke-Tool 'get-hmi-screen-json' @{'hmi-name'='HMI_RT_1';'screen-path'='Overview';'item-names'=@('StartButton');properties=@('Text','Left','Top','Width','Height');'working-path'=$folder}
    Write-Json (Join-Path $folder 'screen-envelope.json') $snapshot
    return @{tags=@(Import-Csv -LiteralPath (Join-Path $folder 'tags.csv'));hmi=$snapshot.data}
}
function New-Blueprint {
    param([string]$Name,[object[]]$Steps)
    $path=Join-Path $output ($Name+'.json')
    Write-Json $path @{schemaVersion='1.0';name=$Name;requiredWorker='V21';steps=$Steps}
    [void](Invoke-Tool 'validate-blueprint' @{'blueprint-path'=$path})
    return (Invoke-Tool 'plan-blueprint' @{'blueprint-path'=$path}).data
}
function Get-MainHash {
    param([string]$Path)
    $settings=[Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    try {
        $document=[Xml.XmlDocument]::new()
        $document.XmlResolver=$null
        $document.Load($reader)
        # Only the export's document-level metadata is volatile. Preserve all block leaves and IDs.
        foreach($metadata in @($document.SelectNodes('/Document/DocumentInfo'))){[void]$metadata.ParentNode.RemoveChild($metadata)}
        return [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($document.OuterXml))).Replace('-','').ToLowerInvariant()
    }
    finally{$reader.Dispose()}
}
try {
    [void](Invoke-Request 'initialize' @{protocolVersion='2024-11-05';capabilities=@{};clientInfo=@{name='OTT36 independent blueprint qualification';version='1.0'}})
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
    $process.StandardInput.Flush()
    [void](Invoke-Tool 'list-instances')
    [void](Invoke-Tool 'open-tia-portal' @{'with-ui'=$false})
    $portalOpen=$true
    [void](Invoke-Tool 'retrieve-project' @{'archive-path'=$archive;'target-directory'=$restore;confirm=$true})
    $projectOpen=$true
    $identity=(Invoke-Tool 'get-project-info').data
    $projectPath=[IO.Path]::GetFullPath([string]$identity.Path)
    if(-not $projectPath.StartsWith($restore.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $identity.IsToolkitOwned){throw 'Disposable ownership not proven.'}
    [void](Invoke-Tool 'open-plc' @{'plc-name'='PLC_Main'})
    [void](Invoke-Tool 'open-unified-hmi' @{'hmi-name'='HMI_RT_1'})
    $baseline=Get-State 'baseline'
    [void](Invoke-Tool 'export-block' @{'plc-name'='PLC_Main';'block-name'='Main';'export-path'=(Join-Path $output 'Main-before.xml')})
    $compile=(Invoke-Tool 'compile-software' @{'plc-name'='PLC_Main'}).data
    if($compile.errorCount -ne 0 -or $compile.warningCount -ne 0){throw 'Baseline compile is not clean.'}
    if($BaselineOnly){
        if($baseline.tags.Count -ne 2 -or
            @($baseline.tags | Where-Object {$_.Name -ceq 'OccupiedBefore' -and $_.DataType -ceq 'Bool' -and $_.LogicalAddress -ceq '%M99.7' -and $_.Comment -ceq 'Benchmark boundary tag'}).Count -ne 1 -or
            @($baseline.tags | Where-Object {$_.Name -ceq 'OccupiedAfter' -and $_.DataType -ceq 'DInt' -and $_.LogicalAddress -ceq '%MD108' -and $_.Comment -ceq 'Benchmark boundary tag'}).Count -ne 1){throw 'Fresh fixture tags differ from the immutable baseline.'}
        $items=@($baseline.hmi.Screen.Items)
        if($items.Count -ne 1 -or $items[0].Name -cne 'StartButton' -or $items[0].Type -cne 'HmiButton' -or
            $items[0].Properties.Text -cne 'Start' -or $items[0].Properties.Left -cne '40' -or $items[0].Properties.Top -cne '40' -or
            $items[0].Properties.Width -cne '160' -or $items[0].Properties.Height -cne '48'){throw 'Fresh HMI fixture differs from the immutable baseline.'}
        Write-Json (Join-Path $output 'qualification.json') @{status='PASS_HEALTH_ONLY';archiveHash=$archiveHash;project=$identity;tagCount=$baseline.tags.Count;baselineHash=$baseline.hmi.baseHash;compilation=$compile}
        return
    }
    $tagArgs=New-TagArguments 'OccupiedBefore' '%M99.7' 'Operator label: Run'
    [void](Invoke-Tool 'modify-tags' $tagArgs)
    $stale=New-Blueprint 'OTT36_Stale' @(@{id='label-metadata';command='modify-tags';arguments=$tagArgs;continueOnError=$false})
    [void](Invoke-Tool 'close-project' @{save=$false;confirm=$true})
    $projectOpen=$false
    $rejected=Invoke-Tool 'apply-blueprint' @{'apply-token'=$stale.applyToken;confirm=$true} -AllowFailure
    if($rejected.success -or $rejected.message -notmatch 'stale|state changed'){throw 'A stale context was not rejected.'}
    [void](Invoke-Tool 'open-project' @{'project-path'=$projectPath})
    $projectOpen=$true
    [void](Invoke-Tool 'open-plc' @{'plc-name'='PLC_Main'})
    [void](Invoke-Tool 'open-unified-hmi' @{'hmi-name'='HMI_RT_1'})
    $afterStale=Get-State 'after-stale'
    if(($baseline.tags | ConvertTo-Json -Depth 30 -Compress) -cne ($afterStale.tags | ConvertTo-Json -Depth 30 -Compress) -or $baseline.hmi.baseHash -cne $afterStale.hmi.baseHash){throw 'Stale rejection changed state.'}
    $prefixArgs=New-TagArguments 'OccupiedBefore' '%M99.7' 'Pending operator label'
    [void](Invoke-Tool 'modify-tags' $prefixArgs)
    $blockedHmi=@{'hmi-name'='HMI_RT_1';'screen-path'='Overview';'base-hash'=([string]$afterStale.hmi.baseHash);'item-names'=@('StartButton');properties=@('Text','Left','Top','Width','Height');'dry-run'=$true;backup=$true;operations=@(@{op='set-property';item='StartButton';property='Text';value='Run'})}
    # A fixed non-baseline hash creates a reversible guard rejection without unsafe parameters or writes.
    $blockedHmi['base-hash']='0'*64
    $guardPreview=Invoke-Tool 'apply-hmi-screen-json-patch' $blockedHmi
    Write-Json (Join-Path $output 'guard-preview.json') $guardPreview
    if(-not $guardPreview.data.SafetyBlocked -or $guardPreview.data.Applied){throw 'The control did not prove a non-mutating HMI safety block.'}
    $failure=New-Blueprint 'OTT36_StopOnFailure' @(
        @{id='prefix-metadata';command='modify-tags';arguments=$prefixArgs;continueOnError=$false},
        @{id='blocked-label';command='apply-hmi-screen-json-patch';arguments=$blockedHmi;continueOnError=$false},
        @{id='must-not-run';command='modify-tags';arguments=(New-TagArguments 'OccupiedAfter' '%MD108' 'Unexpected later step');continueOnError=$false}
    )
    $partial=Invoke-Tool 'apply-blueprint' @{'apply-token'=$failure.applyToken;confirm=$true} -AllowFailure
    Write-Json (Join-Path $output 'stop-on-failure-result.json') $partial
    $afterFailure=Get-State 'after-failure'
    if($partial.success -or $partial.data.applied -or -not $partial.data.stateMatched -or @($partial.data.steps).Count -ne 2 -or
        -not $partial.data.steps[0].success -or $partial.data.steps[1].success -or
        -not $partial.data.steps[1].TransportSuccess -or $partial.data.steps[1].ErrorCode -cne 'PolicyDenied' -or
        -not $partial.data.steps[1].Data.SafetyBlocked -or $partial.data.steps[1].Data.Applied){throw 'Stop-on-failure prefix or retained safety-block evidence is not accurately reported.'}
    $expectedPartialTags=$baseline.tags | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    @($expectedPartialTags | Where-Object {$_.Name -ceq 'OccupiedBefore'})[0].Comment='Pending operator label'
    if(($expectedPartialTags | ConvertTo-Json -Depth 30 -Compress) -cne ($afterFailure.tags | ConvertTo-Json -Depth 30 -Compress) -or
        $baseline.hmi.baseHash -cne $afterFailure.hmi.baseHash){throw 'Successful prefix, HMI preservation, or skipped suffix is unproven.'}
    $replay=Invoke-Tool 'apply-blueprint' @{'apply-token'=$failure.applyToken;confirm=$true} -AllowFailure
    if($replay.success -or $replay.message -notmatch 'consumed|invalid'){throw 'Failed apply token replay was accepted.'}
    if($CheckContinuation){
        $continuation=New-Blueprint 'OTT36_IndependentContinuation' @(
            @{id='prefix-metadata';command='modify-tags';arguments=$prefixArgs;continueOnError=$false},
            @{id='blocked-label';command='apply-hmi-screen-json-patch';arguments=$blockedHmi;continueOnError=$true},
            @{id='independent-suffix';command='modify-tags';arguments=(New-TagArguments 'OccupiedAfter' '%MD108' 'Explicit independent continuation');continueOnError=$false}
        )
        $continued=Invoke-Tool 'apply-blueprint' @{'apply-token'=$continuation.applyToken;confirm=$true} -AllowFailure
        Write-Json (Join-Path $output 'continuation-result.json') $continued
        $continuedState=Get-State 'after-continuation'
        $expectedContinuedTags=$expectedPartialTags | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        @($expectedContinuedTags | Where-Object {$_.Name -ceq 'OccupiedAfter'})[0].Comment='Explicit independent continuation'
        if($continued.success -or $continued.data.Applied -or -not $continued.data.StateMatched -or @($continued.data.Steps).Count -ne 3 -or
            -not $continued.data.Steps[0].Success -or $continued.data.Steps[1].Success -or -not $continued.data.Steps[2].Success -or
            -not $continued.data.Steps[1].TransportSuccess -or -not $continued.data.Steps[1].Data.SafetyBlocked -or
            $continued.data.Steps[1].Data.Applied -or
            ($expectedContinuedTags | ConvertTo-Json -Depth 30 -Compress) -cne ($continuedState.tags | ConvertTo-Json -Depth 30 -Compress) -or
            $continuedState.hmi.BaseHash -cne $baseline.hmi.BaseHash){throw 'Explicit continuation is not accurately reported or independently verified.'}
        $replay=Invoke-Tool 'apply-blueprint' @{'apply-token'=$continuation.applyToken;confirm=$true} -AllowFailure
        if($replay.success -or $replay.message -notmatch 'consumed|invalid'){throw 'Continuation token replay was accepted.'}
        [void](Invoke-Tool 'export-block' @{'plc-name'='PLC_Main';'block-name'='Main';'export-path'=(Join-Path $output 'Main-after.xml')})
        if((Get-MainHash (Join-Path $output 'Main-before.xml')) -cne (Get-MainHash (Join-Path $output 'Main-after.xml'))){throw 'Unrelated Main block changed during continuation.'}
        $compile=(Invoke-Tool 'compile-software' @{'plc-name'='PLC_Main'}).data
        if($compile.errorCount -ne 0 -or $compile.warningCount -ne 0){throw 'Final continuation compile is not clean.'}
        Write-Json (Join-Path $output 'qualification.json') @{status='PASS_CONTINUATION_CONTROL';archiveHash=$archiveHash;project=$identity;staleRejected=$true;partialFailureVerified=$true;independentSuffixVerified=$true;overallApplied=$false;compilation=$compile}
        return
    }
    $recoveryHmi=$blockedHmi.Clone()
    $recoveryHmi['base-hash']=[string]$afterFailure.hmi.baseHash
    [void](Invoke-Tool 'modify-tags' $tagArgs)
    [void](Invoke-Tool 'apply-hmi-screen-json-patch' $recoveryHmi)
    $recovery=New-Blueprint 'OTT36_Recover' @(
        @{id='label-metadata';command='modify-tags';arguments=$tagArgs;continueOnError=$false},
        @{id='operator-label';command='apply-hmi-screen-json-patch';arguments=$recoveryHmi;continueOnError=$false}
    )
    $applied=Invoke-Tool 'apply-blueprint' @{'apply-token'=$recovery.applyToken;confirm=$true}
    if(-not $applied.data.applied -or -not $applied.data.stateMatched -or @($applied.data.steps).Count -ne 2){throw 'Recovery apply is incomplete.'}
    $final=Get-State 'readback'
    if(@($final.tags | Where-Object {$_.Name -ceq 'OccupiedBefore' -and $_.Comment -ceq 'Operator label: Run' -and $_.LogicalAddress -ceq '%M99.7' -and $_.DataType -ceq 'Bool'}).Count -ne 1){throw 'Final PLC tag mismatch.'}
    $expectedTags=$baseline.tags | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    @($expectedTags | Where-Object {$_.Name -ceq 'OccupiedBefore'})[0].Comment='Operator label: Run'
    if(($expectedTags | ConvertTo-Json -Depth 30 -Compress) -cne ($final.tags | ConvertTo-Json -Depth 30 -Compress)){throw 'A tag identity, address, type, or unrelated comment changed.'}
    $expectedScreen=$baseline.hmi.Screen | ConvertTo-Json -Depth 50 | ConvertFrom-Json
    @($expectedScreen.Items | Where-Object {$_.Name -ceq 'StartButton'})[0].Properties.Text='Run'
    if(($expectedScreen | ConvertTo-Json -Depth 50 -Compress) -cne ($final.hmi.Screen | ConvertTo-Json -Depth 50 -Compress)){throw 'Final HMI label or preservation mismatch.'}
    $replay=Invoke-Tool 'apply-blueprint' @{'apply-token'=$recovery.applyToken;confirm=$true} -AllowFailure
    if($replay.success -or $replay.message -notmatch 'consumed|invalid'){throw 'Successful token replay was accepted.'}
    [void](Invoke-Tool 'export-block' @{'plc-name'='PLC_Main';'block-name'='Main';'export-path'=(Join-Path $output 'Main-after.xml')})
    if((Get-MainHash (Join-Path $output 'Main-before.xml')) -cne (Get-MainHash (Join-Path $output 'Main-after.xml'))){throw 'Unrelated Main block changed.'}
    $compile=(Invoke-Tool 'compile-software' @{'plc-name'='PLC_Main'}).data
    if($compile.errorCount -ne 0 -or $compile.warningCount -ne 0){throw 'Final compile is not clean.'}
    Write-Json (Join-Path $output 'qualification.json') @{status='PASS';archiveHash=$archiveHash;project=$identity;staleRejected=$true;partialFailureVerified=$true;failedTokenReplayRejected=$true;successfulTokenReplayRejected=$true;baselineHash=$baseline.hmi.baseHash;finalHash=$final.hmi.baseHash;compilation=$compile}
}
catch {
    Write-Json (Join-Path $output 'failure.json') @{status='BLOCKED';message=$_.Exception.Message;archiveHash=$archiveHash;retainSession=$retainSession;capturedAtUtc=[DateTime]::UtcNow.ToString('o')}
    throw
}
finally {
    if(-not $retainSession){
        if($projectOpen){[void](Invoke-Tool 'close-project' @{save=$false;confirm=$true});$projectOpen=$false}
        if($portalOpen){[void](Invoke-Tool 'close-tia-portal' @{confirm=$true});$portalOpen=$false}
        if(-not $process.HasExited){$process.StandardInput.Close();if(-not $process.WaitForExit(10000)){throw "MCP PID $($process.Id) did not exit; inspect before cleanup."}}
        [IO.File]::WriteAllText((Join-Path $output 'stderr.log'),$stderr.Result,$utf8)
        if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -cne $archiveHash){throw 'Source archive changed.'}
        Write-Json (Join-Path $output 'cleanup.json') @{projectClosedWithoutSave=(-not $projectOpen);portalClosed=(-not $portalOpen);mcpExited=$process.HasExited;sourceArchiveUnchanged=$true}
    }
}
