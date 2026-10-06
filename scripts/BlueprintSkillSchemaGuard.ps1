function Assert-BundledBlueprintSkillSchema {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$SkillDirectory,
        [Parameter(Mandatory=$true)][string]$SourceSchemaPath
    )

    $reference = 'references/engineering-blueprint.schema.json'
    $skillPath = Join-Path $SkillDirectory 'SKILL.md'
    $bundledPath = Join-Path $SkillDirectory $reference
    foreach ($path in @($skillPath, $SourceSchemaPath, $bundledPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Blueprint skill schema resource is missing: $path"
        }
    }

    $links = @([regex]::Matches((Get-Content -LiteralPath $skillPath -Raw), '\[[^\]]+\]\(([^)]+)\)') |
        ForEach-Object { $_.Groups[1].Value })
    if ($reference -cnotin $links) {
        throw 'Blueprint SKILL.md must link its bundled skill-local schema.'
    }

    [void](Get-Content -LiteralPath $SourceSchemaPath -Raw | ConvertFrom-Json)
    if ((Get-FileHash -LiteralPath $SourceSchemaPath -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath $bundledPath -Algorithm SHA256).Hash) {
        throw "Bundled blueprint schema differs from its canonical source: $bundledPath"
    }
}
