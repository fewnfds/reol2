#!/usr/bin/env python3
"""One-shot consultations and media jobs through the local Gemini Web gateway.

Give a prompt and explicit attachments. Output token budgets belong to the
gateway; this client does not set them. Only the Python standard library is used.
"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
import math
from pathlib import Path
import sys
from urllib.parse import quote

from inputs import prepare_inputs, read_input
from media import save_media
from runner import execute, resume_video, timestamp
from transport import (LOCAL_DIR, PROVIDER, WEB_CONFIG,
                       configuration, redact, request, write_json)

RUNS = LOCAL_DIR / "runs"
WEB_ADVISOR = "gemini-4-neon"
WEB_TASK_ADVISOR = "gemini-4-neon-task"
MEDIA_MODELS = {"image": "gemini-3.1-flash-image", "speech": "gemini-4-hydrogen",
                "music": "gemini-music", "video": "gemini-video"}


def emit(summary: dict, print_answer: bool = False) -> int:
    print(json.dumps(summary, ensure_ascii=False, indent=2), flush=True)
    if print_answer and summary.get("answer"):
        print(Path(summary["answer"]).read_text(encoding="utf-8"), flush=True)
    return summary["code"]


def gateway_timeout(override: float | None, config: dict) -> float:
    timeout = override if override is not None else float(config.get("timeout_seconds", 900))
    if not math.isfinite(timeout) or timeout <= 0:
        raise ValueError("HTTP timeout must be finite and positive.")
    return timeout


def run_consultation(args, config: dict, timeout: float) -> dict:
    models = [model.strip() for model in args.model] or [WEB_ADVISOR, WEB_TASK_ADVISOR]
    if not all(models):
        raise ValueError("--model requires a model ID.")
    routes = {f"web-{index}": {"model": model, **({"reasoning_effort": "high"} if model == WEB_ADVISOR else {})}
              for index, model in enumerate(models, 1)}
    prepared = prepare_inputs(args)
    output = args.out_dir or RUNS / (timestamp() + "-consultation")
    output.mkdir(parents=True, exist_ok=False)
    (output / "prompt.md").write_text(prepared["prompt"], encoding="utf-8")
    state = {key: prepared[key] for key in ("prompt_sha256", "input_sha256", "attachments")}
    state.update({"provider": PROVIDER, "status": "prepared" if args.dry_run else "requesting",
                  "automatic_retries": 0, "results": {}})
    write_json(output / "consultation.json", state)
    print(f"Prepared {PROVIDER} consultation with {len(routes)} request(s); output={output.resolve()}", flush=True)
    messages = [{"role": "user", "content": prepared["chat_content"]}]

    def ask_advisor(label, options):
        payload = {"stream": False, "messages": messages, **options}
        try:
            return execute(config, "/v1/chat/completions", payload, output / label, timeout,
                           prepared=prepared, record_prompt=False, dry_run=args.dry_run, expected_text=True)
        except OSError as error:
            return {"status": "local_error", "code": 1, "provider": PROVIDER,
                    "requested_model": options["model"], "error": redact(error, config)}

    with ThreadPoolExecutor(max_workers=len(routes)) as pool:
        futures = {pool.submit(ask_advisor, label, options): label for label, options in routes.items()}
        for future in as_completed(futures):
            label = futures[future]
            result = future.result()
            state["results"][label] = result
            print(f"{label} ({PROVIDER}/{result['requested_model']}) {result['status']} "
                  f"{result.get('answer') or result.get('output_dir', '')}", flush=True)
            write_json(output / "consultation.json", state)
            sections = []
            for name in routes:
                if name not in state["results"]:
                    continue
                item = state["results"][name]
                body = Path(item["answer"]).read_text(encoding="utf-8") if item.get("answer") else item.get("error", "")
                title = f"{name} · {item['requested_model']}"
                sections.append(f"## {title} ({item['status']})\n\n{body}")
            (output / "answers.md").write_text("\n\n".join(sections) + "\n", encoding="utf-8")

    usable = sum(result["code"] == 0 for result in state["results"].values())
    state["status"] = "prepared" if args.dry_run else "complete" if usable == len(routes) else "partial" if usable else "failed"
    write_json(output / "consultation.json", state)
    summary = {"status": state["status"], "code": 0 if usable else 2, "provider": state["provider"],
               "output_dir": str(output.resolve()), "answer": str((output / "answers.md").resolve()),
               "results": state["results"]}
    write_json(output / "summary.json", summary)
    return summary


def generate(args, config: dict, timeout: float) -> dict:
    model = args.model or MEDIA_MODELS[args.kind]
    model_id = model.rsplit("/", 1)[-1].split("@", 1)[0].split("(", 1)[0].strip().lower()
    if model_id in (WEB_ADVISOR, WEB_TASK_ADVISOR):
        raise ValueError(f"{model} is a text advisor. Omit --model to use {MEDIA_MODELS[args.kind]} for {args.kind}.")
    prepared = prepare_inputs(args)
    if args.aspect_ratio and args.kind != "image":
        raise ValueError("--aspect-ratio applies to image generation; videos use --size.")
    if args.seconds is not None and (args.kind != "video" or args.seconds <= 0):
        raise ValueError("--seconds must be positive and applies to video generation.")
    if args.size and args.kind != "video":
        raise ValueError("--size applies to video generation; images use --aspect-ratio.")
    media = prepared["media_inputs"]
    if args.kind == "video":
        if len(media) > 1 or any(not item["mime"].startswith("image/") for item in media):
            raise ValueError("Video generation accepts at most one reference image.")
        payload = {"model": model, "prompt": prepared["prompt"], "size": args.size or "1280x720"}
        if args.seconds is not None:
            payload["seconds"] = str(args.seconds)
        if media:
            payload["input_reference"] = {"image_url": media[0]["url"]}
        endpoint = "/v1/videos"
    elif args.kind == "speech":
        if media:
            raise ValueError("Speech generation accepts text input.")
        payload = {"model": model, "input": prepared["prompt"], "response_format": "wav"}
        endpoint = "/v1/audio/speech"
    elif args.kind == "image" and not media:
        payload = {"model": model, "prompt": prepared["prompt"], "response_format": "b64_json"}
        if args.aspect_ratio:
            payload["aspect_ratio"] = args.aspect_ratio
        endpoint = "/v1/images/generations"
    else:
        if args.kind == "image" and any(not item["mime"].startswith("image/") for item in media):
            raise ValueError("Image generation accepts image references.")
        payload = {"contents": [{"role": "user", "parts": prepared["native_parts"]}]}
        if args.aspect_ratio:
            payload["generationConfig"] = {"imageConfig": {"aspectRatio": args.aspect_ratio}}
        endpoint = "/v1beta/models/" + quote(model, safe="") + ":generateContent"
    output = args.out_dir or RUNS / (timestamp() + "-" + args.kind)
    print(f"Prepared {args.kind} generation; output={output.resolve()}", flush=True)
    return execute(config, endpoint, payload, output, timeout, prepared=prepared,
                   dry_run=args.dry_run, expected_media=args.kind, wait_video=args.kind == "video",
                   wait_timeout=args.wait_timeout, poll_interval=args.poll_interval)


def send(args, config: dict, timeout: float) -> dict:
    payload = None
    if args.method == "POST":
        if args.request_file is None:
            raise ValueError("POST needs --request-file with a JSON object.")
        payload = json.loads(read_input(args.request_file, args.config))
        if not isinstance(payload, dict):
            raise ValueError("Request body must be a JSON object.")
        if payload.get("stream") or payload.get("stream_format") == "sse" or "streamGenerateContent" in args.endpoint:
            raise ValueError("send supports non-streaming responses; use generateContent or stream:false.")
    elif args.request_file is not None:
        raise ValueError("GET does not take --request-file.")
    if args.wait and not (args.endpoint == "/v1/videos" or args.endpoint.startswith("/v1/videos/")):
        raise ValueError("--wait supports video job endpoints; other raw async APIs return their saved pending state.")
    output = args.out_dir or RUNS / timestamp()
    return execute(config, args.endpoint, payload, output, timeout, dry_run=args.dry_run,
                   wait_video=args.wait, expected_media="video" if args.wait else None,
                   wait_timeout=args.wait_timeout, poll_interval=args.poll_interval)


def runtime_options(parser, *, child=False):
    default = argparse.SUPPRESS if child else None
    parser.add_argument("--config", type=Path, default=default,
                        help="Override the Gemini Web config file.")
    parser.add_argument("--timeout", type=float, default=default, help="HTTP timeout seconds; default config or 900.")
    parser.add_argument("--wait-timeout", type=float, default=argparse.SUPPRESS if child else 900,
                        help="Wait for an existing async job, in seconds; default 900. Resume without creating another job.")
    parser.add_argument("--poll-interval", type=float, default=argparse.SUPPRESS if child else 5,
                        help="Video status query interval in seconds; default 5.")


def output_options(parser, *, required=False):
    parser.add_argument("--out-dir", type=Path, required=required, help="New output directory; existing directories are never overwritten.")
    parser.add_argument("--dry-run", action="store_true", help="Save inputs and requests without HTTP calls.")
    parser.add_argument("--print-answer", action="store_true", help="Also print the final answer, avoiding a separate file read.")


def prompt_options(parser):
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--prompt", help="Question or generation prompt, directly on the command line.")
    source.add_argument("--prompt-file", type=Path, help="UTF-8 prompt file.")
    parser.add_argument("--attach", "--context", dest="attach", action="append", default=[],
                        help="Explicit text/image/audio/PDF/video file or image/video/YouTube URL; repeatable.")
    output_options(parser)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    runtime_options(parser)
    sub = parser.add_subparsers(dest="command", required=True)
    models = sub.add_parser("models", help="List model IDs advertised by the local gateway.")
    models.add_argument("--out-file", type=Path, help="Also preserve the full model/capability catalog.")
    consult = sub.add_parser("ask", help="Ask the selected Gemini Web advisors concurrently.")
    prompt_options(consult)
    consult.add_argument("--model", action="append", default=[],
                         help=f"Override the Web defaults ({WEB_ADVISOR} High and {WEB_TASK_ADVISOR}). Repeat to select multiple models.")
    consult.add_argument("--file-id", action="append", default=[],
                         help="Reference a file already uploaded to the Gemini Web gateway; repeatable.")
    media = sub.add_parser("generate", help="Submit one media generation request and save the result.")
    media.add_argument("kind", choices=tuple(MEDIA_MODELS))
    prompt_options(media)
    media.add_argument("--model", help="Select one model compatible with this media kind.")
    media.add_argument("--size", help="Video dimensions for the aspect ratio; default 1280x720.")
    media.add_argument("--aspect-ratio", help="Image aspect ratio, for example 16:9.")
    media.add_argument("--seconds", type=int, help="Video duration hint; actual duration is set by Gemini Web.")
    raw = sub.add_parser("send", help="Advanced: send exact JSON or fetch a local API path.")
    raw.add_argument("--endpoint", required=True)
    raw.add_argument("--method", choices=("GET", "POST"), default="POST")
    raw.add_argument("--request-file", type=Path)
    raw.add_argument("--wait", action="store_true", help="Collect an async video job and download the result.")
    output_options(raw)
    resume = sub.add_parser("resume", help="Continue querying/downloading an existing video job; never POST again.")
    resume.add_argument("run_dir", type=Path)
    resume.add_argument("--print-answer", action="store_true")
    extract = sub.add_parser("extract", help="Extract media from saved JSON without HTTP calls.")
    extract.add_argument("--response-file", type=Path, required=True)
    extract.add_argument("--out-dir", type=Path, required=True)
    for command in (consult, models, raw):
        command.add_argument("--provider", choices=(PROVIDER,), help="Select Gemini Web.")
    resume.add_argument("--provider", choices=(PROVIDER,), help=argparse.SUPPRESS)
    for command in (models, consult, media, raw, resume, extract):
        runtime_options(command, child=True)
    args = parser.parse_args(argv)
    explicit_config = args.config is not None
    args.config = args.config or WEB_CONFIG
    config = None
    try:
        if args.command == "extract":
            response = json.loads(read_input(args.response_file, args.config))
            if not isinstance(response, dict):
                raise ValueError("Response must be a JSON object.")
            args.out_dir.mkdir(parents=True, exist_ok=False)
            manifest = save_media(response, args.out_dir)
            write_json(args.out_dir / "media.json", manifest)
            summary = {"status": "media_error" if manifest["errors"] else "received", "code": 2 if manifest["errors"] else 0,
                       "output_dir": str(args.out_dir.resolve()), "media": [str((args.out_dir / asset.get("wav_file", asset["file"])).resolve()) for asset in manifest["assets"]],
                       "links_not_downloaded": manifest["links_not_downloaded"]}
            write_json(args.out_dir / "summary.json", summary)
            return emit(summary)
        durations = (args.timeout, args.wait_timeout, args.poll_interval)
        if any(value is not None and (not math.isfinite(value) or value <= 0) for value in durations):
            raise ValueError("HTTP timeout, wait timeout and polling interval must be finite and positive.")
        config = configuration(args.config, require_file=explicit_config)
        timeout = gateway_timeout(args.timeout, config)
        if args.command == "ask":
            return emit(run_consultation(args, config, timeout), args.print_answer)
        if args.command == "models":
            result = request(config, "/v1/models", None, min(timeout, 30))
            rows = result.get("data")
            if not isinstance(rows, list):
                raise ValueError("The gateway model catalogue must contain a data array.")
            if args.out_file:
                with args.out_file.open("x", encoding="utf-8") as handle:
                    json.dump(result, handle, ensure_ascii=False, indent=2)
            print("\n".join(row["id"] for row in rows if isinstance(row, dict) and isinstance(row.get("id"), str)))
            return 0
        if args.command == "generate":
            summary = generate(args, config, timeout)
        elif args.command == "resume":
            summary = resume_video(args.run_dir, config, timeout, args.wait_timeout, args.poll_interval)
        else:
            summary = send(args, config, timeout)
        return emit(summary, args.print_answer)
    except (OSError, ValueError, TypeError, KeyError) as error:
        message = redact(error, config) if config else str(error)
        print(f"Advisor error: {message}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
