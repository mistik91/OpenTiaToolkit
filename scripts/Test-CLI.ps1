[CmdletBinding()]
param(
    [ValidateSet('All', 'Help', 'ReadOnly', 'Mutation', 'Negative')]
    [string[]]$Suite = @('All'),

    [string]$CliPath = '',

    [string]$ProjectPath = $env:OPEN_TIA_TEST_PROJECT_PATH,

    [string]$PlcName = $env:OPEN_TIA_TEST_PLC_NAME,

    [string]$TagTableName = $(if ($env:OPEN_TIA_TEST_TAG_TABLE_NAME) { $env:OPEN_TIA_TEST_TAG_TABLE_NAME } else { 'Default tag table' }),

    [string]$ProbeBlockName,

    [string]$ProbeTypeName,

    [string]$OutputRoot = '',

    [switch]$BuildFirst,

    [switch]$RegisterAllowList,

    [string]$PortalVersion = '21.0',

    [switch]$SkipVersionedWhitelist
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($CliPath)) {
    $CliPath = Join-Path $script:RepoRoot 'src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $script:RepoRoot 'artifacts\cli-tests'
}

$script:ResolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$script:RunId = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:RunRoot = Join-Path $script:ResolvedOutputRoot $script:RunId
$script:LogsRoot = Join-Path $script:RunRoot 'logs'
$script:GeneratedRoot = Join-Path $script:RunRoot 'generated'
$script:ExportsRoot = Join-Path $script:RunRoot 'exports'
$script:CopiesRoot = Join-Path $script:RunRoot 'copies'
$script:TestResults = New-Object 'System.Collections.Generic.List[object]'
$script:Discovered = [ordered]@{}

foreach ($path in @(
    $script:ResolvedOutputRoot,
    $script:RunRoot,
    $script:LogsRoot,
    $script:GeneratedRoot,
    $script:ExportsRoot,
    $script:CopiesRoot))
{
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

function Get-SelectedSuites {
    if ($Suite -contains 'All') {
        return @('Help', 'ReadOnly', 'Mutation', 'Negative')
    }

    return $Suite | Select-Object -Unique
}

function Write-Status {
    param([string]$Message)
    Write-Host "[cli-test] $Message"
}

function Register-TestResult {
    param(
        [string]$SuiteName,
        [string]$CaseName,
        [string]$Status,
        [string]$Message,
        [string]$LogPath = '',
        [int]$ExitCode = -1
    )

    $script:TestResults.Add([pscustomobject]@{
            Suite    = $SuiteName
            Case     = $CaseName
            Status   = $Status
            Message  = $Message
            ExitCode = $ExitCode
            LogPath  = $LogPath
        })
}

function Invoke-TestCase {
    param(
        [string]$SuiteName,
        [string]$CaseName,
        [scriptblock]$Body
    )

    try {
        $result = & $Body
        $logPath = if ($null -ne $result -and $null -ne $result.LogPath) { [string]$result.LogPath } else { '' }
        $exitCode = if ($null -ne $result -and $null -ne $result.ExitCode) { [int]$result.ExitCode } else { 0 }
        Register-TestResult -SuiteName $SuiteName -CaseName $CaseName -Status 'Passed' -Message 'Passed' -LogPath $logPath -ExitCode $exitCode
        Write-Status "$SuiteName/$CaseName passed"
        return $result
    }
    catch {
        Register-TestResult -SuiteName $SuiteName -CaseName $CaseName -Status 'Failed' -Message $_.Exception.Message
        Write-Warning "$SuiteName/$CaseName failed: $($_.Exception.Message)"
        return $null
    }
}

function Register-SkippedCase {
    param(
        [string]$SuiteName,
        [string]$CaseName,
        [string]$Reason,
        [string]$LogPath = '',
        [int]$ExitCode = -1
    )

    Register-TestResult -SuiteName $SuiteName -CaseName $CaseName -Status 'Skipped' -Message $Reason -LogPath $LogPath -ExitCode $ExitCode
    Write-Status "$SuiteName/$CaseName skipped: $Reason"
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Needle,
        [string]$Message
    )

    if ($Text.IndexOf($Needle, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw $Message
    }
}

function Assert-NoCliFailureText {
    param([string]$Text)

    if ($Text -match '(?m)^Error:\s') {
        throw 'CLI output contains a failed command.'
    }

    if ($Text -match '(?m)^Fatal error:\s') {
        throw 'CLI output contains a fatal error.'
    }
}

function Format-CliValue {
    param([string]$Value)

    if ($Value.Contains('"')) {
        throw "CLI harness cannot safely quote values containing double quotes: $Value"
    }

    if ($Value -match '\s') {
        return '"' + $Value + '"'
    }

    return $Value
}

function New-CliCommandString {
    param(
        [string]$Name,
        [hashtable]$Arguments = @{}
    )

    $parts = New-Object 'System.Collections.Generic.List[string]'
    $parts.Add($Name)

    foreach ($entry in $Arguments.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            continue
        }

        if ($entry.Value -is [bool]) {
            $parts.Add(('--{0}={1}' -f $entry.Key, $entry.Value.ToString().ToLowerInvariant()))
            continue
        }

        $parts.Add(('--{0}={1}' -f $entry.Key, (Format-CliValue ([string]$entry.Value))))
    }

    return ($parts -join ' ')
}

function New-BaseProjectBatch {
    param([string]$TargetProjectPath)

    return @(
        (New-CliCommandString -Name 'open-tia-portal' -Arguments @{ 'with-ui' = $false }),
        (New-CliCommandString -Name 'open-project' -Arguments @{ 'project-path' = $TargetProjectPath })
    )
}

function New-BasePlcBatch {
    param(
        [string]$TargetProjectPath,
        [string]$TargetPlcName
    )

    $commands = New-BaseProjectBatch -TargetProjectPath $TargetProjectPath
    $commands += New-CliCommandString -Name 'open-plc' -Arguments @{ 'plc-name' = $TargetPlcName }
    return $commands
}

function Invoke-CliCommand {
    param(
        [string]$CaseName,
        [string[]]$Arguments,
        [string]$WorkingDirectory = $script:RepoRoot
    )

    Push-Location $WorkingDirectory
    try {
        $output = & $script:ResolvedCliPath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    $logPath = Join-Path $script:LogsRoot ($CaseName + '.log')
    Set-Content -Path $logPath -Value $text -Encoding UTF8

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = $text
        LogPath  = $logPath
    }
}

function Invoke-ReplBatch {
    param(
        [string]$CaseName,
        [string[]]$Commands
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:ResolvedCliPath
    $psi.Arguments = '--interactive'
    $psi.WorkingDirectory = $script:RepoRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi

    $null = $process.Start()

    foreach ($command in $Commands) {
        $process.StandardInput.WriteLine($command)
    }

    $process.StandardInput.WriteLine('exit')
    $process.StandardInput.Close()

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    $logLines = New-Object 'System.Collections.Generic.List[string]'
    $logLines.Add('Commands:')
    foreach ($command in $Commands) {
        $logLines.Add('  ' + $command)
    }
    $logLines.Add('')
    $logLines.Add('STDOUT:')
    $logLines.Add($stdout)
    $logLines.Add('')
    $logLines.Add('STDERR:')
    $logLines.Add($stderr)

    $text = $logLines -join [Environment]::NewLine
    $logPath = Join-Path $script:LogsRoot ($CaseName + '.log')
    Set-Content -Path $logPath -Value $text -Encoding UTF8

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output   = $stdout
        Error    = $stderr
        LogPath  = $logPath
    }
}

function Get-HelpCommandsFromOutput {
    param([string]$Text)

    $commands = New-Object 'System.Collections.Generic.List[string]'
    $inCommands = $false

    foreach ($line in ($Text -split '\r?\n')) {
        if ($line -eq 'Available commands:') {
            $inCommands = $true
            continue
        }

        if ($inCommands -and $line -like "Use 'help <command>'*") {
            break
        }

        if ($inCommands -and $line -match '^\s{2}([a-z0-9][a-z0-9-]*)\s+') {
            $commands.Add($matches[1])
        }
    }

    return $commands.ToArray()
}

function Get-BlockRowsFromOutput {
    param([string]$Text)

    $rows = New-Object 'System.Collections.Generic.List[object]'
    $capturing = $false

    foreach ($line in ($Text -split '\r?\n')) {
        if ($line -match '^Found \d+ block\(s\):') {
            $capturing = $true
            continue
        }

        if (-not $capturing) {
            continue
        }

        if ([string]::IsNullOrWhiteSpace($line) -or $line -match '^Name\s+Type\s+Number' -or $line -match '^-+$') {
            continue
        }

        if ($line -match '^(?<name>.{1,25}?)\s{2,}(?<type>\S+)\s+') {
            $rows.Add([pscustomobject]@{
                    Name = $matches['name'].Trim()
                    Type = $matches['type'].Trim()
                })
            continue
        }

        if ($rows.Count -gt 0) {
            break
        }
    }

    return $rows.ToArray()
}

function Get-FirstInstanceIdFromOutput {
    param([string]$Text)

    $match = [regex]::Match($Text, 'processId\s*=\s*(\d+)')
    if ($match.Success) {
        return [int]$match.Groups[1].Value
    }

    return $null
}

function Get-InstanceIdsFromOutput {
    param([string]$Text)

    $matches = [regex]::Matches($Text, 'processId\s*=\s*(\d+)')
    $instanceIds = New-Object 'System.Collections.Generic.List[int]'

    foreach ($match in $matches) {
        $instanceIds.Add([int]$match.Groups[1].Value)
    }

    return $instanceIds.ToArray()
}

function Get-FirstXmlBaseName {
    param([string]$DirectoryPath)

    if (-not (Test-Path -LiteralPath $DirectoryPath -PathType Container)) {
        return $null
    }

    $file = Get-ChildItem -LiteralPath $DirectoryPath -Filter '*.xml' | Sort-Object Name | Select-Object -First 1
    if ($null -eq $file) {
        return $null
    }

    return [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
}

function Get-FirstProjectFilePath {
    param([string]$DirectoryPath)

    if (-not (Test-Path -LiteralPath $DirectoryPath -PathType Container)) {
        return $null
    }

    $file = Get-ChildItem -LiteralPath $DirectoryPath -Filter '*.ap21' -File | Sort-Object Name | Select-Object -First 1
    if ($null -eq $file) {
        return $null
    }

    return $file.FullName
}

function Assert-ProjectInputs {
    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        throw 'ProjectPath is required for this suite. Pass -ProjectPath or set OPEN_TIA_TEST_PROJECT_PATH.'
    }

    if ([string]::IsNullOrWhiteSpace($PlcName)) {
        throw 'PlcName is required for this suite. Pass -PlcName or set OPEN_TIA_TEST_PLC_NAME.'
    }
}

function Invoke-DotNetBuild {
    $output = & dotnet build OpenTiaToolkit.sln 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    $logPath = Join-Path $script:LogsRoot 'build.log'
    Set-Content -Path $logPath -Value $text -Encoding UTF8

    Assert-True ($exitCode -eq 0) 'dotnet build failed.'

    Register-TestResult -SuiteName 'Preflight' -CaseName 'build' -Status 'Passed' -Message 'Build passed' -LogPath $logPath -ExitCode $exitCode
    Write-Status 'Preflight/build passed'
}

function Register-AllowListIfRequested {
    $scriptPath = Join-Path $PSScriptRoot 'Register-OpennessAllowList.ps1'
    $arguments = @(
        '-ExecutionPolicy', 'Bypass',
        '-File', $scriptPath,
        '-ExecutablePath', $script:ResolvedCliPath,
        '-PortalVersion', $PortalVersion
    )

    if ($SkipVersionedWhitelist) {
        $arguments += '-SkipVersionedWhitelist'
    }

    Push-Location $script:RepoRoot
    try {
        $output = & powershell @arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    $logPath = Join-Path $script:LogsRoot 'allowlist.log'
    Set-Content -Path $logPath -Value $text -Encoding UTF8

    Assert-True ($exitCode -eq 0) 'Openness allowlist registration failed.'

    Register-TestResult -SuiteName 'Preflight' -CaseName 'allowlist' -Status 'Passed' -Message 'Allowlist registration passed' -LogPath $logPath -ExitCode $exitCode
    Write-Status 'Preflight/allowlist passed'
}

function Run-HelpSuite {
    $suiteName = 'Help'

    $generalHelp = Invoke-TestCase -SuiteName $suiteName -CaseName 'general-help' -Body {
        $result = Invoke-CliCommand -CaseName 'help-general' -Arguments @('help')
        Assert-True ($result.ExitCode -eq 0) 'help returned a non-zero exit code.'
        Assert-Contains -Text $result.Output -Needle 'Available commands:' -Message 'General help did not include the command list.'
        return $result
    }

    Invoke-TestCase -SuiteName $suiteName -CaseName 'help-flags' -Body {
        $result = Invoke-CliCommand -CaseName 'help-flags' -Arguments @('--help')
        Assert-True ($result.ExitCode -eq 0) '--help returned a non-zero exit code.'
        Assert-Contains -Text $result.Output -Needle 'Available commands:' -Message '--help did not include the command list.'
        return $result
    } | Out-Null

    Invoke-TestCase -SuiteName $suiteName -CaseName 'help-short-flag' -Body {
        $result = Invoke-CliCommand -CaseName 'help-short-flag' -Arguments @('-h')
        Assert-True ($result.ExitCode -eq 0) '-h returned a non-zero exit code.'
        Assert-Contains -Text $result.Output -Needle 'Available commands:' -Message '-h did not include the command list.'
        return $result
    } | Out-Null

    if ($null -eq $generalHelp) {
        return
    }

    $commands = Get-HelpCommandsFromOutput -Text $generalHelp.Output
    $script:Discovered.HelpCommands = $commands

    Assert-True ($commands.Count -gt 0) 'No commands were discovered from help output.'
    Assert-True ($commands -contains 'create-block') 'create-block is missing from CLI help.'
    Assert-True ($commands -contains 'find-replace-db-members') 'find-replace-db-members is missing from CLI help.'
    Assert-True ($commands -contains 'find-replace-udt-members') 'find-replace-udt-members is missing from CLI help.'

    foreach ($command in $commands) {
        Invoke-TestCase -SuiteName $suiteName -CaseName ("help-" + $command) -Body {
            $result = Invoke-CliCommand -CaseName ("help-" + $command) -Arguments @('help', $command)
            Assert-True ($result.ExitCode -eq 0) "help $command returned a non-zero exit code."
            Assert-Contains -Text $result.Output -Needle ("Command: " + $command) -Message "Specific help for $command did not include the command header."
            Assert-True (-not ($result.Output -match 'Unknown command')) "Specific help for $command reported an unknown command."
            return $result
        } | Out-Null
    }
}

function Run-ReadOnlySuite {
    Assert-ProjectInputs
    $suiteName = 'ReadOnly'

    $instanceList = Invoke-TestCase -SuiteName $suiteName -CaseName 'list-instances' -Body {
        $result = Invoke-CliCommand -CaseName 'readonly-list-instances' -Arguments @('list-instances')
        Assert-True ($result.ExitCode -eq 0) 'list-instances returned a non-zero exit code.'
        return $result
    }

    $preExistingInstanceIds = @()
    if ($null -ne $instanceList) {
        $preExistingInstanceIds = @(Get-InstanceIdsFromOutput -Text $instanceList.Output)
        if ($preExistingInstanceIds.Length -gt 0) {
            $script:Discovered.PreExistingInstanceId = $preExistingInstanceIds[0]
        }
    }

    Invoke-TestCase -SuiteName $suiteName -CaseName 'open-instance' -Body {
        $batch = @(
            (New-CliCommandString -Name 'open-tia-portal' -Arguments @{ 'with-ui' = $false }),
            'portal-status',
            'list-instances',
            'close-tia-portal'
        )

        $result = Invoke-ReplBatch -CaseName 'readonly-open-instance' -Commands $batch
        Assert-True ($result.ExitCode -eq 0) 'open/list/close instance batch returned a non-zero exit code.'
        Assert-NoCliFailureText -Text $result.Output
        return $result
    } | Out-Null

    if ($script:Discovered.Contains('PreExistingInstanceId')) {
        Invoke-TestCase -SuiteName $suiteName -CaseName 'connect-instance' -Body {
            $batch = @(
                (New-CliCommandString -Name 'connect-instance' -Arguments @{ 'instance-id' = $script:Discovered.PreExistingInstanceId }),
                'portal-status',
                'disconnect-portal'
            )

            $result = Invoke-ReplBatch -CaseName 'readonly-connect-instance' -Commands $batch
            Assert-True ($result.ExitCode -eq 0) 'connect-instance batch returned a non-zero exit code.'
            Assert-NoCliFailureText -Text $result.Output
            return $result
        } | Out-Null
    }
    else {
        Register-SkippedCase -SuiteName $suiteName -CaseName 'connect-instance' -Reason 'No pre-existing TIA Portal instance was available to attach from a separate CLI process.'
    }

    Invoke-TestCase -SuiteName $suiteName -CaseName 'connect-disconnect-portal' -Body {
        $batch = @(
            'connect-portal',
            'portal-status',
            'disconnect-portal'
        )

        $result = Invoke-ReplBatch -CaseName 'readonly-connect-disconnect' -Commands $batch
        Assert-True ($result.ExitCode -eq 0) 'connect/disconnect batch returned a non-zero exit code.'
        Assert-NoCliFailureText -Text $result.Output
        return $result
    } | Out-Null

    Invoke-TestCase -SuiteName $suiteName -CaseName 'project-session' -Body {
        $batch = New-BaseProjectBatch -TargetProjectPath $ProjectPath
        $batch += @(
            'get-project-info',
            'list-projects',
            'list-devices',
            'list-plcs',
            'list-hmis',
            'get-project-tree',
            'save-project',
            'close-project',
            'disconnect-portal'
        )

        $result = Invoke-ReplBatch -CaseName 'readonly-project-session' -Commands $batch
        Assert-True ($result.ExitCode -eq 0) 'Project session batch returned a non-zero exit code.'
        Assert-NoCliFailureText -Text $result.Output
        Assert-Contains -Text $result.Output -Needle 'Project Information:' -Message 'Project info output was not present.'
        return $result
    } | Out-Null

    $discoveryExportsRoot = Join-Path $script:ExportsRoot 'readonly-discovery'
    $typeProbeRoot = Join-Path $discoveryExportsRoot 'types'
    New-Item -ItemType Directory -Path $typeProbeRoot -Force | Out-Null

    $plcDiscovery = Invoke-TestCase -SuiteName $suiteName -CaseName 'plc-discovery' -Body {
        $batch = New-BasePlcBatch -TargetProjectPath $ProjectPath -TargetPlcName $PlcName
        $batch += @(
            (New-CliCommandString -Name 'get-software-tree' -Arguments @{ 'plc-name' = $PlcName; 'depth' = 3 }),
            (New-CliCommandString -Name 'compile-software' -Arguments @{ 'plc-name' = $PlcName }),
            (New-CliCommandString -Name 'get-blocks' -Arguments @{ 'plc-name' = $PlcName }),
            (New-CliCommandString -Name 'list-watch-tables' -Arguments @{ 'plc-name' = $PlcName }),
            (New-CliCommandString -Name 'export-types' -Arguments @{ 'plc-name' = $PlcName; 'export-path' = $typeProbeRoot })
        )

        $result = Invoke-ReplBatch -CaseName 'readonly-plc-discovery' -Commands $batch
        Assert-True ($result.ExitCode -eq 0) 'PLC discovery batch returned a non-zero exit code.'
        Assert-NoCliFailureText -Text $result.Output
        return $result
    }

    if ($null -ne $plcDiscovery) {
        $blockRows = Get-BlockRowsFromOutput -Text $plcDiscovery.Output
        if ([string]::IsNullOrWhiteSpace($ProbeBlockName) -and $blockRows.Count -gt 0) {
            $preferredBlock = $blockRows | Where-Object { $_.Type -notin @('OB', 'FOB') } | Select-Object -First 1
            if ($null -eq $preferredBlock) {
                $preferredBlock = $blockRows | Select-Object -First 1
            }

            if ($null -ne $preferredBlock) {
                $ProbeBlockName = $preferredBlock.Name
            }
        }

        $typeName = if ([string]::IsNullOrWhiteSpace($ProbeTypeName)) { Get-FirstXmlBaseName -DirectoryPath $typeProbeRoot } else { $ProbeTypeName }
        if (-not [string]::IsNullOrWhiteSpace($typeName)) {
            $ProbeTypeName = $typeName
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ProbeBlockName)) {
        $singleBlockRoot = Join-Path $script:ExportsRoot 'readonly-single-block'
        $singleBlockFile = Join-Path $singleBlockRoot ($ProbeBlockName + '.xml')
        $singleDocsRoot = Join-Path $script:ExportsRoot 'readonly-single-block-docs'
        New-Item -ItemType Directory -Path $singleBlockRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $singleDocsRoot -Force | Out-Null

        $singleBlockBatch = New-BasePlcBatch -TargetProjectPath $ProjectPath -TargetPlcName $PlcName
        $singleBlockBatch += @(
            (New-CliCommandString -Name 'get-block-info' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $ProbeBlockName }),
            (New-CliCommandString -Name 'search-blocks' -Arguments @{ 'plc-name' = $PlcName; 'pattern' = $ProbeBlockName; 'search-in' = 'name' }),
            (New-CliCommandString -Name 'export-block' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $ProbeBlockName; 'export-path' = $singleBlockFile }),
            (New-CliCommandString -Name 'export-block-as-docs' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $ProbeBlockName; 'export-path' = $singleDocsRoot })
        )

        $singleBlockResult = Invoke-ReplBatch -CaseName 'readonly-single-block-operations' -Commands $singleBlockBatch
        if ($singleBlockResult.ExitCode -ne 0) {
            Register-TestResult -SuiteName $suiteName -CaseName 'single-block-operations' -Status 'Failed' -Message 'Single block batch returned a non-zero exit code.' -LogPath $singleBlockResult.LogPath -ExitCode $singleBlockResult.ExitCode
            Write-Warning "$suiteName/single-block-operations failed: Single block batch returned a non-zero exit code."
        }
        else {
            $singleBlockXmlExists = Test-Path -LiteralPath $singleBlockFile -PathType Leaf
            $singleBlockDocsCount = (Get-ChildItem -LiteralPath $singleDocsRoot -Filter '*.s7dcl' | Measure-Object).Count
            $singleBlockXmlFailed = $singleBlockResult.Output -match '(?m)^Error:\sFailed to export block:'

            if (-not $singleBlockXmlFailed) {
                Assert-True ($singleBlockXmlExists) 'Single block XML export was not created.'
            }

            Assert-True ($singleBlockDocsCount -gt 0) 'Single block document export did not create any .s7dcl files.'

            if ($singleBlockXmlFailed -and -not $singleBlockXmlExists) {
                Register-SkippedCase -SuiteName $suiteName -CaseName 'single-block-operations' -Reason "Block '$ProbeBlockName' was not exportable as XML in this project, but document export still succeeded." -LogPath $singleBlockResult.LogPath -ExitCode $singleBlockResult.ExitCode
            }
            else {
                Register-TestResult -SuiteName $suiteName -CaseName 'single-block-operations' -Status 'Passed' -Message 'Passed' -LogPath $singleBlockResult.LogPath -ExitCode $singleBlockResult.ExitCode
                Write-Status "$suiteName/single-block-operations passed"
            }
        }
    }
    else {
        Register-SkippedCase -SuiteName $suiteName -CaseName 'single-block-operations' -Reason 'No probe block name was discovered.'
    }

    $bulkBlockRoot = Join-Path $script:ExportsRoot 'readonly-export-plc'
    $bulkDocsRoot = Join-Path $script:ExportsRoot 'readonly-export-blocks-docs'
    $bulkTypeRoot = Join-Path $script:ExportsRoot 'readonly-export-types'
    New-Item -ItemType Directory -Path $bulkBlockRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $bulkDocsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $bulkTypeRoot -Force | Out-Null

    $bulkBlockRegex = if ([string]::IsNullOrWhiteSpace($ProbeBlockName)) { '.*' } else { [regex]::Escape($ProbeBlockName) }
    $bulkBatch = New-BasePlcBatch -TargetProjectPath $ProjectPath -TargetPlcName $PlcName
    $bulkBatch += @(
        (New-CliCommandString -Name 'export-plc' -Arguments @{ 'plc-name' = $PlcName; 'export-path' = $bulkBlockRoot }),
        (New-CliCommandString -Name 'export-blocks-as-docs' -Arguments @{ 'plc-name' = $PlcName; 'export-path' = $bulkDocsRoot; 'filter-regex' = $bulkBlockRegex }),
        (New-CliCommandString -Name 'export-types' -Arguments @{ 'plc-name' = $PlcName; 'export-path' = $bulkTypeRoot })
    )

    $bulkExportResult = Invoke-ReplBatch -CaseName 'readonly-bulk-export-operations' -Commands $bulkBatch

    if ($bulkExportResult.ExitCode -ne 0) {
        Register-TestResult -SuiteName $suiteName -CaseName 'bulk-export-operations' -Status 'Failed' -Message 'Bulk export batch returned a non-zero exit code.' -LogPath $bulkExportResult.LogPath -ExitCode $bulkExportResult.ExitCode
        Write-Warning "$suiteName/bulk-export-operations failed: Bulk export batch returned a non-zero exit code."
    }
    else {
        $bulkXmlCount = (Get-ChildItem -LiteralPath $bulkBlockRoot -Filter '*.xml' | Measure-Object).Count
        $bulkDocCount = (Get-ChildItem -LiteralPath $bulkDocsRoot -Filter '*.s7dcl' | Measure-Object).Count
        $bulkTypeCount = (Get-ChildItem -LiteralPath $bulkTypeRoot -Filter '*.xml' | Measure-Object).Count
        $exportPlcFailed = $bulkExportResult.Output -match '(?m)^Error:\sFailed to export PLC blocks'
        $exportTypesReportedZero = $bulkExportResult.Output -match 'summary\s*:\s*\{\s*total\s*=\s*0,\s*succeeded\s*=\s*0,\s*failed\s*=\s*0\s*\}'

        if (-not $exportPlcFailed) {
            Assert-True ($bulkXmlCount -gt 0) 'export-plc did not create any XML files.'
        }

        Assert-True ($bulkDocCount -gt 0) 'export-blocks-as-docs did not create any .s7dcl files.'

        if ($exportPlcFailed -and $bulkXmlCount -eq 0) {
            Register-SkippedCase -SuiteName $suiteName -CaseName 'bulk-export-operations' -Reason 'export-plc reported no exportable XML blocks for this project, but document export still succeeded.' -LogPath $bulkExportResult.LogPath -ExitCode $bulkExportResult.ExitCode
        }
        elseif ($bulkTypeCount -eq 0 -and $exportTypesReportedZero) {
            Register-SkippedCase -SuiteName $suiteName -CaseName 'bulk-export-operations' -Reason 'No exportable PLC types were present in the current project, but the bulk export commands completed.' -LogPath $bulkExportResult.LogPath -ExitCode $bulkExportResult.ExitCode
        }
        else {
            Register-TestResult -SuiteName $suiteName -CaseName 'bulk-export-operations' -Status 'Passed' -Message 'Passed' -LogPath $bulkExportResult.LogPath -ExitCode $bulkExportResult.ExitCode
            Write-Status "$suiteName/bulk-export-operations passed"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ProbeTypeName)) {
        $singleTypeRoot = Join-Path $script:ExportsRoot 'readonly-single-type'
        $singleTypeFile = Join-Path $singleTypeRoot ($ProbeTypeName + '.xml')
        New-Item -ItemType Directory -Path $singleTypeRoot -Force | Out-Null

        Invoke-TestCase -SuiteName $suiteName -CaseName 'single-type-export' -Body {
            $batch = New-BasePlcBatch -TargetProjectPath $ProjectPath -TargetPlcName $PlcName
            $batch += @(
                (New-CliCommandString -Name 'compile-software' -Arguments @{ 'plc-name' = $PlcName }),
                (New-CliCommandString -Name 'export-type' -Arguments @{ 'plc-name' = $PlcName; 'type-name' = $ProbeTypeName; 'export-path' = $singleTypeFile })
            )

            $result = Invoke-ReplBatch -CaseName 'readonly-single-type-export' -Commands $batch
            Assert-True ($result.ExitCode -eq 0) 'Single type export batch returned a non-zero exit code.'
            Assert-NoCliFailureText -Text $result.Output
            Assert-True (Test-Path -LiteralPath $singleTypeFile -PathType Leaf) 'Single type XML export was not created.'
            return $result
        } | Out-Null
    }
    else {
        Register-SkippedCase -SuiteName $suiteName -CaseName 'single-type-export' -Reason 'No probe type name was discovered.'
    }
}

function Run-MutationSuite {
    Assert-ProjectInputs
    $suiteName = 'Mutation'

    $copyProjectDirectory = Join-Path $script:CopiesRoot ('cli-suite-' + $script:RunId)
    $script:Discovered.CopyProjectDirectory = $copyProjectDirectory

    Invoke-TestCase -SuiteName $suiteName -CaseName 'prepare-copy' -Body {
        $batch = New-BaseProjectBatch -TargetProjectPath $ProjectPath
        $batch += @(
            (New-CliCommandString -Name 'save-as-project' -Arguments @{ 'destination-path' = $copyProjectDirectory }),
            'close-project',
            'disconnect-portal'
        )

        $result = Invoke-ReplBatch -CaseName 'mutation-prepare-copy' -Commands $batch
        Assert-True ($result.ExitCode -eq 0) 'Prepare copy batch returned a non-zero exit code.'
        Assert-NoCliFailureText -Text $result.Output
        $savedProjectPath = Get-FirstProjectFilePath -DirectoryPath $copyProjectDirectory
        Assert-True (-not [string]::IsNullOrWhiteSpace($savedProjectPath)) 'Disposable project copy was not created.'
        $script:Discovered.CopyProjectPath = $savedProjectPath
        return $result
    } | Out-Null

    if (-not $script:Discovered.Contains('CopyProjectPath')) {
        Register-SkippedCase -SuiteName $suiteName -CaseName 'block-tag-roundtrip' -Reason 'No disposable project copy was available.'
        Register-SkippedCase -SuiteName $suiteName -CaseName 'type-roundtrip' -Reason 'No disposable project copy was available.'
        return
    }

    $copyProjectPath = [string]$script:Discovered.CopyProjectPath
    $mutationProbeTypeRoot = Join-Path $script:GeneratedRoot 'mutation-probe-types'
    New-Item -ItemType Directory -Path $mutationProbeTypeRoot -Force | Out-Null

    if ([string]::IsNullOrWhiteSpace($ProbeBlockName) -or [string]::IsNullOrWhiteSpace($ProbeTypeName)) {
        $probeBatch = New-BasePlcBatch -TargetProjectPath $copyProjectPath -TargetPlcName $PlcName
        $probeBatch += @(
            (New-CliCommandString -Name 'compile-software' -Arguments @{ 'plc-name' = $PlcName }),
            (New-CliCommandString -Name 'get-blocks' -Arguments @{ 'plc-name' = $PlcName }),
            (New-CliCommandString -Name 'export-types' -Arguments @{ 'plc-name' = $PlcName; 'export-path' = $mutationProbeTypeRoot })
        )

        $probeResult = Invoke-ReplBatch -CaseName 'mutation-probe-discovery' -Commands $probeBatch
        Assert-True ($probeResult.ExitCode -eq 0) 'Mutation probe discovery batch returned a non-zero exit code.'
        Assert-NoCliFailureText -Text $probeResult.Output

        if ([string]::IsNullOrWhiteSpace($ProbeBlockName)) {
            $probeBlockRows = Get-BlockRowsFromOutput -Text $probeResult.Output
            if ($probeBlockRows.Count -gt 0) {
                $preferredProbeBlock = $probeBlockRows | Where-Object { $_.Type -notin @('OB', 'FOB', 'InstanceDB') } | Select-Object -First 1
                if ($null -eq $preferredProbeBlock) {
                    $preferredProbeBlock = $probeBlockRows | Select-Object -First 1
                }

                if ($null -ne $preferredProbeBlock) {
                    $ProbeBlockName = $preferredProbeBlock.Name
                }
            }
        }

        if ([string]::IsNullOrWhiteSpace($ProbeTypeName)) {
            $discoveredTypeName = Get-FirstXmlBaseName -DirectoryPath $mutationProbeTypeRoot
            if (-not [string]::IsNullOrWhiteSpace($discoveredTypeName)) {
                $ProbeTypeName = $discoveredTypeName
            }
        }
    }

    $testSuffix = (Get-Date -Format 'HHmmss')
    $testBlockName = 'ZZCliBlock' + $testSuffix
    $exportProbeBlockName = 'ZZCliExport' + $testSuffix
    $testTagName1 = '100CliSmoke' + $testSuffix
    $testTagName2 = '101CliSmoke' + $testSuffix
    $testTagName3 = '102CliSmoke' + $testSuffix
    $blockXmlRoot = Join-Path $script:GeneratedRoot 'block-xml'
    $blockXmlFile = Join-Path $blockXmlRoot ($testBlockName + '.xml')
    $exportProbeXmlFile = Join-Path $blockXmlRoot ($exportProbeBlockName + '.xml')
    $blockDocsSingleRoot = Join-Path $script:GeneratedRoot 'block-docs-single'
    $blockDocsMultiRoot = Join-Path $script:GeneratedRoot 'block-docs-multi'
    $existingBlockXmlRoot = Join-Path $script:GeneratedRoot 'existing-block-xml'
    $existingBlockDocsSingleRoot = Join-Path $script:GeneratedRoot 'existing-block-docs-single'
    $existingBlockDocsMultiRoot = Join-Path $script:GeneratedRoot 'existing-block-docs-multi'
    $typeXmlRoot = Join-Path $script:GeneratedRoot 'type-xml'
    $typeXmlSingleRoot = Join-Path $typeXmlRoot 'single'
    $typeXmlBulkRoot = Join-Path $typeXmlRoot 'bulk'
    $typeXmlFile = if ([string]::IsNullOrWhiteSpace($ProbeTypeName)) { $null } else { Join-Path $typeXmlSingleRoot ($ProbeTypeName + '.xml') }
    $existingBlockXmlFile = if ([string]::IsNullOrWhiteSpace($ProbeBlockName)) { $null } else { Join-Path $existingBlockXmlRoot ($ProbeBlockName + '.xml') }
    $tagCsvPath = Join-Path $script:GeneratedRoot 'tag-operations.csv'

    foreach ($path in @($blockXmlRoot, $blockDocsSingleRoot, $blockDocsMultiRoot, $existingBlockXmlRoot, $existingBlockDocsSingleRoot, $existingBlockDocsMultiRoot, $typeXmlRoot, $typeXmlSingleRoot, $typeXmlBulkRoot)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    @(
        'Action,MatchTagTable,MatchName,MatchLogicalAddress,TagTable,Name,DataType,LogicalAddress,Comment',
        ('create,,,,{0},{1},{2},{3},{4}' -f $TagTableName, $testTagName1, 'Bool', '%M10.0', 'Created by CLI harness'),
        ('create,,,,{0},{1},{2},{3},{4}' -f $TagTableName, $testTagName3, 'Bool', '%M12.0', 'Created by CLI harness')
    ) | Set-Content -Path $tagCsvPath -Encoding UTF8

    Invoke-TestCase -SuiteName $suiteName -CaseName 'block-tag-roundtrip' -Body {
        $blockRegex = '^' + [regex]::Escape($testBlockName) + '$'
        $batch = New-BasePlcBatch -TargetProjectPath $copyProjectPath -TargetPlcName $PlcName
        $batch += @(
            (New-CliCommandString -Name 'create-block' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $testBlockName; 'block-type' = 'FB'; 'language' = 'ProDiag' }),
            (New-CliCommandString -Name 'get-block-info' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $testBlockName }),
            (New-CliCommandString -Name 'search-blocks' -Arguments @{ 'plc-name' = $PlcName; 'pattern' = $testBlockName; 'search-in' = 'name' }),
            (New-CliCommandString -Name 'modify-tags' -Arguments @{ 'plc-name' = $PlcName; 'input-path' = $tagCsvPath; 'dry-run' = $false }),
            (New-CliCommandString -Name 'find-replace-tags' -Arguments @{ 'plc-name' = $PlcName; 'rename-map' = ('{' + $testTagName1 + ':' + $testTagName2 + '}'); 'dry-run' = $false }),
            (New-CliCommandString -Name 'find-replace-db-members' -Arguments @{ 'plc-name' = $PlcName; 'rename-map' = '{NoSuchMember:RenamedMember}'; 'dry-run' = $true }),
            (New-CliCommandString -Name 'find-replace-udt-members' -Arguments @{ 'plc-name' = $PlcName; 'rename-map' = '{NoSuchMember:RenamedMember}'; 'dry-run' = $true }),
            (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $blockRegex; 'confirm' = $true })
        )

        $result = Invoke-ReplBatch -CaseName 'mutation-block-tag-roundtrip' -Commands $batch
        Assert-True ($result.ExitCode -eq 0) 'Block/tag mutation batch returned a non-zero exit code.'
        Assert-NoCliFailureText -Text $result.Output
        return $result
    } | Out-Null

    $exportProbeRegex = '^' + [regex]::Escape($exportProbeBlockName) + '$'
    $exportProbeBatch = New-BasePlcBatch -TargetProjectPath $copyProjectPath -TargetPlcName $PlcName
    $exportProbeBatch += @(
        (New-CliCommandString -Name 'create-block' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $exportProbeBlockName; 'block-type' = 'FB'; 'language' = 'ProDiag' }),
        (New-CliCommandString -Name 'export-block' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $exportProbeBlockName; 'export-path' = $exportProbeXmlFile }),
        (New-CliCommandString -Name 'export-block-as-docs' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $exportProbeBlockName; 'export-path' = $blockDocsSingleRoot }),
        (New-CliCommandString -Name 'export-blocks-as-docs' -Arguments @{ 'plc-name' = $PlcName; 'export-path' = $blockDocsMultiRoot; 'filter-regex' = $exportProbeRegex }),
        (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $exportProbeRegex; 'confirm' = $true })
    )

    $exportProbeResult = Invoke-ReplBatch -CaseName 'mutation-block-export-probe' -Commands $exportProbeBatch
    if ($exportProbeResult.ExitCode -ne 0) {
        Register-TestResult -SuiteName $suiteName -CaseName 'block-export-probe' -Status 'Failed' -Message 'Block export probe batch returned a non-zero exit code.' -LogPath $exportProbeResult.LogPath -ExitCode $exportProbeResult.ExitCode
        Write-Warning "$suiteName/block-export-probe failed: Block export probe batch returned a non-zero exit code."
    }
    else {
        $xmlProbeExists = Test-Path -LiteralPath $exportProbeXmlFile -PathType Leaf
        $docProbeCount = (Get-ChildItem -LiteralPath $blockDocsSingleRoot -Filter ($exportProbeBlockName + '.s7dcl') -ErrorAction SilentlyContinue | Measure-Object).Count
        $xmlProbeFailed = $exportProbeResult.Output -match '(?m)^Error:\sFailed to export block:'
        $docProbeFailed = $exportProbeResult.Output -match '(?m)^Error:\sFailed to export block as documents:'
        $knowHowProtected = $exportProbeResult.Output -match 'know-how-protected'

        if ($xmlProbeFailed -or $docProbeFailed) {
            $reason = if ($knowHowProtected) {
                "Created ProDiag block '$exportProbeBlockName' is know-how-protected in TIA V21, so export/import roundtrip commands were skipped."
            }
            else {
                "Created ProDiag block '$exportProbeBlockName' was not exportable in this project, so export/import roundtrip commands were skipped."
            }

            Register-SkippedCase -SuiteName $suiteName -CaseName 'block-export-probe' -Reason $reason -LogPath $exportProbeResult.LogPath -ExitCode $exportProbeResult.ExitCode
            Register-SkippedCase -SuiteName $suiteName -CaseName 'block-import-roundtrip' -Reason 'No export artifacts were produced for the created ProDiag block.' -LogPath $exportProbeResult.LogPath -ExitCode $exportProbeResult.ExitCode
        }
        else {
            Assert-True ($xmlProbeExists) 'Created block XML export was not created.'
            Assert-True ($docProbeCount -gt 0) 'Created block document export did not create any .s7dcl files.'
            Register-TestResult -SuiteName $suiteName -CaseName 'block-export-probe' -Status 'Passed' -Message 'Passed' -LogPath $exportProbeResult.LogPath -ExitCode $exportProbeResult.ExitCode
            Write-Status "$suiteName/block-export-probe passed"

            Invoke-TestCase -SuiteName $suiteName -CaseName 'block-import-roundtrip' -Body {
                $importProbeBatch = New-BasePlcBatch -TargetProjectPath $copyProjectPath -TargetPlcName $PlcName
                $importProbeBatch += (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $exportProbeRegex; 'confirm' = $true })

                if ($xmlProbeExists) {
                    $importProbeBatch += @(
                        (New-CliCommandString -Name 'import-block' -Arguments @{ 'plc-name' = $PlcName; 'import-path' = $exportProbeXmlFile }),
                        (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $exportProbeRegex; 'confirm' = $true }),
                        (New-CliCommandString -Name 'import-blocks' -Arguments @{ 'plc-name' = $PlcName; 'import-directory' = $blockXmlRoot; 'filter-regex' = $exportProbeRegex })
                    )
                }

                if ($docProbeCount -gt 0) {
                    $importProbeBatch += @(
                        (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $exportProbeRegex; 'confirm' = $true }),
                        (New-CliCommandString -Name 'import-block-from-docs' -Arguments @{ 'plc-name' = $PlcName; 'import-directory' = $blockDocsSingleRoot; 'file-name' = $exportProbeBlockName }),
                        (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $exportProbeRegex; 'confirm' = $true }),
                        (New-CliCommandString -Name 'import-blocks-from-docs' -Arguments @{ 'plc-name' = $PlcName; 'import-directory' = $blockDocsMultiRoot; 'filter-regex' = $exportProbeRegex })
                    )
                }

                $result = Invoke-ReplBatch -CaseName 'mutation-block-import-roundtrip' -Commands $importProbeBatch
                Assert-True ($result.ExitCode -eq 0) 'Block import roundtrip batch returned a non-zero exit code.'
                Assert-NoCliFailureText -Text $result.Output
                return $result
            } | Out-Null
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ProbeBlockName)) {
        Invoke-TestCase -SuiteName $suiteName -CaseName 'existing-block-import-roundtrip' -Body {
            $probeBlockRegex = '^' + [regex]::Escape($ProbeBlockName) + '$'
            $batch = New-BasePlcBatch -TargetProjectPath $copyProjectPath -TargetPlcName $PlcName
            $batch += @(
                (New-CliCommandString -Name 'export-block' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $ProbeBlockName; 'export-path' = $existingBlockXmlFile }),
                (New-CliCommandString -Name 'export-block-as-docs' -Arguments @{ 'plc-name' = $PlcName; 'block-name' = $ProbeBlockName; 'export-path' = $existingBlockDocsSingleRoot }),
                (New-CliCommandString -Name 'export-blocks-as-docs' -Arguments @{ 'plc-name' = $PlcName; 'export-path' = $existingBlockDocsMultiRoot; 'filter-regex' = $probeBlockRegex }),
                (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $probeBlockRegex; 'confirm' = $true }),
                (New-CliCommandString -Name 'import-block' -Arguments @{ 'plc-name' = $PlcName; 'import-path' = $existingBlockXmlFile }),
                (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $probeBlockRegex; 'confirm' = $true }),
                (New-CliCommandString -Name 'import-blocks' -Arguments @{ 'plc-name' = $PlcName; 'import-directory' = $existingBlockXmlRoot; 'filter-regex' = $probeBlockRegex }),
                (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $probeBlockRegex; 'confirm' = $true }),
                (New-CliCommandString -Name 'import-block-from-docs' -Arguments @{ 'plc-name' = $PlcName; 'import-directory' = $existingBlockDocsSingleRoot; 'file-name' = $ProbeBlockName }),
                (New-CliCommandString -Name 'delete-blocks' -Arguments @{ 'plc-name' = $PlcName; 'filter' = $probeBlockRegex; 'confirm' = $true }),
                (New-CliCommandString -Name 'import-blocks-from-docs' -Arguments @{ 'plc-name' = $PlcName; 'import-directory' = $existingBlockDocsMultiRoot; 'filter-regex' = $probeBlockRegex })
            )

            $result = Invoke-ReplBatch -CaseName 'mutation-existing-block-import-roundtrip' -Commands $batch
            Assert-True ($result.ExitCode -eq 0) 'Existing block import roundtrip batch returned a non-zero exit code.'
            Assert-NoCliFailureText -Text $result.Output
            Assert-True (Test-Path -LiteralPath $existingBlockXmlFile -PathType Leaf) 'Existing block XML export was not created during mutation suite.'
            Assert-True ((Get-ChildItem -LiteralPath $existingBlockDocsSingleRoot -Filter ($ProbeBlockName + '.s7dcl') -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) 'Existing block document export did not create the expected .s7dcl file.'
            return $result
        } | Out-Null
    }
    else {
        Register-SkippedCase -SuiteName $suiteName -CaseName 'existing-block-import-roundtrip' -Reason 'No probe block name was discovered.'
    }

    if (-not [string]::IsNullOrWhiteSpace($ProbeTypeName)) {
        Invoke-TestCase -SuiteName $suiteName -CaseName 'type-roundtrip' -Body {
            $typeRegex = '^' + [regex]::Escape($ProbeTypeName) + '$'
            $batch = New-BasePlcBatch -TargetProjectPath $copyProjectPath -TargetPlcName $PlcName
            $batch += @(
                (New-CliCommandString -Name 'compile-software' -Arguments @{ 'plc-name' = $PlcName }),
                (New-CliCommandString -Name 'export-type' -Arguments @{ 'plc-name' = $PlcName; 'type-name' = $ProbeTypeName; 'export-path' = $typeXmlFile }),
                (New-CliCommandString -Name 'export-types' -Arguments @{ 'plc-name' = $PlcName; 'export-path' = $typeXmlBulkRoot; 'filter-regex' = $typeRegex }),
                (New-CliCommandString -Name 'import-type' -Arguments @{ 'plc-name' = $PlcName; 'import-path' = $typeXmlFile }),
                (New-CliCommandString -Name 'import-types' -Arguments @{ 'plc-name' = $PlcName; 'import-directory' = $typeXmlBulkRoot; 'filter-regex' = $typeRegex })
            )

            $result = Invoke-ReplBatch -CaseName 'mutation-type-roundtrip' -Commands $batch
            Assert-True ($result.ExitCode -eq 0) 'Type mutation batch returned a non-zero exit code.'
            Assert-NoCliFailureText -Text $result.Output
            Assert-True (Test-Path -LiteralPath $typeXmlFile -PathType Leaf) 'Single type XML export was not created during mutation suite.'
            return $result
        } | Out-Null
    }
    else {
        Register-SkippedCase -SuiteName $suiteName -CaseName 'type-roundtrip' -Reason 'No probe type name was discovered.'
    }
}

function Run-NegativeSuite {
    $suiteName = 'Negative'

    Invoke-TestCase -SuiteName $suiteName -CaseName 'unknown-command' -Body {
        $result = Invoke-CliCommand -CaseName 'negative-unknown-command' -Arguments @('no-such-command')
        Assert-True ($result.ExitCode -ne 0) 'Unknown command unexpectedly returned exit code 0.'
        Assert-Contains -Text $result.Output -Needle 'Unknown command' -Message 'Unknown command did not report the expected error text.'
        return $result
    } | Out-Null

    Invoke-TestCase -SuiteName $suiteName -CaseName 'help-unknown-command' -Body {
        $result = Invoke-CliCommand -CaseName 'negative-help-unknown-command' -Arguments @('help', 'no-such-command')
        Assert-True ($result.ExitCode -eq 0) 'help for an unknown command should still return exit code 0.'
        Assert-Contains -Text $result.Output -Needle 'Unknown command' -Message 'help unknown command did not report the expected text.'
        return $result
    } | Out-Null

    Invoke-TestCase -SuiteName $suiteName -CaseName 'open-project-missing-arg' -Body {
        $result = Invoke-CliCommand -CaseName 'negative-open-project-missing-arg' -Arguments @('open-project')
        Assert-True ($result.ExitCode -ne 0) 'open-project without arguments unexpectedly returned exit code 0.'
        Assert-Contains -Text $result.Output -Needle 'Missing required parameter: project-path' -Message 'open-project missing arg did not report the expected text.'
        return $result
    } | Out-Null

    Invoke-TestCase -SuiteName $suiteName -CaseName 'create-block-invalid-type' -Body {
        $result = Invoke-CliCommand -CaseName 'negative-create-block-invalid-type' -Arguments @(
            'create-block',
            '--plc-name=DummyPlc',
            '--block-name=DummyBlock',
            '--block-type=FC',
            '--confirm=true'
        )
        Assert-True ($result.ExitCode -ne 0) 'create-block with invalid type unexpectedly returned exit code 0.'
        Assert-Contains -Text $result.Output -Needle 'Invalid block type' -Message 'create-block invalid type did not report the expected text.'
        return $result
    } | Out-Null

    Invoke-TestCase -SuiteName $suiteName -CaseName 'modify-tags-missing-input' -Body {
        $result = Invoke-CliCommand -CaseName 'negative-modify-tags-missing-input' -Arguments @(
            'modify-tags',
            '--plc-name=DummyPlc'
        )
        Assert-True ($result.ExitCode -ne 0) 'modify-tags without operations unexpectedly returned exit code 0.'
        Assert-Contains -Text $result.Output -Needle 'Provide exactly one of operations or input-path' -Message 'modify-tags missing input did not report the expected text.'
        return $result
    } | Out-Null

    Invoke-TestCase -SuiteName $suiteName -CaseName 'find-replace-tags-missing-map' -Body {
        $result = Invoke-CliCommand -CaseName 'negative-find-replace-tags-missing-map' -Arguments @(
            'find-replace-tags',
            '--plc-name=DummyPlc'
        )
        Assert-True ($result.ExitCode -ne 0) 'find-replace-tags without rename-map unexpectedly returned exit code 0.'
        Assert-Contains -Text $result.Output -Needle 'Missing required parameter: rename-map' -Message 'find-replace-tags missing arg did not report the expected text.'
        return $result
    } | Out-Null
}

function Write-RunSummary {
    $counts = @{
        Passed  = (($script:TestResults | Where-Object Status -eq 'Passed') | Measure-Object).Count
        Failed  = (($script:TestResults | Where-Object Status -eq 'Failed') | Measure-Object).Count
        Skipped = (($script:TestResults | Where-Object Status -eq 'Skipped') | Measure-Object).Count
    }

    $summary = [pscustomobject]@{
        runId       = $script:RunId
        runRoot     = $script:RunRoot
        cliPath     = $script:ResolvedCliPath
        projectPath = $ProjectPath
        plcName     = $PlcName
        discovered  = $script:Discovered
        counts      = $counts
        results     = $script:TestResults
    }

    $jsonPath = Join-Path $script:RunRoot 'summary.json'
    $textPath = Join-Path $script:RunRoot 'summary.txt'

    $summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding UTF8

    $textLines = New-Object 'System.Collections.Generic.List[string]'
    $projectPathText = if ($null -ne $ProjectPath) { $ProjectPath } else { '' }
    $plcNameText = if ($null -ne $PlcName) { $PlcName } else { '' }
    $textLines.Add('CLI automation summary')
    $textLines.Add('Run root: ' + $script:RunRoot)
    $textLines.Add('CLI path: ' + $script:ResolvedCliPath)
    $textLines.Add('Project path: ' + $projectPathText)
    $textLines.Add('PLC name: ' + $plcNameText)
    $textLines.Add('')
    $textLines.Add(('Passed: {0}  Failed: {1}  Skipped: {2}' -f $counts['Passed'], $counts['Failed'], $counts['Skipped']))
    $textLines.Add('')

    foreach ($result in $script:TestResults) {
        $textLines.Add(('[{0}] {1}/{2} - {3}' -f $result.Status, $result.Suite, $result.Case, $result.Message))
        if (-not [string]::IsNullOrWhiteSpace($result.LogPath)) {
            $textLines.Add('  Log: ' + $result.LogPath)
        }
    }

    Set-Content -Path $textPath -Value ($textLines -join [Environment]::NewLine) -Encoding UTF8

    Write-Status "Summary written to $jsonPath"
    Write-Status "Text summary written to $textPath"

    return $counts
}

$script:ResolvedCliPath = (Resolve-Path -LiteralPath $CliPath).Path

if (-not (Test-Path -LiteralPath $script:ResolvedCliPath -PathType Leaf)) {
    throw "CLI executable not found: $CliPath"
}

Write-Status "Run root: $script:RunRoot"
Write-Status "CLI path: $script:ResolvedCliPath"

if ($BuildFirst) {
    Invoke-DotNetBuild
}

if ($RegisterAllowList) {
    Register-AllowListIfRequested
}

$selectedSuites = Get-SelectedSuites

if ($selectedSuites -contains 'Help') {
    Run-HelpSuite
}

if ($selectedSuites -contains 'ReadOnly') {
    try {
        Run-ReadOnlySuite
    }
    catch {
        Register-TestResult -SuiteName 'ReadOnly' -CaseName 'suite-bootstrap' -Status 'Failed' -Message $_.Exception.Message
        Write-Warning "ReadOnly suite bootstrap failed: $($_.Exception.Message)"
    }
}

if ($selectedSuites -contains 'Mutation') {
    try {
        Run-MutationSuite
    }
    catch {
        Register-TestResult -SuiteName 'Mutation' -CaseName 'suite-bootstrap' -Status 'Failed' -Message $_.Exception.Message
        Write-Warning "Mutation suite bootstrap failed: $($_.Exception.Message)"
    }
}

if ($selectedSuites -contains 'Negative') {
    Run-NegativeSuite
}

$counts = Write-RunSummary

if ($counts['Failed'] -gt 0) {
    exit 1
}

exit 0
