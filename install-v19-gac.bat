@echo off
echo ============================================================
echo Installing TIA Portal V19 assemblies to GAC (OPTIONAL)
echo ============================================================
echo.
echo NOTE: GAC installation is OPTIONAL for V19.
echo The custom TiaPortalAssemblyResolver can load assemblies
echo directly from the TIA Portal installation directory.
echo.
echo This script requires Administrator privileges.
echo.

REM Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script must be run as Administrator.
    echo Right-click this file and select "Run as administrator"
    pause
    exit /b 1
)

echo Installing Siemens.Engineering.dll (V19 monolithic assembly)...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\gacutil.exe" /i "C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll"
if %errorLevel% equ 0 (
    echo   SUCCESS: Siemens.Engineering.dll
) else (
    echo   FAILED: Siemens.Engineering.dll
)

echo.
echo ============================================================
echo GAC installation complete!
echo ============================================================
echo.
echo Note: If you encounter assembly loading issues,
echo the custom TiaPortalAssemblyResolver will automatically
echo load assemblies from the TIA Portal installation directory.
echo.
pause
