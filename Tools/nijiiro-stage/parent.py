import json
import math
import subprocess
from pathlib import Path
from PIL import Image
from bake import Bake, base, out
import tempfile

parent = base.parent / "PyTaikoGreen"
# The parent skin is 1280 wide and its scripts keep some of their places (a dancer's poses and
# origins) in its own pixels whatever scale they are told, so they are run at their own size
# and what they draw is enlarged here.
UP = 1.5
for name in ("animal", "buttoburst", "funassyi", "oshiri"):
    records = []
    folders = []
    for file in sorted((parent / "Graphics/background").rglob("texture.json")):
        folder = file.parent.relative_to(parent / "Graphics")
        if "collab" in folder.parts and folder.parts[2] != name:
            continue
        entries = []
        for key, config in json.loads(file.read_text()).items():
            prefix = file.parent / key
            pics = (
                sorted(prefix.glob("[0-9]*.png"), key=lambda p: int(p.stem))
                if prefix.is_dir()
                else [prefix.with_suffix(".png")]
            )
            if not pics or not pics[0].exists():
                continue
            w, h = Image.open(pics[0]).size
            c = config[0] if isinstance(config, list) else config
            if not isinstance(c, dict):
                c = {}
            entries.append(
                f"[{json.dumps(key)}]={{id={len(records)},width={w},height={h},frame_count={len(pics)}}}"
            )
            records.append((pics, c, w, h))
        folders.append(f"[{json.dumps(str(folder))}]={{" + ",".join(entries) + "}")
    with tempfile.TemporaryDirectory() as tmp:
        manifest = Path(tmp) / "info.lua"
        manifest.write_text("return {" + ",".join(folders) + "}")
        stream = subprocess.check_output(
            ["lua", Path(__file__).with_name("parent.lua"), parent, name, manifest],
            text=True,
        )
    files = {}
    clips = []
    timings = []

    def flush():
        if clips:
            b, clip, loop, frames = clips.pop()
            b.clip(
                clip,
                40 if clip == "rig" else 30 if clip in ("full", "release") else 60,
                loop >= 0,
                max(0, loop),
                frames,
            )

    for line in stream.splitlines():
        row = line.split("\t")
        if row[0] == "C":
            flush()
            _, folder, file, clip, count, start = row
            height = {"header": 276, "chibi": 276, "renda": 276, "dancers": 1080}.get(
                folder, 540
            )
            b = files.setdefault((folder, file), Bake(height))
            frames = []
            clips.append((b, clip, int(start), frames))
        elif row[0] == "T":
            timings.append(list(map(int, row[1:])))
        elif row[0] == "F":
            frames.append([])
        elif row[0] == "D":
            _, idx, frame, x, y, dx, dy, ox, oy, rot, alpha, mirror, blend, scale = row
            pics, c, w, h = records[int(idx)]
            pic = pics[int(float(frame))]
            x, y, dx, dy, ox, oy, rot, alpha, scale = map(
                float, (x, y, dx, dy, ox, oy, rot, alpha, scale)
            )
            sx = (int(c.get("x2", w)) * scale + dx) / w
            sy = (int(c.get("y2", h)) * scale + dy) / h
            if not sx or not sy or alpha <= 0:
                continue
            x += int(c.get("x", 0))
            y += int(c.get("y", 0))
            if mirror == "horizontal":
                x += math.cos(math.radians(rot)) * (w * sx - 2 * ox)
                y += math.sin(math.radians(rot)) * (w * sx - 2 * ox)
            frames[-1].append(
                b.draw(
                    pic,
                    len(frames[-1]),
                    x * UP,
                    y * UP - (540 if height == 540 else 0),
                    ox / sx,
                    oy / sy,
                    sx * UP,
                    sy * UP,
                    rot,
                    alpha,
                    mirror == "horizontal",
                    blend={"": 0, "additive": 1, "multiply": 2, "screen": 3}[blend],
                )
            )
    flush()
    for (folder, file), b in files.items():
        if b.parts:
            b.save(out / folder / (file + ".otanim"))
    print(name, timings, flush=True)
