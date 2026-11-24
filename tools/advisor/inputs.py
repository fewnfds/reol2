"""Freeze explicit text and media attachments for all advisor requests."""
from __future__ import annotations

import base64
import hashlib
import json
import mimetypes
from pathlib import Path
import urllib.parse

from transport import LEGACY_CONFIG, ROOT, WEB_CONFIG

MIME_OVERRIDES = {".wav": "audio/wav", ".mp3": "audio/mpeg", ".m4a": "audio/mp4",
                  ".ogg": "audio/ogg", ".opus": "audio/opus", ".webm": "video/webm"}


def input_path(path: Path, config_path: Path) -> Path:
    path = path.resolve()
    config_paths = (config_path, WEB_CONFIG, LEGACY_CONFIG)
    if any(path == config.resolve() for config in config_paths) or path.name.startswith(".env"):
        raise ValueError("Credential/config files cannot be sent as input.")
    return path


def read_input(path: Path, config_path: Path) -> str:
    return input_path(path, config_path).read_text(encoding="utf-8-sig")


def prepare_inputs(args) -> dict:
    prompt = args.prompt if args.prompt is not None else read_input(args.prompt_file, args.config)
    if not prompt.strip():
        raise ValueError("The prompt is empty.")
    references, media, attachments = [], [], []
    for source in args.attach:
        parsed = urllib.parse.urlsplit(source)
        if parsed.scheme in ("http", "https"):
            youtube = (parsed.hostname or "").lower().removeprefix("www.") in (
                "youtube.com", "m.youtube.com", "youtu.be")
            mime = "video/*" if youtube else mimetypes.guess_type(parsed.path)[0] or ""
            if not mime.startswith(("image/", "video/")):
                raise ValueError("URL attachments must be image/video or YouTube URLs; use local files for documents and audio.")
            media.append({"name": source, "mime": mime, "url": source})
            attachments.append({"source": source, "mime_type": mime,
                                "sha256": hashlib.sha256(source.encode()).hexdigest()})
            continue
        path = input_path(Path(source), args.config)
        raw = path.read_bytes()
        label = path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else path.name
        mime = MIME_OVERRIDES.get(path.suffix.lower()) or mimetypes.guess_type(path.name)[0] or ""
        item = {"source": str(path), "mime_type": mime, "bytes": len(raw),
                "sha256": hashlib.sha256(raw).hexdigest()}
        if mime.startswith(("image/", "audio/", "video/")) or mime == "application/pdf":
            encoded = base64.b64encode(raw).decode("ascii")
            media.append({"name": path.name, "mime": mime, "data": encoded,
                          "url": f"data:{mime};base64,{encoded}", "suffix": path.suffix.lower()})
        else:
            text = raw.decode("utf-8-sig")
            if "\0" in text:
                raise ValueError(f"Unsupported binary attachment: {path.name}")
            references.append(f"\n--- BEGIN REFERENCE FILE: {label} ---\n{text}\n--- END REFERENCE FILE: {label} ---")
            item["mime_type"] = "text/plain"
        attachments.append(item)
    prompt = "\n".join([prompt, *references])
    native = [{"text": prompt}]
    for item in media:
        native.append({"text": "Reference attachment: " + item["name"]})
        if "data" in item:
            native.append({"inlineData": {"mimeType": item["mime"], "data": item["data"]}})
        else:
            native.append({"fileData": {"mimeType": item["mime"], "fileUri": item["url"]}})
    file_ids = [file_id.strip() for file_id in getattr(args, "file_id", ())]
    if not all(file_ids):
        raise ValueError("--file-id requires a Gemini Web file ID.")
    attachments.extend({"file_id": file_id} for file_id in file_ids)
    prepared = {"prompt": prompt, "native_parts": native, "media_inputs": media, "attachments": attachments,
                "prompt_sha256": hashlib.sha256(prompt.encode()).hexdigest(), "file_ids": file_ids}
    return consultation_input(prepared)


def consultation_input(prepared: dict) -> dict:
    prompt = prepared["prompt"]
    media = prepared["media_inputs"]
    file_ids = prepared["file_ids"]
    chat = [{"type": "text", "text": prompt}]
    for item in media:
        chat.append({"type": "text", "text": "Reference attachment: " + item["name"]})
        mime = item["mime"]
        if mime.startswith("image/"):
            chat.append({"type": "image_url", "image_url": {"url": item["url"]}})
        elif mime.startswith("audio/"):
            fmt = {".mp3": "mp3", ".m4a": "m4a"}.get(item["suffix"], item["suffix"].lstrip("."))
            chat.append({"type": "input_audio", "input_audio": {"data": item["data"], "format": fmt}})
        elif "data" in item:
            chat.append({"type": "file", "file": {"filename": item["name"], "file_data": item["url"]}})
        else:
            chat.append({"type": "file", "file": {"file_url": item["url"]}})
    for file_id in file_ids:
        chat.append({"type": "file", "file": {"file_id": file_id}})
    content = chat if media or file_ids else prompt
    return {**prepared, "chat_content": content,
            "input_sha256": hashlib.sha256(json.dumps(content, ensure_ascii=False, sort_keys=True).encode()).hexdigest()}
