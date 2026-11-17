package main

import (
	"bytes"
	"image"
	"image/color"
	"image/color/palette"
	"image/draw"
	"image/gif"
	"image/png"
	"math"
	"os"
	"path/filepath"

	"ourtaiko.org/spine2otanim/otanim"
)

// renderer draws .otanim frames the way a player should (premultiplied, back to front), so a
// converted file can be checked without the game. It reads the file back, so what it shows is
// what was written.
type renderer struct {
	f    *otanim.File
	tex  []float32 // premultiplied RGBA, 0..1
	w, h int
}

func newRenderer(f *otanim.File) (*renderer, error) {
	img, err := png.Decode(bytes.NewReader(f.Texture))
	if err != nil {
		return nil, err
	}
	b := img.Bounds()
	nrgba := image.NewNRGBA(b)
	draw.Draw(nrgba, b, img, b.Min, draw.Src)
	r := &renderer{f: f, w: b.Dx(), h: b.Dy(), tex: make([]float32, b.Dx()*b.Dy()*4)}
	for i := 0; i < len(nrgba.Pix); i += 4 {
		a := float32(nrgba.Pix[i+3]) / 255
		for c := 0; c < 3; c++ {
			v := float32(nrgba.Pix[i+c]) / 255
			if !f.Premultiplied {
				v *= a
			}
			r.tex[i+c] = v
		}
		r.tex[i+3] = a
	}
	return r, nil
}

func (r *renderer) sample(u, v float32) [4]float32 {
	x := u*float32(r.w) - 0.5
	y := v*float32(r.h) - 0.5
	x0, y0 := int(math.Floor(float64(x))), int(math.Floor(float64(y)))
	fx, fy := x-float32(x0), y-float32(y0)
	var out [4]float32
	for _, s := range [4]struct {
		dx, dy int
		w      float32
	}{{0, 0, (1 - fx) * (1 - fy)}, {1, 0, fx * (1 - fy)}, {0, 1, (1 - fx) * fy}, {1, 1, fx * fy}} {
		px := min(max(x0+s.dx, 0), r.w-1)
		py := min(max(y0+s.dy, 0), r.h-1)
		i := (py*r.w + px) * 4
		for c := 0; c < 4; c++ {
			out[c] += r.tex[i+c] * s.w
		}
	}
	return out
}

// draw paints a frame into a premultiplied buffer of size w×h showing the file's view.
func (r *renderer) draw(fr *otanim.Frame, w, h int, mirror bool) []float32 {
	buf := make([]float32, w*h*4)
	v := r.f.View
	vw, vh := v[2]-v[0], v[3]-v[1]
	scale := min(float32(w)/vw, float32(h)/vh)
	ox := (float32(w) - vw*scale) / 2
	oy := (float32(h) - vh*scale) / 2
	toScreen := func(x, y float32) (float32, float32) {
		sx := ox + (x-v[0])*scale
		if mirror {
			sx = float32(w) - sx
		}
		return sx, float32(h) - (oy + (y-v[1])*scale)
	}
	for _, d := range fr.Draws {
		p := &r.f.Parts[d.Part]
		col := [4]float32{float32(d.Color[0]) / 255, float32(d.Color[1]) / 255, float32(d.Color[2]) / 255, float32(d.Color[3]) / 255}
		n := p.VertexCount()
		sx, sy := make([]float32, n), make([]float32, n)
		for i := 0; i < n; i++ {
			sx[i], sy[i] = toScreen(d.Vertices[2*i], d.Vertices[2*i+1])
		}
		for t := 0; t+2 < len(p.Indices); t += 3 {
			i0, i1, i2 := int(p.Indices[t]), int(p.Indices[t+1]), int(p.Indices[t+2])
			r.triangle(buf, w, h, p, col, [3]int{i0, i1, i2}, sx, sy)
		}
	}
	return buf
}

func (r *renderer) triangle(buf []float32, w, h int, p *otanim.Part, col [4]float32, idx [3]int, sx, sy []float32) {
	x0, y0, x1, y1, x2, y2 := sx[idx[0]], sy[idx[0]], sx[idx[1]], sy[idx[1]], sx[idx[2]], sy[idx[2]]
	area := (x1-x0)*(y2-y0) - (x2-x0)*(y1-y0)
	if area == 0 {
		return
	}
	minX := max(0, int(math.Floor(float64(min(x0, x1, x2)))))
	maxX := min(w-1, int(math.Ceil(float64(max(x0, x1, x2)))))
	minY := max(0, int(math.Floor(float64(min(y0, y1, y2)))))
	maxY := min(h-1, int(math.Ceil(float64(max(y0, y1, y2)))))
	additive := p.Blend == otanim.BlendAdditive
	for y := minY; y <= maxY; y++ {
		for x := minX; x <= maxX; x++ {
			px, py := float32(x)+0.5, float32(y)+0.5
			w0 := ((x1-px)*(y2-py) - (x2-px)*(y1-py)) / area
			w1 := ((x2-px)*(y0-py) - (x0-px)*(y2-py)) / area
			w2 := 1 - w0 - w1
			if w0 < 0 || w1 < 0 || w2 < 0 {
				continue
			}
			u := w0*p.UVs[2*idx[0]] + w1*p.UVs[2*idx[1]] + w2*p.UVs[2*idx[2]]
			v := w0*p.UVs[2*idx[0]+1] + w1*p.UVs[2*idx[1]+1] + w2*p.UVs[2*idx[2]+1]
			s := r.sample(u, v)
			// Vertex colour multiplies straight colour; in premultiplied terms rgb by rgb·a.
			for c := 0; c < 3; c++ {
				s[c] *= col[c] * col[3]
			}
			s[3] *= col[3]
			i := (y*w + x) * 4
			srcA := s[3]
			if additive {
				srcA = 0
			}
			for c := 0; c < 3; c++ {
				buf[i+c] = s[c] + buf[i+c]*(1-srcA)
			}
			buf[i+3] = srcA + buf[i+3]*(1-srcA)
		}
	}
}

// flatten puts a premultiplied buffer on a background colour.
func flatten(buf []float32, w, h int, bg color.NRGBA) *image.NRGBA {
	img := image.NewNRGBA(image.Rect(0, 0, w, h))
	bgc := [3]float32{float32(bg.R) / 255, float32(bg.G) / 255, float32(bg.B) / 255}
	for i := 0; i < w*h; i++ {
		a := buf[i*4+3]
		for c := 0; c < 3; c++ {
			v := buf[i*4+c] + bgc[c]*(1-a)
			img.Pix[i*4+c] = uint8(min(max(v, 0), 1)*255 + 0.5)
		}
		img.Pix[i*4+3] = 255
	}
	return img
}

// writePreviews writes, for each clip, <clip>.png (its frames side by side, up to 12) and
// <clip>.gif (it playing, on a light background), each frame size×size.
func writePreviews(f *otanim.File, dir string, size int) error {
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return err
	}
	r, err := newRenderer(f)
	if err != nil {
		return err
	}
	bg := color.NRGBA{0xee, 0xe8, 0xdc, 0xff}
	for _, c := range f.Clips {
		step := max(1, (len(c.Frames)+11)/12)
		var sheetFrames []*image.NRGBA
		anim := &gif.GIF{}
		for i := range c.Frames {
			img := flatten(r.draw(&c.Frames[i], size, size, false), size, size, bg)
			if i%step == 0 {
				sheetFrames = append(sheetFrames, img)
			}
			pal := image.NewPaletted(img.Bounds(), palette.Plan9)
			draw.FloydSteinberg.Draw(pal, img.Bounds(), img, image.Point{})
			anim.Image = append(anim.Image, pal)
			anim.Delay = append(anim.Delay, int(math.Round(100/float64(c.FPS))))
		}
		sheet := image.NewNRGBA(image.Rect(0, 0, size*len(sheetFrames), size))
		for i, img := range sheetFrames {
			draw.Draw(sheet, image.Rect(i*size, 0, (i+1)*size, size), img, image.Point{}, draw.Src)
		}
		if err := writeImage(filepath.Join(dir, c.Name+".png"), func(b *bytes.Buffer) error { return png.Encode(b, sheet) }); err != nil {
			return err
		}
		if !c.Loop {
			anim.LoopCount = -1
		}
		if err := writeImage(filepath.Join(dir, c.Name+".gif"), func(b *bytes.Buffer) error { return gif.EncodeAll(b, anim) }); err != nil {
			return err
		}
	}
	return nil
}

func writeImage(path string, enc func(*bytes.Buffer) error) error {
	var b bytes.Buffer
	if err := enc(&b); err != nil {
		return err
	}
	return os.WriteFile(path, b.Bytes(), 0o644)
}
