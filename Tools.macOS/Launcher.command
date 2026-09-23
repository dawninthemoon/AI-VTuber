#!/bin/bash
cd "$(dirname "$0")/.." || exit 1
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/local/share/dotnet:$HOME/.dotnet:$HOME/anaconda3/bin:$HOME/miniconda3/bin:$PATH"
python3 Tools/Launcher/launcher.py
if [ $? -ne 0 ]; then
    echo '실행 실패: Python 3.10 이상과 Tkinter가 필요합니다. Tools/Launcher/README.md를 확인하세요.'
    read -r -p 'Enter를 누르면 닫습니다.'
fi
