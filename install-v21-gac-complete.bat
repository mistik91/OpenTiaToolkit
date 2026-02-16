@echo off
echo ============================================================
echo Installing TIA Portal V21 assemblies to GAC
echo (Including infrastructure assemblies from Bin directory)
echo ============================================================
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

set TIA_PATH=C:\Program Files\Siemens\Automation\Portal V21
set GACUTIL=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\gacutil.exe

echo.
echo ===== Installing PublicAPI assemblies =====
echo.

echo Installing Siemens.Engineering.Base.dll...
"%GACUTIL%" /i "%TIA_PATH%\PublicAPI\V21\net48\Siemens.Engineering.Base.dll" /f
if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)

echo Installing Siemens.Engineering.Step7.dll...
"%GACUTIL%" /i "%TIA_PATH%\PublicAPI\V21\net48\Siemens.Engineering.Step7.dll" /f
if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)

echo Installing Siemens.Engineering.WinCC.dll...
"%GACUTIL%" /i "%TIA_PATH%\PublicAPI\V21\net48\Siemens.Engineering.WinCC.dll" /f
if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)

echo Installing Siemens.Engineering.WinCC.Extension.dll...
"%GACUTIL%" /i "%TIA_PATH%\PublicAPI\V21\net48\Siemens.Engineering.WinCC.Extension.dll" /f
if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)

echo.
echo ===== Installing infrastructure assemblies from Bin\PublicAPI =====
echo.

echo Installing Siemens.Engineering.Contract.dll...
"%GACUTIL%" /i "%TIA_PATH%\Bin\PublicAPI\Siemens.Engineering.Contract.dll" /f
if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)

echo Installing Siemens.Engineering.ClientAdapter.Interfaces.dll...
"%GACUTIL%" /i "%TIA_PATH%\Bin\PublicAPI\Siemens.Engineering.ClientAdapter.Interfaces.dll" /f
if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)

echo Installing Siemens.Engineering.ClientAdapter.MarshallerHook.dll...
"%GACUTIL%" /i "%TIA_PATH%\Bin\PublicAPI\Client\Siemens.Engineering.ClientAdapter.MarshallerHook.dll" /f
if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)

echo Installing Siemens.Engineering.ClientAdapter.MarshallerHook.Hmi.dll...
"%GACUTIL%" /i "%TIA_PATH%\Bin\PublicAPI\Client\Siemens.Engineering.ClientAdapter.MarshallerHook.Hmi.dll" /f
if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)

echo.
echo ===== Installing additional infrastructure assemblies from Bin =====
echo.

echo Installing Siemens.Engineering.Hmi.TypeConverters.dll...
if exist "%TIA_PATH%\Bin\Siemens.Engineering.Hmi.TypeConverters.dll" (
    "%GACUTIL%" /i "%TIA_PATH%\Bin\Siemens.Engineering.Hmi.TypeConverters.dll" /f
    if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)
) else (
    echo   SKIPPED (not found)
)

echo Installing Siemens.Engineering.Vci.Enums.dll...
if exist "%TIA_PATH%\Bin\Siemens.Engineering.Vci.Enums.dll" (
    "%GACUTIL%" /i "%TIA_PATH%\Bin\Siemens.Engineering.Vci.Enums.dll" /f
    if %errorLevel% equ 0 (echo   SUCCESS) else (echo   FAILED)
) else (
    echo   SKIPPED (not found)
)

echo.
echo ============================================================
echo GAC installation complete!
echo ============================================================
echo.
echo Note: If you still see errors about missing assemblies,
echo you may need to check for additional dependencies or
echo repair your TIA Portal installation.
echo.
pause
