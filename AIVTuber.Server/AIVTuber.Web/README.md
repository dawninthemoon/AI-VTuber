# AIVTuber.Web Full

현재까지 만든 백엔드/웹 테스트 UI 전체 구성입니다.

포함 기능:
- Ollama qwen3:8b
- FAST / NORMAL / THINK 자동 분류
- 대화 히스토리
- 시스템 프롬프트 분리
- 웹 채팅 UI
- Enter 전송
- 생각 중 `. .. ...`
- 브라우저 TTS
- Unity용 `/unity/state`
- 대화 초기화

## 실행

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
