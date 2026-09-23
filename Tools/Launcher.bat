@echo off
cd /d "%~dp0.."

set "LAUNCHER_PYTHON=%USERPROFILE%\miniconda3\python.exe"

if exist "%LAUNCHER_PYTHON%" (
    "%LAUNCHER_PYTHON%" Tools\Launcher\launcher.py
) else (
    where python >nul 2>nul
    if not errorlevel 1 (
        python Tools\Launcher\launcher.py
    ) else (
        where py >nul 2>nul
        if not errorlevel 1 py -3 Tools\Launcher\launcher.py
    )
)

if errorlevel 1 (
    echo.
    echo Launcher failed. Python 3.10+ with Tkinter is required.
    echo See Tools\Launcher\README.md.
    pause
)
