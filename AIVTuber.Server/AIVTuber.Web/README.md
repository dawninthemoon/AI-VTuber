# AIVTuber.Web - Refactored

기존 단일 `Program.cs`에서 아래 항목을 분리한 버전입니다.

- 시스템 프롬프트 → `Prompts/system.txt`
- HTML → `wwwroot/index.html`
- CSS → `wwwroot/css/style.css`
- JavaScript → `wwwroot/js/chat.js`
- Ollama API 호출 → `Services/OllamaService.cs`
- 대화 히스토리 관리 → `Services/ChatService.cs`
- 요청/응답 모델 → `Models/`
- Ollama 설정 → `appsettings.json`

## 실행

Ollama 서버:

```bash
ollama serve
```

웹 프로젝트:

```bash
cd AIVTuber.Web
dotnet run
```

터미널에 출력되는 `http://localhost:xxxx` 주소로 접속하세요.

## 모델 변경

`appsettings.json`:

```json
"Model": "qwen3:8b"
```

만 변경하면 됩니다.

## thinking 설정

```json
"Think": true
```

AI의 thinking을 끄고 싶으면 `false`로 바꾸세요.

## 참고

대화 기록은 현재 메모리에만 저장됩니다.
서버를 재시작하면 초기화됩니다.


## 이번 UI 버전
- 사용자 메시지: 오른쪽 파란 말풍선
- AI 메시지: 왼쪽 회색 말풍선
- 입력창: 화면 하단 고정
- Enter 전송
- AI 응답 대기 중 `. .. ...` 애니메이션
- 자동 스크롤
- 대화 초기화 버튼


## 자동 응답 모드 분류

이번 버전에는 `ChatClassifier`가 추가되어 메시지에 따라 자동으로 생성 설정을 바꿉니다.

- FAST
  - 짧은 인사 / 짧은 잡담
  - `Think = false`
  - `NumPredict = 100`

- NORMAL
  - 일반 대화
  - `Think = false`
  - `NumPredict = 180`

- THINK
  - 긴 메시지 / 분석 / 비교 / 이유 설명 요청
  - `Think = true`
  - `NumPredict = 400`

분류 규칙은 `Services/ChatClassifier.cs`에서 수정할 수 있습니다.

서버 터미널 로그에서 현재 선택된 모드를 확인할 수 있습니다.
