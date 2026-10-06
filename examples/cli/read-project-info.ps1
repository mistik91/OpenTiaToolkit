param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [string]$CliPath = ".\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe"
)

if (-not (Test-Path -LiteralPath $CliPath)) {
    throw "CLI executable not found. Build the solution first: $CliPath"
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "TIA project not found: $ProjectPath"
}

@"
connect-portal
open-project --project-path="$ProjectPath"
portal-status
list-devices
close-project
disconnect-portal
exit
"@ | & $CliPath --interactive
