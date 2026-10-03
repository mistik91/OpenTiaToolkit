[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$CaseId,

    [Parameter(Mandatory = $true)]
    [string]$Model,

    [string]$Variant,

    [string]$ReasoningEffort,

    [ValidateSet('standard', 'with-skill', 'without-skill')]
    [string]$SkillEvaluationArm = 'standard',

    [string]$SkillEvaluationTarget,

    [ValidateRange(1, 1000)]
    [int]$TrialIndex = 1,

    [string]$FixtureArchivePath,

    [string]$MultiuserSessionPath,

    [int]$TiaInstanceId,

    [string]$OutputRoot,

    [string]$WorkspaceRoot,

    [string]$CliPath,

    [string]$McpPath,

    [switch]$UseUserOpenCodeAuth,

    [string]$UserAuthPath,

    [ValidateRange(1, 3600)]
    [int]$TimeoutOverrideSeconds,

    [switch]$PrepareOnly,

    [switch]$SkipFixtureRestore,

    [switch]$AllowPrimaryBranch,

    [switch]$FailOnCaseFailure
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OpenCodeBenchmark.Common.ps1')

$effectiveReasoningEffort = if (-not [string]::IsNullOrWhiteSpace($ReasoningEffort)) {
    $ReasoningEffort
}
elseif (-not [string]::IsNullOrWhiteSpace($Variant)) {
    $Variant
}
else {
    'provider-default'
}

if ([string]$effectiveReasoningEffort -eq 'provider-default' -and
    -not [string]::IsNullOrWhiteSpace($Variant)) {
    throw "ReasoningEffort 'provider-default' cannot be combined with an explicit Variant."
}
if ([string]$effectiveReasoningEffort -ne 'provider-default' -and
    [string]::IsNullOrWhiteSpace($Variant)) {
    throw "ReasoningEffort '$effectiveReasoningEffort' requires an explicit Variant."
}
if ($SkillEvaluationArm -eq 'standard' -and
    -not [string]::IsNullOrWhiteSpace($SkillEvaluationTarget)) {
    throw 'SkillEvaluationTarget can only be used with a paired skill-evaluation arm.'
}
if ($SkillEvaluationArm -ne 'standard' -and
    ([string]::IsNullOrWhiteSpace($SkillEvaluationTarget) -or
        $SkillEvaluationTarget -notmatch '^[a-z0-9][a-z0-9-]*$')) {
    throw "SkillEvaluationArm '$SkillEvaluationArm' requires a valid SkillEvaluationTarget."
}

function Stop-OwnedProcessTree {
    param([Parameter(Mandatory = $true)][int]$RootProcessId)

    if ($RootProcessId -le 0) {
        throw "Refusing to stop invalid process id '$RootProcessId'."
    }

    $processes = @(Get-CimInstance Win32_Process |
            Select-Object ProcessId, ParentProcessId)
    $owned = [System.Collections.Generic.HashSet[int]]::new()
    [void]$owned.Add($RootProcessId)

    do {
        $added = $false
        foreach ($process in $processes) {
            if ($owned.Contains([int]$process.ParentProcessId) -and
                -not $owned.Contains([int]$process.ProcessId)) {
                [void]$owned.Add([int]$process.ProcessId)
                $added = $true
            }
        }
    } while ($added)

    $ids = @($owned) | Sort-Object -Descending
    foreach ($processId in $ids) {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $process) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [Parameter(Mandatory = $true)][string]$StderrPath,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $process = Start-Process -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $StdoutPath `
        -RedirectStandardError $StderrPath `
        -WindowStyle Hidden `
        -PassThru

    $timedOut = $false
    try {
        Wait-Process -Id $process.Id -Timeout $TimeoutSeconds -ErrorAction Stop
    }
    catch {
        $stillRunning = $null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)
        if (-not $stillRunning) {
            throw
        }
        $timedOut = $true
        Stop-OwnedProcessTree -RootProcessId $process.Id
        Wait-Process -Id $process.Id -Timeout 15 -ErrorAction SilentlyContinue
    }

    $process.Refresh()
    $exitCode = if ($timedOut) {
        124
    }
    else {
        [int]$process.ExitCode
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        TimedOut = $timedOut
    }
}

function Get-OnlyTiaInstanceId {
    param(
        [Parameter(Mandatory = $true)][string]$CliExecutable,
        [int]$RequestedId
    )

    if ($RequestedId -gt 0) {
        return $RequestedId
    }

    $raw = (& $CliExecutable list-instances --format json | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "TIA instance discovery failed: $raw"
    }
    $result = $raw | ConvertFrom-Json
    $instances = @($result.data.instances)
    if ($instances.Count -ne 1) {
        throw "Expected exactly one running TIA instance, found $($instances.Count). Pass -TiaInstanceId when multiple instances are intentional."
    }
    return [int]$instances[0].processId
}

function Assert-SafeCliValue {
    param([Parameter(Mandatory = $true)][string]$Value, [string]$Label)
    if ($Value.Contains('"') -or $Value.Contains("`r") -or $Value.Contains("`n")) {
        throw "$Label contains a character that cannot be represented safely in a CLI command file."
    }
}

function Get-GitStateSha256 {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $statusLines = @(& git -C $RepositoryRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Git worktree state.'
    }
    $previousErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $diffLines = @(& git -C $RepositoryRoot diff --binary HEAD 2>$null)
        $diffExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }
    if ($diffExitCode -ne 0) {
        throw 'Unable to capture the tracked Git diff.'
    }
    $untrackedPaths = @(& git -C $RepositoryRoot ls-files --others --exclude-standard | Sort-Object)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to list untracked files.'
    }

    $state = [System.Text.StringBuilder]::new()
    [void]$state.AppendLine('STATUS')
    foreach ($line in $statusLines) {
        [void]$state.AppendLine([string]$line)
    }
    [void]$state.AppendLine('DIFF')
    foreach ($line in $diffLines) {
        [void]$state.AppendLine([string]$line)
    }
    [void]$state.AppendLine('UNTRACKED')
    foreach ($relativePath in $untrackedPaths) {
        $absolutePath = Join-Path $RepositoryRoot ([string]$relativePath)
        if (Test-Path -LiteralPath $absolutePath -PathType Leaf) {
            $hash = (Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash
            [void]$state.AppendLine("$relativePath`t$hash")
        }
    }

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($state.ToString())
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Restore-BenchmarkFixture {
    param(
        [Parameter(Mandatory = $true)][object]$Case,
        [Parameter(Mandatory = $true)][object]$Fixture,
        [Parameter(Mandatory = $true)][string]$RunDirectory,
        [Parameter(Mandatory = $true)][string]$CliExecutable,
        [Parameter(Mandatory = $true)][int]$InstanceId,
        [Parameter(Mandatory = $true)][string[]]$AllowedOpenProjectNames,
        [string]$ArchivePath,
        [string]$SessionPath
    )

    $preflightPath = Join-Path $RunDirectory 'fixture-preflight.commands.txt'
    $preflightOut = Join-Path $RunDirectory 'fixture-preflight.stdout.log'
    $preflightErr = Join-Path $RunDirectory 'fixture-preflight.stderr.log'
    @(
        "connect-instance --instance-id=$InstanceId"
        'list-projects'
    ) | Set-Content -LiteralPath $preflightPath -Encoding UTF8
    $preflight = Invoke-CapturedProcess `
        -FilePath $CliExecutable `
        -ArgumentList @('--script', "`"$preflightPath`"") `
        -WorkingDirectory $RunDirectory `
        -StdoutPath $preflightOut `
        -StderrPath $preflightErr `
        -TimeoutSeconds 120
    $preflightStdout = if (Test-Path -LiteralPath $preflightOut) { Get-Content -LiteralPath $preflightOut -Raw } else { '' }
    $preflightStderr = if (Test-Path -LiteralPath $preflightErr) { Get-Content -LiteralPath $preflightErr -Raw } else { '' }
    $preflightFailure = ($preflightStdout + "`n" + $preflightStderr) -match '(?im)^(Error(?:\s|\[)|Script stopped)'
    if ($preflight.ExitCode -ne 0 -or $preflightFailure) {
        throw "Fixture preflight failed (exit $($preflight.ExitCode)).`n$preflightStdout`n$preflightStderr"
    }
    $hasOpenProject = $preflightStdout.TrimEnd() -notmatch '\[\]\s*$'
    if ($hasOpenProject) {
        $isKnownBenchmarkProject = $false
        foreach ($allowedName in $AllowedOpenProjectNames) {
            $namePattern = '"Name"\s*:\s*"' + [regex]::Escape($allowedName) + '"'
            if ($preflightStdout -match $namePattern) {
                $isKnownBenchmarkProject = $true
                break
            }
        }
        if (-not $isKnownBenchmarkProject) {
            throw "Refusing to close the currently open project because it is not a fixture declared in '$manifestPath'.`n$preflightStdout"
        }
    }

    $commands = [System.Collections.Generic.List[string]]::new()
    $commands.Add("connect-instance --instance-id=$InstanceId")
    if ($hasOpenProject) {
        $commands.Add('close-project --confirm=true')
    }

    if ($Fixture.kind -eq 'tia-project-archive') {
        if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
            throw "Case '$($Case.id)' requires -FixtureArchivePath."
        }
        $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
        Assert-SafeCliValue -Value $resolvedArchive -Label 'Fixture archive path'
        $actualHash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash
        if ($actualHash -cne [string]$Fixture.sha256) {
            throw "Fixture hash mismatch for '$resolvedArchive'. Expected $($Fixture.sha256), got $actualHash."
        }

        $restoreRoot = Join-Path $RunDirectory 'restored'
        New-Item -ItemType Directory -Path $restoreRoot | Out-Null
        Assert-SafeCliValue -Value $restoreRoot -Label 'Fixture restore directory'
        $commands.Add("retrieve-project --archive-path=`"$resolvedArchive`" --target-directory=`"$restoreRoot`" --confirm=true")
        $commands.Add('save-project --confirm=true')
        $commands.Add('get-project-info')
    }
    elseif ($Fixture.kind -eq 'preprovisioned-multiuser-session') {
        if ([string]::IsNullOrWhiteSpace($SessionPath)) {
            throw "Case '$($Case.id)' requires -MultiuserSessionPath."
        }
        $resolvedSession = (Resolve-Path -LiteralPath $SessionPath).Path
        Assert-SafeCliValue -Value $resolvedSession -Label 'Multiuser session path'
        $commands.Add("open-project --project-path=`"$resolvedSession`"")
        $commands.Add('get-project-info')
    }
    else {
        throw "Unsupported fixture kind '$($Fixture.kind)'."
    }

    $commandPath = Join-Path $RunDirectory 'fixture-setup.commands.txt'
    $setupOut = Join-Path $RunDirectory 'fixture-setup.stdout.log'
    $setupErr = Join-Path $RunDirectory 'fixture-setup.stderr.log'
    $commands | Set-Content -LiteralPath $commandPath -Encoding UTF8

    $setup = Invoke-CapturedProcess `
        -FilePath $CliExecutable `
        -ArgumentList @('--script', "`"$commandPath`"") `
        -WorkingDirectory $RunDirectory `
        -StdoutPath $setupOut `
        -StderrPath $setupErr `
        -TimeoutSeconds 600
    $stderr = if (Test-Path -LiteralPath $setupErr) { Get-Content -LiteralPath $setupErr -Raw } else { '' }
    $stdout = if (Test-Path -LiteralPath $setupOut) { Get-Content -LiteralPath $setupOut -Raw } else { '' }
    $reportedFailure = ($stdout + "`n" + $stderr) -match '(?im)^(Error(?:\s|\[)|Script stopped)'
    $projectVerified = $stdout -match [regex]::Escape([string]$Fixture.projectName)
    if ($setup.ExitCode -ne 0 -or $reportedFailure -or -not $projectVerified) {
        throw "Fixture setup failed (exit $($setup.ExitCode)).`n$stdout`n$stderr"
    }
}

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $benchmarkRoot
$pluginRoot = Join-Path $repoRoot 'plugins\opentia-toolkit-opencode'
$manifestPath = Join-Path $benchmarkRoot 'fixtures\manifest.json'
$evaluatorPath = Join-Path $PSScriptRoot 'Measure-OpenCodeTrace.ps1'
$openCodeExecutable = Join-Path $benchmarkRoot 'node_modules\opencode-ai\bin\opencode.exe'

if (-not (Test-Path -LiteralPath $openCodeExecutable -PathType Leaf)) {
    throw "Pinned OpenCode executable not found. Run 'npm ci' in '$benchmarkRoot'."
}
if ($SkillEvaluationArm -ne 'standard' -and
    -not (Test-Path -LiteralPath (Join-Path (Join-Path $pluginRoot ".opencode\skills\$SkillEvaluationTarget") 'SKILL.md') -PathType Leaf)) {
    throw "SkillEvaluationTarget '$SkillEvaluationTarget' is not shipped in the OpenCode plugin."
}

& (Join-Path $PSScriptRoot 'Test-BenchmarkDefinitions.ps1')

$branch = (& git -C $repoRoot branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) {
    throw 'Unable to identify the current Git branch.'
}
if (-not $AllowPrimaryBranch -and $branch -in @('main', 'master')) {
    throw "Refusing to run live benchmarks from primary branch '$branch'. Create a disposable branch or pass -AllowPrimaryBranch explicitly."
}
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$gitStatus = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all)
$gitDirty = $gitStatus.Count -gt 0
$gitStateSha256 = Get-GitStateSha256 -RepositoryRoot $repoRoot

$cliExecutable = Resolve-ExistingFile `
    -ExplicitPath $CliPath `
    -SearchRoot (Join-Path $repoRoot 'src\OpenTiaToolkit.CLI\bin') `
    -Filter 'OpenTiaToolkit.CLI.exe' `
    -Label 'OpenTiaToolkit CLI'
$mcpExecutable = Resolve-ExistingFile `
    -ExplicitPath $McpPath `
    -SearchRoot (Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\bin') `
    -Filter 'OpenTiaToolkit.MCP.exe' `
    -Label 'OpenTiaToolkit MCP server'

$openCodeVersion = (& $openCodeExecutable --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $openCodeVersion -ne '1.18.10') {
    throw "Expected OpenCode 1.18.10, got '$openCodeVersion'."
}

$modelProvider = Get-OpenCodeModelProvider -Model $Model
$resolvedUserAuthPath = $null
if ($UseUserOpenCodeAuth) {
    $resolvedUserAuthPath = Resolve-OpenCodeUserAuthPath -ExplicitPath $UserAuthPath
    [void](Get-OpenCodeProviderCredential -AuthPath $resolvedUserAuthPath -Provider $modelProvider)
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $benchmarkRoot 'artifacts'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $systemDriveRoot = [System.IO.Path]::GetPathRoot([Environment]::SystemDirectory)
    $WorkspaceRoot = Join-Path $systemDriveRoot 'OTT\Bench'
}
$WorkspaceRoot = [System.IO.Path]::GetFullPath($WorkspaceRoot)
New-Item -ItemType Directory -Path $WorkspaceRoot -Force | Out-Null

$resolvedRepoRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
$resolvedWorkspaceRoot = $WorkspaceRoot.TrimEnd('\') + '\'
if ($resolvedWorkspaceRoot.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "WorkspaceRoot must be outside the repository so the model cannot inspect benchmark definitions or evaluators."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$knownProjectNames = @($manifest.fixtures | ForEach-Object { [string]$_.projectName } | Sort-Object -Unique)
$results = @()
$suiteResults = @()
$suiteStarted = [DateTime]::UtcNow
$suiteStamp = $suiteStarted.ToString('yyyyMMddTHHmmssfffZ')

foreach ($requestedCaseId in $CaseId) {
    $casePath = Join-Path (Join-Path (Join-Path $benchmarkRoot 'cases') $requestedCaseId) 'case.json'
    if (-not (Test-Path -LiteralPath $casePath -PathType Leaf)) {
        throw "Unknown benchmark case '$requestedCaseId'."
    }
    $case = Get-Content -LiteralPath $casePath -Raw | ConvertFrom-Json
    $fixture = @($manifest.fixtures | Where-Object { $_.id -eq $case.fixtureId })
    if ($fixture.Count -ne 1) {
        throw "Case '$requestedCaseId' references unresolved fixture '$($case.fixtureId)'."
    }
    $fixture = $fixture[0]

    $runStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $safeModel = $Model -replace '[^A-Za-z0-9._-]', '_'
    $modelHashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $modelHashBytes = [System.Text.Encoding]::UTF8.GetBytes($Model)
        $modelHash = ([BitConverter]::ToString($modelHashAlgorithm.ComputeHash($modelHashBytes))).Replace('-', '').Substring(0, 8)
    }
    finally {
        $modelHashAlgorithm.Dispose()
    }
    $armSuffix = if ($SkillEvaluationArm -eq 'standard') { '' } else { "-$SkillEvaluationArm" }
    $runDirectory = Join-Path $OutputRoot "$runStamp-$requestedCaseId-trial-$TrialIndex$armSuffix-$safeModel"
    $executionDirectory = Join-Path $WorkspaceRoot "$runStamp-$requestedCaseId-t$TrialIndex$armSuffix-$modelHash"
    $workspace = Join-Path $executionDirectory 'workspace'
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    $benchmarkMcpAllowedRoots = Get-BenchmarkMcpAllowedRoots -WorkspacePath $workspace
    $workspaceInputs = @(Copy-BenchmarkWorkspaceInputs -Case $case `
            -CaseDirectory (Split-Path -Parent $casePath) -WorkspacePath $workspace)

    if (-not $SkipFixtureRestore) {
        $instanceId = Get-OnlyTiaInstanceId -CliExecutable $cliExecutable -RequestedId $TiaInstanceId
        Restore-BenchmarkFixture `
            -Case $case `
            -Fixture $fixture `
            -RunDirectory $executionDirectory `
            -CliExecutable $cliExecutable `
            -InstanceId $instanceId `
            -AllowedOpenProjectNames $knownProjectNames `
            -ArchivePath $FixtureArchivePath `
            -SessionPath $MultiuserSessionPath
        Get-ChildItem -LiteralPath $executionDirectory -Filter 'fixture-setup.*' -File |
            Copy-Item -Destination $runDirectory
    }
    if ($PrepareOnly) {
        Write-Host "Prepared fixture '$($fixture.id)' for '$requestedCaseId' in '$executionDirectory'."
        continue
    }

    $visibleSkills = [System.Collections.Generic.List[string]]::new()
    foreach ($skill in @($case.allowedSkills)) {
        if ($SkillEvaluationArm -ne 'without-skill' -or
            [string]$skill -cne $SkillEvaluationTarget) {
            $visibleSkills.Add([string]$skill)
        }
    }
    if ($SkillEvaluationArm -eq 'with-skill' -and
        -not $visibleSkills.Contains($SkillEvaluationTarget)) {
        $visibleSkills.Add($SkillEvaluationTarget)
    }
    $copiedSkills = @(Copy-OpenCodeBenchmarkWorkspace `
            -PluginRoot $pluginRoot `
            -Workspace $workspace `
            -VisibleSkills @($visibleSkills))
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $casePath) $case.promptFile) `
        -Destination (Join-Path $workspace 'task.md')
    if (-not $SkipFixtureRestore) {
        $fixtureContext = @(
            '',
            'Harness-selected fixture context (verify independently before engineering):',
            "- TIA process ID: $instanceId",
            "- Fixture ID: $($fixture.id)",
            "- Disposable restore root: $(Join-Path $executionDirectory 'restored')",
            '- Do not select another project or TIA instance merely because its project name matches.'
        ) -join "`r`n"
        Add-Content -LiteralPath (Join-Path $workspace 'task.md') -Value $fixtureContext -Encoding UTF8
    }

    $skillPermissions = [ordered]@{ '*' = 'deny' }
    foreach ($skill in $copiedSkills) {
        $skillPermissions[[string]$skill] = 'allow'
    }
    $allowWorkspaceEdits = $null -ne $case.PSObject.Properties['allowWorkspaceEdits'] -and
        [bool]$case.allowWorkspaceEdits
    $config = [ordered]@{
        '$schema'  = 'https://opencode.ai/config.json'
        mcp        = [ordered]@{
            opentia = [ordered]@{
                type        = 'local'
                command     = @(
                    'powershell',
                    '-NoProfile',
                    '-ExecutionPolicy',
                    'Bypass',
                    '-File',
                    '.opencode/plugins/run-opentia-mcp.ps1'
                )
                environment = [ordered]@{
                    OPEN_TIA_MCP_LOG_LEVEL     = 'Error'
                    OPEN_TIA_MCP_ALLOWED_ROOTS = $benchmarkMcpAllowedRoots
                }
                enabled     = $true
                timeout     = 600000
            }
        }
        permission = [ordered]@{
            skill              = $skillPermissions
            'opentia_*'        = 'allow'
            read               = 'allow'
            glob               = 'allow'
            grep               = 'allow'
            list               = 'allow'
            bash               = 'deny'
            edit               = if ($allowWorkspaceEdits) { 'allow' } else { 'deny' }
            task               = 'deny'
            webfetch           = 'deny'
            websearch          = 'deny'
            question           = 'deny'
            external_directory = 'deny'
            doom_loop          = 'deny'
        }
    }
    $configPath = Join-Path $workspace 'opencode.json'
    $config | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $configPath -Encoding UTF8
    & (Join-Path $PSScriptRoot 'Assert-OpenTiaMcpWorkspacePolicy.ps1') `
        -McpPath $mcpExecutable `
        -WorkspacePath $workspace `
        -AllowedRoots $benchmarkMcpAllowedRoots |
        Out-Null

    $isolatedConfigRoot = Join-Path $executionDirectory 'xdg-config'
    $isolatedDataRoot = Join-Path $executionDirectory 'xdg-data'
    $isolatedCacheRoot = Join-Path $executionDirectory 'xdg-cache'
    $isolatedStateRoot = Join-Path $executionDirectory 'xdg-state'
    New-Item -ItemType Directory -Path $isolatedConfigRoot | Out-Null
    New-Item -ItemType Directory -Path $isolatedDataRoot | Out-Null
    New-Item -ItemType Directory -Path $isolatedCacheRoot | Out-Null
    New-Item -ItemType Directory -Path $isolatedStateRoot | Out-Null
    $isolatedAuthPath = Join-Path $isolatedDataRoot 'opencode\auth.json'
    if ($UseUserOpenCodeAuth) {
        [void](New-ProviderScopedOpenCodeAuthFile `
                -SourceAuthPath $resolvedUserAuthPath `
                -Provider $modelProvider `
                -DestinationPath $isolatedAuthPath)
    }
    $tracePath = Join-Path $runDirectory 'events.jsonl'
    $stderrPath = Join-Path $runDirectory 'opencode.stderr.log'

    $oldXdg = $env:XDG_CONFIG_HOME
    $oldXdgData = $env:XDG_DATA_HOME
    $oldXdgCache = $env:XDG_CACHE_HOME
    $oldXdgState = $env:XDG_STATE_HOME
    $oldConfig = $env:OPENCODE_CONFIG
    $oldConfigDir = $env:OPENCODE_CONFIG_DIR
    $oldClaudeCompatibility = $env:OPENCODE_DISABLE_CLAUDE_CODE
    $oldMcpExecutable = $env:OPEN_TIA_MCP_EXE
    try {
        $env:XDG_CONFIG_HOME = $isolatedConfigRoot
        $env:XDG_DATA_HOME = $isolatedDataRoot
        $env:XDG_CACHE_HOME = $isolatedCacheRoot
        $env:XDG_STATE_HOME = $isolatedStateRoot
        $env:OPENCODE_CONFIG = $configPath
        $env:OPENCODE_CONFIG_DIR = Join-Path $workspace '.opencode'
        $env:OPENCODE_DISABLE_CLAUDE_CODE = '1'
        $env:OPEN_TIA_MCP_EXE = $mcpExecutable

        $arguments = @(
            'run',
            '--format', 'json',
            '--auto',
            '--model', $Model,
            '--dir', "`"$workspace`""
        )
        if (-not [string]::IsNullOrWhiteSpace($Variant)) {
            $arguments += @('--variant', $Variant)
        }
        $arguments += '"Read task.md completely and carry out the requested task."'

        $started = [DateTime]::UtcNow
        $effectiveTimeout = if ($TimeoutOverrideSeconds -gt 0) {
            $TimeoutOverrideSeconds
        }
        else {
            [int]$case.timeoutSeconds
        }
        $execution = Invoke-CapturedProcess `
            -FilePath $openCodeExecutable `
            -ArgumentList $arguments `
            -WorkingDirectory $workspace `
            -StdoutPath $tracePath `
            -StderrPath $stderrPath `
            -TimeoutSeconds $effectiveTimeout
        $finished = [DateTime]::UtcNow
    }
    finally {
        $env:XDG_CONFIG_HOME = $oldXdg
        $env:XDG_DATA_HOME = $oldXdgData
        $env:XDG_CACHE_HOME = $oldXdgCache
        $env:XDG_STATE_HOME = $oldXdgState
        $env:OPENCODE_CONFIG = $oldConfig
        $env:OPENCODE_CONFIG_DIR = $oldConfigDir
        $env:OPENCODE_DISABLE_CLAUDE_CODE = $oldClaudeCompatibility
        $env:OPEN_TIA_MCP_EXE = $oldMcpExecutable
        if ($UseUserOpenCodeAuth) {
            Remove-BenchmarkSecretFile -Path $isolatedAuthPath
        }
    }

    $metadata = [ordered]@{
        model           = $Model
        trialIndex      = $TrialIndex
        reasoningEffort = $effectiveReasoningEffort
        variant         = if ([string]::IsNullOrWhiteSpace($Variant)) { $null } else { $Variant }
        authProvider    = if ($UseUserOpenCodeAuth) { $modelProvider } else { $null }
        authSource      = if ($UseUserOpenCodeAuth) { 'user-opencode-provider-scoped' } else { 'isolated' }
        openCodeVersion = $openCodeVersion
        gitCommit       = $commit
        gitBranch       = $branch
        gitDirty        = $gitDirty
        gitStateSha256  = $gitStateSha256
        fixtureSha256   = if ($null -ne $fixture.PSObject.Properties['sha256']) {
            [string]$fixture.sha256
        }
        else {
            $null
        }
        startedAtUtc    = $started.ToString('o')
        finishedAtUtc   = $finished.ToString('o')
        durationSeconds = [Math]::Round(($finished - $started).TotalSeconds, 3)
        exitCode        = [int]$execution.ExitCode
        timedOut        = [bool]$execution.TimedOut
        workspacePath   = $workspace
        workspaceInputs = @($workspaceInputs | Select-Object RelativePath, Sha256, Bytes)
        mcpAllowedRoots = $benchmarkMcpAllowedRoots
        mcpWorkspacePolicyPreflight = 'passed'
        skillEvaluation = if ($SkillEvaluationArm -eq 'standard') {
            $null
        }
        else {
            [ordered]@{
                arm           = $SkillEvaluationArm
                targetSkill   = $SkillEvaluationTarget
                visibleSkills = @($copiedSkills)
            }
        }
    }
    $metadataPath = Join-Path $runDirectory 'metadata.json'
    $resultPath = Join-Path $runDirectory 'result.json'
    $metadata | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $result = & $evaluatorPath `
        -CasePath $casePath `
        -TracePath $tracePath `
        -MetadataPath $metadataPath `
        -OutputPath $resultPath
    $results += $result
    $executionFailureKind = Get-BenchmarkTrialExecutionFailureKind -Trial $result
    $targetSkillCalls = @(if ($SkillEvaluationArm -ne 'standard') {
            Get-BenchmarkSkillCalls -Result $result -SkillName $SkillEvaluationTarget
        })
    $suiteResults += [pscustomobject][ordered]@{
        caseId          = [string]$result.caseId
        trialIndex      = [int]$result.trialIndex
        score           = [double]$result.score
        passed          = [bool]$result.passed
        durationSeconds = [double]$result.durationSeconds
        timedOut        = [bool]$result.timedOut
        exitCode        = [int]$result.exitCode
        validForModelComparison = [bool]$result.validForModelComparison
        executionFailureKind = $executionFailureKind
        criticalFailure = @($result.assertions |
                Where-Object { [bool]$_.critical -and -not [bool]$_.passed }).Count -gt 0
        cost            = [double]$result.usage.cost
        reasoningTokens = [long]$result.usage.tokens.reasoning
        validatorBlockedCallCount = [int]$result.validatorInterventions.blockedCallCount
        validatorIssueCount = [int]$result.validatorInterventions.issueCount
        resultPath      = $resultPath
        skillEvaluationArm = if ($SkillEvaluationArm -eq 'standard') {
            $null
        }
        else {
            $SkillEvaluationArm
        }
        skillEvaluationTarget = if ($SkillEvaluationArm -eq 'standard') {
            $null
        }
        else {
            $SkillEvaluationTarget
        }
        targetSkillAttempted = @($targetSkillCalls).Count -gt 0
        targetSkillLoaded = @($targetSkillCalls | Where-Object {
                [string]$_.status -ceq 'completed'
            }).Count -gt 0
    }

    Write-Host ("{0} trial {1}: score={2} passed={3} validForComparison={4} artifacts={5}" -f
        $result.caseId, $result.trialIndex, $result.score, $result.passed,
        $result.validForModelComparison, $runDirectory)
}

$results
if ($suiteResults.Count -gt 0) {
    $suiteFinished = [DateTime]::UtcNow
    $statistics = Get-BenchmarkTrialStatistics -Trials @($suiteResults)
    $totalCost = [double](($suiteResults | Measure-Object -Property cost -Sum).Sum)
    $suiteSummary = [ordered]@{
        schemaVersion   = 2
        model           = $Model
        trialIndex      = $TrialIndex
        reasoningEffort = $effectiveReasoningEffort
        variant         = if ([string]::IsNullOrWhiteSpace($Variant)) { $null } else { $Variant }
        authProvider    = if ($UseUserOpenCodeAuth) { $modelProvider } else { $null }
        authSource      = if ($UseUserOpenCodeAuth) { 'user-opencode-provider-scoped' } else { 'isolated' }
        skillEvaluation = if ($SkillEvaluationArm -eq 'standard') {
            $null
        }
        else {
            [ordered]@{
                arm           = $SkillEvaluationArm
                targetSkill   = $SkillEvaluationTarget
                visibleSkills = @($copiedSkills)
            }
        }
        openCodeVersion = $openCodeVersion
        gitCommit       = $commit
        gitBranch       = $branch
        gitDirty        = $gitDirty
        gitStateSha256  = $gitStateSha256
        startedAtUtc    = $suiteStarted.ToString('o')
        finishedAtUtc   = $suiteFinished.ToString('o')
        durationSeconds = [Math]::Round(($suiteFinished - $suiteStarted).TotalSeconds, 3)
        caseCount       = $suiteResults.Count
        validCaseCount  = [int]$statistics.validTrialCount
        invalidCaseCount = [int]$statistics.invalidTrialCount
        endpointInvalidCount = [int]$statistics.endpointInvalidCount
        passedCount     = @($suiteResults | Where-Object { $_.passed }).Count
        meanScore       = $statistics.meanScore
        rawMeanScore    = [double]$statistics.rawMeanScore
        totalCost       = $totalCost
        results         = $suiteResults
    }
    $suitePath = Join-Path $OutputRoot "suite-$suiteStamp-trial-$TrialIndex-$safeModel.json"
    $suiteSummary | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $suitePath -Encoding UTF8
    Write-Host "Suite summary: $suitePath"
}
if ($FailOnCaseFailure -and @($results | Where-Object { -not [bool]$_.passed }).Count -gt 0) {
    throw 'One or more benchmark cases failed.'
}
