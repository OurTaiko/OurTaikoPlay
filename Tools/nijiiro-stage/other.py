import math
from PIL import Image
from bake import Bake, base, graphics, out, scripts, table


def draw(b, path, i, x, y, scale=1):
    return b.draw(path, i, x, y, 0, 0, scale, scale, 0, 1)


def static(b, path, name, x, y, scale=1):
    b.clip(name, 60, True, 0, [[draw(b, path, 0, x, y, scale)]])


for i in range(3):
    b = Bake(540)
    static(b, graphics / "footer" / f"{i}.png", "normal", 0, 474)
    b.save(out / "footer" / f"footer{i}.otanim")
    b = Bake(276)
    source = graphics / "renda" / f"renda_{i}"
    pics = (
        sorted(source.glob("*.png"))
        if source.is_dir()
        else [source.with_suffix(".png")]
    )
    for j, path in enumerate(pics):
        with Image.open(path) as im:
            w, h = im.size
        vx, vy, spin = [(17.3, -10.9, 0), (17.3, 0, -6), (13.1, -13.8, 0)][i]
        b.clip(
            f"hit_{j}",
            60,
            False,
            0,
            (
                [
                    b.draw(
                        path,
                        0,
                        300 + vx * f,
                        180 + vy * f,
                        w / 2,
                        h / 2,
                        1,
                        1,
                        spin * f,
                        1,
                    )
                ]
                for f in range(120)
            ),
        )
    b.save(out / "renda" / f"renda_{i}.otanim")
b = Bake(540)
directory = graphics / "bg_fever/bg_fever_3"
static(b, directory / "background.png", "clear", 0, 0)
b.clip(
    "clear_strip",
    60,
    True,
    0,
    (
        [draw(b, directory / "overlay_1.png", i, i * 3840 - f, 0) for i in range(2)]
        for f in range(3840)
    ),
)
b.save(out / "clear/bg_fever_3.otanim")
practice = base.parent / "PyTaikoGreen/Graphics/background/collab/practice"
b = Bake(540)
static(
    b,
    practice / "bg_normal/bg_0/background.png",
    "normal",
    326 * 1.5,
    392 * 1.5 - 540,
    1.5,
)
b.save(out / "scene/practice.otanim")
b = Bake(276)
for k, n in enumerate(("normal", "clear")):
    p = practice / "donbg/0_1/background" / f"{k}.png"
    w = Image.open(p).width * 1.5
    b.clip(
        n,
        60,
        True,
        0,
        (
            [draw(b, p, i, 30 + (i - f / 180) * w, 0, 1.5) for i in range(5)]
            for f in range(180)
        ),
    )
b.save(out / "header/practice.otanim")
t = table(scripts / "bg_data/tutorial_ac.lua")
layers = t["layers"]
directory = graphics / "tutorial_ac"
for folder, height, offset in [("header", 276, 0), ("scene", 540, 540)]:
    b = Bake(height)

    def frames():
        for f in range(960):
            ds = []
            for name in ["floor", "pat", "mtn", "cloud", "w13", "w16", "w21", "w24"]:
                path = directory / (name + ".png")
                if name == "floor":
                    coords = [(0, layers[name]["y"])]
                elif name == "cloud":
                    tr = t[name]
                    cx = tr["x"][f % tr["n"]]
                    cy = t["c9"]["y"][f % 120] - 30
                    coords = [
                        (
                            cx + 600 + 744 * i + layers[name]["dx"],
                            cy + layers[name]["dy"],
                        )
                        for i in range(5)
                    ]
                else:
                    tr = t[name]
                    coords = [
                        (
                            tr["x"][f % tr["n"]],
                            layers[name]["ytop"]
                            + (0 if name in ["pat", "mtn"] else tr["y"][f % 120]),
                        )
                    ]
                for i, (x, y) in enumerate(coords):
                    im, box, _ = b.image(path)
                    if y + box[3] <= offset or y + box[1] >= offset + height:
                        continue
                    y = math.floor(y + 0.5)
                    cut = max(0, 60 - y) if name not in ("floor", "cloud") else 0
                    picture = path
                    if cut:
                        picture = path.with_name(f"{name}_cut_{cut}.png")
                        if picture not in b.images:
                            top = max(cut, box[1])
                            if top >= box[3]:
                                continue
                            b.images[picture] = (
                                im.crop((0, top - box[1], im.width, im.height)),
                                (box[0], top, box[2], box[3]),
                                b.images[path][2],
                            )
                    ds.append(draw(b, picture, i, math.floor(x + 0.5), y - offset))
            yield ds

    b.clip("normal", 60, True, 0, frames())
    b.save(out / folder / "tutorial.otanim")

# The fourth standard crowd is available to named presets only.
overlay = graphics / "fever/fever_3/overlay.png"
b = Bake(540)


def bounce(frame, opacity):
    phase = frame % 40 / 20
    y = -75 * (1 - (phase - 1) ** 2)
    return [b.draw(overlay, 0, 0, y, 0, 0, 1, 1, 0, opacity)]


b.clip("full", 40, True, 40, (bounce(f, min(1, f / 39)) for f in range(80)))
b.clip("release", 40, False, 0, (bounce(f, 1 - f / 39) for f in range(40)))
b.save(out / "crowd/3.otanim")
