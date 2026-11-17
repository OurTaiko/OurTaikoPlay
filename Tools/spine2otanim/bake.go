package main

import (
	"fmt"
	"math"
	"sort"

	"ourtaiko.org/spine2otanim/otanim"
	"ourtaiko.org/spine2otanim/spine"
)

// clipSpec is one clip to bake: its name in the .otanim and the Spine animations it plays in
// turn; when it loops, the last one loops after the others played once.
type clipSpec struct {
	Name string
	Play []string
	Loop bool
}

type baker struct {
	sk       *spine.Skeleton
	atlas    *spine.Atlas
	pose     *spine.Pose
	fps      float32
	parts    []otanim.Part
	partKeys map[string]int
	// pageOffset maps an atlas page to where it sits in the combined texture (v offset and
	// scale; pages are stacked top to bottom).
	pageV    map[*spine.AtlasPage][2]float32
	warnings map[string]bool
	minX     float32
	minY     float32
	maxX     float32
	maxY     float32
}

func (b *baker) warn(format string, args ...any) {
	msg := fmt.Sprintf(format, args...)
	if !b.warnings[msg] {
		b.warnings[msg] = true
	}
}

func (b *baker) part(slot *spine.Slot, a *spine.Attachment, region *spine.AtlasRegion) int {
	key := fmt.Sprintf("%d/%s/%p", slot.Data.Index, a.Name, a)
	if i, ok := b.partKeys[key]; ok {
		return i
	}
	var uvs []spine.UV
	var indices []uint16
	if a.Kind == spine.KindRegion {
		r := region.RegionUVs()
		uvs = r[:]
		indices = []uint16{0, 1, 2, 2, 3, 0}
	} else {
		m := a.Mesh()
		uvs = region.MeshUVs(m.RegionUVs)
		indices = append([]uint16(nil), m.Triangles...)
	}
	pv := b.pageV[region.Page]
	p := otanim.Part{Name: slot.Data.Name + "/" + a.Name, Blend: otanim.Blend(slot.Data.Blend), Indices: indices}
	for _, uv := range uvs {
		p.UVs = append(p.UVs, uv.U, pv[0]+uv.V*pv[1])
	}
	b.parts = append(b.parts, p)
	b.partKeys[key] = len(b.parts) - 1
	return len(b.parts) - 1
}

func to8(v float32) uint8 {
	if v <= 0 {
		return 0
	}
	if v >= 1 {
		return 255
	}
	return uint8(math.Round(float64(v) * 255))
}

func (b *baker) frame(anim *spine.Animation, t float32) otanim.Frame {
	b.pose.Apply(anim, t)
	var fr otanim.Frame
	for _, s := range b.pose.DrawOrder {
		a := s.Attachment
		if a == nil {
			continue
		}
		switch a.Kind {
		case spine.KindRegion, spine.KindMesh, spine.KindLinkedMesh:
		case spine.KindClipping:
			b.warn("slot %q uses a clipping attachment, which is not baked: what it clips is drawn whole", s.Data.Name)
			continue
		default:
			continue
		}
		if s.Data.Dark != nil {
			b.warn("slot %q has a dark (tint black) colour, which otanim does not carry", s.Data.Name)
		}
		region := b.atlas.Regions[a.Path]
		if region == nil {
			b.warn("attachment %q: the atlas has no region %q; it is left out", a.Name, a.Path)
			continue
		}
		c := s.Color
		ac := a.Color
		col := [4]uint8{to8(c.R * ac.R), to8(c.G * ac.G), to8(c.B * ac.B), to8(c.A * ac.A)}
		if col[3] == 0 {
			continue
		}
		if s.Data.Blend == spine.BlendMultiply || s.Data.Blend == spine.BlendScreen {
			b.warn("slot %q blends %v, which a player may draw as normal", s.Data.Name, []string{"normal", "additive", "multiply", "screen"}[s.Data.Blend])
		}
		v := b.pose.WorldVertices(s, region)
		for i := 0; i < len(v); i += 2 {
			b.minX = min(b.minX, v[i])
			b.maxX = max(b.maxX, v[i])
			b.minY = min(b.minY, v[i+1])
			b.maxY = max(b.maxY, v[i+1])
		}
		fr.Draws = append(fr.Draws, otanim.Draw{Part: b.part(s, a, region), Color: col, Vertices: v})
	}
	return fr
}

func (b *baker) clip(spec clipSpec) (otanim.Clip, error) {
	c := otanim.Clip{Name: spec.Name, Loop: spec.Loop, FPS: b.fps}
	for i, name := range spec.Play {
		anim := b.sk.FindAnimation(name)
		if anim == nil {
			return otanim.Clip{}, fmt.Errorf("the skeleton has no animation %q", name)
		}
		last := i == len(spec.Play)-1
		if last && spec.Loop {
			c.LoopStart = len(c.Frames)
		}
		d := anim.Duration
		if d <= 0 {
			c.Frames = append(c.Frames, b.frame(anim, 0))
			continue
		}
		// Whole frames over the animation: each lasts d/n, close to 1/fps. Alone, a clip's rate is
		// nudged so it lasts exactly d; animations in turn share the requested rate.
		n := max(1, int(math.Round(float64(d*b.fps))))
		if len(spec.Play) == 1 {
			c.FPS = float32(n) / d
		}
		end := n // a loop's or an intro's end is the next frame's start
		if last && !spec.Loop {
			end = n + 1 // a one-shot keeps its last pose
		}
		for k := 0; k < end; k++ {
			c.Frames = append(c.Frames, b.frame(anim, min(d, float32(k)*d/float32(n))))
		}
	}
	return c, nil
}

func (b *baker) sortedWarnings() []string {
	out := make([]string, 0, len(b.warnings))
	for w := range b.warnings {
		out = append(out, w)
	}
	sort.Strings(out)
	return out
}
