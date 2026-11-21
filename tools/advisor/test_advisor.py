"""Short end-to-end checks against a local fake gateway; no upstream calls."""
from __future__ import annotations

import base64
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import unittest
import wave

from transport import DEFAULT_CONFIG, WEB_CONFIG

HERE = Path(__file__).resolve().parent
PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aAVcAAAAASUVORK5CYII=")
VIDEO = b"\x00\x00\x00\x18ftypmp42" + b"\0" * 20


def chat(text="已收到附件，咨询接口正常。", reason="stop", **message):
    return {"choices": [{"message": {"content": text, **message}, "finish_reason": reason}]}


class AdvisorCLI(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="reol-advisor-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.requests = []
        self.reply = lambda method, path, payload: chat()
        outer = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def handle_request(self):
                raw = self.rfile.read(int(self.headers.get("Content-Length", "0")))
                payload = json.loads(raw) if raw else None
                outer.requests.append((self.command, self.path, payload, self.headers.get("Authorization")))
                result = outer.reply(self.command, self.path, payload)
                if isinstance(result, tuple):
                    code, mime, body = result
                else:
                    code, mime, body = 200, "application/json", json.dumps(result, ensure_ascii=False).encode()
                self.send_response(code)
                self.send_header("Content-Type", mime)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            do_GET = handle_request
            do_POST = handle_request

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=lambda: self.server.serve_forever(poll_interval=0.01), daemon=True)
        self.thread.start()
        self.addCleanup(self.close_server)
        self.config = self.root / "config.json"
        self.key = "offline-test-secret"
        self.web_key = "offline-web-test-secret"
        self.base_url = f"http://127.0.0.1:{self.server.server_port}/v1"
        self.web_env = {"REOL_ADVISOR_WEB_BASE_URL": self.base_url, "REOL_ADVISOR_WEB_API_KEY": self.web_key}
        # Legacy output settings must not leak into any constructed request.
        self.config.write_text(json.dumps({"base_url": self.base_url,
                                           "api_key": self.key, "max_tokens": 1}), encoding="utf-8")

    def close_server(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)

    def cli(self, *args, expected=0, legacy_config=True, env=None):
        overrides = ["--config", str(self.config)] if legacy_config else []
        child_env = {key: value for key, value in os.environ.items() if not key.upper().startswith("REOL_ADVISOR_")}
        child_env.update(env or {})
        result = subprocess.run([sys.executable, "-B", "-X", "utf8", str(HERE / "advisor.py"), *overrides,
                                 "--timeout", "2", "--wait-timeout", "2", "--poll-interval", "0.01",
                                 *map(str, args)], cwd=self.root, env=child_env, capture_output=True,
                                encoding="utf-8", timeout=20)
        self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
        self.assertNotIn(self.key, result.stdout + result.stderr)
        self.assertNotIn(self.web_key, result.stdout + result.stderr)
        return result

    def load(self, output, filename="summary.json"):
        return json.loads((output / filename).read_text(encoding="utf-8-sig"))

    def assert_uncapped(self, value):
        if isinstance(value, dict):
            self.assertFalse({"max_tokens", "max_completion_tokens", "maxOutputTokens", "max_output_tokens"} & value.keys())
            for child in value.values():
                self.assert_uncapped(child)
        elif isinstance(value, list):
            for child in value:
                self.assert_uncapped(child)

    def test_default_consultation_sends_neon_high_and_task_concurrently(self):
        source = self.root / "Chunk.cs"
        source.write_text("public int Revision;", encoding="utf-8")
        arrived = threading.Barrier(2, timeout=1.5)

        def reply(*args):
            arrived.wait()
            return chat()

        self.reply = reply
        output = self.root / "web-default"
        self.cli("ask", "--prompt", "检查区块提交", "--attach", source, "--out-dir", output, legacy_config=False,
                 env={**self.web_env, "REOL_ADVISOR_BASE_URL": self.base_url, "REOL_ADVISOR_API_KEY": self.key})
        self.assertEqual(len(self.requests), 2)
        for method, path, body, auth in self.requests:
            self.assertEqual((method, path, auth), ("POST", "/v1/chat/completions", "Bearer " + self.web_key))
            self.assertIn("public int Revision;", body["messages"][-1]["content"])
            self.assertIs(body["stream"], False)
            self.assertFalse({"temperature", "thinking"} & body.keys())
            self.assert_uncapped(body)
        bodies = {row[2]["model"]: row[2] for row in self.requests}
        self.assertEqual(set(bodies), {"gemini-4-neon", "gemini-4-neon-task"})
        self.assertEqual(bodies["gemini-4-neon"]["reasoning_effort"], "high")
        self.assertNotIn("reasoning_effort", bodies["gemini-4-neon-task"])
        self.assertEqual(bodies["gemini-4-neon"]["messages"], bodies["gemini-4-neon-task"]["messages"])
        summary = self.load(output)
        self.assertEqual((summary["provider"], summary["status"]), ("gemini-web", "complete"))

    def test_web_model_overrides_can_collect_one_shot_and_task_in_the_same_run(self):
        source = self.root / "chunk.cs"
        source.write_text("public int Epoch;", encoding="utf-8")
        output = self.root / "web-models"
        self.cli("ask", "--provider", "gemini-web", "--model", "gemini-4-neon", "--model", "gemini-4-neon-task",
                 "--prompt", "比较两份评审", "--attach", source, "--out-dir", output,
                 legacy_config=False, env=self.web_env)
        bodies = [row[2] for row in self.requests]
        self.assertCountEqual([body["model"] for body in bodies], ["gemini-4-neon", "gemini-4-neon-task"])
        self.assertEqual(bodies[0]["messages"], bodies[1]["messages"])
        self.assertIn("public int Epoch;", bodies[0]["messages"][-1]["content"])
        results = self.load(output)["results"].values()
        self.assertCountEqual([(run["requested_model"], run["status"]) for run in results],
                              [("gemini-4-neon", "complete"), ("gemini-4-neon-task", "complete")])

    def test_models_uses_the_selected_provider(self):
        self.reply = lambda method, path, body: {"data": [{"id": "gemini-4-neon"}]}
        web = self.cli("models", legacy_config=False,
                       env={**self.web_env, "REOL_ADVISOR_WEB_BASE_URL": self.base_url.removesuffix("/v1")})
        studio = self.cli("models", "--provider", "aistudio", env=self.web_env)
        self.assertEqual(web.stdout.strip(), "gemini-4-neon")
        self.assertEqual(studio.stdout.strip(), "gemini-4-neon")
        self.assertEqual(self.requests, [("GET", "/v1/models", None, "Bearer " + self.web_key),
                                         ("GET", "/v1/models", None, "Bearer " + self.key)])

    def test_models_rejects_an_unexpected_response_shape(self):
        result = self.cli("models", legacy_config=False, env=self.web_env, expected=1)
        self.assertIn("model catalogue must contain a data array", result.stderr)

    def test_missing_explicit_config_cannot_fall_back_to_another_config(self):
        env = {**self.web_env, "REOL_ADVISOR_BASE_URL": self.base_url, "REOL_ADVISOR_API_KEY": self.key}
        for provider in ("aistudio", "gemini-web"):
            with self.subTest(provider=provider):
                result = self.cli("models", "--provider", provider, "--config", self.root / "missing.json",
                                  legacy_config=False, env=env, expected=1)
                self.assertIn("Advisor config file not found", result.stderr)
        self.assertEqual(self.requests, [])

    def test_web_video_attachments_use_openai_file_parts(self):
        video = self.root / "clip.mp4"
        video.write_bytes(VIDEO)
        youtube = "https://youtu.be/abcdefghijk"
        self.cli("ask", "--model", "gemini-4-neon", "--prompt", "检查附件", "--attach", video,
                 "--attach", youtube, "--out-dir", self.root / "web-media", legacy_config=False, env=self.web_env)
        parts = self.requests[0][2]["messages"][-1]["content"]
        files = [part["file"] for part in parts if part["type"] == "file"]
        self.assertEqual(files, [
            {"filename": "clip.mp4", "file_data": "data:video/mp4;base64," + base64.b64encode(VIDEO).decode()},
            {"file_url": youtube},
        ])

    def test_provider_credentials_cannot_be_mixed_or_attached(self):
        wrong = self.cli("ask", "--provider", "gemini-web", "--config", DEFAULT_CONFIG,
                         "--prompt", "检查", expected=1, legacy_config=False, env=self.web_env)
        self.assertIn("aistudio", wrong.stderr)
        attached = self.cli("ask", "--provider", "aistudio", "--prompt", "检查", "--attach",
                            WEB_CONFIG, expected=1)
        self.assertIn("Credential/config files cannot be sent", attached.stderr)
        self.assertEqual(self.requests, [])

    def test_consultation_sends_identical_attachments_concurrently_and_collects_partial_results(self):
        text = self.root / "source.gd"
        text.write_text("original source", encoding="utf-8")
        image, audio, pdf, video = [self.root / name for name in ("reference.png", "sound.wav", "guide.pdf", "clip.mp4")]
        for path, data in ((image, PNG), (audio, b"RIFF0000WAVE"), (pdf, b"%PDF-test"), (video, VIDEO)):
            path.write_bytes(data)

        arrived = threading.Barrier(2, timeout=1.5)

        def reply(method, path, payload):
            temperature = payload["temperature"]
            if temperature == 1.2:
                text.write_text("changed while requests are running", encoding="utf-8")
            arrived.wait()
            if temperature == 1.2:
                return chat("partial", "length")
            return chat(f"received at temperature {temperature}")

        self.reply = reply
        output = self.root / "consultation"
        self.cli("ask", "--provider", "aistudio", "--prompt", "检查这些附件", "--context", text, "--attach", image,
                 "--attach", audio, "--attach", pdf, "--attach", video,
                 "--attach", "https://youtu.be/abcdefghijk", "--out-dir", output, env=self.web_env)
        summary = self.load(output)
        self.assertEqual(summary["status"], "partial")
        self.assertEqual(summary["results"]["neon-1.2"]["status"], "truncated")
        self.assertEqual(len(self.requests), 2)
        payloads = [request[2] for request in self.requests]
        self.assertEqual({payload["model"] for payload in payloads}, {"gemini-4-neon-search"})
        self.assertEqual({payload["temperature"] for payload in payloads}, {1.0, 1.2})
        self.assertEqual(summary["results"]["neon-1.0"]["status"], "complete")
        content = payloads[0]["messages"][1]["content"]
        self.assertIn("original source", content[0]["text"])
        self.assertNotIn("changed while", content[0]["text"])
        self.assertTrue({"text", "image_url", "input_audio", "file", "video_url"} <= {part["type"] for part in content})
        for payload in payloads:
            self.assert_uncapped(payload)
            self.assertEqual(payload["messages"], payloads[0]["messages"])
            self.assertEqual(payload["thinking"], "high")
            self.assertEqual(payload["reasoning_effort"], "high")
        state = self.load(output, "consultation.json")
        self.assertIn("original source", (output / "prompt.md").read_text(encoding="utf-8"))
        for name in ("neon-1.0", "neon-1.2"):
            self.assertEqual(self.load(output / name, "run.json")["input_sha256"], state["input_sha256"])
            self.assertFalse((output / name / "prompt.md").exists())
        answers = (output / "answers.md").read_text(encoding="utf-8")
        self.assertIn("received at temperature 1.0", answers)
        self.assertIn("## neon-1.2 (truncated)", answers)

    def test_inline_prompt_prints_answers_and_runtime_options_work_after_command(self):
        output = self.root / "consultation"
        result = self.cli("ask", "--prompt", "检查", "--timeout", "2", "--print-answer", "--out-dir", output)
        self.assertIn("咨询接口正常", result.stdout)
        summary = self.load(output)
        self.assertEqual(summary["status"], "complete")
        self.assertEqual(set(summary["results"]), {"neon-1.0", "neon-1.2"})
        self.assertEqual(self.load(output / "neon-1.0", "run.json")["answer_chars"], len("已收到附件，咨询接口正常。"))

    def test_all_high_level_requests_omit_output_limits_in_dry_run(self):
        prompt = self.root / "question.md"
        prompt.write_text("自含问题", encoding="utf-8")
        for kind in ("ask", "image", "speech", "music", "video"):
            with self.subTest(kind=kind):
                output = self.root / kind
                command = ["ask"] if kind == "ask" else ["generate", kind]
                self.cli(*command, "--prompt-file", prompt, "--dry-run", "--out-dir", output)
                for path in output.rglob("request.json"):
                    self.assert_uncapped(json.loads(path.read_text(encoding="utf-8")))
        self.assertEqual(self.requests, [])
        self.cli("ask", "--prompt", "x", "--max-tokens", "10", expected=2)

    def test_image_generation_saves_native_inline_data(self):
        reference = self.root / "reference.png"
        reference.write_bytes(PNG)
        self.reply = lambda *args: {"candidates": [{"content": {"parts": [
            {"text": "生成说明"}, {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(PNG).decode()}}]}, "finishReason": "STOP"}]}
        output = self.root / "image"
        self.cli("generate", "image", "--prompt", "修改参考图", "--attach", reference, "--out-dir", output)
        summary = self.load(output)
        self.assertEqual(Path(summary["media"][0]).read_bytes(), PNG)
        self.assertEqual(len(self.requests), 1)
        self.assertIn("inlineData", self.requests[0][2]["contents"][0]["parts"][-1])
        self.assert_uncapped(self.requests[0][2])

    def test_pcm_speech_is_wrapped_using_response_sample_rate(self):
        self.reply = lambda *args: {"candidates": [{"content": {"parts": [{"inline_data": {
            "mime_type": "audio/L16;rate=24000", "data": base64.b64encode(b"\0\0\1\0").decode()}}]}, "finishReason": "STOP"}]}
        output = self.root / "speech"
        self.cli("generate", "speech", "--prompt", "欢迎", "--out-dir", output)
        with wave.open(self.load(output)["media"][0], "rb") as audio:
            self.assertEqual((audio.getframerate(), audio.getnframes()), (24000, 2))

    def test_send_preserves_payload_and_downloads_only_supported_links(self):
        self.reply = lambda method, path, payload: (200, "image/png", PNG) if method == "GET" else chat([
            {"type": "text", "text": "![inline](data:image/png;base64," + base64.b64encode(PNG).decode() + ") ![local](/media/local.png) ![external](https://example.com/image.png)"},
            {"fileData": {"fileUri": "https://generativelanguage.googleapis.com/v1beta/files/file-123"}}])
        body = self.root / "body.json"
        payload = {"model": "gemini-4-hydrogen-search", "thinking": "low", "messages": [{"role": "user", "content": "draw"}]}
        body.write_text(json.dumps(payload), encoding="utf-8")
        output = self.root / "send"
        self.cli("send", "--endpoint", "/v1/chat/completions", "--request-file", body, "--out-dir", output)
        self.assertEqual(self.requests[0][2], payload)
        gets = [request[1] for request in self.requests if request[0] == "GET"]
        self.assertCountEqual(gets, ["/media/local.png", "/v1beta/files/file-123:download"])
        self.assertTrue(all(row[3] == "Bearer " + self.key for row in self.requests))
        manifest = self.load(output, "media.json")
        self.assertEqual(len(manifest["assets"]), 1)  # Identical bytes are deduplicated.
        self.assertEqual(manifest["links_not_downloaded"][0]["url"], "https://example.com/image.png")

    def test_incomplete_results_never_report_success(self):
        cases = [
            (chat("partial", "length"), "truncated"),
            (chat(""), "empty_answer"),
            (chat("", "tool_calls", tool_calls=[{"id": "call-1"}]), "tool_calls_not_executed"),
            ({"candidates": [{"content": {"parts": [{"functionCall": {"name": "draw", "args": {}}}]}, "finishReason": "STOP"}]}, "tool_calls_not_executed"),
            ({"candidates": [{"content": {"parts": [{"text": "partial"}]}, "finishReason": "MAX_TOKENS"}]}, "truncated"),
            ({"choices": [{"message": "invalid", "finish_reason": "stop"}]}, "failed"),
        ]
        body = self.root / "body.json"
        body.write_text("{}", encoding="utf-8")
        for i, (response, status) in enumerate(cases):
            with self.subTest(status=status):
                self.reply = lambda *args, response=response: response
                output = self.root / str(i)
                self.cli("send", "--endpoint", "/v1/chat/completions", "--request-file", body, "--out-dir", output, expected=2)
                self.assertEqual(self.load(output)["status"], status)
                self.assertEqual(self.load(output, "response.json"), response)

    def video_reply(self, method, path, payload):
        if method == "POST":
            return {"id": "job-1", "object": "video", "status": "queued"}
        if path.endswith("/content"):
            return 200, "video/mp4", VIDEO
        return {"id": "job-1", "object": "video", "status": "completed", "progress": 100}

    def test_video_waits_downloads_and_completed_resume_is_idempotent(self):
        self.reply = self.video_reply
        output = self.root / "video"
        self.cli("generate", "video", "--prompt", "山村", "--out-dir", output)
        self.assertEqual(self.load(output)["status"], "complete")
        self.assertEqual(Path(self.load(output)["media"][0]).read_bytes(), VIDEO)
        self.assertFalse(self.load(output, "run.json")["resume_available"])
        count = len(self.requests)
        self.cli("resume", output)
        self.assertEqual(len(self.requests), count)
        self.assertEqual(sum(row[0] == "POST" for row in self.requests), 1)

    def test_pending_video_resume_never_creates_a_second_job(self):
        self.reply = lambda *args: {"id": "job-1", "object": "video", "status": "queued"}
        output = self.root / "video"
        self.cli("generate", "video", "--prompt", "山村", "--wait-timeout", "0.04", "--out-dir", output, expected=2)
        self.assertEqual(self.load(output)["status"], "remote_task_pending")
        self.assertTrue(self.load(output)["resume_available"])
        self.reply = self.video_reply
        self.cli("resume", output)
        self.assertEqual(sum(row[0] == "POST" for row in self.requests), 1)
        self.assertEqual(len(self.load(output)["media"]), 1)

    def test_web_raw_video_resumes_only_on_its_original_provider(self):
        self.reply = lambda *args: {"id": "job-1", "object": "video", "status": "queued"}
        body = self.root / "video.json"
        body.write_text('{"prompt":"测试"}', encoding="utf-8")
        output = self.root / "web-video"
        self.cli("send", "--provider", "gemini-web", "--endpoint", "/v1/videos", "--request-file", body,
                 "--wait", "--wait-timeout", "0.04", "--out-dir", output,
                 expected=2, legacy_config=False, env=self.web_env)
        saved = self.load(output)
        self.assertEqual((saved["provider"], saved["status"]), ("gemini-web", "remote_task_pending"))
        count = len(self.requests)
        self.cli("resume", output, expected=1)
        self.assertEqual(len(self.requests), count)
        self.reply = self.video_reply
        self.cli("resume", output, "--provider", "gemini-web", legacy_config=False, env=self.web_env)
        self.assertEqual(self.load(output)["status"], "complete")
        self.assertEqual(sum(row[0] == "POST" for row in self.requests), 1)
        self.assertTrue(all(row[3] == "Bearer " + self.web_key for row in self.requests))

    def test_video_query_error_is_redacted_and_resumes_from_creation_receipt(self):
        def reply(method, path, payload):
            return self.video_reply(method, path, payload) if method == "POST" else {"error": {"message": self.key}}
        self.reply = reply
        output = self.root / "video"
        self.cli("generate", "video", "--prompt", "山村", "--out-dir", output, expected=2)
        self.assertNotIn(self.key, (output / "run.json").read_text(encoding="utf-8"))
        self.reply = self.video_reply
        self.cli("resume", output)
        self.assertEqual(sum(row[0] == "POST" for row in self.requests), 1)

    def test_credentials_are_rejected_before_any_request_and_outputs_are_not_overwritten(self):
        self.cli("ask", "--prompt", "检查", "--attach", self.config, expected=1)
        self.assertEqual(self.requests, [])
        output = self.root / "existing"
        output.mkdir()
        sentinel = output / "sentinel.txt"
        sentinel.write_text("keep", encoding="utf-8")
        self.cli("generate", "image", "--prompt", "x", "--out-dir", output, expected=1)
        self.assertEqual(sentinel.read_text(encoding="utf-8"), "keep")
        self.assertEqual(self.requests, [])

    def test_video_invalid_json_status_can_resume_using_creation_receipt(self):
        self.reply = lambda method, path, payload: self.video_reply(method, path, payload) if method == "POST" else (200, "application/json", b"broken json")
        output = self.root / "video"
        self.cli("generate", "video", "--prompt", "山村", "--out-dir", output, expected=2)
        self.assertEqual(self.load(output)["status"], "failed")
        self.reply = self.video_reply
        self.cli("resume", output)
        self.assertEqual(sum(row[0] == "POST" for row in self.requests), 1)

    def test_broken_media_does_not_discard_other_assets(self):
        self.reply = lambda *args: {"candidates": [{"content": {"parts": [
            {"inlineData": {"mimeType": "image/png", "data": "!!!"}},
            {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(PNG).decode()}}]}, "finishReason": "STOP"}]}
        output = self.root / "image"
        self.cli("generate", "image", "--prompt", "x", "--out-dir", output, expected=2)
        self.assertEqual(self.load(output)["status"], "media_error")
        self.assertEqual(len(self.load(output, "media.json")["assets"]), 1)
        self.assertTrue(self.load(output, "media.json")["errors"])


if __name__ == "__main__":
    unittest.main()
