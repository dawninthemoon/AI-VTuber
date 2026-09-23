#!/bin/bash
source "$(dirname -- "$0")/common.sh"
cd "$PROJECT_ROOT"
export AIVTUBER_STT_DEVICE="${AIVTUBER_STT_DEVICE:-cpu}"
export AIVTUBER_STT_COMPUTE_TYPE="${AIVTUBER_STT_COMPUTE_TYPE:-int8}"
printf 'RealtimeSTT 실행 (종료: Ctrl+C)\n'
run_python "${STT_PYTHON:-}" "${STT_CONDA_ENV:-RealtimeSTT}" "$PROJECT_ROOT/Tools/RealtimeSTT/stt_bridge.py"
