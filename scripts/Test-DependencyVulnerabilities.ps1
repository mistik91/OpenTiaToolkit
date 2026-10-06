[CmdletBinding()]
param(
    [string]$SolutionPath = '.\OpenTiaToolkit.sln',
    [string]$InventoryPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    $json = & dotnet list $SolutionPath package --vulnerable --include-transitive --format json --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw "Dependency vulnerability query failed with exit code $LASTEXITCODE." }
    $inventory = ($json -join [Environment]::NewLine) | ConvertFrom-Json
} else {
    $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
}

if (-not $inventory.PSObject.Properties['projects']) { throw 'Dependency inventory has no projects array.' }
$findings = @()
foreach ($project in $inventory.projects) {
    # dotnet omits frameworks for projects with no vulnerable packages.
    if (-not $project.PSObject.Properties['frameworks']) { continue }
    foreach ($framework in $project.frameworks) {
        foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
            if (-not $framework.PSObject.Properties[$kind]) { continue }
            foreach ($package in $framework.$kind) {
                if ($package.PSObject.Properties['vulnerabilities'] -and @($package.vulnerabilities).Count -gt 0) {
                    $findings += "$($project.path): $($package.id) $($package.resolvedVersion)"
                }
            }
        }
    }
}
if ($findings.Count -gt 0) { throw "Vulnerable dependencies detected:`n$($findings -join [Environment]::NewLine)" }
Write-Output 'PASS: dependency inventory contains no reported vulnerabilities.'
