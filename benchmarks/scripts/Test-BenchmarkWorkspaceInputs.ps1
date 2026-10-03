[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'BenchmarkWorkspaceInputs.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaWorkspaceInputTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$sourceRoot = Join-Path $testRoot 'case'
$workspace = Join-Path $testRoot 'workspace'
New-Item -ItemType Directory -Path $sourceRoot, $workspace | Out-Null
[IO.File]::WriteAllText((Join-Path $sourceRoot 'vector.json'), '{"frozen":true}', [Text.UTF8Encoding]::new($false))
$hash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot 'vector.json')).Hash
$definition = [pscustomobject]@{workspaceInputs=@([pscustomobject]@{sourcePath='vector.json';targetPath='inputs/vector.json';sha256=$hash})}
$copied = @(Copy-BenchmarkWorkspaceInputs -Case $definition -CaseDirectory $sourceRoot -WorkspacePath $workspace)
if ($copied.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $workspace 'inputs/vector.json')).Hash -cne $hash) { throw 'Staging did not preserve exact bytes.' }
$rejected = 0
foreach ($path in @('../escape', 'C:/escape', 'inputs/../vector', 'inputs//vector', 'input:stream', '.opencode/skills/inject.md', 'OPENCODE.JSON', 'task.md', '.git/config', 'inputs/trailing.', 'CON.json', 'inputs/LPT1')) {
    $bad = [pscustomobject]@{workspaceInputs=@([pscustomobject]@{sourcePath='vector.json';targetPath=$path;sha256=$hash})}
    try { [void]@(Get-BenchmarkWorkspaceInputPlan -Case $bad -CaseDirectory $sourceRoot -WorkspacePath $workspace) }
    catch { $rejected++; continue }
    throw "Unsafe path was accepted: $path"
}
foreach ($scenario in @('wrong-hash', 'duplicate', 'source-traversal', 'missing-hash', 'overwrite', 'overlap')) {
    $bad = ($definition | ConvertTo-Json -Depth 10 | ConvertFrom-Json)
    switch ($scenario) {
        'wrong-hash' { $bad.workspaceInputs[0].sha256 = '0' * 64 }
        'duplicate' { $bad.workspaceInputs += $bad.workspaceInputs[0] }
        'source-traversal' { $bad.workspaceInputs[0].sourcePath = '../case/vector.json' }
        'missing-hash' { $bad.workspaceInputs[0].PSObject.Properties.Remove('sha256') }
        'overlap' { $bad.workspaceInputs += [pscustomobject]@{sourcePath='vector.json';targetPath='inputs/vector.json/child';sha256=$hash} }
    }
    try { [void]@(Copy-BenchmarkWorkspaceInputs -Case $bad -CaseDirectory $sourceRoot -WorkspacePath $workspace) }
    catch { $rejected++; continue }
    throw "Unsafe input was accepted: $scenario"
}
if ((Get-FileHash -LiteralPath (Join-Path $workspace 'inputs/vector.json')).Hash -cne $hash) { throw 'Rejected inputs changed the original staged file.' }
$largePath = Join-Path $sourceRoot 'large.bin'
[IO.File]::WriteAllBytes($largePath, [byte[]]::new(1048577))
$large = [pscustomobject]@{workspaceInputs=@([pscustomobject]@{sourcePath='large.bin';targetPath='large.bin';sha256=(Get-FileHash -LiteralPath $largePath).Hash})}
try { [void]@(Get-BenchmarkWorkspaceInputPlan -Case $large -CaseDirectory $sourceRoot -WorkspacePath $workspace) }
catch { $rejected++ }
if ($rejected -ne 19) { throw 'Oversized input was accepted.' }
[IO.File]::WriteAllBytes($largePath, [byte[]]::new(1048576))
$largeHash = (Get-FileHash -LiteralPath $largePath).Hash
$total = [pscustomobject]@{workspaceInputs=@(1..5 | ForEach-Object { [pscustomobject]@{sourcePath='large.bin';targetPath="large-$_.bin";sha256=$largeHash} })}
try { [void]@(Get-BenchmarkWorkspaceInputPlan -Case $total -CaseDirectory $sourceRoot -WorkspacePath $workspace) }
catch { $rejected++ }
if ($rejected -ne 20) { throw 'Total byte limit was not enforced.' }
$outside = Join-Path $testRoot 'outside'
New-Item -ItemType Directory -Path $outside | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceRoot 'vector.json') -Destination (Join-Path $outside 'vector.json')
New-Item -ItemType Junction -Path (Join-Path $sourceRoot 'redirect') -Target $outside | Out-Null
$redirect = [pscustomobject]@{workspaceInputs=@([pscustomobject]@{sourcePath='redirect/vector.json';targetPath='redirected.json';sha256=$hash})}
try { [void]@(Get-BenchmarkWorkspaceInputPlan -Case $redirect -CaseDirectory $sourceRoot -WorkspacePath $workspace) }
catch { $rejected++ }
if ($rejected -ne 21) { throw 'A source junction was accepted.' }
Write-Host "PASS: exact input staging and $rejected fail-closed cases. Temporary evidence: $testRoot"
