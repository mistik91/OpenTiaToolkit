[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MultiuserMcpProbeAssertions.ps1')
$result = [pscustomobject]@{ success = $true; data = [pscustomobject]@{ IsUpToDate = $true; ConflictCount = 0; Name = 'ExactFixture' } }
$checks = 0
Assert-MultiuserMcpProbeResult $result ([pscustomobject]@{ 'success' = $true; 'data.IsUpToDate' = $true; 'data.ConflictCount' = 0; 'data.Name' = 'ExactFixture' })
$checks++
foreach ($bad in @(
    [pscustomobject]@{ 'data.IsUpToDate' = $false },
    [pscustomobject]@{ 'data.IsUpToDate' = 'true' },
    [pscustomobject]@{ 'data.ConflictCount' = 1 },
    [pscustomobject]@{ 'data.ConflictCount' = '0' },
    [pscustomobject]@{ 'data.Name' = 'exactfixture' },
    [pscustomobject]@{ 'data.Missing' = $null },
    [pscustomobject]@{ 'missing.property' = $true }
)) {
    $rejected = $false
    try { Assert-MultiuserMcpProbeResult $result $bad } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsafe or absent probe evidence was accepted.' }
    $checks++
}
Write-Host "PASS: $checks multiuser probe result assertions; no credentials, TIA, or processes used."
