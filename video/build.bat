@echo off
rem Build Strata Video as a Windows app folder: video\dist\StrataVideo\StrataVideo.exe
rem Usage:  build.bat            build and run the self-test
rem         build.bat notest     build only
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

set "PY=.venv\Scripts\python.exe"
if not exist "%PY%" (
    echo Creating virtual environment...
    python -m venv .venv || goto :error
)

echo Installing build requirements...
"%PY%" -m pip install --quiet --upgrade pip
"%PY%" -m pip install --quiet -r requirements-build.txt || goto :error

if not exist "..\shared\icons\app\strata-video.ico" (
    "%PY%" ..\shared\tools\make_icons.py || goto :error
)

echo Building with PyInstaller...
"%PY%" -m PyInstaller strata.spec --noconfirm --clean --log-level WARN || goto :error

if /i "%~1"=="notest" goto :done

echo Running self-test of the built app...
echo (A window opens for a moment and closes by itself: that is the automatic test.)
if exist "build\selftest.json" del "build\selftest.json"
start "" /wait "dist\StrataVideo\StrataVideo.exe" --self-test "%CD%\build\selftest.json"
if errorlevel 1 (
    echo Self-test FAILED. Report: build\selftest.json
    type "build\selftest.json"
    if defined PAUSE_AT_END pause
    exit /b 1
)
echo Self-test passed.

:done
echo.
echo Build complete: %CD%\dist\StrataVideo\StrataVideo.exe
if defined PAUSE_AT_END pause
exit /b 0

:error
echo.
echo Build FAILED.
if defined PAUSE_AT_END pause
exit /b 1
