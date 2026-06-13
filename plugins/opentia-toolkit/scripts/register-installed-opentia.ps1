param(
    [string]$ExecutablePath,
    [string]$RegisterScriptPath = "$PSScriptRoot\..\..\..\scripts\Register-OpennessAllowList.ps1"
)

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:OPEN_TIA_MCP_EXE)) {
        $ExecutablePath = $env:OPEN_TIA_MCP_EXE
    } else {
        $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
        $candidates = @(
            (Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe'),
            (Join-Path $repoRoot 'src\OpenTiaToolkit.MCP\bin\Release\net48\OpenTiaToolkit.MCP.exe')
        )

        $ExecutablePath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    throw "OpenTiaToolkit MCP executable was not found. Build the MCP project, pass -ExecutablePath, or set OPEN_TIA_MCP_EXE."
}

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "OpenTiaToolkit MCP executable was not found at '$ExecutablePath'."
}

if (-not (Test-Path -LiteralPath $RegisterScriptPath)) {
    throw "Register-OpennessAllowList.ps1 was not found at '$RegisterScriptPath'."
}

& powershell -ExecutionPolicy Bypass -File $RegisterScriptPath -ExecutablePath $ExecutablePath
