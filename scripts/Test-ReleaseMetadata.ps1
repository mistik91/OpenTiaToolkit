[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PayloadRoot,
    [string]$MetadataRoot,
    [string]$ExpectedCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedPayloadRoot = (Resolve-Path -LiteralPath $PayloadRoot).Path
if ([string]::IsNullOrWhiteSpace($MetadataRoot)) { $MetadataRoot = Join-Path $resolvedPayloadRoot 'release-metadata' }
$provenance = Get-Content -LiteralPath (Join-Path $MetadataRoot 'provenance.intoto.json') -Raw | ConvertFrom-Json
$checksumMap = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $MetadataRoot 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw "Invalid checksum entry." }
    $name = $Matches[2]
    if ($checksumMap.ContainsKey($name)) { throw "Duplicate checksum subject: $name" }
    $checksumMap[$name] = $Matches[1].ToLowerInvariant()
}
if (@($provenance.subject).Count -eq 0 -or @($provenance.subject).Count -ne $checksumMap.Count) {
    throw 'Provenance and checksum inventories differ or are empty.'
}
$seen = @{}
foreach ($subject in $provenance.subject) {
    $name = [string]$subject.name
    if ([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::IsPathRooted($name) -or $name -match ':|(^|[/\\])\.\.($|[/\\])') {
        throw "Provenance subject must be payload-relative: $name"
    }
    if ($seen.ContainsKey($name) -or -not $checksumMap.ContainsKey($name)) { throw "Unexpected or duplicate provenance subject: $name" }
    $seen[$name] = $true
    $actual = (Get-FileHash -LiteralPath (Join-Path $resolvedPayloadRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $checksumMap[$name] -or $actual -ne $subject.digest.sha256) { throw "Payload hash mismatch: $name" }
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit) -and $provenance.predicate.buildDefinition.externalParameters.commit -ne $ExpectedCommit) {
    throw 'Release provenance commit differs from the expected source commit.'
}
Write-Output "PASS: $($seen.Count) relative release subjects match payload hashes, checksums, and provenance."
