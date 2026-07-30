[CmdletBinding()]
param(
    [string]$CliPath,
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CliPath)) {
    $CliPath = Join-Path $repoRoot 'src\OpenTiaToolkit.CLI\bin\Release\net48\OpenTiaToolkit.CLI.exe'
}

$resolvedCli = (Resolve-Path -LiteralPath $CliPath).Path
$catalogJson = (& $resolvedCli --command-catalog | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Command catalog generation failed with exit code $LASTEXITCODE."
}

$catalog = $catalogJson | ConvertFrom-Json
if ($catalog.commandCount -ne @($catalog.commands).Count) {
    throw 'Generated command catalog count does not match the command array.'
}

function Escape-MarkdownCell {
    param([object]$Value)
    if ($null -eq $Value) { return '' }
    return ([string]$Value).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

$commandReference = [System.Text.StringBuilder]::new()
[void]$commandReference.AppendLine('# Generated command reference')
[void]$commandReference.AppendLine()
[void]$commandReference.AppendLine('Generated from the runtime `CommandSchema` catalog. Do not edit by hand; run `scripts/Generate-CommandReference.ps1`.')
[void]$commandReference.AppendLine()
[void]$commandReference.AppendLine("Commands: **$($catalog.commandCount)**")

foreach ($group in @($catalog.commands | Group-Object category | Sort-Object Name)) {
    [void]$commandReference.AppendLine()
    [void]$commandReference.AppendLine("## $($group.Name)")
    foreach ($command in @($group.Group | Sort-Object name)) {
        [void]$commandReference.AppendLine()
        [void]$commandReference.AppendLine("### $($command.name)")
        [void]$commandReference.AppendLine()
        [void]$commandReference.AppendLine([string]$command.description)
        [void]$commandReference.AppendLine()
        if (@($command.parameters).Count -eq 0) {
            [void]$commandReference.AppendLine('No parameters.')
            continue
        }

        [void]$commandReference.AppendLine('| Parameter | Type | Required | Default | Description |')
        [void]$commandReference.AppendLine('|---|---|---:|---|---|')
        foreach ($parameter in @($command.parameters)) {
            $required = if ($parameter.required) { 'yes' } else { 'no' }
            $default = if ($null -eq $parameter.defaultValue) { '' } else { Escape-MarkdownCell $parameter.defaultValue }
            [void]$commandReference.AppendLine(
                "| ``$($parameter.name)`` | $($parameter.type) | $required | $default | $(Escape-MarkdownCell $parameter.description) |")
        }
    }
}

$mcpReference = [System.Text.StringBuilder]::new()
[void]$mcpReference.AppendLine('# Generated MCP tool reference')
[void]$mcpReference.AppendLine()
[void]$mcpReference.AppendLine('Generated from the same runtime command catalog used by the CLI. Every command below is exposed as an MCP tool with the listed kebab-case name and parameters.')
[void]$mcpReference.AppendLine()
[void]$mcpReference.AppendLine('| Tool | Domain | Parameters | Description |')
[void]$mcpReference.AppendLine('|---|---|---|---|')
foreach ($command in @($catalog.commands | Sort-Object name)) {
    $parameters = @($command.parameters | ForEach-Object { "``$($_.name)``" }) -join ', '
    [void]$mcpReference.AppendLine(
        "| ``$($command.name)`` | $(Escape-MarkdownCell $command.category) | $parameters | $(Escape-MarkdownCell $command.description) |")
}

[void]$mcpReference.AppendLine()
[void]$mcpReference.AppendLine('## Infrastructure tools')
[void]$mcpReference.AppendLine()
[void]$mcpReference.AppendLine('| Tool | Description |')
[void]$mcpReference.AppendLine('|---|---|')
[void]$mcpReference.AppendLine('| `operation-status` | Reports the active serialized operation. |')
[void]$mcpReference.AppendLine('| `cancel-operation` | Requests cancellation, then terminates and replaces the worker running the active operation. |')
[void]$mcpReference.AppendLine('| `reset-session` | Requests cancellation and waits for quiescence, then replaces the worker; fails closed after the configured grace period. |')
[void]$mcpReference.AppendLine()
[void]$mcpReference.AppendLine('MCP path parameters are restricted by `OPEN_TIA_MCP_ALLOWED_ROOTS`, input size by `OPEN_TIA_MCP_MAX_INPUT_FILE_BYTES`, and explicit output overwrite by `OPEN_TIA_MCP_ALLOW_FILE_OVERWRITE`.')

$generatedRoot = Join-Path $repoRoot 'docs\generated'
$outputs = [ordered]@{
    (Join-Path $generatedRoot 'command-catalog.json') = $catalogJson + [Environment]::NewLine
    (Join-Path $generatedRoot 'COMMAND_REFERENCE.md') = $commandReference.ToString()
    (Join-Path $generatedRoot 'MCP_TOOL_REFERENCE.md') = $mcpReference.ToString()
}

if ($CheckOnly) {
    foreach ($entry in $outputs.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $entry.Key)) {
            throw "Generated command reference is missing: $($entry.Key)"
        }

        $actual = (Get-Content -LiteralPath $entry.Key -Raw).Replace("`r`n", "`n")
        $expected = ([string]$entry.Value).Replace("`r`n", "`n")
        if ($actual -ne $expected) {
            throw "Generated command reference is stale: $($entry.Key)"
        }
    }

    [pscustomobject]@{ CommandCount = $catalog.commandCount; Status = 'Current' }
    return
}

New-Item -ItemType Directory -Force -Path $generatedRoot | Out-Null
foreach ($entry in $outputs.GetEnumerator()) {
    Set-Content -LiteralPath $entry.Key -Value ([string]$entry.Value) -Encoding UTF8 -NoNewline
}

[pscustomobject]@{ CommandCount = $catalog.commandCount; OutputDirectory = $generatedRoot }
