@echo off
:: ============================================================
::  MONARCH Intelligence Report System - Build Script
::
::  Source  : C:\Dev\Kaiju\MONARCH\src\
::  Output  : %USERPROFILE%\Documents\NinjaTrader 8\MONARCH\MONARCH.exe
:: ============================================================

setlocal

set DEPLOY_DIR=%USERPROFILE%\Documents\NinjaTrader 8\MONARCH

echo.
echo  ============================================================
echo   MONARCH Intelligence Report System - Build
echo  ============================================================
echo.
echo   Source  : %~dp0src\monarch.py
echo   Deploy  : %DEPLOY_DIR%\MONARCH.exe
echo.

:: ── Locate the Python that owns PyInstaller ──────────────────────────────────
::
::  If you have multiple Python versions (e.g. via pyenv), PyInstaller must
::  be invoked with the same interpreter it was installed under.
::  We derive that python.exe from the site-packages path pip reports.
::
pip show pyinstaller >nul 2>&1
if errorlevel 1 (
    echo  [INFO] PyInstaller not found. Installing into active Python...
    pip install pyinstaller
    if errorlevel 1 (
        echo  [ERROR] pip install pyinstaller failed.
        echo  Try:  pip install pyinstaller   (then re-run this script)
        pause & exit /b 1
    )
)

:: Extract the site-packages path where pyinstaller lives
for /f "tokens=1,* delims=: " %%a in ('pip show pyinstaller ^| findstr /i "^Location"') do set PY_SITE=%%b
:: PY_SITE is now e.g. C:\Users\dcjon\.pyenv\pyenv-win\versions\3.10.5\Lib\site-packages
:: python.exe is two levels up: Lib\site-packages -> Lib -> <root>
for %%d in ("%PY_SITE%\..\..") do set PY_ROOT=%%~fd
set PY_EXE=%PY_ROOT%\python.exe

if not exist "%PY_EXE%" (
    echo  [WARN] Could not derive python.exe from pip location.
    echo  Falling back to active 'python' command.
    set PY_EXE=python
)

for /f "tokens=*" %%v in ('"%PY_EXE%" --version 2^>^&1') do set PYVER=%%v
for /f "tokens=2" %%v in ('pip show pyinstaller ^| findstr /i "^Version"') do set PIVER=%%v
echo  Python     : %PYVER%  ^(%PY_EXE%^)
echo  PyInstaller: %PIVER%

echo.

:: ── Clean previous build artefacts ────────────────────────────────────────────
echo  [CLEAN] Removing previous build...
if exist "%~dp0build"        rmdir /s /q "%~dp0build"
if exist "%~dp0dist"         rmdir /s /q "%~dp0dist"
if exist "%~dp0MONARCH.spec" del /q "%~dp0MONARCH.spec"

:: ── Compile ───────────────────────────────────────────────────────────────────
echo  [BUILD] Compiling MONARCH.exe (this takes ~30-60 seconds)...
echo.

"%PY_EXE%" -m PyInstaller ^
    --onefile ^
    --console ^
    --name MONARCH ^
    --paths "%~dp0src" ^
    "%~dp0src\monarch.py"

if errorlevel 1 (
    echo.
    echo  [ERROR] Compilation failed. See output above.
    pause & exit /b 1
)

:: ── Create deployment folder if needed ───────────────────────────────────────
if not exist "%DEPLOY_DIR%" (
    echo  [INFO] Creating %DEPLOY_DIR%
    mkdir "%DEPLOY_DIR%"
)

:: ── Deploy ────────────────────────────────────────────────────────────────────
echo.
echo  [DEPLOY] Copying to %DEPLOY_DIR%\MONARCH.exe
copy /y "%~dp0dist\MONARCH.exe" "%DEPLOY_DIR%\MONARCH.exe" >nul

if errorlevel 1 (
    echo  [ERROR] Copy failed. Is the NT8 MONARCH folder accessible?
    pause & exit /b 1
)

echo.
echo  ============================================================
echo   Build complete!
echo.
echo   Deployed to:
echo   %DEPLOY_DIR%\MONARCH.exe
echo.
echo   First run will create:
echo     %DEPLOY_DIR%\CastleBravo.html    (hub page)
echo     %DEPLOY_DIR%\logs\               (synced CSV files)
echo     %DEPLOY_DIR%\reports\            (daily + weekly HTML)
echo  ============================================================
echo.

set /p RUN="Run MONARCH.exe now? [Y/N]: "
if /i "%RUN%"=="Y" (
    echo.
    echo  Running...
    "%DEPLOY_DIR%\MONARCH.exe"
)

endlocal
pause
