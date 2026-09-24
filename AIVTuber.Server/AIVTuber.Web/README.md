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
지속될 때 캐릭터가 혼잣말을 시작합니다. 기본 주제는 게임, 만화/애니메이션,
락·J-POP 음악, 디저트입니다. 같은 주제에서 약 3개 단락을 이어간 뒤 다른 주제로 넘어갑니다.
`MinimumSilenceSeconds`와 `MaximumSilenceSeconds`는 첫 혼잣말까지의 무작위 간격이며 최소 10초입니다.
이후 채팅이 없으면 재생 완료 후 `ContinuationPauseSeconds`(기본 2초)를 쉬고 다음 단락을 준비합니다.
`Idle__Enabled=false` 환경 변수로 비활성화할 수도 있습니다.

기존 `Prompts/system.txt`의 성격과 최근 대화를 참고하고, 30초 이내에 받은 게임
상태가 있으면 그 사실에 대한 리액션도 생성합니다. 최근 24개 혼잣말, 바뀌는 전개 관점,
문자열 유사도 검사를 통해 반복을 줄입니다. 반복으로 판정되면 한 번만 다시 생성합니다.
단락은 2~4문장을 요청하며, 유휴 요청은 시청자 메시지로 대화 기록에 저장하지 않습니다.
채팅/게임 입력은 아직 공개 전인 혼잣말 준비만 취소합니다. 첫 음성을 공개한 후에는
해당 단락의 모든 TTS 조각을 재생하고 Unity의 마지막 조각 완료 보고를 받은 뒤 채팅에 응답합니다.
새 채팅이 대기 중이면 다음 혼잣말 단락은 시작하지 않습니다. 명시적인 대화 초기화는 음성을 중단합니다.

대기 중인 발화는 직접 채팅(STT·웹 입력), YouTube 채팅, 게임 대사, 혼잣말 순으로 선택합니다.
이미 Unity에 게시한 발화는 새 입력 때문에 끊지 않습니다. 일반 채팅과 게임 대사도
마지막 음성 조각의 Unity 재생 완료 보고를 받은 뒤 다음 발화에 순서를 넘깁니다.
Unity 연결이 없거나 완료 보고가 90초 동안 없으면 대기 순서를 풀고 경고를 남깁니다.
웹 테스트 화면의 **음성 중단** 버튼 또는 `POST /voice/interrupt`는 진행 중인 생성과
Unity 재생을 중단합니다. Unity는 재생 중인 조각 번호와 재생 시간을 보고하며, 서버는
완료된 조각과 현재 조각의 재생 비율로 들린 문장을 추정해 대화 기록을 줄입니다.
이 추정은 보고 간격과 실제 발음 속도 때문에 글자 단위로 정확하지 않을 수 있습니다.
일반 채팅은 OpenAI 응답의 문장 조각을 스트리밍으로 읽으며 TTS 생성을 먼저 시작합니다.
음성은 최종 JSON 응답을 검증한 뒤 순서대로 게시합니다. Unity 자막은 조각의 표시 문구를,
발화는 조각의 음성 문구를 사용하고 표정은 해당 조각의 재생 시작에 맞춰 적용됩니다.
현재 모델은 답변 전체에 하나의 감정·강도를 생성하므로 조각마다 같은 값을 전달합니다.

Unity 연결이 없거나 음성 재생 중이면 새 혼잣말을 시작하지 않습니다.
음성은 어구별로 생성하며, 현재 조각이 재생되는 동안 다음 조각을 준비합니다.
TTS 실패 시 이미 게시한 조각은 끝까지 기다립니다. Unity 연결이 끊기거나 조각 완료 보고가
90초 동안 없으면 대기 잠금을 풀고 경고를 남깁니다. 이런 장애 상황에서는 전체 단락 완주를 보장하지 않습니다.
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
`!지윤이`를 설정하세요. 응답 대기열 기본값은 `QueueCapacity: 100`입니다.
수신 기록은 응답 대기열과 별도로 최근 500개를 메모리에 보관합니다.
대기열 초과 메시지는 `not_selected / queue_full`로 기록하며 조용히 누락시키지 않습니다.
기록은 재시작하면 초기화되고 500개를 넘으면 오래된 기록부터 지워집니다.
수신 상태 화면 `/youtube.html` 또는 JSON `/youtube/status`에서 연결/재시도 시각과
메시지별 수신·필터 제외·응답 대기·처리 중·응답 준비·실패 상태를 확인할 수 있습니다.
화면 갱신은 로컬 서버만 조회합니다. `response_ready`는 Unity 음성 재생 완료가 아닙니다.

현재 구현은 API 키만으로 채팅을 읽고 캐릭터가 방송에서 말하게 합니다.
유튜브 채팅창에 답변 텍스트를 다시 쓰지는 않습니다(그 기능에는 OAuth 인증이 필요합니다).

채팅 수신은 공식 gRPC `liveChatMessages.streamList`의 지속 연결을 사용합니다.
연결이 끊기면 마지막 `nextPageToken`으로 재연결하며 최근 메시지 ID로 중복을 제거합니다.
시작 전 기록은 `published_at`으로 걸러내고, 방송 종료 시 수집을 멈춥니다.
방송 ID 조회는 활성 채팅 ID를 찾으면 재접속 시에도 재사용합니다.
방송 시작 전 조회 간격은 60초부터 최대 5분까지 늘어나며 자동 확인을 계속합니다.
정상 종료하며 토큰이 진행된 스트림은 1~3초 후 재개합니다(연결 시작 간격 최소 3초).
오류 또는 토큰 진행 없는 종료가 반복될 때만 5초부터 최대 5분까지 대기를 늘립니다.
재접속 횟수에 따른 영구 중단은 없습니다. 명시적인 잘못된 토큰 오류는 한 번 초기화합니다.
연결 종료 로그에서 연결 지속 시간, 수신 배치/메시지 수, 누적 연결/영상 조회 횟수를 확인할 수 있습니다.
명시적인 `quotaExceeded`/`dailyLimitExceeded`는 다음 태평양 시간 자정+1분까지
요청 없이 대기 후 자동 재개합니다. gRPC 상세 사유가 명확하지 않은
`ResourceExhausted`는 일시적 호출 제한일 수도 있으므로 30초부터 최대 5분까지 재시도합니다.
인증·권한 오류, 방송 종료 등은 상태 화면에 사유를 표시하고 수집을 멈춥니다.
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
