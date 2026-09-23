#!/bin/bash
source "$(dirname -- "$0")/common.sh"
GPT_SOVITS_DIR="${GPT_SOVITS_DIR:-$HOME/GPT-SoVITS}"
[[ -f "$GPT_SOVITS_DIR/api_v2.py" ]] || fail "api_v2.py가 없습니다. config.local.sh의 GPT_SOVITS_DIR을 설치 경로로 설정하세요: $GPT_SOVITS_DIR"
cd "$GPT_SOVITS_DIR"
printf 'GPT-SoVITS API: http://127.0.0.1:9881 (종료: Ctrl+C)\n'
run_python "${GPT_SOVITS_PYTHON:-}" "${GPT_SOVITS_CONDA_ENV:-GPTSoVits}" ./api_v2.py -a 127.0.0.1 -p 9881
