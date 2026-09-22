@echo off
call "%USERPROFILE%\miniconda3\Scripts\activate.bat" RealtimeSTT
cd /d "%~dp0.."
python ".\Tools\RealtimeSTT\stt_bridge.py"
