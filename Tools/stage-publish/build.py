#!/usr/bin/env python3
"""Lays out the play backdrops' original files as they are published under the distribution root.

    stage/otanim/index.json                        every file: its name, where it is, its size and SHA-256
    stage/otanim/<part>/<name>.<hash16>.otanim     the files, each under a name that carries its hash

Only the originals: Tools/stage-publish/publish.sh uploads them, and the "Stage bundles" workflow
fetches them from there, builds every platform's bundles and uploads those and the catalog.
The files are the game's own sets (Assets/OurTaiko/Resources/Stage) and the rest
(Assets/OurTaiko/StageSource, which is not in the repository). A published file is never replaced:
one that changes gets a new name, and only the JSON, which may be replaced, says which is current.
Every set the catalog (Tools/stage-publish/catalog.json) lists must have its file, or nothing is
written.

Usage: Tools/stage-publish/build.py <output folder>
"""
import hashlib
import json
import re
import shutil
import sys
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
roots = [repo / "Assets/OurTaiko/Resources/Stage", repo / "Assets/OurTaiko/StageSource"]
parts = ["header", "scene", "clear", "dancers", "footer", "crowd", "renda", "chibi"]


def catalog():
    text = (Path(__file__).parent / "catalog.json").read_text(encoding="utf-8")
    # The game reads comments; json does not.
    plain = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    plain = "\n".join(line for line in plain.splitlines() if not line.lstrip().startswith("//"))
    return json.loads(plain)


def main_files(listed):
    """The file each set the catalog lists cannot be drawn without."""
    for part, sets in listed.get("sets", {}).items():
        for name, options in sets.items():
            first = name if part != "dancers" or options.get("movie") else name + "_0"
            yield f"{part}/{first}"
    for preset in listed.get("presets", {}).values():
        if preset.get("chibi"):
            yield "chibi/" + preset["chibi"]


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    out = Path(sys.argv[1]) / "stage" / "otanim"
    files = {}
    for root in roots:
        for part in parts:
            for path in sorted((root / part).glob("*.otanim")):
                files.setdefault(f"{part}/{path.stem}", path)
    listed = catalog()
    missing = sorted(set(main_files(listed)) - set(files))
    if missing:
        sys.exit("The catalog lists sets that have no file: " + ", ".join(missing))
    if out.exists():
        shutil.rmtree(out)
    entries = []
    for name, path in sorted(files.items()):
        data = path.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        published = f"{name}.{digest[:16]}.otanim"
        (out / published).parent.mkdir(parents=True, exist_ok=True)
        (out / published).write_bytes(data)
        entries.append({"name": name, "file": published, "size": len(data), "sha256": digest})
    (out / "index.json").write_text(json.dumps({"version": 1, "files": entries}, indent=1) + "\n", encoding="utf-8")
    print(f"{len(entries)} files, {sum(e['size'] for e in entries) / 1e6:.0f} MB -> {out}")


if __name__ == "__main__":
    main()
