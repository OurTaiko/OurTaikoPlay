import math
from bake import RENDA_HEIGHT, RENDA_SPAWN_Y, Bake, graphics, out, scripts, table

alphabet = [chr(i) for i in range(33, 127) if i not in (34, 92)]
digits = {c: i for i, c in enumerate(alphabet)}


def channel(v):
    if not isinstance(v, list):
        return [v]
    if len(v) != 4 or not isinstance(v[3], str):
        return v
    base, step, w, s = v
    values = []
    for i in range(0, len(s), w):
        n = 0
        for c in s[i : i + w]:
            n = n * 92 + digits[c]
        values.append(base + step * n)
    return values


def renda_spawn(dx):
    """Where a collab drumroll clip starts across the screen, as the skin's RendaClip.add_renda
    picks it: a part that travels left enters from the right edge (1700..2100), one that travels
    right from the left (-180..220), one that stays put anywhere between (200..1720). The middle
    of each range; the catalog's spread for the set is its half width."""
    return 1900 if dx < -1 else 20 if dx > 1 else 960


# Collab drumrolls whose numbers and pictures are the 1280x720 cabinet's rather than 1920x1080,
# with where their pieces start. Every other renda_anim file is 1.5 times PyTaikoGreen's 720p port
# of it (miku's -2060 across is -3090 here, lovelive's 1560/-1648 is 2340/-2472); TOHO's equals it
# number for number (rings -40 down and +-160/+-320 across, a fan of nine spraying ~1000), and its
# pictures are the 720p ones too (the ring is 128 px in both), so the cabinet draws it half as big
# again as the file says. Its `dx` is no direction either (the fan spreads both ways), so its hits
# start where the cabinet starts them, as PyTaikoGreen's port keeps it: x 400..880, y 300..320 of
# 720, above the dancers.
RENDA_720P = {"b_002": (960, 465)}


def shape(t, k):
    return t[str(k)] if isinstance(t, dict) else t[k - 1]


def strips(b, group, t, directory, prefix, offset=0):
    # Adjacent placements can share a clip if their loops come round together.
    # Different periods keep independent playheads, preserving layer order.
    prepared = []
    for i, e in enumerate(group):
        channels = {
            k: channel(e[k])
            for k in ("tx", "ty", "sx", "sy", "rot", "a", "mir", "cr", "cg", "cb")
            if k in e
        }
        period = math.lcm(e["period"], e.get("on_period", 1))
        pic = e["shape"]
        prepared.append(
            (
                period,
                i,
                directory / f"s{pic}.png",
                shape(t["shapes"], pic),
                channels,
                e.get("on"),
                # The movie's blend 8 is add (lantern light, spotlights); the rest draw as they are.
                1 if e.get("blend") == 8 else 0,
            )
        )
    batches = []
    for row in prepared:
        if batches and math.lcm(batches[-1][0], row[0]) <= max(batches[-1][0], row[0]):
            batches[-1][0] = max(batches[-1][0], row[0])
            batches[-1][1].append(row)
        else:
            batches.append([row[0], [row]])
    if prefix == "rig":
        batches = [[math.lcm(*(r[0] for r in prepared)), prepared]]
    for k, (period, rows) in enumerate(batches):

        def frames():
            for f in range(period):
                ds = []
                for _, i, path, s, ch, on, blend in rows:
                    if on and on[f % len(on)] != "1":
                        continue

                    def val(key, default=1):
                        v = ch.get(key)
                        return v[f % len(v)] if v else default

                    a = val("a")
                    if a <= 0.002:
                        continue
                    rgb = tuple(
                        max(0, min(255, round(255 * val(c))))
                        for c in ("cr", "cg", "cb")
                    )
                    ds.append(
                        b.draw(
                            path,
                            i,
                            val("tx"),
                            offset + val("ty"),
                            s[2],
                            s[3],
                            val("sx"),
                            val("sy"),
                            val("rot", 0),
                            a,
                            val("mir", 0) == 1,
                            rgb,
                            blend,
                        )
                    )
                yield ds

        b.clip(
            prefix if k == 0 else f"{prefix}_{k}",
            40 if prefix == "rig" else 60,
            True,
            0,
            frames(),
        )


def export(src):
    t = table(src)
    key = src.stem
    parent = src.parent.name
    if parent == "donbg_anim":
        directory = graphics / ("donbga_" + key.removeprefix("a_"))
        folder = "header"
        name = key.removesuffix("_1p")
        b = Bake(276)
        for source, clip in [("normal", "normal"), ("fever", "clear"), ("top", "top")]:
            if t.get(source):
                strips(b, t[source], t, directory, clip)
    elif parent == "bg_anim":
        folder = (
            "clear"
            if key.endswith("_f") or key.startswith("bg_fever")
            else "footer"
            if key.endswith("_dai")
            else "dancers"
            if key.endswith("_d")
            else "scene"
        )
        movie = folder == "dancers"
        b = Bake(1080 if movie else 540)
        name = key
        # BGAnim draws in a lower-half canvas, including for its beat-driven dancer movies.
        strips(
            b,
            t["normal"],
            t,
            graphics / ("bga_" + key),
            "rig" if movie else "clear" if folder == "clear" else "normal",
            540 if movie else 0,
        )
    else:
        crowd = parent == "fever_anim"
        name = key.removeprefix("f_")
        folder = "crowd" if crowd else "renda"
        b = Bake(540 if crowd else RENDA_HEIGHT)
        directory = graphics / (("fevera_" if crowd else "rendaa_") + name)

        def frames(rows, x, y, k=1):
            for row in rows:
                ds = []
                for i, (pic, dx, dy, sx, sy, rot, a, *mir) in enumerate(row):
                    if a <= 0.002:
                        continue
                    s = shape(t["shapes"], pic)
                    ds.append(
                        b.draw(
                            directory / f"s{pic}.png",
                            i,
                            x + dx * k,
                            y + dy * k,
                            s[2],
                            s[3],
                            sx * k,
                            sy * k,
                            rot,
                            a,
                            bool(mir and mir[0]),
                        )
                    )
                yield ds

        if crowd:
            lab = t["labels"]
            first = lab.get("normal_fever", 0)
            loop = lab.get("fever", first)
            end = lab.get("fever_normal", t["nframes"])
            b.clip("full", 40, True, loop - first, frames(t["frames"][first:end], 0, 0))
            if end < t["nframes"]:
                b.clip("release", 40, False, 0, frames(t["frames"][end:], 0, 0))
        else:
            spawn = RENDA_720P.get(key)
            for i, p in enumerate(t["parts"]):
                x, y = spawn or (renda_spawn(p["dx"]), RENDA_SPAWN_Y)
                b.clip("hit_" + str(i), 60, False, 0, frames(p["frames"], x, y, 1.5 if spawn else 1))
    path = out / folder / (name + ".otanim")
    b.save(path)
    print(path, flush=True)


if __name__ == "__main__":
    import concurrent.futures

    with concurrent.futures.ProcessPoolExecutor(4) as pool:
        list(pool.map(export, sorted((scripts / "bg_data").glob("*anim/*.lua"))))
