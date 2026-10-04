[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BlueprintSkillSchemaGuard.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceSchema = Join-Path $repoRoot 'schemas/engineering-blueprint.schema.json'
$canonicalSkill = Join-Path $repoRoot 'skills/engineering-blueprint-workflow'
$skillRoots = @(
    $canonicalSkill,
    (Join-Path $repoRoot 'plugins/opentia-toolkit-agent-plugin/skills/engineering-blueprint-workflow'),
    (Join-Path $repoRoot 'plugins/opentia-toolkit/skills/engineering-blueprint-workflow'),
    (Join-Path $repoRoot 'plugins/opentia-toolkit-claude-code/skills/engineering-blueprint-workflow'),
    (Join-Path $repoRoot 'plugins/opentia-toolkit-opencode/.opencode/skills/engineering-blueprint-workflow')
)
foreach ($skill in $skillRoots) {
    Assert-BundledBlueprintSkillSchema -SkillDirectory $skill -SourceSchemaPath $sourceSchema
}

# Test the actual guard on a standalone installed-style directory, with no
# repository schema/examples adjacent to the skill and no production mutations.
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('OpenTiaBlueprintSkill-' + [Guid]::NewGuid().ToString('N'))
$skillDirectory = Join-Path $testRoot 'installed/engineering-blueprint-workflow'
$testSource = Join-Path $testRoot 'canonical.json'
$bundledPath = Join-Path $skillDirectory 'references/engineering-blueprint.schema.json'
$skillPath = Join-Path $skillDirectory 'SKILL.md'
New-Item -ItemType Directory -Path (Split-Path -Parent $bundledPath) | Out-Null
Copy-Item -LiteralPath $sourceSchema -Destination $testSource
Copy-Item -LiteralPath $sourceSchema -Destination $bundledPath
Copy-Item -LiteralPath (Join-Path $canonicalSkill 'SKILL.md') -Destination $skillPath
Assert-BundledBlueprintSkillSchema -SkillDirectory $skillDirectory -SourceSchemaPath $testSource
$rejected = 0
foreach ($scenario in @('missing-copy','changed-copy','changed-source','repository-only-link')) {
    Copy-Item -LiteralPath $sourceSchema -Destination $testSource -Force
    Copy-Item -LiteralPath $sourceSchema -Destination $bundledPath -Force
    Copy-Item -LiteralPath (Join-Path $canonicalSkill 'SKILL.md') -Destination $skillPath -Force
    switch ($scenario) {
        'missing-copy' {
            # Rename this exact synthetic leaf; do not recursively delete paths.
            Move-Item -LiteralPath $bundledPath -Destination ($bundledPath + '.missing')
        }
        'changed-copy' { [IO.File]::AppendAllText($bundledPath, ' ') }
        'changed-source' { [IO.File]::AppendAllText($testSource, ' ') }
        'repository-only-link' {
            $text = [IO.File]::ReadAllText($skillPath).Replace('references/engineering-blueprint.schema.json', 'schemas/engineering-blueprint.schema.json')
            [IO.File]::WriteAllText($skillPath, $text)
        }
    }
    try {
        Assert-BundledBlueprintSkillSchema -SkillDirectory $skillDirectory -SourceSchemaPath $testSource
    } catch {
        $rejected++
        continue
    }
    throw "Invalid bundled skill schema accepted: $scenario"
}
if ($rejected -ne 4) { throw 'Blueprint schema resource negative-control count drifted.' }
Write-Output "PASS: canonical + four packaged schemas, standalone installed-style reference, $rejected negative controls. Evidence: $testRoot"
