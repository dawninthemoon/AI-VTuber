@echo off
cd /d "%~dp0.."
where py >nul 2>nul
if errorlevel 1 (
    python Tools\Launcher\launcher.py
) else (
    py -3 Tools\Launcher\launcher.py
)
if errorlevel 1 (
    echo Python 3.10+ with Tkinter is required. See Tools/Launcher/README.md.
    pause
)
