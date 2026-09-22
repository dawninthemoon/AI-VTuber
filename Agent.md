# AI VTuber 프로젝트 작업 지침

## 목표와 현재 상태

- 이 저장소의 목표는 YouTube, 치지직 등 방송 플랫폼의 실시간 채팅을 읽고, AI 캐릭터가 응답하며, Unity Live2D 아바타가 대사와 감정을 표현하는 것이다.
- 현재 웹 채팅 UI는 대화 흐름을 시험하는 입력 도구다. 웹 브라우저를 최종 채팅 소스로 가정해 구조를 고정하지 않는다.
- 아직 플랫폼 채팅 수집, 발화 대상 선택, 방송 운영 정책, 실제 음성 재생은 완성되지 않았다. 계획과 구현 상태를 구분해 설명한다.
- Unity 버전은 `6000.0.81f1`이다. 서버는 ASP.NET Core, AI 응답은 Ollama, 아바타는 Live2D Cubism SDK를 사용한다.

## 디렉터리와 소유 범위

- `AIVTuber.Server/AIVTuber.Web`: 현재 실제 백엔드. `Program.cs`에 HTTP 엔드포인트, `Services`에 대화·상태·음성 서비스, `Models`에 계약 타입, `Prompts/system.txt`에 캐릭터 프롬프트가 있다.
- `AIVTuber.Server/AIVTuber.Web/wwwroot`: 개발용 웹 채팅 UI다.
- `AIVTuber.Unity/Assets/Scripts`: 직접 작성한 Unity 런타임 코드다.
- `AIVTuber.Unity/Assets/Scenes/SampleScene.unity`: 현재 Unity 씬이다.
- `AIVTuber.Unity/Assets/Models/Hiyori`: Live2D 모델 애셋이다.
- `AIVTuber.Unity/Assets/Live2D/Cubism`, `TextMesh Pro`: 외부 패키지 코드와 애셋이다. 별도 요청 없이 수정하지 않는다.
- `AIVTuber.Server/Program.cs`와 `AIVTuber.Server/AIVTuber.Server.csproj`도 있으므로 서버 작업 시 실제 실행 프로젝트와 엔드포인트 위치를 먼저 확인한다.

## 현재 데이터 흐름

1. 웹 테스트 UI가 `POST /chat`에 메시지를 보낸다.
2. `ChatService`가 대화 기록과 `ChatClassifier`의 생성 프로필을 사용해 `OllamaService`에서 응답을 얻는다.
3. `CharacterStateService`가 `Text`, `Emotion`, `Intensity`, `Version`을 보관한다. `POST /chat/reset`은 기록과 상태를 초기화한다.
4. Unity의 `AIVTuberStateReceiver`가 기본값 `http://localhost:5000/unity/state`를 0.25초 간격으로 조회하고 버전이 바뀌면 자막, 감정, 임시 립싱크를 갱신한다.
5. `Live2DEmotionController`는 Cubism 파라미터를 직접 변경한다. `Live2DExpressionController`도 존재하지만 현재 수신 코드에서는 호출되지 않는다.
6. `/unity/audio` 엔드포인트와 Unity의 `PlayVoice` 코드는 있으나 현재 대화 흐름에서 음성 생성과 재생이 연결되어 있지 않다. `Live2DLipSyncController`는 글자 수로 발화 시간을 추정하는 임시 구현이다.

## 플랫폼 채팅 확장 원칙

- YouTube·치지직별 인증, 연결, 재연결, 이벤트 파싱은 서버의 별도 어댑터에 둔다. Unity가 플랫폼 API를 직접 호출하지 않도록 한다.
- 웹 채팅과 플랫폼 채팅은 공통 입력 모델로 정규화한다. 출처 플랫폼, 방송 또는 채널 ID, 메시지 ID, 작성자 식별자와 표시 이름, 수신 시각, 본문, 메시지 종류를 필요에 맞게 보존한다. 플랫폼 전용 필드는 어댑터 경계에 둔다.
- 채팅 수집과 AI 응답 생성은 분리한다. 중복 제거, 필터링, 우선순위, 응답 대상 선택, 속도 제한과 큐 처리 이후에 `ChatService`를 호출하도록 설계한다. 모든 메시지에 자동으로 답하도록 가정하지 않는다.
- 하나의 방송 세션에 속하는 대화 기록과 캐릭터 상태를 명확히 관리한다. 현재 `ChatService`와 `CharacterStateService`는 싱글턴이므로 다중 방송·다중 채널 지원 시 상태 격리가 필요하다.
- 플랫폼 장애나 재연결 중에도 Unity 표현 계층이 안정적으로 동작하도록 입력 수집 실패와 출력 전달 실패를 분리한다. 취소, 타임아웃, 재시도, 순서 보장 정책을 기능 추가 시 명시한다.
- 외부 채팅 본문은 신뢰할 수 없는 입력으로 취급한다. 캐릭터의 시스템 프롬프트와 방송 운영 규칙을 채팅 텍스트가 덮어쓰지 못하게 한다. 토큰과 비밀값은 소스나 Unity 애셋에 저장하지 않는다.
- 플랫폼 API, 약관, 인증 방식은 구현 당시 공식 문서로 확인한다. 현재 프로젝트에 없는 플랫폼 연동이 이미 동작한다고 기록하지 않는다.

## 코딩 스타일

- C# 코드는 `/Users/hanjunseo/Desktop/SpaceDiver/Assets/Scripts`의 스타일을 참고한다. 실제로 사용할 수 있는 언어 문법은 각 프로젝트의 대상 프레임워크와 Unity 설정에 맞춘다.
- 파일명과 public 타입명을 일치시키고, 타입과 메서드는 `PascalCase`, 매개변수와 지역 변수는 `camelCase`, private 런타임 필드는 `_camelCase`를 사용한다. 기존 파일의 다른 스타일은 관련 코드를 수정할 때 점진적으로 맞춘다.
- 중괄호는 다음 줄에 두고, 조건문에도 중괄호를 쓴다. 긴 식은 의미 단위로 줄바꿈하고 조기 반환으로 중첩을 줄인다. 주석은 코드만으로 드러나지 않는 의도와 제약을 설명한다.
- Unity 컴포넌트 참조는 현재 오브젝트나 안정적인 자식에서 찾을 수 있으면 `Awake`에서 한 번 찾아 캐시한다. 씬 간 연결, 프리팹, 모델, 조정 가능한 수치처럼 Inspector 설정이 필요한 값은 `[SerializeField] private`로 둔다. SpaceDiver의 `Bind` 헬퍼는 이 저장소에 없으므로 복사 없이 호출하지 않는다.
- `MonoBehaviour`는 Unity 수명주기와 표현에 집중시키고, 입력 정규화·선택·대화 정책처럼 테스트 가능한 로직은 일반 C# 타입 또는 서버 서비스에 둔다.
- 프레임 루프에서 반복적인 `GetComponent`, LINQ, 불필요한 할당과 네트워크 요청을 추가하지 않는다. 네트워크 호출은 취소와 오류 처리를 갖추고 Unity 오브젝트 변경은 메인 스레드에서 수행한다.
- 서버는 기존 `AIVTuber.Web` 네임스페이스와 DI 패턴을 따르며, JSON 계약이나 엔드포인트를 변경하면 Unity 소비 코드와 함께 수정한다. `Emotion` 값은 현재 `neutral`, `happy`, `angry`, `sad`, `surprised`를 사용하고 `Intensity`는 0~1 범위다.

## Unity와 Live2D 주의사항

- 감정, 표정, 립싱크가 같은 Cubism 파라미터를 쓰면 갱신 순서와 소유자를 명확히 정한다. 특히 `ParamMouthOpenY`는 감정 표현과 립싱크가 모두 접근한다.
- 모델 파라미터 ID와 표정 인덱스는 모델 애셋에 종속된다. 코드 변경 시 실제 Hiyori 모델과 씬 연결을 확인한다.
- 새 Unity 애셋과 스크립트의 `.meta` 파일을 보존하고 기존 GUID를 임의로 재생성하지 않는다. 씬·프리팹 YAML 수정은 필요한 범위로 제한한다.
- `Library`, `Temp`, `Logs`, `Obj`, 생성된 IDE 프로젝트 파일은 소스 변경 대상으로 취급하지 않는다.

## 검증과 작업 보고

- 작업 전 `git status --short`를 확인하고 기존 사용자 변경을 보존한다.
- 서버 코드 변경은 대상 `.csproj`의 `dotnet build`로 확인한다. API 계약 변경 시 실제 요청과 Unity 역직렬화 필드명을 함께 확인한다.
- Unity 코드·애셋 변경은 Unity 에디터 컴파일과 Play Mode에서 자막, 감정, 입 움직임, 서버 연결 실패 시 동작을 확인한다. 에디터 실행이 불가능하면 확인하지 못한 항목을 명시한다.
- 변경 후 `git diff --check`를 실행한다. 완료 보고에는 바뀐 파일, 확인한 동작, 남은 수동 확인 사항을 간단히 적는다.
