import os
import sys
import threading
import time
from typing import Any

import requests
from RealtimeSTT import AudioToTextRecorder


SERVER_URL = os.getenv("AIVTUBER_SERVER_URL", "http://127.0.0.1:5050").rstrip("/")
CHAT_URL = f"{SERVER_URL}/chat"
HEALTH_URL = f"{SERVER_URL}/health"
PLAYBACK_URL = f"{SERVER_URL}/voice/playback"

STT_MODEL = os.getenv("AIVTUBER_STT_MODEL", "small")
STT_DEVICE = os.getenv("AIVTUBER_STT_DEVICE", "cpu")
STT_COMPUTE_TYPE = os.getenv("AIVTUBER_STT_COMPUTE_TYPE", "int8")

session = requests.Session()
stop_event = threading.Event()


class PendingSpeech:
    """Keep incoming transcripts separate from the batch already being sent."""

    def __init__(self) -> None:
        self._condition = threading.Condition()
        self._texts: list[str] = []
        self._last_text = ""
        self._last_text_time = 0.0
        self._closed = False

    def add(self, text: str) -> None:
        text = text.strip()
        if not text:
            return
        with self._condition:
            if self._closed:
                return
            now = time.monotonic()
            duplicate = text == self._last_text and now - self._last_text_time < 2.0
            self._last_text = text
            self._last_text_time = now
            if duplicate:
                return
            self._texts.append(text)
            print(f"[STT] 입력 보관 ({len(self._texts)}개): {text}")
            self._condition.notify()

    def wait(self) -> bool:
        with self._condition:
            self._condition.wait_for(lambda: self._texts or self._closed)
            return not self._closed

    def take(self) -> str:
        with self._condition:
            if self._closed:
                return ""
            text = "\n".join(self._texts)
            self._texts.clear()
            return text

    def close(self) -> None:
        with self._condition:
            if not self._closed and self._texts:
                print("[STT] 아직 전송하지 않은 입력:\n" + "\n".join(self._texts), file=sys.stderr)
            self._closed = True
            self._condition.notify_all()


pending_speech = PendingSpeech()


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


def wait_until_ai_finishes(audio_version: int = 0) -> bool:
    # A failed status check must not be mistaken for finished playback.
    quiet_since: float | None = None
    next_notice = time.monotonic() + 10.0
    while not stop_event.is_set():
        status = get_json(PLAYBACK_URL, timeout=0.6)
        if (status is None or status.get("speaking")
                or status.get("completedVersion", 0) < audio_version):
            quiet_since = None
        else:
            if quiet_since is None:
                quiet_since = time.monotonic()
            if time.monotonic() - quiet_since >= 0.8:
                return True
        if audio_version and time.monotonic() >= next_notice:
            print(f"[STT] 음성 {audio_version} 재생 완료 대기 중. Unity 연결과 오디오 재생을 확인하세요.")
            next_notice = time.monotonic() + 10.0
        stop_event.wait(0.1)
    return False


def process_pending_speech() -> None:
    try:
        while pending_speech.wait():
            if not wait_until_ai_finishes():
                break
            # Drain only when ready to send: all speech received during the
            # previous request/playback becomes a single next request.
            text = pending_speech.take()
            if text:
                audio_version = send_to_vtuber(text)
                if not wait_until_ai_finishes(audio_version):
                    break
    finally:
        session.close()


def send_to_vtuber(text: str) -> int:
    text = text.strip()

    if not text:
        return 0

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
        return int(payload.get("audioVersion", 0))
    except requests.RequestException as error:
        print(f"[STT] 채팅 서버 요청 실패: {error}", file=sys.stderr)
        print(f"[STT] 처리 여부를 확인할 입력: {text}", file=sys.stderr)
        # The server may still be processing a timed-out request. Do not send
        # another batch or automatically replay a possibly accepted message.
        stop_event.set()
        pending_speech.close()
    except ValueError as error:
        print(f"[STT] 서버 응답 JSON 오류: {error}", file=sys.stderr)
        stop_event.set()
        pending_speech.close()
    return 0


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

    worker = threading.Thread(target=process_pending_speech, name="stt-request-worker", daemon=True)
    worker.start()

    try:
        print("[STT] 준비 완료. 말해 주세요. 종료: Ctrl+C")

        while not stop_event.is_set():
            # Synchronous transcription preserves utterance order. Network
            # requests run only on the worker, so recording can continue.
            text = recorder.text()
            if text:
                pending_speech.add(text)
    except KeyboardInterrupt:
        print("\n[STT] 종료합니다.")
    finally:
        stop_event.set()
        pending_speech.close()
        recorder.shutdown()
        worker.join(timeout=2.0)


if __name__ == "__main__":
    main()
