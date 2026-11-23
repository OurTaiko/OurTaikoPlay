"""Bake Nijiiro skin drawing tables to textured otanim quads."""

from pathlib import Path
import io, json, math, struct, subprocess, zlib, sys
from PIL import Image
from names import named

base = Path(sys.argv[1])
out = Path(sys.argv[2])
graphics = base / "Graphics/background"
scripts = base / "Scripts/background"


def table(path):
    return json.loads(
        subprocess.check_output(["lua", Path(__file__).with_name("tables.lua"), path])
    )


def pack(fmt, *v):
    return struct.pack("<" + fmt, *v)


def string(s):
    b = s.encode()
    return pack("H", len(b)) + b


def chunk(tag, b):
    return tag.encode() + pack("I", len(b)) + b


class Bake:
    def __init__(self, height):
        self.height = height
        self.parts = {}
        self.images = {}
        self.clips = []

    def image(self, path):
        if path not in self.images:
            with Image.open(path) as file:
                image = file.convert("RGBA")
                bounds = image.getchannel("A").getbbox() or (0, 0, 1, 1)
                self.images[path] = (image.crop(bounds), bounds, image.width)
        return self.images[path]

    def draw(
        self,
        path,
        instance,
        x,
        y,
        ox,
        oy,
        sx,
        sy,
        angle,
        a,
        mirror=False,
        rgb=(255, 255, 255),
        blend=0,
    ):
        _, box, _ = self.image(path)
        rad = math.radians(angle)
        c, s = math.cos(rad), math.sin(rad)
        xy = []
        for u, v in (
            (box[0], box[1]),
            (box[2], box[1]),
            (box[2], box[3]),
            (box[0], box[3]),
        ):
            dx = (u - ox) * sx * (-1 if mirror else 1)
            dy = (v - oy) * sy
            xy.extend(
                (
                    round((x + c * dx - s * dy) * 2),
                    round((self.height - y - s * dx - c * dy) * 2),
                )
            )
        return (path, instance, blend), (*rgb, max(0, min(255, round(255 * a)))), xy

    # A player eases a part from one frame's place to the next's, so a picture that jumps (a
    # tile going back to where its scroll started, most of all where a loop comes round) would
    # be swept across the screen for a frame. It is given another part of the same picture
    # there, and the player shows the jump as a jump.
    @staticmethod
    def jumps(before, after, step):
        move = max(abs(a - b) for a, b in zip(before, after))
        # Half units: more than 32 px in a frame, and several times what it was moving at.
        return move > 64 and move > 4 * step

    def clip(self, name, fps, loop, start, frames):
        frames = [list(draws) for draws in frames]
        twins = []
        last = {}  # key -> (frame, twin, xy, how far it last moved in a frame)
        for f, draws in enumerate(frames):
            row = []
            for key, _, xy in draws:
                twin, step = 0, 0
                if key in last and last[key][0] == f - 1:
                    _, twin, before, step = last[key]
                    if self.jumps(before, xy, step):
                        twin = 1 - twin
                    else:
                        step = max(abs(a - b) for a, b in zip(before, xy))
                last[key] = (f, twin, xy, step)
                row.append(twin)
            twins.append(row)
        if loop and len(frames) > 1 and start < len(frames) - 1:
            # Where the loop comes round, from the last frame to the one it goes back to.
            first = {key: (twin, xy) for (key, _, xy), twin in zip(frames[start], twins[start])}
            for i, (key, _, xy) in enumerate(frames[-1]):
                step = last[key][3] if last[key][0] == len(frames) - 1 else 0
                if key in first and first[key][0] == twins[-1][i] and self.jumps(xy, first[key][1], step):
                    # A twin no other frame uses (the others take turns between two), so the
                    # frame before is not eased into a jump either.
                    twins[-1][i] = 2
        data = bytearray()
        count = 0
        for draws, row in zip(frames, twins):
            data += pack("H", len(draws))
            for (key, color, xy), twin in zip(draws, row):
                idx = self.parts.setdefault((*key, twin), len(self.parts))
                data += pack("HB4B8h", idx, 0, *color, *xy)
            count += 1
        self.clips.append(
            zlib.compress(string(name) + pack("fBII", fps, loop, count, start) + data)[
                2:-4
            ]
        )

    def save(self, path):
        path = path.with_name(named(path.parent.name, path.stem) + path.suffix)
        ordered = sorted(self.images, key=lambda p: (-self.images[p][0].height, str(p)))
        area = sum(
            (im.width + 2) * (im.height + 2) for im, _, _ in self.images.values()
        )
        width = max(
            max(self.images[p][0].width + 2 for p in ordered),
            2 ** math.ceil(math.log2(math.sqrt(area))),
        )
        positions = {}
        x = y = row = 0
        for p in ordered:
            im = self.images[p][0]
            if x + im.width + 2 > width:
                x = 0
                y += row
                row = 0
            positions[p] = (x + 1, y + 1)
            x += im.width + 2
            row = max(row, im.height + 2)
        height = y + row
        if path.parent.name == "dancers":
            # Both BC7 and ASTC 6x6 blocks fit without resampling the atlas.
            width = (width + 11) // 12 * 12
            height = (height + 11) // 12 * 12
        atlas = Image.new("RGBA", (width, height))
        for p, pt in positions.items():
            atlas.paste(self.images[p][0], pt)
        png = io.BytesIO()
        atlas.save(png, format="PNG", compress_level=2)
        parts = bytearray()
        for p, i, blend, twin in self.parts:
            x, y = positions[p]
            im = self.images[p][0]
            uv = []
            for u, v in [
                (x, y),
                (x + im.width, y),
                (x + im.width, y + im.height),
                (x, y + im.height),
            ]:
                uv.extend(
                    (round(65535 * u / atlas.width), round(65535 * v / atlas.height))
                )
            parts += string(f"{p.stem}_{i}" + (f"#{twin}" if twin else "")) + pack(
                "BHH8H6H", blend, 4, 6, *uv, 0, 1, 2, 0, 2, 3
            )
        info = pack(
            "5fHH", 0, 0, 1920, self.height, 0.5, len(self.parts), len(self.clips)
        ) + string(path.stem)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(
            b"OTAN"
            + pack("HH", 1, 0)
            + chunk("INFO", info)
            + chunk("TEXR", png.getvalue())
            + chunk("PART", zlib.compress(parts)[2:-4])
            + b"".join(chunk("CLIP", c) for c in self.clips)
        )


def dancers(src):
    data = table(src)
    name = src.stem
    variants = data["variants"]
    result = []
    members = (
        min(len(data["pos"]), len(variants)) if data.get("fixed") else len(data["pos"])
    )
    for i in range(members):
        v = i % len(variants)
        d = variants[str(v)]
        b = Bake(1080)

        def frames():
            keys = iter(d["keys"])
            nxt = next(keys, None)
            rows = []
            for f in range(d["n"]):
                while nxt and nxt[0] <= f:
                    rows = nxt[1]
                    nxt = next(keys, None)
                draws = []
                for j, (pic, dx, dy, sx, sy, angle, a, *flip) in enumerate(rows):
                    if a <= 0.002:
                        continue
                    path = graphics / "dancer" / name / f"{v}_loop" / f"{pic}.png"
                    draws.append(
                        b.draw(
                            path,
                            j,
                            data["pos"][i] + dx,
                            data["posy"] + dy,
                            *d["o"][pic],
                            sx,
                            sy,
                            angle,
                            a,
                            bool(flip and flip[0]),
                        )
                    )
                yield draws

        b.clip("rig", 40, False, 0, frames())
        b.save(out / "dancers" / f"{name}_{i}.otanim")
        result.append((d["dance"], d["out"]))
    print(name, members, result, flush=True)


if __name__ == "__main__":
    for src in sorted((scripts / "dancer_data").glob("*.lua")):
        dancers(src)
