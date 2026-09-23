#!/bin/bash
set -e

TOOLS_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd -- "$TOOLS_DIR/.." && pwd)"
# Finder에서 실행해도 일반적인 macOS 설치 경로를 찾는다.
export PATH="$PATH:/opt/homebrew/bin:/usr/local/bin:/usr/local/share/dotnet:$HOME/.dotnet"
if [[ -f "$TOOLS_DIR/config.local.sh" ]]; then
    source "$TOOLS_DIR/config.local.sh"
fi

fail() {
    printf '\n오류: %s\n' "$*" >&2
    if [[ -t 0 ]]; then
        read -r -p "Enter 키를 누르면 종료합니다." || true
    fi
    exit 1
}

find_conda() {
    local candidate
    if [[ -n "${CONDA_EXE:-}" && -x "$CONDA_EXE" ]]; then
        return
    fi
    CONDA_EXE="$(command -v conda || true)"
    if [[ -n "$CONDA_EXE" ]]; then
        return
    fi
    for candidate in "$HOME/miniconda3/bin/conda" "$HOME/miniforge3/bin/conda" "$HOME/anaconda3/bin/conda" /opt/miniconda3/bin/conda /opt/anaconda3/bin/conda /opt/homebrew/bin/conda; do
        if [[ -x "$candidate" ]]; then
            CONDA_EXE="$candidate"
            return
        fi
    done
    fail "Conda를 찾을 수 없습니다. config.local.sh에서 CONDA_EXE 또는 서비스의 Python 경로를 지정하세요."
}

run_python() {
    local python_path="$1"
    local conda_env="$2"
    shift 2
    if [[ -n "$python_path" ]]; then
        [[ -x "$python_path" ]] || fail "Python 실행 파일이 없습니다: $python_path"
        "$python_path" -u "$@" || fail "Python 실행에 실패했습니다. 위 로그를 확인하세요."
    else
        find_conda
        "$CONDA_EXE" run --no-capture-output -n "$conda_env" python -u "$@" || fail "Conda 환경 '$conda_env' 실행에 실패했습니다. 위 로그를 확인하세요."
    fi
}
