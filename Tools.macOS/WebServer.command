#!/bin/bash
source "$(dirname -- "$0")/common.sh"
cd "$PROJECT_ROOT"
command -v dotnet >/dev/null 2>&1 || fail ".NET 10 SDK를 설치하고 다시 실행하세요."
printf '웹 서버: http://127.0.0.1:5000 (종료: Ctrl+C)\n'
dotnet run --project "$PROJECT_ROOT/AIVTuber.Server/AIVTuber.Web/AIVTuber.Web.csproj" --urls http://127.0.0.1:5000 || fail "웹 서버 실행에 실패했습니다. 위 로그를 확인하세요."
