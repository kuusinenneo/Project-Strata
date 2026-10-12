@echo off
rem Build the Strata launcher as a self-contained Windows app: launcher\publish\Strata\Strata.exe
rem Usage:  build.bat            publish, then run the launcher's self-test
rem         build.bat notest     publish only
setlocal
cd /d "%~dp0"
rem Started by double-clicking in Explorer? Then keep the window open at the end so the result
rem can be read. (Not when run from a terminal or from build-all.bat.) The process chain is
rem powershell < cmd (for /f) < cmd (this script) < explorer, hence three levels up.
set "PAUSE_AT_END="
if not defined STRATA_NO_PAUSE (
    for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "$id = $PID; for ($i = 0; $i -lt 3; $i++) { $id = (Get-CimInstance Win32_Process -Filter ('ProcessId=' + $id)).ParentProcessId }; (Get-Process -Id $id -ErrorAction SilentlyContinue).ProcessName"`) do (
        if /i "%%P"=="explorer" set "PAUSE_AT_END=1"
    )
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found. Install .NET 8 or newer: winget install Microsoft.DotNet.SDK.10
    if defined PAUSE_AT_END pause
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
echo (A window opens for a moment and closes by itself: that is the automatic test.)
start "" /wait "%OUT%\Strata.exe" --self-test "%CD%\publish\selftest.png"
if errorlevel 1 (
    echo Self-test FAILED: no Strata app was found. Build the apps first.
    if defined PAUSE_AT_END pause
    exit /b 1
)
type "publish\selftest.txt"
echo.

:done
echo Build complete: %CD%\%OUT%\Strata.exe
if defined PAUSE_AT_END pause
exit /b 0

:error
echo.
echo Build FAILED.
if defined PAUSE_AT_END pause
exit /b 1
