[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet('SinglePlc', 'MultiPlc')]
    [string]$Scenario,

    [string]$TiaPublicApiPath = 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this generator with Windows PowerShell 5.1 to load the V21 .NET Framework Openness API.'
}

$root = [System.IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) {
    throw "Choose an empty output directory for the disposable fixture: $root"
}

$core = Join-Path $PSScriptRoot '..\src\OpenTiaToolkit.Core\bin\Release\net48\OpenTiaToolkit.Core.dll'
$base = (Resolve-Path -LiteralPath (Join-Path $TiaPublicApiPath 'Siemens.Engineering.Base.dll')).Path
$step7 = (Resolve-Path -LiteralPath (Join-Path $TiaPublicApiPath 'Siemens.Engineering.Step7.dll')).Path

[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $core).Path)
[OpenTiaToolkit.Core.TiaPortalAssemblyResolver]::Register()
[void][Reflection.Assembly]::LoadFrom($base)
[void][Reflection.Assembly]::LoadFrom($step7)
Add-Type -Path (Join-Path $PSScriptRoot 'test-assets\AutomationMlAcceptanceFixture.cs') -ReferencedAssemblies @($base, $step7, 'System.Core.dll')

[void][System.IO.Directory]::CreateDirectory($root)
[AutomationMlAcceptanceFixture]::Create($root, ($Scenario -eq 'MultiPlc'))

$archive = Get-Item -LiteralPath (Join-Path $root 'Baseline.zap21')
$archive.IsReadOnly = $true
[pscustomobject]@{
    Scenario = $Scenario
    BaselineArchive = $archive.FullName
    DesiredAml = (Resolve-Path -LiteralPath (Join-Path $root 'Desired.aml')).Path
}
