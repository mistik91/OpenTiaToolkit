[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$casePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'cases\ott-03\case.json'
$case = Get-Content -LiteralPath $casePath -Raw | ConvertFrom-Json
$assertion = @($case.assertions | Where-Object id -eq 'tag-readback')
if ($assertion.Count -ne 1) {
    throw "Expected exactly one ott-03 tag-readback assertion, found $($assertion.Count)."
}

$pattern = [string]$assertion[0].pattern
$jsonTrace = @'
opentia_export-tags
{"TagTableName":"Default tag table","Name":"OccupiedBefore","DataType":"Bool","LogicalAddress":"%M99.7","Comment":"Benchmark boundary tag"}
opentia_export-tags
{"TagTableName":"Default tag table","Name":"OccupiedBefore","DataType":"Bool","LogicalAddress":"%M99.7","Comment":"Updated by benchmark OTT-03"}
'@
$csvTrace = @'
opentia_export-tags
TagTableName,Name,DataType,LogicalAddress,Comment
Default tag table,OccupiedBefore,Bool,%M99.7,Benchmark boundary tag
opentia_export-tags
TagTableName,Name,DataType,LogicalAddress,Comment
Default tag table,OccupiedBefore,Bool,%M99.7,Updated by benchmark OTT-03
'@
$singleReadTrace = @'
opentia_export-tags
{"TagTableName":"Default tag table","Name":"OccupiedBefore","DataType":"Bool","LogicalAddress":"%M99.7","Comment":"Updated by benchmark OTT-03"}
'@

if ($jsonTrace -notmatch $pattern) {
    throw 'ott-03 tag-readback did not accept structured JSON readback evidence.'
}
if ($csvTrace -notmatch $pattern) {
    throw 'ott-03 tag-readback did not accept CSV readback evidence.'
}
if ($singleReadTrace -match $pattern) {
    throw 'ott-03 tag-readback accepted only one export-tags call.'
}

Write-Host 'ott-03 readback assertion regression test passed.'
