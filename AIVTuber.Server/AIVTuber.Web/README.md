# AIVTuber.Web

현재까지 만든 백엔드/웹 테스트 UI 전체 구성입니다.

포함 기능:
- OpenAI GPT-6 Luna
- CHAT / FACT / THINK 자동 분류
- FACT 및 일부 THINK 질문에 한국어 위키백과 검색 근거 제공
- 검색·답변 생성이 길어지면 Unity에서 안내 대사와 음성 재생
- 대화 히스토리
- 시스템 프롬프트 분리
- 웹 채팅 UI
- Enter 전송
- 생각 중 `. .. ...`
- GPT-SoVITS 음성 생성과 Unity용 `/unity/state`, `/unity/audio`
- YouTube 라이브 채팅 수집 및 캐릭터 자동 응답
- 대화 초기화

## 실행

Supertonic 3를 다시 사용할 경우 모델을 최초 한 번 다운로드합니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\download-supertonic.ps1
```

음성 설정은 `appsettings.json`의 `Supertonic` 섹션에서 변경합니다. `SpeakerId`는 0~9,
`Speed`는 큰 값일수록 빠르게 발화합니다.

OpenAI API 키:

```bash
export OPENAI_API_KEY="YOUR_API_KEY"
```

웹 서버:

```bash
dotnet run
```

터미널에 출력되는 localhost 주소로 접속하세요.

## 유휴 시간 혼잣말

`appsettings.json`의 `Idle.Enabled`가 `true`이면 Unity 연결 중 설정된 조용한 시간이
지속될 때 캐릭터가 짧게 혼잣말합니다. 기본 주제는 게임, 만화/애니메이션,
락·J-POP 음악, 디저트이며 같은 주제를 연속으로 선택하지 않습니다. `MinimumSilenceSeconds`와
`MaximumSilenceSeconds`로 무작위 간격을 조절하며 최소 간격은 10초입니다.
`Idle__Enabled=false` 환경 변수로 비활성화할 수도 있습니다.

기존 `Prompts/system.txt`의 성격과 최근 대화를 참고하고, 30초 이내에 받은 게임
상태가 있으면 그 사실에 대한 짧은 리액션도 생성합니다. 최근 8개 혼잣말을 참고해
반복을 줄입니다. 유휴 요청은 시청자가 보낸 메시지로 대화 기록에 저장하지 않습니다.
새 채팅 응답이나 게임 대사가 시작되면 생성 중인 혼잣말을 취소합니다.
이미 재생을 시작한 음성을 강제로 끊지는 않습니다.

Unity 연결이 없거나 음성 재생 중이면 실행하지 않으며, TTS까지 완성된 응답만
공개합니다. 생성 실패는 다음 유휴 간격까지 기다린 뒤 다시 시도합니다.
혼잣말 생성에도 OpenAI API 사용량이 발생합니다.

## YouTube 라이브 채팅 설정

Google Cloud Console에서 **YouTube Data API v3**를 활성화하고 API 키를 만든 뒤,
방송 시작 전에 다음 환경 변수를 설정합니다. API 키는 저장소의
`appsettings.json`에 직접 넣지 않는 것을 권장합니다.

macOS/Linux:

```bash
export YouTube__Enabled=true
export YouTube__ApiKey="YOUR_API_KEY"
export YouTube__VideoId="YOUTUBE_VIDEO_ID"
dotnet run
```

PowerShell:

```powershell
$env:YouTube__Enabled = "true"
$env:YouTube__ApiKey = "YOUR_API_KEY"
$env:YouTube__VideoId = "YOUTUBE_VIDEO_ID"
dotnet run
```

`VideoId`는 방송 URL의 `watch?v=` 뒤 값입니다. 서버는 시작 시점까지 올라온
기존 채팅은 건너뛰고, 이후 새 일반 텍스트 채팅만 순서대로 답변·음성화합니다.
방송 채널 본인의 메시지를 제외하려면 `YouTube__IgnoreChannelId`에 채널 ID를,
특정 호출어가 붙은 채팅만 답하려면 `YouTube__TriggerPrefix`에 예를 들어
`!지윤이`를 설정하세요. 답변이 밀리면 `QueueCapacity`를 넘는 새 메시지는
방송 지연이 무한히 늘지 않도록 버립니다.

현재 구현은 API 키만으로 채팅을 읽고 캐릭터가 방송에서 말하게 합니다.
유튜브 채팅창에 답변 텍스트를 다시 쓰지는 않습니다(그 기능에는 OAuth 인증이 필요합니다).

채팅 수신은 공식 gRPC `liveChatMessages.streamList`의 지속 연결을 사용합니다.
연결이 끊기면 마지막 `nextPageToken`으로 재연결하며 최근 메시지 ID로 중복을 제거합니다.
시작 전 기록은 `published_at`으로 걸러내고, 방송 종료 시 수집을 멈춥니다.
방송 ID 조회는 활성 채팅 ID를 찾으면 재접속 시에도 재사용합니다.
방송 시작 전 조회 간격은 60초부터 최대 5분까지 늘어나고 12회 후 중단합니다.
2분 미만의 짧은 연결이 반복되면 재접속 대기가 5초부터 최대 5분까지 늘어납니다.
2분 이상 유지된 연결의 정상 종료는 1초 후 재개합니다.
한 프로세스에서 최근 1시간 동안 스트림 연결을 12회 시도하면 수집을 중단합니다.
이 제한은 API 호출 폭증 방지용이며 Google의 실제 할당량 단위와 같지 않습니다.
서버 재시작 시 횟수가 초기화되므로 오류 중 반복 재시작하지 마세요.
연결 종료 로그에서 연결 지속 시간, 수신 배치/메시지 수, 누적 연결/영상 조회 횟수를 확인할 수 있습니다.
영상 조회에서 `quotaExceeded` 등 인증/할당량 오류가 발생하면 수집을 중단하므로,
문제를 해결한 후 서버를 재시작하세요. gRPC `ResourceExhausted`도 수집을 중단합니다.
호출 빈도 제한과 할당량 소진을 구분할 수 없어 할당량 보호를 우선합니다.
스트리밍 역시 할당량 제한을 받으며 이미 소진된 할당량을 복구하지는 않습니다.
프로토콜 출처: https://developers.google.com/youtube/v3/live/streaming-live-chat

Unity에서는 같은 포트의:

```text
http://localhost:5050/unity/state
```

를 사용하세요.

## 사실 검색 범위

검색은 한국어 위키백과의 문서 검색 API를 사용하며 별도 API 키가 필요하지 않습니다. 인물·개념처럼 비교적 안정적인 사실을 확인하는 용도입니다. 최신 뉴스, 현재 가격, 실시간 정보는 확인할 수 없으므로 추측 대신 확인 불가 응답을 반환합니다. 웹 테스트 UI에서는 답변 아래에 검색 문서 링크를 표시하며, 방송용 음성에는 링크를 읽지 않습니다.

검색이 필요한 응답이 1.2초 안에 준비되지 않으면 짧은 안내 음성을 생성합니다. 생성 도중 최종 답이 완성되면 안내를 생략할 수 있습니다. 음성 생성에 실패하면 텍스트 답변은 계속 제공됩니다.
