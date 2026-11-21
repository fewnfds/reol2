"""Freeze explicit text and media attachments for all advisor requests."""
from __future__ import annotations

import base64
import hashlib
import json
import mimetypes
from pathlib import Path
import urllib.parse

from transport import PROVIDER_CONFIGS, ROOT

MIME_OVERRIDES = {".wav": "audio/wav", ".mp3": "audio/mpeg", ".m4a": "audio/mp4",
                  ".ogg": "audio/ogg", ".opus": "audio/opus", ".webm": "video/webm"}


def input_path(path: Path, config_path: Path) -> Path:
    path = path.resolve()
    config_paths = (config_path, *PROVIDER_CONFIGS.values())
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
    chat, native = [{"type": "text", "text": prompt}], [{"text": prompt}]
    for item in media:
        label = "Reference attachment: " + item["name"]
        chat.append({"type": "text", "text": label})
        native.append({"text": label})
        mime = item["mime"]
        if mime.startswith("image/"):
            chat.append({"type": "image_url", "image_url": {"url": item["url"]}})
        elif mime.startswith("video/") and args.provider == "aistudio":
            chat.append({"type": "video_url", "video_url": {"url": item["url"]}})
        elif mime.startswith("audio/"):
            fmt = {".mp3": "mp3", ".m4a": "m4a"}.get(item["suffix"], item["suffix"].lstrip("."))
            chat.append({"type": "input_audio", "input_audio": {"data": item["data"], "format": fmt}})
        elif "data" in item:
            chat.append({"type": "file", "file": {"filename": item["name"], "file_data": item["url"]}})
        else:
            chat.append({"type": "file", "file": {"file_url": item["url"]}})
        if "data" in item:
            native.append({"inlineData": {"mimeType": mime, "data": item["data"]}})
        else:
            native.append({"fileData": {"mimeType": mime, "fileUri": item["url"]}})
    content = chat if media else prompt
    return {"prompt": prompt, "chat_content": content, "native_parts": native,
            "media_inputs": media, "attachments": attachments,
            "prompt_sha256": hashlib.sha256(prompt.encode()).hexdigest(),
            "input_sha256": hashlib.sha256(json.dumps(content, ensure_ascii=False, sort_keys=True).encode()).hexdigest()}
