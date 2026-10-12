@echo off
rem Build Strata Photo as a self-contained Windows app: photo\publish\StrataPhoto\StrataPhoto.exe
rem (runs on PCs without .NET installed)
rem Usage:  build.bat            run unit tests, publish, then run the app's self-test
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

set "OUT=publish\StrataPhoto"

if /i not "%~1"=="notest" (
    echo Running unit tests...
    dotnet test tests\StrataPhoto.Tests -c Release --nologo -v q || goto :error
)

echo Publishing self-contained win-x64 build...
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish src\StrataPhoto.App -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true ^
    -p:DebugType=none -o "%OUT%" --nologo -v q || goto :error

if /i "%~1"=="notest" goto :done

echo Running self-test of the built app...
echo (A window opens for a moment and closes by itself: that is the automatic test.)
if exist "publish\selftest.json" del "publish\selftest.json"
start "" /wait "%OUT%\StrataPhoto.exe" --self-test "%CD%\publish\selftest.png"
if errorlevel 1 (
    echo Self-test FAILED. Report: publish\selftest.json
    type "publish\selftest.json"
    if defined PAUSE_AT_END pause
    exit /b 1
)
echo Self-test passed. Screenshot: publish\selftest.png

:done
echo.
echo Build complete: %CD%\%OUT%\StrataPhoto.exe
if defined PAUSE_AT_END pause
exit /b 0

:error
echo.
echo Build FAILED.
if defined PAUSE_AT_END pause
exit /b 1
