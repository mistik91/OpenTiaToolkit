# TIA Portal V21 GAC Installation Script
# This script will self-elevate to run as Administrator

$ErrorActionPreference = "Stop"

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "This script requires Administrator privileges. Elevating..." -ForegroundColor Yellow
    # Re-launch this script as Administrator
    $psiArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell.exe -ArgumentList $psiArgs -Verb RunAs
    exit
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Installing TIA Portal V21 assemblies to GAC" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

$tiaPath = "C:\Program Files\Siemens\Automation\Portal V21"
$gacutil = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\gacutil.exe"

function Install-GacAssembly {
    param(
        [string]$AssemblyPath,
        [string]$Description
    )

    if (Test-Path $AssemblyPath) {
        Write-Host "Installing $Description..." -NoNewline
        $result = & $gacutil /i $AssemblyPath /f 2>&1

        if ($LASTEXITCODE -eq 0) {
            Write-Host " SUCCESS" -ForegroundColor Green
            return $true
        } else {
            Write-Host " FAILED" -ForegroundColor Red
            Write-Host "  Error: $result" -ForegroundColor DarkRed
            return $false
        }
    } else {
        Write-Host "Installing $Description... SKIPPED (not found)" -ForegroundColor Yellow
        return $false
    }
}

Write-Host "===== Installing PublicAPI assemblies =====" -ForegroundColor Cyan
Write-Host ""

Install-GacAssembly "$tiaPath\PublicAPI\V21\net48\Siemens.Engineering.Base.dll" "Siemens.Engineering.Base.dll"
Install-GacAssembly "$tiaPath\PublicAPI\V21\net48\Siemens.Engineering.Step7.dll" "Siemens.Engineering.Step7.dll"
Install-GacAssembly "$tiaPath\PublicAPI\V21\net48\Siemens.Engineering.WinCC.dll" "Siemens.Engineering.WinCC.dll"
Install-GacAssembly "$tiaPath\PublicAPI\V21\net48\Siemens.Engineering.WinCC.Extension.dll" "Siemens.Engineering.WinCC.Extension.dll"

Write-Host ""
Write-Host "===== Installing infrastructure assemblies from Bin\PublicAPI =====" -ForegroundColor Cyan
Write-Host ""

Install-GacAssembly "$tiaPath\Bin\PublicAPI\Siemens.Engineering.Contract.dll" "Siemens.Engineering.Contract.dll"
Install-GacAssembly "$tiaPath\Bin\PublicAPI\Siemens.Engineering.ClientAdapter.Interfaces.dll" "Siemens.Engineering.ClientAdapter.Interfaces.dll"
Install-GacAssembly "$tiaPath\Bin\PublicAPI\Client\Siemens.Engineering.ClientAdapter.MarshallerHook.dll" "Siemens.Engineering.ClientAdapter.MarshallerHook.dll"
Install-GacAssembly "$tiaPath\Bin\PublicAPI\Client\Siemens.Engineering.ClientAdapter.MarshallerHook.Hmi.dll" "Siemens.Engineering.ClientAdapter.MarshallerHook.Hmi.dll"

Write-Host ""
Write-Host "===== Installing additional infrastructure assemblies from Bin =====" -ForegroundColor Cyan
Write-Host ""

Install-GacAssembly "$tiaPath\Bin\Siemens.Engineering.Hmi.TypeConverters.dll" "Siemens.Engineering.Hmi.TypeConverters.dll"
Install-GacAssembly "$tiaPath\Bin\Siemens.Engineering.Vci.Enums.dll" "Siemens.Engineering.Vci.Enums.dll"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "GAC installation complete!" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
