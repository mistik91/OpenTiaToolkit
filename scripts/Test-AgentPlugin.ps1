[CmdletBinding()]
param(
    [string]$PluginRoot = '.\plugins\opentia-toolkit-agent-plugin',
    [string]$ExpectedVersion,
    [switch]$TestLauncher
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pluginSchema = 'https://agent-plugins.org/schemas/1.0.0/plugin.schema.json'
$mcpSchema = 'https://agent-plugins.org/schemas/1.0.0/mcp.schema.json'
$allowedManifestKeys = @('$schema', 'name', 'version', 'description', 'author', 'homepage', 'repository', 'license', 'keywords', 'extensions')
$allowedMcpKeys = @('$schema', 'mcpServers')
$allowedStdioKeys = @('type', 'command', 'args', 'env', 'cwd')

if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $propsPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props'
    if (Test-Path -LiteralPath $propsPath -PathType Leaf) {
        [xml]$props = Get-Content -LiteralPath $propsPath
        $ExpectedVersion = $props.Project.PropertyGroup.Version
    }
}

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function ConvertTo-Hashtable {
    param($InputObject)

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($entry in $InputObject.GetEnumerator()) {
            $result[$entry.Key] = ConvertTo-Hashtable -InputObject $entry.Value
        }
        return $result
    }

    if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
        $result = [ordered]@{}
        foreach ($property in $InputObject.PSObject.Properties) {
            $result[$property.Name] = ConvertTo-Hashtable -InputObject $property.Value
        }
        return $result
    }

    if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
        return ,@($InputObject | ForEach-Object { ConvertTo-Hashtable -InputObject $_ })
    }

    return $InputObject
}

function Read-JsonObject {
    param([string]$Path)

    Assert-Condition (Test-Path -LiteralPath $Path -PathType Leaf) "Required Agent Plugins file was not found: $Path"
    $value = ConvertTo-Hashtable -InputObject (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    Assert-Condition ($value -is [System.Collections.IDictionary]) "Agent Plugins JSON must contain a top-level object: $Path"
    return $value
}

function Assert-ClosedKeys {
    param(
        [System.Collections.IDictionary]$Object,
        [string[]]$Allowed,
        [string]$Context
    )

    $unknown = @($Object.Keys | Where-Object { $_ -notin $Allowed })
    Assert-Condition ($unknown.Count -eq 0) "$Context contains unsupported fields: $($unknown -join ', ')"
}

function Assert-StringValue {
    param(
        [System.Collections.IDictionary]$Object,
        [string]$Key,
        [string]$Context,
        [switch]$Required
    )

    if (-not $Object.Contains($Key)) {
        Assert-Condition (-not $Required) "$Context is missing required field '$Key'."
        return
    }

    Assert-Condition ($Object[$Key] -is [string]) "$Context field '$Key' must be a string."
    if ($Required) {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($Object[$Key])) "$Context field '$Key' must not be empty."
    }
}

function Assert-PluginRootPath {
    param(
        [string]$Value,
        [string]$Context,
        [string]$Root
    )

    if ($Value -notmatch '^\$\{PLUGIN_ROOT\}(?:/|$)') {
        return
    }

    $relative = $Value.Substring('${PLUGIN_ROOT}'.Length).TrimStart('/', '\')
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $Root ($relative -replace '/', [System.IO.Path]::DirectorySeparatorChar)))
    $normalizedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    Assert-Condition ($resolved.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) "$Context escapes the plugin root: $Value"
}

$resolvedPluginRoot = (Resolve-Path -LiteralPath $PluginRoot).Path
$manifestPath = Join-Path $resolvedPluginRoot 'plugin.json'
$mcpPath = Join-Path $resolvedPluginRoot 'mcp.json'
$manifest = Read-JsonObject -Path $manifestPath
$mcp = Read-JsonObject -Path $mcpPath

foreach ($legacyPath in @('.codex-plugin', '.claude-plugin', '.plugin', '.mcp.json', '.opencode', 'opencode.json')) {
    Assert-Condition (-not (Test-Path -LiteralPath (Join-Path $resolvedPluginRoot $legacyPath))) "Portable package contains legacy client-specific path '$legacyPath'. Keep legacy formats in separate packages."
}

Assert-ClosedKeys -Object $manifest -Allowed $allowedManifestKeys -Context 'plugin.json'
Assert-StringValue -Object $manifest -Key '$schema' -Context 'plugin.json' -Required
Assert-StringValue -Object $manifest -Key 'name' -Context 'plugin.json' -Required
Assert-Condition ($manifest['$schema'] -eq $pluginSchema) "plugin.json targets an unsupported schema: $($manifest['$schema'])"
Assert-Condition ($manifest.name.Length -le 64 -and $manifest.name -match '^(?!.*(?:--|\.\.))[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?$') "plugin.json name is invalid: $($manifest.name)"

foreach ($field in @('version', 'description', 'homepage', 'repository', 'license')) {
    Assert-StringValue -Object $manifest -Key $field -Context 'plugin.json'
}

if ($manifest.Contains('author')) {
    Assert-Condition ($manifest.author -is [System.Collections.IDictionary]) 'plugin.json author must be an object.'
    Assert-ClosedKeys -Object $manifest.author -Allowed @('name', 'email', 'url') -Context 'plugin.json author'
    foreach ($field in $manifest.author.Keys) {
        Assert-StringValue -Object $manifest.author -Key $field -Context 'plugin.json author'
    }
}

if ($manifest.Contains('keywords')) {
    Assert-Condition ($manifest.keywords -is [System.Collections.IList]) 'plugin.json keywords must be an array.'
    foreach ($keyword in @($manifest.keywords)) {
        Assert-Condition ($keyword -is [string]) 'Every plugin.json keyword must be a string.'
    }
}

if ($manifest.Contains('extensions')) {
    Assert-Condition ($manifest.extensions -is [System.Collections.IDictionary]) 'plugin.json extensions must be an object.'
    foreach ($extension in $manifest.extensions.GetEnumerator()) {
        Assert-Condition ($extension.Value -is [System.Collections.IDictionary]) "plugin.json extension '$($extension.Key)' must be an object."
    }
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    Assert-Condition ($manifest.Contains('version')) 'plugin.json must declare a version matching the product release.'
    Assert-Condition ($manifest.version -eq $ExpectedVersion) "Agent plugin version '$($manifest.version)' does not match product version '$ExpectedVersion'."
}

Assert-ClosedKeys -Object $mcp -Allowed $allowedMcpKeys -Context 'mcp.json'
Assert-StringValue -Object $mcp -Key '$schema' -Context 'mcp.json' -Required
Assert-Condition ($mcp['$schema'] -eq $mcpSchema) "mcp.json targets an unsupported schema: $($mcp['$schema'])"
Assert-Condition ($mcp.Contains('mcpServers') -and $mcp.mcpServers -is [System.Collections.IDictionary]) 'mcp.json mcpServers must be an object.'
Assert-Condition ($mcp.mcpServers.Count -gt 0) 'mcp.json must define at least one MCP server.'

foreach ($serverEntry in $mcp.mcpServers.GetEnumerator()) {
    $server = $serverEntry.Value
    Assert-Condition ($server -is [System.Collections.IDictionary]) "MCP server '$($serverEntry.Key)' must be an object."
    Assert-ClosedKeys -Object $server -Allowed $allowedStdioKeys -Context "MCP server '$($serverEntry.Key)'"
    Assert-StringValue -Object $server -Key 'type' -Context "MCP server '$($serverEntry.Key)'" -Required
    Assert-StringValue -Object $server -Key 'command' -Context "MCP server '$($serverEntry.Key)'" -Required
    Assert-Condition ($server.type -eq 'stdio') "MCP server '$($serverEntry.Key)' must use the stdio transport."
    Assert-Condition ($server.command -notmatch '\s') "MCP server '$($serverEntry.Key)' command must be one executable token."
    Assert-Condition ($server.command -notmatch '\$\{') "MCP server '$($serverEntry.Key)' command must not contain placeholders."

    if ($server.command.StartsWith('./', [System.StringComparison]::Ordinal)) {
        $commandPath = [System.IO.Path]::GetFullPath((Join-Path $resolvedPluginRoot $server.command.Substring(2)))
        $rootPrefix = $resolvedPluginRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        Assert-Condition ($commandPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) "MCP server '$($serverEntry.Key)' command escapes the plugin root."
    }

    if ($server.Contains('args')) {
        Assert-Condition ($server.args -is [System.Collections.IList]) "MCP server '$($serverEntry.Key)' args must be an array."
        foreach ($argument in @($server.args)) {
            Assert-Condition ($argument -is [string]) "MCP server '$($serverEntry.Key)' args must contain only strings."
            Assert-Condition ($argument -notmatch '\$\{(?!PLUGIN_ROOT\}|PLUGIN_DATA\})') "MCP server '$($serverEntry.Key)' contains an unsupported placeholder: $argument"
            Assert-PluginRootPath -Value $argument -Context "MCP server '$($serverEntry.Key)' argument" -Root $resolvedPluginRoot
        }
    }

    if ($server.Contains('env')) {
        Assert-Condition ($server.env -is [System.Collections.IDictionary]) "MCP server '$($serverEntry.Key)' env must be an object."
        foreach ($environmentEntry in $server.env.GetEnumerator()) {
            Assert-Condition ($environmentEntry.Key -notin @('PLUGIN_ROOT', 'PLUGIN_DATA')) "MCP server '$($serverEntry.Key)' must not override $($environmentEntry.Key)."
            Assert-Condition ($environmentEntry.Value -is [string]) "MCP server '$($serverEntry.Key)' environment values must be strings."
        }
    }

    if ($server.Contains('cwd')) {
        Assert-StringValue -Object $server -Key 'cwd' -Context "MCP server '$($serverEntry.Key)'"
        Assert-Condition ($server.cwd -match '^(?:\./|\$\{PLUGIN_ROOT\}(?:/|$)|\$\{PLUGIN_DATA\}(?:/|$))') "MCP server '$($serverEntry.Key)' cwd must be plugin-relative, PLUGIN_ROOT-rooted, or PLUGIN_DATA-rooted."
        Assert-PluginRootPath -Value $server.cwd -Context "MCP server '$($serverEntry.Key)' cwd" -Root $resolvedPluginRoot
    }
}

$skillsRoot = Join-Path $resolvedPluginRoot 'skills'
Assert-Condition (Test-Path -LiteralPath $skillsRoot -PathType Container) 'Agent plugin skills directory was not found.'
$skillDirectories = @(Get-ChildItem -LiteralPath $skillsRoot -Directory)
Assert-Condition ($skillDirectories.Count -gt 0) 'Agent plugin contains no skills.'

foreach ($skillDirectory in $skillDirectories) {
    $skillPath = Join-Path $skillDirectory.FullName 'SKILL.md'
    Assert-Condition (Test-Path -LiteralPath $skillPath -PathType Leaf) "Skill '$($skillDirectory.Name)' has no SKILL.md."
    $lines = @(Get-Content -LiteralPath $skillPath)
    Assert-Condition ($lines.Count -ge 4 -and $lines[0] -eq '---') "Skill '$($skillDirectory.Name)' has invalid frontmatter."
    $frontmatterEnd = [Array]::IndexOf($lines, '---', 1)
    Assert-Condition ($frontmatterEnd -gt 1) "Skill '$($skillDirectory.Name)' has unterminated frontmatter."
    $frontmatter = $lines[1..($frontmatterEnd - 1)]
    $nameLine = $frontmatter | Where-Object { $_ -match '^name:\s*(.+)$' } | Select-Object -First 1
    $descriptionLine = $frontmatter | Where-Object { $_ -match '^description:\s*(.+)$' } | Select-Object -First 1
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($nameLine)) "Skill '$($skillDirectory.Name)' frontmatter has no name."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($descriptionLine)) "Skill '$($skillDirectory.Name)' frontmatter has no description."
    $skillName = ([regex]::Match($nameLine, '^name:\s*(.+)$')).Groups[1].Value.Trim()
    $skillDescription = ([regex]::Match($descriptionLine, '^description:\s*(.+)$')).Groups[1].Value.Trim()
    Assert-Condition ($skillName -eq $skillDirectory.Name) "Skill directory '$($skillDirectory.Name)' does not match frontmatter name '$skillName'."
    Assert-Condition ($skillName.Length -le 64 -and $skillName -match '^[a-z0-9]+(?:-[a-z0-9]+)*$') "Skill '$($skillDirectory.Name)' has an invalid Agent Skills name."
    Assert-Condition ($skillDescription.Length -gt 0 -and $skillDescription.Length -le 1024) "Skill '$($skillDirectory.Name)' description must contain 1-1024 characters."
}

$launcherPath = Join-Path $resolvedPluginRoot 'scripts\run-opentia-mcp.ps1'
Assert-Condition (Test-Path -LiteralPath $launcherPath -PathType Leaf) 'Portable MCP launcher script was not found.'
$launcherToken = '${PLUGIN_ROOT}/scripts/run-opentia-mcp.ps1'
Assert-Condition (@($mcp.mcpServers.opentia.args) -contains $launcherToken) "Portable MCP configuration must launch $launcherToken."

if ($TestLauncher) {
    $testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('opentia-agent-plugin-' + [guid]::NewGuid().ToString('N'))
    $testPluginRoot = Join-Path $testRoot 'plugins\opentia-toolkit-agent-plugin'
    $testScriptRoot = Join-Path $testPluginRoot 'scripts'
    $testMcpRoot = Join-Path $testRoot 'mcp'
    $overrideMcpRoot = Join-Path $testRoot 'custom-install\mcp'
    try {
        New-Item -ItemType Directory -Force -Path $testScriptRoot, $testMcpRoot, $overrideMcpRoot | Out-Null
        Copy-Item -LiteralPath $launcherPath -Destination (Join-Path $testScriptRoot 'run-opentia-mcp.ps1')
        New-Item -ItemType File -Path (Join-Path $testMcpRoot 'OpenTiaToolkit.MCP.exe') | Out-Null
        $overrideExecutable = Join-Path $overrideMcpRoot 'OpenTiaToolkit.MCP.exe'
        New-Item -ItemType File -Path $overrideExecutable | Out-Null
        $savedOverride = $env:OPEN_TIA_MCP_EXE
        $env:OPEN_TIA_MCP_EXE = $null
        try {
            $resolved = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $testScriptRoot 'run-opentia-mcp.ps1') -ResolveOnly
            Assert-Condition ($LASTEXITCODE -eq 0) "Portable MCP launcher resolution smoke failed with exit code $LASTEXITCODE."
            Assert-Condition ([System.IO.Path]::GetFullPath($resolved) -eq [System.IO.Path]::GetFullPath((Join-Path $testMcpRoot 'OpenTiaToolkit.MCP.exe'))) 'Portable MCP launcher did not resolve the adjacent installed payload.'

            $env:OPEN_TIA_MCP_EXE = $overrideExecutable
            $resolvedOverride = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $testScriptRoot 'run-opentia-mcp.ps1') -ResolveOnly
            Assert-Condition ($LASTEXITCODE -eq 0) "Portable MCP launcher override smoke failed with exit code $LASTEXITCODE."
            Assert-Condition ([System.IO.Path]::GetFullPath($resolvedOverride) -eq [System.IO.Path]::GetFullPath($overrideExecutable)) 'Portable MCP launcher did not honor OPEN_TIA_MCP_EXE.'
        } finally {
            $env:OPEN_TIA_MCP_EXE = $savedOverride
        }
    } finally {
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        $tempPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        if ($resolvedTestRoot.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTestRoot)) {
            Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
        }
    }
}

[pscustomobject]@{
    PluginRoot = $resolvedPluginRoot
    Specification = '1.0.0'
    Skills = $skillDirectories.Count
    McpServers = $mcp.mcpServers.Count
    LauncherTested = [bool]$TestLauncher
}
