@echo off
rem Build the Strata launcher as a self-contained Windows app: launcher\publish\Strata\Strata.exe
rem Usage:  build.bat            publish, then run the launcher's self-test
rem         build.bat notest     publish only
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found. Install .NET 8 or newer: winget install Microsoft.DotNet.SDK.10
    exit /b 1
)

set "OUT=publish\Strata"
echo Publishing self-contained win-x64 build...
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish src\StrataLauncher -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true ^
    -p:DebugType=none -o "%OUT%" --nologo -v q || goto :error

if /i "%~1"=="notest" goto :done
echo Running self-test of the launcher...
start "" /wait "%OUT%\Strata.exe" --self-test "%CD%\publish\selftest.png"
if errorlevel 1 (
    echo Self-test FAILED: no Strata app was found. Build the apps first.
    exit /b 1
)
type "publish\selftest.txt"
echo.

:done
echo Build complete: %CD%\%OUT%\Strata.exe
exit /b 0

:error
echo.
echo Build FAILED.
exit /b 1
