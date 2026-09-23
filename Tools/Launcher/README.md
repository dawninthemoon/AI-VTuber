# AI VTuber Launcher

Windows: `Tools/Launcher.bat` 더블클릭.
macOS: `Tools.macOS/Launcher.command` 더블클릭.

Python 3.10 이상과 Tkinter가 필요합니다. Python 설치 프로그램에서 Tcl/Tk를 포함하세요.
`python3 -m tkinter` (Windows: `py -3 -m tkinter`)로 설치를 확인할 수 있습니다.
런처는 외부 Python 패키지가 필요 없으며, .NET 10 / GPT-SoVITS / RealtimeSTT / Java와 게임은 기존 설치를 사용합니다.

1. YouTube/OpenAI API 키를 입력하고 각 **적용** 버튼을 누릅니다. 보기/숨김 버튼으로 값을 전환합니다.
2. 채팅 수집을 원하면 방송 Video ID와 채팅 수집 체크도 적용합니다.
3. GPT-SoVITS 폴더를 선택합니다. Windows 패키지는 `runtime/python.exe`를 자동 사용합니다.
4. Python 경로를 비우면 Conda의 `GPTSoVits`, `RealtimeSTT` 환경을 사용합니다. 다른 가상환경은 해당 `python`/`python.exe`를 선택합니다.
5. 슬더스 설치의 `ModTheSpire.jar`를 선택하고 **경로 적용**을 누릅니다.
6. 웹서버 → SoVITS → RealtimeSTT → 슬더스 순서로 시작합니다. 같은 버튼을 다시 누르면 종료합니다.

웹서버와 STT 목적지는 `http://127.0.0.1:5050`, SoVITS는 `9881`로 고정합니다.
포트를 다른 프로그램이 쓰고 있으면 시작을 거부합니다. 실행 중 표시는 프로세스 상태이며 모델 로딩/서비스 준비 완료는 로그를 확인하세요.
슬더스 버튼은 모드 런처를 엽니다. ModTheSpire에서 CommunicationMod를 활성화하고 게임을 시작하세요.
브리지는 CommunicationMod의 자식 프로세스로 실행되므로 별도 버튼으로 실행하지 않습니다.
CommunicationMod 설정은 `AIVTuber.SpireBridge/README.md`를 참고하세요. Unity 아바타는 별도로 실행합니다.

각 서비스의 콘솔은 웹서버, GPT-SoVITS, RealtimeSTT, 슬더스 탭으로 분리됩니다.
서비스를 시작하면 해당 콘솔 탭이 자동으로 열립니다.
콘솔에서는 `Ctrl+A/C/V`를 사용할 수 있고 macOS의 `Command+A/C/V`도 지원합니다.
우클릭 메뉴에서도 전체 선택, 복사, 붙여넣기를 사용할 수 있습니다. 붙여넣기는
프로세스에 명령을 보내는 기능이 아니라 화면에 표시된 콘솔 텍스트를 편집합니다.

API 키는 다음 운영체제 사용자 저장소에 보관하고 다음 실행 때 자동으로 불러옵니다.

- Windows: `HKEY_CURRENT_USER\Software\AIVTuber\Launcher`
- macOS: `~/Library/Preferences/com.aivtuber.launcher.plist`

기존 `settings.json`에 저장된 키는 첫 실행 시 위 저장소로 자동 이전되고 JSON에서는 제거됩니다.
실행 경로와 방송 설정은 macOS `~/Library/Application Support/AIVTuberLauncher/settings.json`,
Windows `%APPDATA%/AIVTuberLauncher/settings.json`에 계속 저장됩니다.
GUI 마스킹은 암호화가 아닙니다. 레지스트리나 plist를 내보내 공유하지 마세요.
설정 적용은 저장만 하므로 실행 중인 서비스는 재시작해야 합니다.
기존 `config.local.sh`와는 독립된 설정입니다. Ollama는 현재 GPT 기반 대화에 필요하지 않습니다.

종료 버튼은 런처가 실행한 프로세스 트리를 종료합니다. 게임 진행 상황을 먼저 저장하세요.
Windows에서는 `taskkill /T`, macOS에서는 독립 프로세스 그룹을 사용합니다.
외부에서 별도로 시작한 서비스는 종료하지 않습니다.
