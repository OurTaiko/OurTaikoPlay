package spine

import (
	"math"
	"testing"
)

func TestCurves(t *testing.T) {
	linear := Curve{}
	if linear.Percent(0.3) != 0.3 {
		t.Fatal("linear")
	}
	stepped := Curve{Kind: 1}
	if stepped.Percent(0.9) != 0 {
		t.Fatal("stepped")
	}
	// A Bézier with straight handles is (almost) linear; an ease-in starts slow.
	straight := bezierCurve(0.25, 0.25, 0.75, 0.75)
	if p := straight.Percent(0.5); math.Abs(float64(p)-0.5) > 0.01 {
		t.Fatalf("straight bezier at 0.5: %v", p)
	}
	easeIn := bezierCurve(0.5, 0, 1, 1)
	if p := easeIn.Percent(0.3); p > 0.2 {
		t.Fatalf("ease-in at 0.3: %v", p)
	}
	if easeIn.Percent(1) != 1 || easeIn.Percent(0) != 0 {
		t.Fatal("bezier ends")
	}
}

func TestAtlasRegions(t *testing.T) {
	a, err := ReadAtlas([]byte("\npage.png\nsize: 100,50\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\nflat\n  rotate: false\n  xy: 10, 20\n  size: 30, 10\n  orig: 40, 20\n  offset: 5, 4\n  index: -1\nturned\n  rotate: true\n  xy: 50, 0\n  size: 20, 10\n  orig: 20, 10\n  offset: 0, 0\n  index: -1\n"))
	if err != nil {
		t.Fatal(err)
	}
	flat := a.Regions["flat"]
	uv := flat.RegionUVs()
	if uv[1] != (UV{0.1, 0.4}) || uv[3] != (UV{0.4, 0.6}) {
		t.Fatalf("flat corners %v", uv)
	}
	// The mesh's (0,0)..(1,1) is the original picture, of which the packed part is 30×10 at (5, 4)
	// from its bottom left: u starts 5 px left of the region, v 6 px above it.
	m := flat.MeshUVs([]float32{0, 0, 1, 1})
	if math.Abs(float64(m[0].U-0.05)) > 1e-6 || math.Abs(float64(m[0].V-0.28)) > 1e-6 || math.Abs(float64(m[1].U-0.45)) > 1e-6 || math.Abs(float64(m[1].V-0.68)) > 1e-6 {
		t.Fatalf("flat mesh uvs %v", m)
	}
	turned := a.Regions["turned"]
	if !turned.Rotate {
		t.Fatal("rotate")
	}
	// Rotated: the picture's top left is the page region's top right.
	if m := turned.MeshUVs([]float32{0, 0}); m[0] != (UV{0.5, 0.4}) {
		t.Fatalf("turned top left %v", m[0])
	}
}

// A two-bone chain: a child of a rotated parent lands where the parent's rotation puts it, and a
// rotate key moves the child.
func TestPose(t *testing.T) {
	root := &BoneData{Index: 0, Name: "root", ScaleX: 1, ScaleY: 1}
	arm := &BoneData{Index: 1, Name: "arm", Parent: root, Rotation: 90, ScaleX: 1, ScaleY: 1}
	hand := &BoneData{Index: 2, Name: "hand", Parent: arm, X: 10, ScaleX: 1, ScaleY: 1}
	sk := &Skeleton{Bones: []*BoneData{root, arm, hand}, DefaultSkin: &Skin{Name: "default"}}
	anim := &Animation{Name: "wave", Duration: 1, Timelines: []*Timeline{
		{Kind: TLRotate, Index: 1, Keys: []Key{{Time: 0, V: []float32{0}}, {Time: 1, V: []float32{-90}}}},
	}}
	p, err := NewPose(sk, nil)
	if err != nil {
		t.Fatal(err)
	}
	p.Apply(anim, 0)
	h := p.Bones[2]
	if math.Abs(float64(h.WorldX)) > 1e-4 || math.Abs(float64(h.WorldY-10)) > 1e-4 {
		t.Fatalf("hand at %v,%v; want 0,10", h.WorldX, h.WorldY)
	}
	p.Apply(anim, 1)
	if math.Abs(float64(h.WorldX-10)) > 1e-4 || math.Abs(float64(h.WorldY)) > 1e-4 {
		t.Fatalf("hand at %v,%v after the key; want 10,0", h.WorldX, h.WorldY)
	}
}
