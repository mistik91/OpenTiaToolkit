# TIA Portal V21 Infrastructure Assemblies GAC Installation
# This script will self-elevate and install all required infrastructure DLLs

$ErrorActionPreference = "Stop"

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "This script requires Administrator privileges. Elevating..." -ForegroundColor Yellow
    $psiArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell.exe -ArgumentList $psiArgs -Verb RunAs
    exit
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Installing TIA Portal V21 Infrastructure Assemblies" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

$tiaPath = "C:\Program Files\Siemens\Automation\Portal V21"
$gacutil = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\gacutil.exe"
$binPath = "$tiaPath\Bin"

$successCount = 0
$failedCount = 0
$skippedCount = 0

function Install-GacAssembly {
    param(
        [string]$AssemblyPath,
        [string]$Description
    )

    if (Test-Path $AssemblyPath) {
        Write-Host "Installing $Description..." -NoNewline
        $result = & $gacutil /i $AssemblyPath /f 2>&1 | Out-String

        if ($LASTEXITCODE -eq 0) {
            Write-Host " SUCCESS" -ForegroundColor Green
            return $true
        } else {
            Write-Host " FAILED" -ForegroundColor Red
            return $false
        }
    } else {
        Write-Host "Installing $Description... SKIPPED (not found)" -ForegroundColor Yellow
        return $false
    }
}

Write-Host "===== V21 Infrastructure Assemblies =====" -ForegroundColor Cyan
Write-Host ""

# Core infrastructure assemblies
if (Install-GacAssembly "$binPath\Siemens.Engineering.Hmi.TypeConverters.dll" "Siemens.Engineering.Hmi.TypeConverters.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$binPath\Siemens.Engineering.Vci.Enums.dll" "Siemens.Engineering.Vci.Enums.dll") { $successCount++ } else { $failedCount++ }

# V21-specific meta assemblies
if (Install-GacAssembly "$binPath\Siemens.Engineering.Meta.Cax.21.0.0.0.dll" "Siemens.Engineering.Meta.Cax.21.0.0.0.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$binPath\Siemens.Engineering.Meta.TcGateway.21.0.0.0.dll" "Siemens.Engineering.Meta.TcGateway.21.0.0.0.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$binPath\Siemens.Engineering.Meta.Vci.21.0.0.0.dll" "Siemens.Engineering.Meta.Vci.21.0.0.0.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$binPath\Siemens.Engineering.SystemDiagnosis.21.0.0.0.dll" "Siemens.Engineering.SystemDiagnosis.21.0.0.0.dll") { $successCount++ } else { $failedCount++ }

Write-Host ""
Write-Host "===== Bin\PublicAPI Infrastructure Assemblies =====" -ForegroundColor Cyan
Write-Host ""

if (Install-GacAssembly "$tiaPath\Bin\PublicAPI\Siemens.Engineering.Contract.dll" "Siemens.Engineering.Contract.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$tiaPath\Bin\PublicAPI\Siemens.Engineering.ClientAdapter.Interfaces.dll" "Siemens.Engineering.ClientAdapter.Interfaces.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$tiaPath\Bin\PublicAPI\Client\Siemens.Engineering.ClientAdapter.MarshallerHook.dll" "Siemens.Engineering.ClientAdapter.MarshallerHook.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$tiaPath\Bin\PublicAPI\Client\Siemens.Engineering.ClientAdapter.MarshallerHook.Hmi.dll" "Siemens.Engineering.ClientAdapter.MarshallerHook.Hmi.dll") { $successCount++ } else { $failedCount++ }

Write-Host ""
Write-Host "===== PublicAPI V21 Main Assemblies =====" -ForegroundColor Cyan
Write-Host ""

if (Install-GacAssembly "$tiaPath\PublicAPI\V21\net48\Siemens.Engineering.Base.dll" "Siemens.Engineering.Base.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$tiaPath\PublicAPI\V21\net48\Siemens.Engineering.Step7.dll" "Siemens.Engineering.Step7.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$tiaPath\PublicAPI\V21\net48\Siemens.Engineering.WinCC.dll" "Siemens.Engineering.WinCC.dll") { $successCount++ } else { $failedCount++ }
if (Install-GacAssembly "$tiaPath\PublicAPI\V21\net48\Siemens.Engineering.WinCC.Extension.dll" "Siemens.Engineering.WinCC.Extension.dll") { $successCount++ } else { $failedCount++ }

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Installation Summary:" -ForegroundColor Cyan
Write-Host "  Successful: $successCount" -ForegroundColor Green
Write-Host "  Failed: $failedCount" -ForegroundColor Red
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
