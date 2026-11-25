// Writes sample nameplate layer items (.otanim, view 0 0 408 96, clip "idle"), drawn from shapes
// here, to try the layers with: band/sakura, band/aurora, effect/sparkle, effect/shine, icon/star,
// icon/medal. Put them in the game's Nameplate folder (beside its songs; in the Editor,
// persistentDataPath) and pick them in OurTaiko/Nameplate Preview.
// usage: go run . <out dir>   (writes <out>/<slot>/<name>.otanim)
package main

import (
	"bytes"
	"fmt"
	"image"
	"image/color"
	"image/png"
	"math"
	"os"
	"path/filepath"

	"ourtaiko.org/otanim"
)

const (
	W, H     = 408, 96
	BandTop  = 96 - 56 // y (up) of the band's bottom edge
	Unit     = 1.0 / 16
	Supersam = 4
)

// paint renders a w x h picture where f gives straight RGBA (0..1) at a point (pixels, y down).
func paint(w, h int, f func(x, y float64) (r, g, b, a float64)) []byte {
	img := image.NewNRGBA(image.Rect(0, 0, w, h))
	for py := 0; py < h; py++ {
		for px := 0; px < w; px++ {
			var sr, sg, sb, sa float64
			for j := 0; j < Supersam; j++ {
				for i := 0; i < Supersam; i++ {
					x := float64(px) + (float64(i)+0.5)/Supersam
					y := float64(py) + (float64(j)+0.5)/Supersam
					r, g, b, a := f(x, y)
					sr += r * a
					sg += g * a
					sb += b * a
					sa += a
				}
			}
			n := float64(Supersam * Supersam)
			a := sa / n
			c := color.NRGBA{}
			if a > 0 {
				c = color.NRGBA{u8(sr / sa), u8(sg / sa), u8(sb / sa), u8(a)}
			}
			img.SetNRGBA(px, py, c)
		}
	}
	var buf bytes.Buffer
	if err := png.Encode(&buf, img); err != nil {
		panic(err)
	}
	return buf.Bytes()
}

func u8(v float64) uint8          { return uint8(math.Round(math.Max(0, math.Min(1, v)) * 255)) }
func mix(a, b, t float64) float64 { return a + (b-a)*t }
func smooth(e0, e1, x float64) float64 {
	t := math.Max(0, math.Min(1, (x-e0)/(e1-e0)))
	return t * t * (3 - 2*t)
}

// over composites straight colour (r,g,b,a) over (R,G,B,A).
func over(R, G, B, A, r, g, b, a float64) (float64, float64, float64, float64) {
	oa := a + A*(1-a)
	if oa == 0 {
		return 0, 0, 0, 0
	}
	return (r*a + R*A*(1-a)) / oa, (g*a + G*A*(1-a)) / oa, (b*a + B*A*(1-a)) / oa, oa
}

func hsv(h, s, v float64) (float64, float64, float64) {
	h = math.Mod(h, 1) * 6
	c := v * s
	x := c * (1 - math.Abs(math.Mod(h, 2)-1))
	var r, g, b float64
	switch int(h) {
	case 0:
		r, g, b = c, x, 0
	case 1:
		r, g, b = x, c, 0
	case 2:
		r, g, b = 0, c, x
	case 3:
		r, g, b = 0, x, c
	case 4:
		r, g, b = x, 0, c
	default:
		r, g, b = c, 0, x
	}
	m := v - c
	return r + m, g + m, b + m
}

// quad is a part covering the whole picture: corners top-left, top-right, bottom-right, bottom-left.
func quad(name string, blend otanim.Blend) otanim.Part {
	return otanim.Part{Name: name, Blend: blend, UVs: []float32{0, 0, 1, 0, 1, 1, 0, 1}, Indices: []uint16{0, 1, 2, 0, 2, 3}}
}

// rect gives the vertices of a quad part centred at (cx, cy) (units, y up), w x h, turned by a radians.
func rect(cx, cy, w, h, a float64) []float32 {
	c, s := math.Cos(a), math.Sin(a)
	pts := [][2]float64{{-w / 2, h / 2}, {w / 2, h / 2}, {w / 2, -h / 2}, {-w / 2, -h / 2}}
	out := make([]float32, 0, 8)
	for _, p := range pts {
		out = append(out, float32(cx+p[0]*c-p[1]*s), float32(cy+p[0]*s+p[1]*c))
	}
	return out
}

func file(name string, tex []byte, parts []otanim.Part, clip otanim.Clip) *otanim.File {
	clip.Name = "idle"
	clip.Loop = true
	return &otanim.File{Name: name, View: [4]float32{0, 0, W, H}, Unit: Unit, Texture: tex, Parts: parts, Clips: []otanim.Clip{clip}}
}

var opaque = [4]uint8{255, 255, 255, 255}

// The band's shape is the game's (a rounded strip); these fill the whole 408x56 strip and the
// nameplate's band mask cuts them to it.
func sakura() *otanim.File {
	petals := [][3]float64{{40, 14, 7}, {95, 40, 5}, {150, 18, 6}, {210, 42, 8}, {262, 12, 5}, {318, 36, 7}, {372, 16, 6}, {20, 44, 4}, {240, 26, 4}}
	tex := paint(408, 56, func(x, y float64) (float64, float64, float64, float64) {
		t := x / 408
		r, g, b, a := mix(1, 1, t), mix(0.55, 0.88, t), mix(0.72, 0.93, t), 1.0
		// A soft light along the top.
		hl := smooth(14, 0, y) * 0.35
		r, g, b = mix(r, 1, hl), mix(g, 1, hl), mix(b, 1, hl)
		for _, p := range petals {
			dx, dy := (x-p[0])/(p[2]*1.4), (y-p[1])/p[2]
			d := math.Hypot(dx, dy)
			k := smooth(1.0, 0.8, d) * 0.55
			r, g, b, a = over(r, g, b, a, 1, 0.97, 0.98, k)
		}
		return r, g, b, a
	})
	return file("band/sakura", tex, []otanim.Part{quad("band", otanim.BlendNormal)},
		otanim.Clip{FPS: 1, Frames: []otanim.Frame{{Draws: []otanim.Draw{{Part: 0, Color: opaque, Vertices: rect(204, BandTop+28, 408, 56, 0)}}}}})
}

// A rainbow drifting left, seamless every 408 pixels.
func aurora() *otanim.File {
	tex := paint(816, 56, func(x, y float64) (float64, float64, float64, float64) {
		r, g, b := hsv(x/408, 0.45, 1)
		hl := smooth(18, 0, y) * 0.4
		sh := smooth(36, 56, y) * 0.18
		return mix(r, 1, hl) * (1 - sh), mix(g, 1, hl) * (1 - sh), mix(b, 1, hl) * (1 - sh), 1
	})
	const frames = 120
	clip := otanim.Clip{FPS: 30}
	for i := 0; i < frames; i++ {
		x := 408 - 408*float64(i)/frames
		clip.Frames = append(clip.Frames, otanim.Frame{Draws: []otanim.Draw{{Part: 0, Color: opaque, Vertices: rect(x, BandTop+28, 816, 56, 0)}}})
	}
	return file("band/aurora", tex, []otanim.Part{quad("strip", otanim.BlendNormal)}, clip)
}

func star4(x, y, size float64) float64 {
	dx, dy := math.Abs(x), math.Abs(y)
	core := smooth(size*0.22, 0, math.Hypot(dx, dy))
	ray := smooth(size*0.06, 0, math.Min(dx, dy)) * smooth(size, 0, math.Max(dx, dy))
	glow := smooth(size*0.6, 0, math.Hypot(dx, dy)) * 0.35
	return math.Min(1, core+ray+glow)
}

func sparkle() *otanim.File {
	tex := paint(64, 64, func(x, y float64) (float64, float64, float64, float64) {
		a := star4(x-32, y-32, 32)
		return 1, 0.98, 0.85, a
	})
	spots := [][3]float64{{58, 82, 0}, {128, 52, 0.35}, {200, 86, 0.7}, {262, 50, 0.15}, {320, 80, 0.5}, {380, 56, 0.85}}
	parts := make([]otanim.Part, len(spots))
	for i := range parts {
		parts[i] = quad(fmt.Sprintf("sparkle%d", i), otanim.BlendAdditive)
	}
	const frames = 60
	clip := otanim.Clip{FPS: 30}
	for f := 0; f < frames; f++ {
		var fr otanim.Frame
		for i, s := range spots {
			p := math.Mod(float64(f)/frames+s[2], 1)
			// Each twinkles over the first 40% of its cycle.
			k := math.Sin(math.Min(1, p/0.4) * math.Pi)
			if p >= 0.4 || k <= 0 {
				continue
			}
			size := 42 * k
			fr.Draws = append(fr.Draws, otanim.Draw{Part: i, Color: [4]uint8{255, 255, 255, u8(k)}, Vertices: rect(s[0], s[1], size, size, p*2)})
		}
		clip.Frames = append(clip.Frames, fr)
	}
	return file("effect/sparkle", tex, parts, clip)
}

func shine() *otanim.File {
	tex := paint(64, 64, func(x, y float64) (float64, float64, float64, float64) {
		d := math.Abs(x-32) / 32
		return 1, 1, 1, smooth(1, 0, d) * 0.75
	})
	const frames = 90
	clip := otanim.Clip{FPS: 30}
	for f := 0; f < frames; f++ {
		var fr otanim.Frame
		if f < 36 {
			x := -60 + 530*float64(f)/35
			// A parallelogram leaning right: the bottom edge sits 24 units left of the top.
			v := []float32{
				float32(x - 18 + 12), BandTop + 56, float32(x + 18 + 12), BandTop + 56,
				float32(x + 18 - 12), BandTop, float32(x - 18 - 12), BandTop,
			}
			fr.Draws = []otanim.Draw{{Part: 0, Color: opaque, Vertices: v}}
		}
		clip.Frames = append(clip.Frames, fr)
	}
	return file("effect/shine", tex, []otanim.Part{quad("shine", otanim.BlendAdditive)}, clip)
}

// A five-pointed star's signed inside test: distance-ish value, positive inside.
func starShape(x, y, r float64) float64 {
	a := math.Atan2(x, -y)
	n := 5.0
	seg := math.Mod(a+math.Pi*2+math.Pi/n, 2*math.Pi/n) - math.Pi/n
	inner := r * 0.45
	// Radius of the star's edge at this angle: between the tip (r) and the notch (inner).
	t := math.Abs(seg) / (math.Pi / n)
	edge := mix(r, inner, t)
	return edge - math.Hypot(x, y)
}

func star() *otanim.File {
	tex := paint(96, 96, func(x, y float64) (float64, float64, float64, float64) {
		x, y = x-48, y-50
		outer := smooth(-1, 1, starShape(x, y, 46))
		inner := smooth(-1, 1, starShape(x, y, 38))
		shade := smooth(-30, 30, -x-y)
		r, g, b := mix(1, 1, shade), mix(0.68, 0.9, shade), mix(0.05, 0.35, shade)
		R, G, B, A := 0.25, 0.12, 0.02, outer
		return over(R, G, B, A, r, g, b, inner)
	})
	const frames = 60
	clip := otanim.Clip{FPS: 30}
	for f := 0; f < frames; f++ {
		p := 2 * math.Pi * float64(f) / frames
		size := 54 * (1 + 0.05*math.Sin(2*p))
		clip.Frames = append(clip.Frames, otanim.Frame{Draws: []otanim.Draw{{Part: 0, Color: opaque, Vertices: rect(362, BandTop+30+2*math.Sin(p), size, size, 0.14*math.Sin(p))}}})
	}
	return file("icon/star", tex, []otanim.Part{quad("star", otanim.BlendNormal)}, clip)
}

func medal() *otanim.File {
	tex := paint(64, 64, func(x, y float64) (float64, float64, float64, float64) {
		d := math.Hypot(x-32, y-32)
		edge := smooth(31, 29.5, d)
		ring := smooth(26, 24.5, d)
		face := smooth(21, 19.5, d)
		shade := smooth(-25, 25, -(x-32)-(y-32))
		R, G, B, A := 0.05, 0.1, 0.25, edge
		R, G, B, A = over(R, G, B, A, 1, 1, 1, ring)
		R, G, B, A = over(R, G, B, A, mix(0.1, 0.35, shade), mix(0.35, 0.65, shade), mix(0.8, 1, shade), face)
		// A small white star in the middle.
		s := smooth(-0.8, 0.8, starShape(x-32, y-33, 12))
		return over(R, G, B, A, 1, 1, 1, s)
	})
	return file("icon/medal", tex, []otanim.Part{quad("medal", otanim.BlendNormal)},
		otanim.Clip{FPS: 1, Frames: []otanim.Frame{{Draws: []otanim.Draw{{Part: 0, Color: opaque, Vertices: rect(364, BandTop+28, 44, 44, 0)}}}}})
}

func main() {
	if len(os.Args) != 2 {
		fmt.Fprintln(os.Stderr, "usage: platesamples <out dir>")
		os.Exit(2)
	}
	for _, f := range []*otanim.File{sakura(), aurora(), sparkle(), shine(), star(), medal()} {
		data, err := f.Encode()
		if err != nil {
			panic(err)
		}
		path := filepath.Join(os.Args[1], f.Name+".otanim")
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			panic(err)
		}
		if err := os.WriteFile(path, data, 0o644); err != nil {
			panic(err)
		}
		fmt.Printf("%s %d bytes\n", path, len(data))
	}
}
