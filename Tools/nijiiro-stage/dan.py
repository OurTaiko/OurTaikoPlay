from bake import Bake, base, graphics, out

d = graphics / "collab/dan/donbg/0_1"
b = Bake(276)


def draw(b, p, k, x, y, scale=1):
    return b.draw(p, k, x, y, 0, 0, scale, scale, 0, 1)


for layer, name, tile, speed in [
    ("a", "normal_back", 1920, 90),
    ("b", "normal_mid_b", 1920, 72.045),
    ("c", "normal_mid_c", 1920, 65.4918),
    ("petal", "normal_petals", 960, 0),
    ("d", "normal_front", 960, 45),
]:

    def frames():
        for f in range(320 if layer == "petal" else round(tile * 60 / speed)):
            ds = [draw(b, d / "background.png", 0, 0, 0)] if layer == "a" else []
            if layer == "petal":
                top = -218 + 2.321875 * f - 120 + 2
                if top < 278:
                    ds.extend(
                        draw(
                            b,
                            d / "petal.png",
                            i,
                            2073 - 4.36875 * f - 960 * i - 240,
                            top,
                        )
                        for i in range(2)
                    )
            else:
                ds.extend(
                    draw(b, d / f"layer_{layer}.png", i, i * tile - speed * f / 60, 2)
                    for i in range(1 + 1920 // tile)
                )
            yield ds

    b.clip(name, 60, True, 0, frames())
b.save(out / "header/dojo.otanim")
d = base.parent / "PyTaikoGreen/Graphics/background/collab/dan"
b = Bake(540)
b.clip(
    "normal",
    60,
    True,
    0,
    [
        [
            draw(b, d / "bg_normal/bg_0/background.png", 0, 0, 0, 1.5),
            draw(b, d / "footer/0.png", 0, 0, 444, 1.5),
        ]
    ],
)
b.save(out / "scene/dojo.otanim")
