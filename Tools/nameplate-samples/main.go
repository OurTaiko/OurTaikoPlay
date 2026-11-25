// Writes the aurora band to <out>/band/aurora.otanim for Nameplate Preview.
// Usage: go run . <out>
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

func main() {
	if len(os.Args) != 2 {
		fmt.Fprintln(os.Stderr, "usage: platesamples <out dir>")
		os.Exit(2)
	}
	f := aurora()
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
