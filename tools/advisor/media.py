"""Extract inline and linked media; network access is supplied by the caller."""
from __future__ import annotations

import base64
import hashlib
from pathlib import Path
import re
import wave

DATA_URL = re.compile(r"data:([^,\s]+);base64,([A-Za-z0-9+/_=\-]+)")
MARKDOWN_IMAGE = re.compile(r"!\[[^\]]*\]\(\s*<?([^\s)>]+)>?(?:\s+[^)]*)?\)")


def extension(data: bytes, mime: str) -> str:
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return ".png"
    if data.startswith(b"\xff\xd8\xff"):
        return ".jpg"
    if data.startswith(b"GIF8"):
        return ".gif"
    if data.startswith(b"RIFF"):
        if data[8:12] == b"WAVE":
            return ".wav"
        if data[8:12] == b"WEBP":
            return ".webp"
    if data[4:8] == b"ftyp":
        return ".mp4"
    return {"image/png": ".png", "image/jpeg": ".jpg", "image/webp": ".webp",
            "image/gif": ".gif", "audio/wav": ".wav", "audio/x-wav": ".wav",
            "audio/mpeg": ".mp3", "audio/mp3": ".mp3", "audio/aac": ".aac",
            "audio/ogg": ".ogg", "audio/opus": ".opus", "audio/flac": ".flac",
            "audio/l16": ".pcm", "audio/pcm": ".pcm", "video/mp4": ".mp4",
            "application/pdf": ".pdf"}.get(mime.lower().split(";")[0].strip(), ".bin")


def save_binary(data: bytes, mime: str, output: Path, stem: str, source: str) -> dict:
    if not data:
        raise ValueError("Empty media response.")
    filename = stem + extension(data, mime)
    (output / filename).write_bytes(data)
    asset = {"file": filename, "mime_type": mime, "bytes": len(data),
             "sha256": hashlib.sha256(data).hexdigest(), "source": source}
    if filename.endswith(".pcm"):
        params = dict(item.strip().lower().split("=", 1) for item in mime.split(";")[1:] if "=" in item)
        rate, channels = int(params.get("rate", 0)), int(params.get("channels", 1))
        if rate > 0 and channels > 0:
            wav_name = str(Path(filename).with_suffix(".wav"))
            with wave.open(str(output / wav_name), "wb") as wav:
                wav.setnchannels(channels)
                wav.setsampwidth(2)
                wav.setframerate(rate)
                wav.writeframes(data)
            asset["wav_file"] = wav_name
    return asset


def save_media(response: dict, output: Path, fetcher=None) -> dict:
    """Extract inline media, Markdown images and supported file/media links.

    A fetcher returns (bytes, MIME) for a supported URL, or None for other links.
    One broken asset is recorded without losing other assets or the raw response.
    """
    assets, links, errors, seen, seen_links = [], [], [], set(), set()

    def save(data: bytes, mime: str, source: str):
        digest = hashlib.sha256(data).hexdigest()
        if digest not in seen:
            asset = save_binary(data, mime, output, f"media-{len(assets) + 1:02d}", source)
            assets.append(asset)
            seen.add(digest)

    def encoded_media(encoded: str, mime: str, source: str):
        try:
            if not isinstance(encoded, str) or not isinstance(mime, str):
                raise ValueError("Media data and MIME type must be strings.")
            encoded = "".join(encoded.split())
            data = base64.b64decode(encoded + "=" * (-len(encoded) % 4), altchars=b"-_", validate=True)
            save(data, mime, source)
        except (ValueError, OSError, wave.Error) as error:
            errors.append({"source": source, "error": str(error)})

    def reference(url: str, source: str):
        if url in seen_links:
            return
        seen_links.add(url)
        try:
            result = fetcher(url) if fetcher else None
            if result is not None:
                save(*result, source)
                return
        except (ValueError, OSError) as error:
            errors.append({"source": source, "error": str(error)})
        links.append({"source": source, "url": url})

    def visit(value, source: str = "$", hint: str = ""):
        if isinstance(value, dict):
            mime = value.get("mimeType") or value.get("mime_type")
            if "b64_json" in value:
                fmt = response.get("output_format", "png")
                encoded_media(value["b64_json"], "image/" + ("jpeg" if fmt == "jpg" else fmt), source + ".b64_json")
            elif isinstance(value.get("data"), str) and (mime or hint in ("audio", "output_audio")):
                encoded_media(value["data"], mime or "audio/" + value.get("format", "wav"), source + ".data")
            for key, child in value.items():
                if key != "b64_json" and not (key == "data" and isinstance(child, str)):
                    visit(child, f"{source}.{key}", key)
        elif isinstance(value, list):
            for index, child in enumerate(value):
                visit(child, f"{source}[{index}]", hint)
        elif isinstance(value, str):
            for match in DATA_URL.finditer(value):
                encoded_media(match[2], match[1], source)
            for match in MARKDOWN_IMAGE.finditer(value):
                if not match[1].startswith("data:"):
                    reference(match[1], source)
            if hint in ("uri", "url", "fileUri", "file_uri") and value.startswith(("http://", "https://", "/media/", "/v1/", "/v1beta/")):
                reference(value, source)

    visit(response)
    return {"assets": assets, "links_not_downloaded": links, "errors": errors}
