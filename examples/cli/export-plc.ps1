param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [string]$PlcName,

    [Parameter(Mandatory = $true)]
    [string]$ExportPath,

    [string]$CliPath = ".\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe"
)

if (-not (Test-Path -LiteralPath $CliPath)) {
    throw "CLI executable not found. Build the solution first: $CliPath"
}

New-Item -ItemType Directory -Force -Path $ExportPath | Out-Null

@"
connect-portal
open-project --project-path="$ProjectPath"
open-plc --plc-name="$PlcName"
export-plc --plc-name="$PlcName" --export-path="$ExportPath"
disconnect-portal
exit
"@ | & $CliPath --interactive

