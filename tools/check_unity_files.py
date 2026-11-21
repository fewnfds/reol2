"""Check the Unity files a clean Git checkout needs."""
from pathlib import Path, PurePosixPath
import json
import re
import subprocess


ROOT = Path(__file__).resolve().parents[1]
ASSETS = PurePosixPath("Assets")


def main() -> int:
    tracked = subprocess.check_output(["git", "ls-files", "-z", "--", "Assets"], cwd=ROOT)
    assets = {PurePosixPath(name) for name in tracked.decode("utf-8").split("\0") if name}
    expected_meta = set()
    for asset in assets:
        if asset.suffix == ".meta" or any(part.startswith(".") or part.endswith("~") for part in asset.parts):
            continue
        expected_meta.add(PurePosixPath(f"{asset}.meta"))
        for parent in asset.parents:
            if parent == ASSETS:
                break
            expected_meta.add(PurePosixPath(f"{parent}.meta"))
    errors = [f"Missing tracked Unity metadata: {path}" for path in sorted(expected_meta - assets)]

    settings = (ROOT / "ProjectSettings/EditorBuildSettings.asset").read_text(encoding="utf-8-sig")
    scenes = re.findall(r"^  - enabled: 1\s*\n    path: ([^\n]+)\n    guid: ([0-9a-f]{32})\s*$", settings, re.MULTILINE)
    if not scenes or len(scenes) != len(re.findall(r"^  - enabled: 1\s*$", settings, re.MULTILINE)):
        errors.append("EditorBuildSettings.asset needs valid enabled scene paths and GUIDs.")
    for value, guid in scenes:
        scene = PurePosixPath(value.strip())
        sidecar = PurePosixPath(f"{scene}.meta")
        if scene.suffix != ".unity" or scene not in assets or sidecar not in assets:
            errors.append(f"Build scene and metadata must be tracked: {scene}")
            continue
        meta = (ROOT / sidecar).read_text(encoding="utf-8-sig")
        if not re.search(rf"^guid: {guid}\s*$", meta, re.MULTILINE):
            errors.append(f"Build scene GUID differs from its metadata: {scene}")

    for file in ("manifest.json", "packages-lock.json"):
        with (ROOT / "Packages" / file).open(encoding="utf-8-sig") as stream:
            json.load(stream)

    if errors:
        print("\n".join(errors))
        return 1
    print(f"Checked Unity metadata, {len(scenes)} build scene(s), and package JSON.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
