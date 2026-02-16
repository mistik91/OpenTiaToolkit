@echo off
echo Installing TIA Portal V21 assemblies to Global Assembly Cache (GAC)...
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

echo Installing Siemens.Engineering.Base.dll...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\gacutil.exe" /i "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll"
if %errorLevel% equ 0 (
    echo   SUCCESS: Siemens.Engineering.Base.dll
) else (
    echo   FAILED: Siemens.Engineering.Base.dll
)

echo Installing Siemens.Engineering.Step7.dll...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\gacutil.exe" /i "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Step7.dll"
if %errorLevel% equ 0 (
    echo   SUCCESS: Siemens.Engineering.Step7.dll
) else (
    echo   FAILED: Siemens.Engineering.Step7.dll
)

echo Installing Siemens.Engineering.WinCC.dll...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\gacutil.exe" /i "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.WinCC.dll"
if %errorLevel% equ 0 (
    echo   SUCCESS: Siemens.Engineering.WinCC.dll
) else (
    echo   FAILED: Siemens.Engineering.WinCC.dll
)

echo Installing Siemens.Engineering.WinCC.Extension.dll...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\gacutil.exe" /i "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.WinCC.Extension.dll"
if %errorLevel% equ 0 (
    echo   SUCCESS: Siemens.Engineering.WinCC.Extension.dll
) else (
    echo   FAILED: Siemens.Engineering.WinCC.Extension.dll
)

echo.
echo GAC installation complete!
echo You can now run the integration tests.
pause
