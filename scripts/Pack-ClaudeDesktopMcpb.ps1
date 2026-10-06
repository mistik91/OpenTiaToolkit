param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = ".\artifacts\claude-desktop-mcpb",
    [switch]$SkipBuild,
    [switch]$RegisterOpenness
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$sourceDir = Join-Path $repoRoot "plugins\opentia-toolkit-claude-desktop"
$mcpProject = Join-Path $repoRoot "src\OpenTiaToolkit.MCP\OpenTiaToolkit.MCP.csproj"
$mcpBin = Join-Path $repoRoot "src\OpenTiaToolkit.MCP\bin\$Configuration\net48"
$cliProject = Join-Path $repoRoot "src\OpenTiaToolkit.CLI\OpenTiaToolkit.CLI.csproj"
$cliBin = Join-Path $repoRoot "src\OpenTiaToolkit.CLI\bin\$Configuration\net48"
$workerProject = Join-Path $repoRoot "src\OpenTiaToolkit.Worker.V21\OpenTiaToolkit.Worker.V21.csproj"
$workerBin = Join-Path $repoRoot "src\OpenTiaToolkit.Worker.V21\bin\$Configuration\net48"
if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $resolvedOutputRoot = $OutputRoot
}
else {
    $resolvedOutputRoot = Join-Path $repoRoot $OutputRoot
}
New-Item -ItemType Directory -Force -Path $resolvedOutputRoot | Out-Null
$resolvedOutputRoot = Resolve-Path $resolvedOutputRoot
$stageDir = Join-Path $resolvedOutputRoot "opentia-toolkit"
$serverDir = Join-Path $stageDir "server"
$bundlePath = Join-Path $resolvedOutputRoot "opentia-toolkit.mcpb"

if (-not $SkipBuild) {
    dotnet build $mcpProject -c $Configuration
    dotnet build $cliProject -c $Configuration
    dotnet build $workerProject -c $Configuration
}

if (-not (Test-Path (Join-Path $mcpBin "OpenTiaToolkit.MCP.exe"))) {
    throw "MCP executable not found at '$mcpBin'. Build the MCP project first or pass a valid -Configuration."
}
if (-not (Test-Path (Join-Path $workerBin "OpenTiaToolkit.Worker.V21.exe"))) {
    throw "V21 worker executable not found at '$workerBin'."
}
if (-not (Test-Path (Join-Path $cliBin "OpenTiaToolkit.CLI.exe"))) {
    throw "CLI executable not found at '$cliBin'."
}

if (Test-Path $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $serverDir | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceDir "manifest.json") -Destination (Join-Path $stageDir "manifest.json") -Force
Copy-Item -Path (Join-Path $mcpBin "*") -Destination $serverDir -Recurse -Force
$workerStageDir = Join-Path $serverDir 'worker-v21'
New-Item -ItemType Directory -Force -Path $workerStageDir | Out-Null
Copy-Item -Path (Join-Path $workerBin '*') -Destination $workerStageDir -Recurse -Force

$stagedExe = Join-Path $workerStageDir "OpenTiaToolkit.Worker.V21.exe"
$stagedMcpExe = Join-Path $serverDir "OpenTiaToolkit.MCP.exe"
$catalogExe = Join-Path $cliBin "OpenTiaToolkit.CLI.exe"

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\Test-McpStdio.ps1') `
    -ExecutablePath $stagedMcpExe `
    -CommandCatalogExecutablePath $catalogExe `
    -WorkerExecutablePath $stagedExe
if ($LASTEXITCODE -ne 0) {
    throw "Staged MCPB stable-SDK and stdio compatibility smoke failed with exit code $LASTEXITCODE."
}

if ($RegisterOpenness) {
    $registerScript = Join-Path $repoRoot "scripts\Register-OpennessAllowList.ps1"
    & powershell -ExecutionPolicy Bypass -File $registerScript -ExecutablePath $stagedExe
}

$mcpb = Get-Command mcpb -ErrorAction SilentlyContinue
if ($mcpb) {
    & mcpb validate (Join-Path $stageDir "manifest.json")
    & mcpb pack $stageDir $bundlePath
    Write-Host "Created $bundlePath"
}
else {
    $npx = Get-Command npx -ErrorAction SilentlyContinue
    if ($npx) {
        & npx -y @anthropic-ai/mcpb validate (Join-Path $stageDir "manifest.json")
        & npx -y @anthropic-ai/mcpb pack $stageDir $bundlePath
        Write-Host "Created $bundlePath"
    }
    else {
        Write-Warning "mcpb CLI not found. Install it with 'npm install -g @anthropic-ai/mcpb', then run: mcpb pack `"$stageDir`" `"$bundlePath`""
        Write-Host "Staged MCPB source at $stageDir"
    }
}

if (Test-Path -LiteralPath $bundlePath -PathType Leaf) {
    $bundleHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$bundleHash  $([System.IO.Path]::GetFileName($bundlePath))" |
        Set-Content -LiteralPath (Join-Path $resolvedOutputRoot 'MCPB-SHA256SUMS.txt') -Encoding UTF8
    Write-Host "Created MCPB-SHA256SUMS.txt"
}
