"""Build portable v1 clips and the ordered layer list for each label of the main plate."""

import json
from xml.etree import ElementTree
import math


def export_catalog(layout, output, encoder, Item, compose):
    plate = (1, 0, 0, 1, 204, 48)
    main = layout.instance(layout.root, "main")["child"]
    layout.sampled_sprites.add(main)
    labels = layout.labels(main)
    base = Item(layout)
    catalog = dict(states={}, titles={}, aiTitles={}, blank=[], badges=[], ranks=[])
    instances = {}

    def save(item, path):
        item.save(output / (path + ".otanim"), encoder)

    for name, frame in labels.items():
        rows = []
        catalog["states"][name] = rows
        # F105 is the full display list used when the game jumps to a label.
        keyframe = next(
            f for f in layout.sprites[main]["keyframes"] if f["id"] == frame
        )
        for c in sorted(
            (c for c in keyframe["commands"] if c["op"] == "place"),
            key=lambda c: c["depth"],
        ):
            role, child = c["name_text"], c["character"]
            mat = compose(plate, c["matrix"])
            color, add = c["color_mult_rgba"], c["color_add_rgba"]
            if child in layout.sprites:
                if role in instances and instances[role] != (child, mat):
                    raise ValueError(f"{role} changes its canvas transform")
                instances[role] = child, mat
            if color[3] == 0:
                continue
            if str(child) in layout.data["texts"]:
                t = layout.data["texts"][str(child)]
                left, top, right, bottom = t["bounds_ltrb"]
                assert mat[:4] == (1, 0, 0, 1) and t["alignment"] == 2
                rgba = [
                    min(1, max(0, v * m + a))
                    for v, m, a in zip(t["color_rgba"], color, add)
                ]
                rows.append(
                    dict(
                        role=role,
                        character=child,
                        sample="".join(
                            ElementTree.fromstring(t["placeholder_text"]).itertext()
                        ),
                        x=mat[4] + (left + right) / 2,
                        y=mat[5] + (top + bottom) / 2,
                        width=right - left,
                        size=t["size"],
                        rgba=rgba,
                        stroke=t["stroke_width"],
                    )
                )
            elif role:
                if color != [1, 1, 1, 1] or any(add):
                    raise ValueError(f"unsupported instance tint: {role}")
                rows.append(dict(role=role))
            else:
                clip = f"{name}/{c['depth']}"
                draws = layout.graphic(child, (child,), mat, color, add, c["blend"])
                base.clip(clip, [draws])
                rows.append(dict(role="base", clip=clip))
    save(base, "composition/plates")

    for role, bucket in [
        ("title_instance", "titles"),
        ("ai_title_instance", "aiTitles"),
    ]:
        sprite, mat = instances[role]
        for label, frame in layout.labels(sprite).items():
            period = math.lcm(
                1,
                *(
                    layout.period(o["child"])
                    for o in layout.states(sprite)[frame].values()
                    if o["color"][3]
                ),
            )
            item = Item(layout)
            item.clip(
                "idle",
                (
                    layout.draw(sprite, frame, mat, {sprite: frame}, age=t)
                    for t in range(period)
                ),
                loop=period > 1,
            )
            path = (
                "band/lumen-"
                + ("ai-title-" if bucket == "aiTitles" else "title-")
                + label
            )
            catalog[bucket][label] = path
            save(item, path)

    for role, bucket, path in [
        ("plate_player_no", "badges", "badges"),
        ("blank_instance", "blank", "blank"),
    ]:
        sprite, mat = instances[role]
        item = Item(layout)
        for label, frame in layout.labels(sprite).items():
            catalog[bucket].append(label)
            item.clip(label, [layout.draw(sprite, frame, mat, {sprite: frame}, age=0)])
        save(item, "composition/" + path)

    sprite, mat = instances["dani"]
    for ring, frame in layout.labels(sprite).items():
        item = Item(layout)
        rank = layout.instance(sprite, "dani_" + ring + "_instance")["child"]
        for label, rank_frame in layout.labels(rank).items():
            catalog["ranks"].append(ring + "/" + label)
            overrides = {sprite: frame, rank: rank_frame}
            item.clip(label, [layout.draw(sprite, frame, mat, overrides, age=0)])
        save(item, "composition/dan-" + ring)
    unused = layout.shapes.keys() - layout.drawn_shapes - layout.mask_shapes
    unsampled = layout.sprites.keys() - layout.sampled_sprites
    missing_text = set(map(int, layout.data["texts"])) - {
        row["character"]
        for rows in catalog["states"].values()
        for row in rows
        if "character" in row
    }
    if unused or unsampled or missing_text:
        raise ValueError(
            f"Unaccounted source objects: shapes={unused}, sprites={unsampled}, text={missing_text}"
        )
    print(
        f"{len(layout.drawn_shapes)} drawn shapes, {len(layout.mask_shapes)} masks, "
        f"{len(layout.sampled_sprites)} sprites and {len(layout.data['texts'])} text definitions"
    )
    (output / "composition/catalog.json").write_text(
        json.dumps(catalog, ensure_ascii=False, indent=1) + "\n"
    )
