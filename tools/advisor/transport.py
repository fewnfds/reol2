"""Exact, non-streaming HTTP requests to the configured loopback gateway."""
from __future__ import annotations

import json
import os
from pathlib import Path
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
LOCAL_DIR = ROOT / ".local" / "advisor"
WEB_CONFIG = LOCAL_DIR / "gemini-web.local.json"
LEGACY_CONFIG = LOCAL_DIR / "config.local.json"
PROVIDER = "gemini-web"


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError("Gateway redirects are disabled; check base_url.")


def configuration(path: Path, *, require_file: bool = False) -> dict:
    if path.resolve() == LEGACY_CONFIG.resolve():
        raise ValueError(f"{path.name} is the retired gateway configuration. Use {WEB_CONFIG.name}.")
    if require_file and not path.is_file():
        raise FileNotFoundError(f"Advisor config file not found: {path}")

    config = json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else {}
    if not isinstance(config, dict):
        raise ValueError("Advisor configuration must be a JSON object.")
    for field in ("base_url", "api_key"):
        config[field] = os.environ.get("REOL_ADVISOR_WEB_" + field.upper(), config.get(field, ""))
    base = str(config["base_url"] or "http://127.0.0.1:8007/v1").rstrip("/")
    address = urllib.parse.urlsplit(base)
    if address.scheme not in ("http", "https") or address.hostname not in ("127.0.0.1", "localhost", "::1"):
        raise ValueError("This tool requires a loopback gateway base_url.")
    if address.username or address.password or address.query or address.fragment:
        raise ValueError("Put credentials in api_key, not in the URL.")
    if not isinstance(config["api_key"], str):
        raise ValueError("api_key must be a string.")
    config["base_url"] = base
    config["provider"] = PROVIDER
    return config


def redact(value, config: dict) -> str:
    key = config["api_key"]
    return str(value).replace(key, "[redacted]") if key else str(value)


def exchange(config: dict, endpoint: str, payload: dict | None, timeout: float,
             *, root_path: bool = False) -> tuple[bytes, str]:
    parsed = urllib.parse.urlsplit(endpoint)
    if not endpoint.startswith("/") or endpoint.startswith("//") or parsed.scheme or parsed.netloc or parsed.fragment:
        raise ValueError("Endpoint must be a path on the configured local gateway.")
    if ".." in urllib.parse.unquote(parsed.path).split("/"):
        raise ValueError("Endpoint cannot contain parent-directory segments.")
    if root_path and not parsed.path.startswith(("/v1/", "/v1beta/", "/v1alpha/", "/media/")):
        raise ValueError("Use a documented /v1/, /v1beta/, /v1alpha/ or /media/ path.")
    if parsed.path.startswith("/media/") and payload is not None:
        raise ValueError("Media paths only support GET.")
    base = config["base_url"]
    if root_path:
        origin = urllib.parse.urlsplit(base)
        base = urllib.parse.urlunsplit((origin.scheme, origin.netloc, "", "", ""))
    data = None if payload is None else json.dumps(payload, ensure_ascii=False).encode("utf-8")
    headers = {"Content-Type": "application/json", "Accept": "*/*"}
    if config["api_key"]:
        headers["Authorization"] = "Bearer " + config["api_key"]
    req = urllib.request.Request(base + endpoint, data=data, headers=headers)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    try:
        with opener.open(req, timeout=timeout) as response:
            return response.read(), response.headers.get("Content-Type", "application/octet-stream")
    except urllib.error.HTTPError as error:
        detail = error.read(4096).decode("utf-8", errors="replace")
        raise ValueError(f"Gateway HTTP {error.code}: {redact(detail, config)}") from None


def request(config: dict, endpoint: str, payload: dict | None, timeout: float) -> dict:
    body, _ = exchange(config, endpoint, payload, timeout, root_path=True)
    result = json.loads(body)
    if not isinstance(result, dict):
        raise ValueError("Gateway returned a non-object JSON response.")
    if result.get("error"):
        raise ValueError("Gateway API error: " + redact(result["error"], config))
    return result


def write_json(path: Path, value: dict) -> None:
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)
