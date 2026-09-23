# 이 파일을 config.local.sh로 복사한 뒤 필요한 항목을 수정하세요.
# 공백이 포함된 경로는 반드시 따옴표로 감싸고, ~ 대신 $HOME을 사용하세요.

GPT_SOVITS_DIR="$HOME/GPT-SoVITS"
GPT_SOVITS_CONDA_ENV="GPTSoVits"
STT_CONDA_ENV="RealtimeSTT"

# Conda 대신 기존 가상환경의 Python을 직접 지정할 수도 있습니다.
# STT_PYTHON="$HOME/venvs/realtimestt/bin/python"
# GPT_SOVITS_PYTHON="$HOME/GPT-SoVITS/.venv/bin/python"

# Conda 자동 탐색이 실패하는 경우 지정하세요.
# CONDA_EXE="$HOME/miniforge3/bin/conda"

# STT 설정을 변경하려면 export를 사용하세요.
# export AIVTUBER_SERVER_URL="http://127.0.0.1:5000"
# export AIVTUBER_STT_MODEL="small"
# export AIVTUBER_STT_DEVICE="cpu"
# export AIVTUBER_STT_COMPUTE_TYPE="int8"
