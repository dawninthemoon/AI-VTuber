import os
import sys
import time
from typing import Any

import requests
from RealtimeSTT import AudioToTextRecorder


SERVER_URL = os.getenv("AIVTUBER_SERVER_URL", "http://127.0.0.1:5000").rstrip("/")
CHAT_URL = f"{SERVER_URL}/chat"
HEALTH_URL = f"{SERVER_URL}/health"
PLAYBACK_URL = f"{SERVER_URL}/voice/playback"

STT_MODEL = os.getenv("AIVTUBER_STT_MODEL", "small")
STT_DEVICE = os.getenv("AIVTUBER_STT_DEVICE", "cpu")
STT_COMPUTE_TYPE = os.getenv("AIVTUBER_STT_COMPUTE_TYPE", "int8")

session = requests.Session()
last_text = ""
last_text_time = 0.0


def get_json(url: str, timeout: float) -> dict[str, Any] | None:
    try:
        response = session.get(url, timeout=timeout)
        response.raise_for_status()
        return response.json()
    except (requests.RequestException, ValueError):
        return None


def wait_for_server() -> None:
    announced = False

    while get_json(HEALTH_URL, timeout=1.0) is None:
        if not announced:
            print(f"[STT] AI 서버를 기다리는 중: {SERVER_URL}")
            announced = True
        time.sleep(1.0)


def is_ai_speaking() -> bool:
    status = get_json(PLAYBACK_URL, timeout=0.6)
    return bool(status and status.get("speaking"))


def wait_until_ai_finishes() -> None:
    # Require a short stable quiet period so a gap between TTS segments does not
    # reopen the microphone and capture the next segment.
    quiet_since: float | None = None

    while True:
        if is_ai_speaking():
            quiet_since = None
            time.sleep(0.15)
            continue

        if quiet_since is None:
            quiet_since = time.monotonic()

        if time.monotonic() - quiet_since >= 0.8:
            return

        time.sleep(0.1)


def is_duplicate(text: str) -> bool:
    global last_text, last_text_time

    now = time.monotonic()
    duplicate = text == last_text and now - last_text_time < 2.0
    last_text = text
    last_text_time = now
    return duplicate


def send_to_vtuber(text: str) -> None:
    text = text.strip()

    if not text or is_duplicate(text):
        return

    print(f"\n[나] {text}")

    try:
        response = session.post(
            CHAT_URL,
            json={"message": text},
            timeout=180,
        )
        response.raise_for_status()
        payload = response.json()
        print(f"[AI] {payload.get('response', '')}")
    except requests.RequestException as error:
        print(f"[STT] 채팅 서버 요청 실패: {error}", file=sys.stderr)
    except ValueError as error:
        print(f"[STT] 서버 응답 JSON 오류: {error}", file=sys.stderr)


def on_recording_start() -> None:
    print("[STT] 듣는 중...")


def on_recording_stop() -> None:
    print("[STT] 인식 중...")


def main() -> None:
    wait_for_server()

    print(
        f"[STT] 모델={STT_MODEL}, 장치={STT_DEVICE}, "
        f"정밀도={STT_COMPUTE_TYPE}"
    )
    print("[STT] 첫 실행은 음성 인식 모델 다운로드 때문에 오래 걸릴 수 있습니다.")

    recorder = AudioToTextRecorder(
        model=STT_MODEL,
        language="ko",
        device=STT_DEVICE,
        compute_type=STT_COMPUTE_TYPE,
        batch_size=0,
        beam_size=3,
        silero_use_onnx=True,
        webrtc_sensitivity=2,
        post_speech_silence_duration=0.55,
        min_length_of_recording=0.5,
        min_gap_between_recordings=0.3,
        pre_recording_buffer_duration=0.4,
        ensure_sentence_starting_uppercase=False,
        ensure_sentence_ends_with_period=False,
        initial_prompt=(
            "한국어 대화입니다. 게임, 애니메이션, 버튜버, GPT-SoVITS, "
            "Ollama 같은 고유명사가 등장할 수 있습니다."
        ),
        spinner=False,
        on_recording_start=on_recording_start,
        on_recording_stop=on_recording_stop,
    )

    try:
        print("[STT] 준비 완료. 말해 주세요. 종료: Ctrl+C")

        while True:
            wait_until_ai_finishes()
            recorder.text(send_to_vtuber)
    except KeyboardInterrupt:
        print("\n[STT] 종료합니다.")
    finally:
        recorder.shutdown()
        session.close()


if __name__ == "__main__":
    main()
