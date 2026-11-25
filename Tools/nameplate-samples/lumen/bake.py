"""Bake the masked ai_3 band and the rankup timeline from a decoded name_plate."""
import argparse
import base64
import io
import json
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
    return (a*e+c*f, b*e+d*f, a*g+c*h, b*g+d*h, a*u+c*v+x, b*u+d*v+y)


def transform(matrix, vertex):
    a, b, c, d, x, y = matrix
    u, v, s, t = vertex
    return x+a*u+c*v, y+b*u+d*v, s, t


def clip_polygon(vertices, box):
    """Clip in plate space, interpolating UVs at each intersection."""
    for axis, limit, sign in ((0, box[0], 1), (0, box[2], -1),
                              (1, box[1], 1), (1, box[3], -1)):
        if not vertices:
            break
        out = []
        prev = vertices[-1]
        for point in vertices:
            left = (prev[axis] - limit) * sign
            right = (point[axis] - limit) * sign
            if (left < 0) != (right < 0):
                t = left / (left - right)
                out.append(tuple(a + (b-a)*t for a, b in zip(prev, point)))
            if right >= 0:
                out.append(point)
            prev = point
        vertices = out
    return vertices


class Layout:
    def __init__(self, decoded):
        data = json.loads((decoded / 'layout.json').read_text())
        self.sprites = {int(k): v for k, v in data['sprites'].items()}
        self.shapes = {int(k): v for k, v in data['shapes'].items()}
        self.atlas = Image.open(decoded / 'atlas.png').convert('RGBA')
        self.fps = data['properties']['framerate']
        self.root = data['properties']['root_character_id']
        if data['actionscript'] != ['070000']:
            raise ValueError('this baker only handles stop actions')

    def labels(self, sprite):
        return {l['name_text']: l['frame'] for l in self.sprites[sprite]['labels']}

    @lru_cache(maxsize=None)
    def states(self, sprite):
        layers = {}
        states = []
        for frame in self.sprites[sprite]['frames']:
            for cmd in frame['commands']:
                if cmd['op'] == 'action':
                    continue
                depth = cmd['depth']
                if cmd['op'] == 'remove':
                    del layers[depth]
                    continue
                previous = layers.get(depth, {})
                if cmd['mode'] == 'place':
                    previous = {}
                if any(cmd.get('color_add_rgba', ())):
                    # Other labels can use colour offsets. Check only when actually drawing.
                    offset = True
                else:
                    offset = False if 'color_add_rgba' in cmd else previous.get('offset', False)
                replace = cmd['mode'] != 'move'
                layers[depth] = dict(
                    child=cmd['character'] if replace else previous['child'],
                    born=frame['id'] if replace else previous['born'],
                    name=cmd['name_text'] or previous.get('name', ''),
                    matrix=cmd['matrix'] or previous.get('matrix', IDENTITY),
                    color=cmd.get('color_mult_rgba', previous.get('color', WHITE)),
                    offset=offset,
                    blend=cmd['blend'] or previous.get('blend', 0),
                    mask=cmd['unk2'] or previous.get('mask', 0),
                )
            states.append(dict(layers))
        return states

    def instance(self, parent, name):
        return next(obj for obj in self.states(parent)[0].values() if obj['name'] == name)

    def opaque_box(self, child, matrix):
        graphics = self.shapes[child]['graphics']
        if len(graphics) != 1:
            raise ValueError('mask has multiple graphics')
        vertices = [transform(matrix, v) for v in graphics[0]['vertices']]
        xs, ys, us, vs = zip(*vertices)
        if len(vertices) != 4 or len(set(xs)) != 2 or len(set(ys)) != 2:
            raise ValueError('mask is not an axis-aligned rectangle')
        w, h = self.atlas.size
        alpha = self.atlas.crop((round(min(us)*w), round(min(vs)*h),
                                 round(max(us)*w), round(max(vs)*h))).getchannel('A')
        if alpha.getextrema() != (255, 255):
            raise ValueError('mask needs texture alpha')
        return min(xs), min(ys), max(xs), max(ys)

    def draw(self, sprite, frame, matrix, overrides, path=(), tint=WHITE, blend=0, box=None):
        mask_end = -1
        for depth, obj in sorted(self.states(sprite)[frame].items()):
            child = obj['child']
            local = compose(matrix, obj['matrix'])
            color = tuple(a*b for a, b in zip(tint, obj['color']))
            if obj['mask']:
                mask = self.opaque_box(child, local)
                mask_end = obj['mask']
                continue
            if color[3] == 0:
                continue
            if obj['offset']:
                raise ValueError('visible additive colour transform needs baking')
            clip = mask if depth <= mask_end else box
            mode = obj['blend'] or blend
            key = path + (depth, child)
            if child in self.sprites:
                subframe = overrides.get(child)
                if subframe is None:
                    subframe = (frame - obj['born']) % len(self.states(child))
                yield from self.draw(child, subframe, local, overrides, key, color, mode, clip)
                continue
            for i, graphic in enumerate(self.shapes[child]['graphics']):
                if graphic['fill_type'] != 65 or graphic['indices'] != [0, 1, 2, 3, 0, 2]:
                    raise ValueError('example requires a textured quad')
                vertices = [transform(local, v) for v in graphic['vertices']]
                if clip is not None:
                    vertices = clip_polygon(vertices, clip)
                if len(vertices) >= 3:
                    yield key+(i,), color, mode, vertices


class Item:
    def __init__(self, layout):
        self.layout = layout
        self.parts = {}
        self.clips = []

    def clip(self, name, frames, loop=False):
        rows = []
        for frame in frames:
            draws = []
            for key, color, blend, vertices in frame:
                uv = tuple(round(v*65535) for point in vertices for v in point[2:])
                shape = (key, blend, uv)
                index = self.parts.setdefault(shape, len(self.parts))
                draws.append(dict(Part=index, Color=[round(v*255) for v in color],
                                  Vertices=[v for x, y, _, _ in vertices for v in (x, 96-y)]))
            rows.append(dict(Draws=draws))
        self.clips.append(dict(Name=name, FPS=self.layout.fps, Loop=loop, Frames=rows))

    def save(self, path, encoder):
        # Pack only the source graphics drawn by this item, with their original
        # neighbouring texels as gutters for bilinear filtering at the edges.
        atlas = self.layout.atlas
        regions = {}
        for key, _, _ in self.parts:
            graphic_id = key[-2:]
            if graphic_id in regions:
                continue
            child, index = graphic_id
            vertices = self.layout.shapes[child]['graphics'][index]['vertices']
            us = [point[2]*atlas.width for point in vertices]
            vs = [point[3]*atlas.height for point in vertices]
            regions[graphic_id] = (max(0, int(min(us))-1), max(0, int(min(vs))-1),
                                   min(atlas.width, int(max(us))+2), min(atlas.height, int(max(vs))+2))
        width = max(right-left for left, _, right, _ in regions.values())
        height = sum(bottom-top for _, top, _, bottom in regions.values())
        picture = Image.new('RGBA', (width, height))
        offsets = {}
        top = 0
        for graphic_id, bounds in regions.items():
            picture.paste(atlas.crop(bounds), (0, top))
            offsets[graphic_id] = (-bounds[0], top-bounds[1])
            top += bounds[3]-bounds[1]
        stream = io.BytesIO()
        picture.save(stream, format='PNG')
        parts = []
        for key, blend, uv in self.parts:
            dx, dy = offsets[key[-2:]]
            coords = []
            for u, v in zip(uv[::2], uv[1::2]):
                coords.extend(((u/65535*atlas.width+dx)/picture.width,
                               (v/65535*atlas.height+dy)/picture.height))
            indices = [n for i in range(1, len(uv)//2-1) for n in (0, i, i+1)]
            parts.append(dict(Name='/'.join(map(str, key)), Blend={0: 0, 8: 1}[blend],
                              UVs=coords, Indices=indices))
        data = dict(Name=path.stem, Unit=1/16, View=[0, 0, 408, 96], Parts=parts,
                    Clips=self.clips, Texture=base64.b64encode(stream.getvalue()).decode())
        path.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run([encoder, path], input=json.dumps(data).encode(), check=True)


def bake(decoded, output, encoder):
    layout = Layout(decoded)
    main = layout.instance(layout.root, 'main')['child']
    # Centre of the source plate in the existing 408 x 96 nameplate canvas (y down).
    plate = (1, 0, 0, 1, 204, 48)
    title = layout.instance(main, 'title_instance')
    start = layout.labels(title['child'])['ai_3']
    child, = layout.states(title['child'])[start].values()
    matrix = compose(compose(plate, title['matrix']), child['matrix'])
    band = Item(layout)
    band.clip('idle', (layout.draw(child['child'], f, matrix, {})
                       for f in range(len(layout.states(child['child'])))), loop=True)
    band.save(output/'band/lumen-ai-3.otanim', encoder)

    rankup = layout.instance(main, 'rankup_instance')
    sprite = rankup['child']
    rank = layout.instance(sprite, 'dani_rankup_efc_02_instance')['child']
    labels = list(layout.labels(sprite).items())
    matrix = compose(plate, rankup['matrix'])
    # Keep all ranks in one atlas. A shared idle keeps wearing the effect unobtrusive.
    item = Item(layout)
    item.clip('idle', [[]])
    for rank_name, rank_frame in layout.labels(rank).items():
        for i, (name, start) in enumerate(labels):
            end = labels[i+1][1] if i+1 < len(labels) else len(layout.states(sprite))
            if name == 'init':
                continue
            # A one-shot start reaches and holds the source's stopped wait frame.
            if name == 'rankup_start':
                end = layout.labels(sprite)['rankup_wait']+1
            item.clip(f'{rank_name}/{name}', (layout.draw(sprite, f, matrix, {rank: rank_frame})
                                               for f in range(start, end)))
    item.save(output/'icon/lumen-rankup.otanim', encoder)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('decoded', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory() as tmp:
        encoder = Path(tmp)/'encode'
        subprocess.run(['go', 'build', '-o', encoder, '.'], cwd=Path(__file__).resolve().parent, check=True)
        bake(args.decoded, args.output, encoder)
