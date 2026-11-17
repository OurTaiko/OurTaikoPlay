package otanim

import (
	"bytes"
	"image"
	"image/png"
	"testing"
)

func tinyPNG(t *testing.T) []byte {
	var b bytes.Buffer
	if err := png.Encode(&b, image.NewNRGBA(image.Rect(0, 0, 2, 2))); err != nil {
		t.Fatal(err)
	}
	return b.Bytes()
}

func TestRoundTrip(t *testing.T) {
	square := func(x float32) []float32 { return []float32{x, 0, x, 10, x + 10, 10, x + 10, 0} }
	f := &File{
		Name: "t", Premultiplied: true, View: [4]float32{-1, -1, 30, 11}, Unit: 1.0 / 16, Texture: tinyPNG(t),
		Parts: []Part{{Name: "a", UVs: []float32{0, 1, 0, 0, 1, 0, 1, 1}, Indices: []uint16{0, 1, 2, 2, 3, 0}},
			{Name: "b", Blend: BlendAdditive, UVs: []float32{0, 1, 0, 0, 1, 0, 1, 1}, Indices: []uint16{0, 1, 2}}},
		Clips: []Clip{
			{Name: "idle", FPS: 30, Loop: true, LoopStart: 1, Frames: []Frame{
				{Draws: []Draw{{Part: 0, Color: [4]uint8{255, 255, 255, 255}, Vertices: square(0)}}},
				{Draws: []Draw{{Part: 1, Color: [4]uint8{1, 2, 3, 4}, Vertices: square(5)}, {Part: 0, Color: [4]uint8{9, 9, 9, 9}, Vertices: square(1.5)}}},
				{Draws: []Draw{{Part: 0, Color: [4]uint8{255, 255, 255, 255}, Vertices: square(-2000)}}},
			}},
			{Name: "once", FPS: 10, Frames: []Frame{{}}},
		},
	}
	b, err := f.Encode()
	if err != nil {
		t.Fatal(err)
	}
	g, err := Decode(b)
	if err != nil {
		t.Fatal(err)
	}
	if g.Name != "t" || !g.Premultiplied || g.View != f.View || len(g.Parts) != 2 || g.Parts[1].Blend != BlendAdditive || len(g.Clips) != 2 {
		t.Fatalf("header or parts differ: %+v", g)
	}
	for i, fr := range f.Clips[0].Frames {
		got := g.Clips[0].Frames[i]
		if len(got.Draws) != len(fr.Draws) {
			t.Fatalf("frame %d: %d draws, want %d", i, len(got.Draws), len(fr.Draws))
		}
		for j, d := range fr.Draws {
			e := got.Draws[j]
			if e.Part != d.Part || e.Color != d.Color {
				t.Fatalf("frame %d draw %d: %+v, want %+v", i, j, e, d)
			}
			for k := range d.Vertices {
				if diff := e.Vertices[k] - d.Vertices[k]; diff > f.Unit/2 || diff < -f.Unit/2 {
					t.Fatalf("frame %d draw %d vertex %d: %v, want %v", i, j, k, e.Vertices[k], d.Vertices[k])
				}
			}
		}
	}
	if g.Clips[0].LoopStart != 1 || g.Clips[1].LoopStart != 0 {
		t.Fatal("loop start differs")
	}
	if !g.Clips[0].Loop || g.Clips[1].Loop || g.Clips[0].Duration() != 0.1 || g.Clips[1].Duration() != 0 {
		t.Fatal("clip timing differs")
	}
}

func TestOutOfRange(t *testing.T) {
	f := &File{Unit: 1.0 / 16, View: [4]float32{0, 0, 1, 1}, Texture: tinyPNG(t),
		Parts: []Part{{UVs: []float32{0, 0, 1, 0, 1, 1}, Indices: []uint16{0, 1, 2}}},
		Clips: []Clip{{Name: "x", FPS: 1, Frames: []Frame{{Draws: []Draw{{Vertices: []float32{0, 0, 3000, 0, 0, 0}}}}}}}}
	if _, err := f.Encode(); err == nil {
		t.Fatal("a vertex beyond the unit's range was written")
	}
}
