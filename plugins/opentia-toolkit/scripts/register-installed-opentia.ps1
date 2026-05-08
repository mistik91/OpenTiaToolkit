param(
    [string]$ExecutablePath = "C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe",
    [string]$RegisterScriptPath = "$PSScriptRoot\..\..\..\scripts\Register-OpennessAllowList.ps1"
)

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "OpenTiaToolkit MCP executable was not found at '$ExecutablePath'."
}

if (-not (Test-Path -LiteralPath $RegisterScriptPath)) {
    throw "Register-OpennessAllowList.ps1 was not found at '$RegisterScriptPath'."
}

& powershell -ExecutionPolicy Bypass -File $RegisterScriptPath -ExecutablePath $ExecutablePath
