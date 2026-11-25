"""Bake nameplate layouts, title bands and rank effects from a decoded Lumen atlas and timeline."""

import argparse
import base64
import io
import json
import math
import subprocess
import tempfile
from functools import lru_cache
from pathlib import Path

from PIL import Image

IDENTITY = (1, 0, 0, 1, 0, 0)
WHITE = (1, 1, 1, 1)


def compose(parent, local):
    a, b, c, d, x, y = parent
    e, f, g, h, u, v = local
    return (
        a * e + c * f,
        b * e + d * f,
        a * g + c * h,
        b * g + d * h,
        a * u + c * v + x,
        b * u + d * v + y,
    )


def transform(matrix, vertex):
    a, b, c, d, x, y = matrix
    u, v, s, t = vertex
    return x + a * u + c * v, y + b * u + d * v, s, t


def clip_polygon(vertices, box):
    """Clip in plate space, interpolating UVs at each intersection."""
    for axis, limit, sign in (
        (0, box[0], 1),
        (0, box[2], -1),
        (1, box[1], 1),
        (1, box[3], -1),
    ):
        if not vertices:
            break
        out = []
        prev = vertices[-1]
        for point in vertices:
            left = (prev[axis] - limit) * sign
            right = (point[axis] - limit) * sign
            if (left < 0) != (right < 0):
                t = left / (left - right)
                out.append(tuple(a + (b - a) * t for a, b in zip(prev, point)))
            if right >= 0:
                out.append(point)
            prev = point
        vertices = out
    return vertices


class Layout:
    def __init__(self, decoded):
        data = json.loads((decoded / "layout.json").read_text())
        self.data = data
        self.sprites = {int(k): v for k, v in data["sprites"].items()}
        self.shapes = {int(k): v for k, v in data["shapes"].items()}
        self.atlas = Image.open(decoded / "atlas.png").convert("RGBA")
        self.fps = data["properties"]["framerate"]
        self.root = data["properties"]["root_character_id"]
        self.drawn_shapes = set()
        self.mask_shapes = set()
        self.sampled_sprites = {self.root}
        if data["actionscript"] != ["070000"]:
            raise ValueError("this baker only handles stop actions")

    def labels(self, sprite):
        return {l["name_text"]: l["frame"] for l in self.sprites[sprite]["labels"]}

    @lru_cache(maxsize=None)
    def states(self, sprite):
        layers = {}
        states = []
        for frame in self.sprites[sprite]["frames"]:
            for cmd in frame["commands"]:
                if cmd["op"] == "action":
                    continue
                depth = cmd["depth"]
                if cmd["op"] == "remove":
                    del layers[depth]
                    continue
                previous = layers.get(depth, {})
                if cmd["mode"] == "place":
                    previous = {}
                replace = cmd["mode"] != "move"
                layers[depth] = dict(
                    child=cmd["character"] if replace else previous["child"],
                    born=frame["id"] if replace else previous["born"],
                    name=cmd["name_text"] or previous.get("name", ""),
                    matrix=cmd["matrix"] or previous.get("matrix", IDENTITY),
                    color=cmd.get("color_mult_rgba", previous.get("color", WHITE)),
                    offset=cmd.get(
                        "color_add_rgba", previous.get("offset", (0, 0, 0, 0))
                    ),
                    blend=cmd["blend"] or previous.get("blend", 0),
                    mask=cmd["unk2"] or previous.get("mask", 0),
                )
            states.append(dict(layers))
        return states

    def instance(self, parent, name):
        return next(
            obj for obj in self.states(parent)[0].values() if obj["name"] == name
        )

    @lru_cache(maxsize=None)
    def period(self, sprite):
        if sprite not in self.sprites:
            return 1
        states = self.states(sprite)
        local = (
            len(states)
            if any(f["commands"] for f in self.sprites[sprite]["frames"][1:])
            else 1
        )
        # A child that occupies the same depth for the entire parent loop
        # keeps running on its own timeline (e.g. the 800-frame glint).
        persistent = [
            self.period(obj["child"])
            for depth, obj in states[0].items()
            if states[-1].get(depth, {}).get("born") == 0
        ]
        return math.lcm(local, *persistent)

    @lru_cache(maxsize=None)
    def mask_geometry(self, child, matrix):
        self.mask_shapes.add(child)
        graphics = self.shapes[child]["graphics"]
        triangles = []
        for graphic in graphics:
            vertices = [transform(matrix, v) for v in graphic["vertices"]]
            xs, ys, us, vs = zip(*vertices)
            if graphic["fill_type"] == 65:
                w, h = self.atlas.size
                alpha = self.atlas.crop(
                    (
                        round(min(us) * w),
                        round(min(vs) * h),
                        round(max(us) * w),
                        round(max(vs) * h),
                    )
                ).getchannel("A")
                if alpha.getextrema() != (255, 255):
                    raise ValueError("mask requires an alpha texture")
            if (
                len(graphics) == 1
                and len(vertices) == 4
                and len(set(xs)) == len(set(ys)) == 2
            ):
                return (min(xs), min(ys), max(xs), max(ys)), ()
            indices = graphic["indices"]
            for i in range(0, len(indices), 3):
                a, b, c = [vertices[j][:2] for j in indices[i : i + 3]]
                area = cross(a, b, c)
                if abs(area) > 1e-8:
                    triangles.append((a, b, c) if area > 0 else (a, c, b))
        return None, triangles

    def draw(
        self,
        sprite,
        frame,
        matrix,
        overrides,
        path=(),
        tint=WHITE,
        blend=0,
        masks=(),
        age=None,
        offset=(0, 0, 0, 0),
    ):
        self.sampled_sprites.add(sprite)
        age = frame if age is None else age
        mask_end = -1
        for depth, obj in sorted(self.states(sprite)[frame].items()):
            child = obj["child"]
            local = compose(matrix, obj["matrix"])
            color = tuple(a * b for a, b in zip(tint, obj["color"]))
            added = tuple(a + b * c for a, b, c in zip(offset, tint, obj["offset"]))
            if obj["mask"]:
                mask = self.mask_geometry(child, local)
                mask_end = obj["mask"]
                continue
            if color[3] == 0 and added[3] == 0:
                continue
            clips = masks + (mask,) if depth <= mask_end else masks
            mode = obj["blend"] or blend
            key = path + (depth, child)
            persistent = (
                obj["born"] == 0
                and self.states(sprite)[-1].get(depth, {}).get("born") == 0
            )
            child_age = (
                age if sprite in overrides or persistent else frame - obj["born"]
            )
            if child in self.sprites:
                subframe = overrides.get(child, child_age % len(self.states(child)))
                yield from self.draw(
                    child,
                    subframe,
                    local,
                    overrides,
                    key,
                    color,
                    mode,
                    clips,
                    child_age,
                    added,
                )
            else:
                yield from self.graphic(child, key, local, color, added, mode, clips)

    def graphic(self, child, key, local, color, added, mode, clips=()):
        for i, g in enumerate(self.shapes[child]["graphics"]):
            if g["indices"] != [0, 1, 2, 3, 0, 2]:
                raise ValueError(f"non-quad visible graphic {child}/{i}")
            polygons = [[transform(local, v) for v in g["vertices"]]]
            for box, triangles in clips:
                if box:
                    polygons = [clip_polygon(poly, box) for poly in polygons]
                else:
                    polygons = [
                        clip_triangle(poly, tri)
                        for poly in polygons
                        for tri in triangles
                    ]
                polygons = [poly for poly in polygons if len(poly) >= 3]
            vertices, indices = [], []
            for poly in polygons:
                start = len(vertices)
                vertices.extend(poly)
                indices.extend(
                    start + n for j in range(1, len(poly) - 1) for n in (0, j, j + 1)
                )
            if vertices:
                self.drawn_shapes.add(child)
                yield key + (i,), color, mode, vertices, indices, added


def cross(a, b, p):
    return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])


def clip_triangle(poly, tri):
    for a, b in zip(tri, tri[1:] + tri[:1]):
        if not poly:
            break
        result = []
        prev = poly[-1]
        left = cross(a, b, prev)
        for point in poly:
            right = cross(a, b, point)
            if (left < 0) != (right < 0):
                t = left / (left - right)
                result.append(tuple(v + (w - v) * t for v, w in zip(prev, point)))
            if right >= 0:
                result.append(point)
            prev, left = point, right
        poly = result
    return poly


class Item:
    def __init__(self, layout):
        self.layout = layout
        self.parts = {}
        self.clips = []

    def clip(self, name, frames, loop=False):
        rows = []
        for frame in frames:
            draws = []
            for key, color, blend, vertices, indices, added in frame:
                uv = tuple(round(v * 65535) for point in vertices for v in point[2:])
                # A colour offset is applied to the texture before compositing.
                # Bake RGB for that variant; opacity remains an animatable draw value.
                variant = (
                    (tuple(color[:3]), tuple(added[:3])) if any(added[:3]) else None
                )
                if added[3]:
                    raise ValueError("additive alpha is unsupported")
                if variant:
                    color = (*WHITE[:3], color[3])
                shape = (key, blend, uv, tuple(indices), variant)
                index = self.parts.setdefault(shape, len(self.parts))
                draws.append(
                    dict(
                        Part=index,
                        Color=[round(v * 255) for v in color],
                        Vertices=[v for x, y, _, _ in vertices for v in (x, 96 - y)],
                    )
                )
            rows.append(dict(Draws=draws))
        self.clips.append(dict(Name=name, FPS=self.layout.fps, Loop=loop, Frames=rows))

    def save(self, path, encoder):
        # Pack only the source graphics drawn by this item, with their original
        # neighbouring texels as gutters for bilinear filtering at the edges.
        atlas = self.layout.atlas
        regions = {}
        for key, _, _, _, variant in self.parts:
            region = (*key[-2:], variant)
            if region in regions:
                continue
            child, index = key[-2:]
            graphic = self.layout.shapes[child]["graphics"][index]
            if graphic["fill_type"] == 0:
                rgba = self.layout.data["colors"][graphic["atlas"]]
                image = Image.new("RGBA", (3, 3), tuple(round(v * 255) for v in rgba))
                bounds = (0, 0, 3, 3)
            else:
                us = [p[2] * atlas.width for p in graphic["vertices"]]
                vs = [p[3] * atlas.height for p in graphic["vertices"]]
                bounds = (
                    max(0, int(min(us)) - 1),
                    max(0, int(min(vs)) - 1),
                    min(atlas.width, int(max(us)) + 2),
                    min(atlas.height, int(max(vs)) + 2),
                )
                image = atlas.crop(bounds)
            if variant:
                mult, add = variant
                channels = list(image.split())
                for i in range(3):
                    channels[i] = channels[i].point(
                        [
                            min(255, round(x * mult[i] + 255 * add[i]))
                            for x in range(256)
                        ]
                    )
                image = Image.merge("RGBA", channels)
            regions[region] = (bounds, image, graphic["fill_type"] == 0)
        width = max((im.width for _, im, _ in regions.values()), default=1)
        height = max(1, sum(im.height for _, im, _ in regions.values()))
        picture = Image.new("RGBA", (width, height))
        offsets = {}
        top = 0
        for region, (bounds, im, solid) in regions.items():
            picture.paste(im, (0, top))
            offsets[region] = (-bounds[0], top - bounds[1], solid)
            top += im.height
        stream = io.BytesIO()
        picture.save(stream, format="PNG")
        parts = []
        for key, blend, uv, indices, variant in self.parts:
            dx, dy, solid = offsets[(*key[-2:], variant)]
            coords = []
            for u, v in zip(uv[::2], uv[1::2]):
                x, y = (
                    (1.5, 1.5)
                    if solid
                    else (u / 65535 * atlas.width, v / 65535 * atlas.height)
                )
                coords.extend(((x + dx) / width, (y + dy) / height))
            parts.append(
                dict(
                    Name="/".join(map(str, key)),
                    Blend={0: 0, 8: 1}[blend],
                    UVs=coords,
                    Indices=indices,
                )
            )
        data = dict(
            Name=path.stem,
            Unit=1 / 16,
            View=[0, 0, 408, 96],
            Parts=parts,
            Clips=self.clips,
            Texture=base64.b64encode(stream.getvalue()).decode(),
        )
        path.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run([encoder, path], input=json.dumps(data).encode(), check=True)


def bake(decoded, output, encoder):
    layout = Layout(decoded)
    main = layout.instance(layout.root, "main")["child"]
    # Centre of the source plate in the existing 408 x 96 nameplate canvas (y down).
    plate = (1, 0, 0, 1, 204, 48)
    rankup = layout.instance(main, "rankup_instance")
    sprite = rankup["child"]
    rank = layout.instance(sprite, "dani_rankup_efc_02_instance")["child"]
    labels = list(layout.labels(sprite).items())
    matrix = compose(plate, rankup["matrix"])
    # Keep all ranks in one atlas. A shared idle keeps wearing the effect unobtrusive.
    item = Item(layout)
    item.clip("idle", [[]])
    for rank_name, rank_frame in layout.labels(rank).items():
        for i, (name, start) in enumerate(labels):
            end = (
                labels[i + 1][1] if i + 1 < len(labels) else len(layout.states(sprite))
            )
            if name == "init":
                continue
            # A one-shot start reaches and holds the source's stopped wait frame.
            if name == "rankup_start":
                end = layout.labels(sprite)["rankup_wait"] + 1
            item.clip(
                f"{rank_name}/{name}",
                (
                    layout.draw(sprite, f, matrix, {rank: rank_frame})
                    for f in range(start, end)
                ),
            )
    item.save(output / "icon/lumen-rankup.otanim", encoder)
    from catalog import export_catalog

    export_catalog(layout, output, encoder, Item, compose)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("decoded", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory() as tmp:
        encoder = Path(tmp) / "encode"
        subprocess.run(
            ["go", "build", "-o", encoder, "."],
            cwd=Path(__file__).resolve().parent,
            check=True,
        )
        bake(args.decoded, args.output, encoder)
