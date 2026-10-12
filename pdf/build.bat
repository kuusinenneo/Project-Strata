@echo off
rem Build Strata PDF as a self-contained Windows app: pdf\publish\StrataPDF\StrataPDF.exe
rem (runs on PCs without .NET installed)
rem Usage:  build.bat            run unit tests, publish, then run the app's self-test
rem         build.bat notest     publish only
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found. Install .NET 8 or newer: winget install Microsoft.DotNet.SDK.10
    exit /b 1
)

set "OUT=publish\StrataPDF"

if /i not "%~1"=="notest" (
    echo Running unit tests...
    dotnet test tests\StrataPdf.Tests -c Release --nologo -v q || goto :error
)

echo Publishing self-contained win-x64 build...
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish src\StrataPdf.App -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true ^
    -p:DebugType=none -o "%OUT%" --nologo -v q || goto :error

if /i "%~1"=="notest" goto :done

echo Running self-test of the built app...
if exist "publish\selftest.json" del "publish\selftest.json"
start "" /wait "%OUT%\StrataPDF.exe" --self-test "%CD%\publish\selftest.png"
if errorlevel 1 (
    echo Self-test FAILED. Report: publish\selftest.json
    type "publish\selftest.json"
    exit /b 1
)
echo Self-test passed. Screenshot: publish\selftest.png

:done
echo.
echo Build complete: %CD%\%OUT%\StrataPDF.exe
exit /b 0

:error
echo.
echo Build FAILED.
exit /b 1
