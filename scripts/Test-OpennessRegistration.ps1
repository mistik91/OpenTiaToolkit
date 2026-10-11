[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Exercise the actual helper against an owned HKCU key, without administrator rights.
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Register-OpennessAllowList.ps1') -Raw
$boundary = $source.IndexOf('$resolvedExecutablePath =', [StringComparison]::Ordinal)
if ($boundary -lt 0) { throw 'Registration helper boundary is missing.' }
. ([scriptblock]::Create($source.Substring(0, $boundary))) -ExecutablePath 'unused-test-path'
$testNamespace = 'HKCU:\Software\OpenTiaToolkit\RegistrationTests\'
$testKey = $testNamespace + [Guid]::NewGuid().ToString('N')
try {
    $first = Ensure-RegistryEntry -ParentRegistryPath $testKey -ExecutablePath 'C:\First\Worker.exe' -DateModified '2026/10/08 00:00:00.000' -FileHash 'first-hash'
    $second = Ensure-RegistryEntry -ParentRegistryPath $testKey -ExecutablePath 'C:\Second\Worker.exe' -DateModified '2026/10/08 01:00:00.000' -FileHash 'second-hash'
    $repeat = Ensure-RegistryEntry -ParentRegistryPath $testKey -ExecutablePath 'C:\Second\Worker.exe' -DateModified '2026/10/08 01:00:00.000' -FileHash 'second-hash'
    $entries = @(Get-ChildItem -LiteralPath $testKey)
    $original = Get-ItemProperty -LiteralPath (Join-Path $testKey $first.EntryName)
    if ($entries.Count -ne 2 -or $original.Path -ne 'C:\First\Worker.exe' -or $original.FileHash -ne 'first-hash' -or $original.DateModified -ne '2026/10/08 00:00:00.000') {
        throw 'Adding a registration changed the existing registration.'
    }
    if ($repeat.Created -or $repeat.EntryName -ne $second.EntryName) { throw 'Identical registration was not idempotent.' }
    Write-Output 'PASS: existing registrations retained; identical registration is idempotent.'
} finally {
    if (-not $testKey.StartsWith($testNamespace, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup path escaped its namespace.' }
    if (Test-Path -LiteralPath $testKey) { Remove-Item -LiteralPath $testKey -Recurse -Force }
}
