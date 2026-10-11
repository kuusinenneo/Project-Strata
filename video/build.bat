@echo off
rem Build Strata Video as a Windows app folder: video\dist\StrataVideo\StrataVideo.exe
rem Usage:  build.bat            build and run the self-test
rem         build.bat notest     build only
setlocal
cd /d "%~dp0"

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
if exist "build\selftest.json" del "build\selftest.json"
start "" /wait "dist\StrataVideo\StrataVideo.exe" --self-test "%CD%\build\selftest.json"
if errorlevel 1 (
    echo Self-test FAILED. Report: build\selftest.json
    type "build\selftest.json"
    exit /b 1
)
echo Self-test passed.

:done
echo.
echo Build complete: %CD%\dist\StrataVideo\StrataVideo.exe
exit /b 0

:error
echo.
echo Build FAILED.
exit /b 1
