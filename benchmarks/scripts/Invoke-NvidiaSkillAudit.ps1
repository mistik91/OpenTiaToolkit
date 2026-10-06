[CmdletBinding()]
param(
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$SkillEvaluatorRevision = '14a888e2a8a2f3470ecd5b5712d5f3567bf7c111',

    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$benchmarkRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $benchmarkRoot
$skillsRoot = Join-Path $repoRoot 'skills'
$policyPath = Join-Path $benchmarkRoot 'skill-evaluation\nvidia-policy.yaml'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $OutputRoot = Join-Path $benchmarkRoot "artifacts\$stamp-nvidia-skill-audit"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

if ($null -eq (Get-Command uvx -ErrorAction SilentlyContinue)) {
    throw 'uvx is required for the pinned NVIDIA SkillEvaluator advisory audit.'
}

$oldPythonUtf8 = $env:PYTHONUTF8
$oldPythonIoEncoding = $env:PYTHONIOENCODING
try {
    # SkillEvaluator 0.2.1 emits Unicode report UI that needs an explicit UTF-8
    # console environment on Windows PowerShell.
    $env:PYTHONUTF8 = '1'
    $env:PYTHONIOENCODING = 'utf-8'
    & uvx `
        --from "git+https://github.com/NVIDIA/SkillEvaluator.git@$SkillEvaluatorRevision" `
        skillevaluator validate $skillsRoot `
        --checks schema,pii,license,quality,unicode,lint `
        --no-dedup `
        --policy $policyPath `
        -r json,markdown `
        -o $OutputRoot
    if ($LASTEXITCODE -ne 0) {
        throw "NVIDIA SkillEvaluator advisory audit failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:PYTHONUTF8 = $oldPythonUtf8
    $env:PYTHONIOENCODING = $oldPythonIoEncoding
}

Write-Host "NVIDIA SkillEvaluator advisory reports: $OutputRoot"
