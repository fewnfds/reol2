"""Run one request, collect its usable results, and resume existing video jobs."""
from __future__ import annotations

from datetime import datetime, timezone
import json
from pathlib import Path
import re
import time
import urllib.parse

from media import extension, save_binary, save_media
from transport import PROVIDER, exchange, redact, write_json

HERE = Path(__file__).resolve().parent
SUCCESS = {"complete", "received", "prepared"}
PENDING = {"queued", "in_progress", "pending", "running"}
MEDIA_ROUTES = re.compile(r"^/(?:media/[^/]+|v1/files/[^/]+/content|v1beta/files/[^/]+:download)$")


def timestamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S-%fZ")


def answer_text(message: dict) -> str:
    content = message.get("content")
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "\n".join(part["text"] for part in content if isinstance(part, dict) and isinstance(part.get("text"), str))
    return ""


def fetch_media(config: dict, url: str, timeout: float):
    """Keep credentials on the gateway; map Gemini Files URIs back to it."""
    parsed, origin = urllib.parse.urlsplit(url), urllib.parse.urlsplit(config["base_url"])
    path = parsed.path
    if parsed.netloc:
        if parsed.hostname == "generativelanguage.googleapis.com" and re.fullmatch(r"/v1beta/files/[^/:]+(?::download)?", path):
            if not path.endswith(":download"):
                path += ":download"
        elif (parsed.scheme, parsed.netloc) != (origin.scheme, origin.netloc):
            return None
    if not MEDIA_ROUTES.fullmatch(path):
        return None
    body, mime = exchange(config, path, None, timeout, root_path=True)
    if extension(body, mime) == ".bin":
        raise ValueError(f"Media URL returned an unsupported content type: {mime}")
    return body, mime


def details(response: dict, manifest: dict) -> dict:
    text, citations, reasons, calls = [], [], [], 0
    has_generation = "choices" in response or "candidates" in response
    has_image_data = "data" in response and response.get("object") != "list"
    for choice in response.get("choices") or []:
        message = choice.get("message") or {}
        text.append(answer_text(message))
        citations.extend(message.get("annotations") or [])
        calls += len(message.get("tool_calls") or []) + bool(message.get("function_call"))
        reasons.append(choice.get("finish_reason"))
    for candidate in response.get("candidates") or []:
        for part in (candidate.get("content") or {}).get("parts") or []:
            if isinstance(part.get("text"), str) and not part.get("thought"):
                text.append(part["text"])
            calls += bool(part.get("functionCall") or part.get("function_call"))
        reasons.append(candidate.get("finishReason", candidate.get("finish_reason")))
        if candidate.get("groundingMetadata"):
            citations.append({"groundingMetadata": candidate["groundingMetadata"]})
        if candidate.get("citationMetadata"):
            citations.append({"citationMetadata": candidate["citationMetadata"]})
    answer = "\n".join(text)
    status = "complete" if has_generation or has_image_data else "received"
    if response.get("status") in PENDING or ("done" in response and not response["done"]):
        status = "remote_task_pending"
    elif response.get("status") in ("failed", "cancelled", "canceled", "expired") or response.get("error"):
        status = "remote_task_failed"
    elif calls:
        status = "tool_calls_not_executed"
    elif any(reason in ("length", "MAX_TOKENS", "CONTINUATION") for reason in reasons):
        status = "truncated"
    elif (has_generation and (not reasons or any(reason not in ("stop", "STOP") for reason in reasons))
          or response.get("promptFeedback", {}).get("blockReason")):
        status = "incomplete"
    elif (has_generation or has_image_data) and not answer.strip() and not manifest["assets"]:
        status = "empty_answer"
    if manifest["errors"] and status in SUCCESS:
        status = "media_error"
    return {"status": status, "answer": answer, "citations": citations,
            "finish_reasons": reasons, "tool_calls_returned": calls,
            "usage": response.get("usageMetadata", response.get("usage"))}


def summarize(output: Path, state: dict) -> dict:
    summary = {"status": state["status"], "code": 0 if state["status"] in SUCCESS else 2,
               "output_dir": str(output.resolve()), "answer": state.get("answer_file"),
               "media": state.get("media_files", [])}
    for key in ("provider", "error", "task_id", "resume_available", "requested_model", "elapsed_seconds"):
        if key in state:
            summary[key] = state[key]
    write_json(output / "summary.json", summary)
    return summary


def process_response(response: dict, output: Path, state: dict, config: dict, timeout: float,
                     extra_asset: dict | None = None) -> None:
    manifest = save_media(response, output, lambda url: fetch_media(config, url, timeout))
    if extra_asset:
        manifest["assets"].append(extra_asset)
    # Extraction errors can originate in HTTP messages, so redact before logging.
    for error in manifest["errors"]:
        error["error"] = redact(error["error"], config)
    write_json(output / "media.json", manifest)
    result = details(response, manifest)
    answer = result.pop("answer")
    (output / "answer.md").write_text(answer, encoding="utf-8")
    write_json(output / "citations.json", {"annotations": result.pop("citations")})
    state.update(result)
    state["answer_chars"] = len(answer)
    if state["answer_chars"]:
        state["answer_file"] = str((output / "answer.md").resolve())
    state["media_count"] = len(manifest["assets"])
    state["media_files"] = [str((output / asset.get("wav_file", asset["file"])).resolve()) for asset in manifest["assets"]]
    expected = state.get("expected_media")
    if expected and state["status"] in SUCCESS:
        prefix = {"image": "image/", "speech": "audio/", "music": "audio/", "video": "video/"}[expected]
        if not any(asset["mime_type"].lower().startswith(prefix) for asset in manifest["assets"]):
            state["status"] = "missing_media"
        else:
            state["status"] = "complete"
    if response.get("error"):
        state["error"] = redact(response["error"], config)
    if state.get("expected_text") and state["status"] in SUCCESS and not answer.strip():
        state["status"] = "empty_answer"


def decode_response(body: bytes, mime: str, output: Path) -> dict | None:
    if "json" not in mime.lower():
        (output / ("response" + extension(body, mime))).write_bytes(body)
        return None
    # Preserve the original even when JSON decoding or later processing fails.
    (output / "response.json").write_bytes(body)
    result = json.loads(body)
    if not isinstance(result, dict):
        raise ValueError("Expected an object JSON response; original preserved.")
    return result


def collect_video(response: dict, output: Path, state: dict, config: dict, timeout: float,
                  wait_timeout: float, poll_interval: float) -> tuple[dict, dict | None]:
    task_id = response.get("id")
    if not isinstance(task_id, str) or not task_id:
        raise ValueError("Video response has no task ID; the original response was saved.")
    route = "/v1/videos/" + urllib.parse.quote(task_id, safe="")
    state.update({"task_id": task_id, "pending_endpoint": route, "resume_available": True})
    write_json(output / "run.json", state)
    deadline = time.monotonic() + wait_timeout
    while response.get("status") in PENDING:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return response, None
        time.sleep(min(poll_interval, remaining))
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return response, None
        body, mime = exchange(config, route, None, min(timeout, remaining), root_path=True)
        response = decode_response(body, mime, output)
        if response is None:
            raise ValueError("Video status endpoint returned a non-JSON response.")
        state["poll_count"] = state.get("poll_count", 0) + 1
        state["progress"] = response.get("progress")
        write_json(output / "run.json", state)
    if response.get("status") != "completed":
        if response.get("status") not in ("failed", "cancelled", "canceled", "expired") and not response.get("error"):
            raise ValueError("Video response has an unrecognized task status.")
        return response, None
    body, mime = exchange(config, route + "/content", None, timeout, root_path=True)
    if not mime.lower().startswith("video/"):
        raise ValueError(f"Video download returned {mime}, expected video media.")
    asset = save_binary(body, mime, output, "video", route + "/content")
    state["resume_available"] = False
    return response, asset


def execute(config: dict, endpoint: str, payload: dict | None, output: Path,
            timeout: float, *, prepared: dict | None = None, record_prompt: bool = True,
            dry_run: bool = False,
            expected_media: str | None = None, expected_text: bool = False, wait_video: bool = False,
            wait_timeout: float = 900, poll_interval: float = 5) -> dict:
    output.mkdir(parents=True, exist_ok=False)
    state = {"started_utc": timestamp(), "endpoint": endpoint,
             "method": "GET" if payload is None else "POST", "status": "prepared", "automatic_retries": 0,
             "provider": config["provider"]}
    if payload is not None:
        write_json(output / "request.json", payload)
        if payload.get("model"):
            state["requested_model"] = payload["model"]
        elif "/models/" in endpoint:
            state["requested_model"] = urllib.parse.unquote(endpoint.split("/models/")[-1].split(":")[0])
    if prepared:
        if record_prompt:
            (output / "prompt.md").write_text(prepared["prompt"], encoding="utf-8")
        state.update({key: prepared[key] for key in ("prompt_sha256", "input_sha256", "attachments")})
    if expected_media:
        state["expected_media"] = expected_media
    if expected_text:
        state["expected_text"] = True
    write_json(output / "run.json", state)
    if dry_run:
        return summarize(output, state)
    started = time.monotonic()
    state["status"] = "requesting"
    write_json(output / "run.json", state)
    try:
        body, mime = exchange(config, endpoint, payload, timeout, root_path=True)
        state.update({"content_type": mime, "response_bytes": len(body)})
        response = decode_response(body, mime, output)
        if response is None:
            asset = save_binary(body, mime, output, "media-01", endpoint)
            manifest = {"assets": [asset], "links_not_downloaded": [], "errors": []}
            write_json(output / "media.json", manifest)
            state.update({"status": "received", "media_count": 1,
                          "media_files": [str((output / asset.get("wav_file", asset["file"])).resolve())]})
            if expected_media and not mime.lower().startswith({"image": "image/", "speech": "audio/", "music": "audio/", "video": "video/"}[expected_media]):
                state["status"] = "missing_media"
            elif expected_media:
                state["status"] = "complete"
            if expected_text:
                state["status"] = "incomplete"
        else:
            extra = None
            if wait_video and not response.get("error"):
                (output / "creation.json").write_bytes(body)
                response, extra = collect_video(response, output, state, config, timeout, wait_timeout, poll_interval)
            process_response(response, output, state, config, timeout, extra)
    except Exception as error:
        state.update({"status": "failed", "error": redact(error, config)})
    except KeyboardInterrupt:
        state.update({"status": "interrupted", "error": "Interrupted; an in-flight request may still run on the gateway."})
    state["elapsed_seconds"] = round(time.monotonic() - started, 3)
    write_json(output / "run.json", state)
    return summarize(output, state)


def resume_video(output: Path, config: dict, timeout: float, wait_timeout: float, poll_interval: float) -> dict:
    state = json.loads((output / "run.json").read_text(encoding="utf-8-sig"))
    if state.get("provider") != PROVIDER:
        raise ValueError("Only video jobs created through Gemini Web can be resumed.")
    if state.get("status") in SUCCESS and not state.get("resume_available"):
        return summarize(output, state)
    if not state.get("pending_endpoint", "").startswith("/v1/videos/"):
        raise ValueError("This run has no known video job to resume. No creation request was sent.")
    try:
        response = json.loads((output / "response.json").read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        response = {}
    if not isinstance(response, dict):
        response = {}
    if response.get("id") != state.get("task_id"):
        # A failed status query may have saved an error object as the latest raw
        # response. The creation receipt still identifies the existing job.
        response = json.loads((output / "creation.json").read_text(encoding="utf-8-sig"))
        if response.get("id") != state.get("task_id"):
            raise ValueError("Saved response and task ID disagree. No request was sent.")
    started = time.monotonic()
    state.pop("error", None)
    try:
        response, extra = collect_video(response, output, state, config, timeout, wait_timeout, poll_interval)
        process_response(response, output, state, config, timeout, extra)
    except Exception as error:
        state.update({"status": "failed", "error": redact(error, config)})
    except KeyboardInterrupt:
        state["status"] = "interrupted"
    state["elapsed_seconds"] = round(state.get("elapsed_seconds", 0) + time.monotonic() - started, 3)
    write_json(output / "run.json", state)
    return summarize(output, state)
