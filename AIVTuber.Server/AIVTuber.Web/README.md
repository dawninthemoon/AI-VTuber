# AIVTuber.Web

현재까지 만든 백엔드/웹 테스트 UI 전체 구성입니다.

포함 기능:
- Ollama qwen3:8b
- CHAT / FACT / THINK 자동 분류
- FACT 및 일부 THINK 질문에 한국어 위키백과 검색 근거 제공
- 검색·답변 생성이 길어지면 Unity에서 안내 대사와 음성 재생
- 대화 히스토리
- 시스템 프롬프트 분리
- 웹 채팅 UI
- Enter 전송
- 생각 중 `. .. ...`
- GPT-SoVITS 음성 생성과 Unity용 `/unity/state`, `/unity/audio`
- 대화 초기화

## 실행

Supertonic 3 모델을 최초 한 번 다운로드합니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\download-supertonic.ps1
```

음성 설정은 `appsettings.json`의 `Supertonic` 섹션에서 변경합니다. `SpeakerId`는 0~9,
`Speed`는 큰 값일수록 빠르게 발화합니다.

Ollama:

```bash
ollama serve
```

웹 서버:

```bash
dotnet run
```

터미널에 출력되는 localhost 주소로 접속하세요.

Unity에서는 같은 포트의:

```text
http://localhost:포트/unity/state
```

를 사용하세요.

## 사실 검색 범위

검색은 한국어 위키백과의 문서 검색 API를 사용하며 별도 API 키가 필요하지 않습니다. 인물·개념처럼 비교적 안정적인 사실을 확인하는 용도입니다. 최신 뉴스, 현재 가격, 실시간 정보는 확인할 수 없으므로 추측 대신 확인 불가 응답을 반환합니다. 웹 테스트 UI에서는 답변 아래에 검색 문서 링크를 표시하며, 방송용 음성에는 링크를 읽지 않습니다.

검색이 필요한 응답이 1.2초 안에 준비되지 않으면 짧은 안내 음성을 생성합니다. 생성 도중 최종 답이 완성되면 안내를 생략할 수 있습니다. 음성 생성에 실패하면 텍스트 답변은 계속 제공됩니다.
