@echo off
echo ============================================================
echo This script will install TIA Portal V21 infrastructure
echo assemblies to the Global Assembly Cache (GAC)
echo ============================================================
echo.
echo An Administrator UAC prompt will appear.
echo Please click "Yes" to continue.
echo.
pause

powershell.exe -ExecutionPolicy Bypass -File "%~dp0install-infrastructure.ps1"

echo.
echo Script execution complete.
pause
