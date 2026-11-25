"""Split every Lumen title into the layers a nameplate can wear: band, effect and icon.

The source draws a title as one sprite: a fill that is the band, glints and sweeps over it, and an
emblem (a character, a crown, a ring) on the band's right end. The three are told apart by what
they draw, then written as separate items that the plate stacks again in the source's own order.
"""

import hashlib
import math
import re
from collections import OrderedDict

SLOTS = ("band", "effect", "icon")
EDGE = 1.0
# An emblem starts at the band's right end (x 292..330 in the source).
EMBLEM_FROM = 280


class Leaf:
    """One drawn graphic over a title's whole loop."""

    def __init__(self, key):
        self.key = key
        self.box = None
        self.blends = set()
        self.alpha = [2.0, -1.0]
        self.frames = 0

    def add(self, color, blend, vertices):
        xs = [v[0] for v in vertices]
        ys = [v[1] for v in vertices]
        box = (min(xs), min(ys), max(xs), max(ys))
        self.box = box if self.box is None else (
            min(self.box[0], box[0]), min(self.box[1], box[1]),
            max(self.box[2], box[2]), max(self.box[3], box[3]))
        self.blends.add(blend)
        self.alpha = [min(self.alpha[0], color[3]), max(self.alpha[1], color[3])]
        self.frames += 1

    # Draw order: the depths down the tree, then the graphic's index.
    @property
    def z(self):
        return self.key[0:-1:2] + (self.key[-1],)


def inside(box, rect):
    return (box[0] >= rect[0] - EDGE and box[1] >= rect[1] - EDGE
            and box[2] <= rect[2] + EDGE and box[3] <= rect[3] + EDGE)


def covers(box, rect):
    return all(abs(a - b) <= EDGE for a, b in zip(box, rect))


def overlaps(a, b):
    return min(a[2], b[2]) - max(a[0], b[0]) > EDGE and min(a[3], b[3]) - max(a[1], b[1]) > EDGE


def leaves(frames):
    found = OrderedDict()
    for frame in frames:
        for key, color, blend, vertices, _, _ in frame:
            found.setdefault(key, Leaf(key)).add(color, blend, vertices)
    return sorted(found.values(), key=lambda leaf: leaf.z)


def band_rect(layout, sprite, matrix):
    """What the plain wood title fills: the rectangle every other title's band is measured by."""
    frame = layout.labels(sprite)["wood"]
    vertices = [v for draw in layout.draw(sprite, frame, matrix, {sprite: frame}, age=0) for v in draw[3]]
    xs, ys = [v[0] for v in vertices], [v[1] for v in vertices]
    return min(xs), min(ys), max(xs), max(ys)


def classify(frames, band, stencil):
    """slot name per draw key, whether the effect leaves the stencil, and the order violations."""
    all_leaves = leaves(frames)
    period = len(frames)
    emblems = [
        leaf for leaf in all_leaves
        if leaf.blends == {0} and leaf.frames == period and leaf.alpha[0] > 0.99
        and not inside(leaf.box, band) and leaf.box[0] >= EMBLEM_FROM
    ]
    slots = {}
    icons = []
    for leaf in all_leaves:
        if leaf in emblems:
            slots[leaf.key] = "icon"
        # What is drawn over the emblem belongs to it, and so is what is drawn over that.
        elif emblems and leaf.z > emblems[0].z and not covers(leaf.box, band) and any(
                overlaps(leaf.box, icon.box) for icon in icons):
            slots[leaf.key] = "icon"
        elif leaf.blends == {0} and covers(leaf.box, band):
            slots[leaf.key] = "band"
        else:
            slots[leaf.key] = "effect"
        if slots[leaf.key] == "icon":
            icons.append(leaf)
    # The plate stacks the layers band, effect, then icon: nothing may need to be drawn
    # the other way round where they meet.
    rank = {"band": 0, "effect": 1, "icon": 2}
    violations = [
        (a.key, b.key)
        for i, a in enumerate(all_leaves) for b in all_leaves[i + 1:]
        if rank[slots[a.key]] > rank[slots[b.key]] and overlaps(a.box, b.box)
    ]
    unclipped = any(slots[l.key] == "effect" and not inside(l.box, stencil) for l in all_leaves)
    return slots, unclipped, violations


def reduce(frames):
    """The shortest loop that repeats to the same frames."""
    for length in range(1, len(frames) + 1):
        if len(frames) % length == 0 and all(frames[i] == frames[i % length] for i in range(len(frames))):
            return frames[:length]
    return frames


def signature(frames):
    # Equal pictures from different titles share one file whatever path each reached it by.
    flat = [[(key[-2:], color, blend, vertices, indices, added) for key, color, blend, vertices, indices, added in frame]
            for frame in frames]
    return hashlib.sha1(repr(flat).encode()).hexdigest()


def stem(labels):
    """The name a shared item goes by: what its users have in common, else the first of them."""
    stems = {re.sub(r"_\d+$", "", label) for label in labels}
    return stems.pop() if len(labels) > 1 and len(stems) == 1 else labels[0]


def split(label, frames, band, stencil):
    """(slot -> frames of that slot's draws, the effect may be clipped to the plate's band)."""
    slots, unclipped, violations = classify(frames, band, stencil)
    if violations:
        raise ValueError(f"{label}: layers cannot be stacked in the source's order: {violations[:3]}")
    parts = {}
    for slot in SLOTS:
        part = [[draw for draw in frame if slots[draw[0]] == slot] for frame in frames]
        if any(part):
            parts[slot] = reduce(part)
    # Every draw lands in exactly one layer and keeps its place within it.
    for t, frame in enumerate(frames):
        for slot in parts:
            assert parts[slot][t % len(parts[slot])] == [d for d in frame if slots[d[0]] == slot]
    return parts, not unclipped


class Registry:
    """Items written from the titles, one file for each distinct picture."""

    def __init__(self):
        self.items = OrderedDict()  # (slot, signature) -> dict(frames, users, ai, clip)

    def add(self, label, ai, parts, clipped):
        ids = {}
        for slot, frames in parts.items():
            entry = self.items.setdefault((slot, signature(frames)), dict(frames=frames, users=[], ai=ai, clip=clipped))
            entry["users"].append(label)
            ids[slot] = entry
        return ids

    def name(self, slot, entry, taken):
        base = ("lumen-ai-title-" if entry["ai"] else "lumen-title-") + stem(entry["users"])
        name, n = base, 2
        while (slot, name) in taken:
            name, n = f"{base}-{n}", n + 1
        taken.add((slot, name))
        return slot + "/" + name

    def named(self):
        taken = set()
        return [(self.name(slot, entry, taken), slot, entry) for (slot, _), entry in self.items.items()]


def lcm_period(layout, sprite, frame):
    return math.lcm(
        1, *(layout.period(o["child"]) for o in layout.states(sprite)[frame].values() if o["color"][3])
    )
