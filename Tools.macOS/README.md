# macOS 실행 도구

루트의 `Tools`에 있는 Windows 배치 파일과 같은 서비스를 실행합니다.
프로젝트 위치에 상관없이 사용할 수 있으며, RealtimeSTT 코드는 기존
`Tools/RealtimeSTT/stt_bridge.py`를 공유합니다.

## 최초 설정

1. 웹 서버에는 프로젝트가 대상으로 하는 **.NET 10 SDK**가 필요합니다.
2. RealtimeSTT와 GPT-SoVITS는 각각 macOS에서 동작하도록 의존성과 모델이 준비된 Python 환경이 필요합니다. 이 실행 도구는 의존성이나 모델을 설치하지 않습니다. Windows의 `runtime/python.exe`는 사용할 수 없습니다.
3. 이 폴더에서 `config.example.sh`를 `config.local.sh`로 복사하고 GPT-SoVITS 설치 위치와 Python 환경을 지정합니다. 개인 설정 파일은 Git에서 제외됩니다.

```bash
cp Tools.macOS/config.example.sh Tools.macOS/config.local.sh
```

기본 Conda 환경 이름은 STT가 `RealtimeSTT`, GPT-SoVITS가 `GPTSoVits`입니다.
가상환경을 사용한다면 `STT_PYTHON`, `GPT_SOVITS_PYTHON`에 해당 환경의 Python 실행 파일 절대 경로를 지정하세요. 이 설정은 Conda보다 우선합니다.

## 실행

Finder에서 필요한 `.command` 파일을 더블클릭하면 각각 터미널에서 실행됩니다.

| 파일 | 역할 |
| --- | --- |
| `WebServer.command` | 웹 서버 실행: `http://127.0.0.1:5050` |
| `RealtimeSTT.command` | 마이크 음성을 인식해서 웹 서버에 전달 |
| `GPT-SoVITS.command` | GPT-SoVITS API 실행: `http://127.0.0.1:9881` |

웹 서버를 먼저 실행하고 STT를 실행하세요. STT는 서버가 준비될 때까지 기다립니다.
GPT-SoVITS를 사용하는 경우 해당 API도 실행하세요. 대화에는 OpenAI GPT-6 Luna를 사용하므로 `config.local.sh`에 `OPENAI_API_KEY`를 설정해야 합니다(서버 설정은 `AIVTuber.Server/AIVTuber.Web/appsettings.json` 참고).

터미널에서 직접 실행할 수도 있습니다.

```bash
./Tools.macOS/WebServer.command
# 다른 터미널 창에서 실행
./Tools.macOS/RealtimeSTT.command
./Tools.macOS/GPT-SoVITS.command
```

각 서비스는 실행한 터미널에서 `Ctrl+C`로 종료합니다.
마이크 접근 요청이 나타나면 터미널 앱의 접근을 허용하세요.
STT 기본 장치는 기존 브리지와 동일한 `cpu`, 정밀도는 `int8`입니다.

실행 권한이 사라진 경우 프로젝트 루트에서 복구하세요.

```bash
chmod +x Tools.macOS/*.command
```

실행 오류가 나면 터미널 로그와 `config.local.sh`의 경로 및 환경 이름을 확인하세요.
