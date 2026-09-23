"""Run without microphone/model dependencies: python3 -m unittest discover -s Tools/RealtimeSTT."""
import importlib.util
from pathlib import Path
import sys
import threading
import types
import unittest
from unittest.mock import Mock, patch


def load_bridge():
    requests = types.ModuleType("requests")
    requests.Session = Mock
    requests.RequestException = type("RequestException", (Exception,), {})
    stt = types.ModuleType("RealtimeSTT")
    stt.AudioToTextRecorder = Mock
    spec = importlib.util.spec_from_file_location("bridge_under_test", Path(__file__).with_name("stt_bridge.py"))
    module = importlib.util.module_from_spec(spec)
    with patch.dict(sys.modules, requests=requests, RealtimeSTT=stt):
        spec.loader.exec_module(module)
    return module


class PendingSpeechTests(unittest.TestCase):
    def setUp(self):
        self.bridge = load_bridge()

    def test_merge_during_generation_and_playback(self):
        bridge = self.bridge
        started = threading.Event()
        finish_generation = threading.Event()
        playback = threading.Event()
        finish_playback = threading.Event()
        second_sent = threading.Event()
        sent = []

        def send(text):
            sent.append(text)
            if len(sent) == 1:
                started.set()
                self.assertTrue(finish_generation.wait(3))
                return 42
            second_sent.set()
            bridge.pending_speech.close()
            return 0

        def wait(version=0):
            if version == 42:
                playback.set()
                self.assertTrue(finish_playback.wait(3))
            return True

        bridge.send_to_vtuber = send
        bridge.wait_until_ai_finishes = wait
        bridge.pending_speech.add("첫 질문")
        worker = threading.Thread(target=bridge.process_pending_speech)
        worker.start()
        try:
            self.assertTrue(started.wait(2))
            bridge.pending_speech.add("추가 설명")
            bridge.pending_speech.add("두 번째 설명")
            self.assertEqual(sent, ["첫 질문"])
            finish_generation.set()
            self.assertTrue(playback.wait(2))
            bridge.pending_speech.add("재생 중 추가 설명")
            self.assertEqual(sent, ["첫 질문"])
            finish_playback.set()
            self.assertTrue(second_sent.wait(2))
            self.assertEqual(sent, ["첫 질문", "추가 설명\n두 번째 설명\n재생 중 추가 설명"])
        finally:
            finish_generation.set()
            finish_playback.set()
            bridge.pending_speech.close()
            worker.join(3)
        self.assertFalse(worker.is_alive())

    def test_silence_and_failed_status_do_not_acknowledge_last_audio(self):
        bridge = self.bridge
        statuses = iter([None, {"speaking": False, "completedVersion": 4},
                         {"speaking": True, "completedVersion": 5},
                         {"speaking": False, "completedVersion": 5},
                         {"speaking": False, "completedVersion": 5}])
        bridge.get_json = Mock(side_effect=lambda *a, **kw: next(statuses))
        bridge.stop_event = Mock()
        bridge.stop_event.is_set.return_value = False
        with patch.object(bridge.time, "monotonic", side_effect=[0, 0, 0, 0, 0, 0, 0, 1]):
            self.assertTrue(bridge.wait_until_ai_finishes(5))
        self.assertEqual(bridge.get_json.call_count, 5)

    def test_request_timeout_stops_dispatch_without_retry(self):
        bridge = self.bridge
        bridge.session.post.side_effect = bridge.requests.RequestException("timeout")
        bridge.send_to_vtuber("이미 수락됐을 수 있는 질문")
        self.assertTrue(bridge.stop_event.is_set())
        self.assertFalse(bridge.pending_speech.wait())
        self.assertEqual(bridge.session.post.call_count, 1)

    def test_duplicates_and_batch_boundary(self):
        queue = self.bridge.pending_speech
        queue.add("  첫 문장  ")
        queue.add("첫 문장")
        queue.add("  ")
        self.assertEqual(queue.take(), "첫 문장")
        queue.add("다음 문장")
        self.assertEqual(queue.take(), "다음 문장")
        self.assertEqual(queue.take(), "")


if __name__ == "__main__":
    unittest.main()
