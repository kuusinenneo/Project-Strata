@echo off
rem Build every Strata app and put them together in one folder:
rem   dist\Strata Suite\Strata.exe           launcher
rem   dist\Strata Suite\StrataVideo\...      Strata Video
rem   dist\Strata Suite\StrataPhoto\...      Strata Photo
rem   dist\Strata Suite\StrataPDF\...        Strata PDF
rem The apps find each other in this layout (Send Frame / Extract Images, launcher tiles).
rem Usage:  build-all.bat           build and self-test everything
rem         build-all.bat notest    skip tests and self-tests
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
set "ARG=%~1"
rem The individual build scripts must not wait for a key press.
set "STRATA_NO_PAUSE=1"

echo === Strata Video ===
call video\build.bat %ARG% || goto :error
echo === Strata Photo ===
call photo\build.bat %ARG% || goto :error
echo === Strata PDF ===
call pdf\build.bat %ARG% || goto :error
echo === Launcher ===
call launcher\build.bat notest || goto :error

set "SUITE=dist\Strata Suite"
echo === Assembling "%SUITE%" ===
if exist "%SUITE%" rmdir /s /q "%SUITE%"
mkdir "%SUITE%"
robocopy "video\dist\StrataVideo" "%SUITE%\StrataVideo" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto :error
robocopy "photo\publish\StrataPhoto" "%SUITE%\StrataPhoto" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto :error
robocopy "pdf\publish\StrataPDF" "%SUITE%\StrataPDF" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto :error
robocopy "launcher\publish\Strata" "%SUITE%" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto :error

if /i not "%ARG%"=="notest" (
    echo Checking that the launcher finds all three apps...
echo (A window opens for a moment and closes by itself: that is the automatic test.)
    start "" /wait "%SUITE%\Strata.exe" --self-test "%CD%\dist\launcher-selftest.png"
    type "dist\launcher-selftest.txt"
    echo.
    findstr /c:"not found" "dist\launcher-selftest.txt" >nul && goto :error
)

echo.
echo Done: %CD%\%SUITE%\Strata.exe
if defined PAUSE_AT_END (
    echo.
    echo Opening the Strata launcher...
    start "" "%SUITE%\Strata.exe"
    pause
)
exit /b 0

:error
echo.
echo Build FAILED.
if defined PAUSE_AT_END pause
exit /b 1
