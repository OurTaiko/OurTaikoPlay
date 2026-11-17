package main

import (
	"bytes"
	"fmt"
	"image"
	"image/draw"
	"image/png"
	"math"
	"os"
	"path/filepath"
	"sort"

	"ourtaiko.org/spine2otanim/otanim"
	"ourtaiko.org/spine2otanim/spine"
)

// loadPages reads every atlas page (from dir), fills in page sizes the atlas does not give, and
// makes the single texture an .otanim carries: the one page as it is, or the pages stacked top
// to bottom. It answers the PNG and each page's (v offset, v scale) in it.
func loadPages(atlas *spine.Atlas, dir string) ([]byte, map[*spine.AtlasPage][2]float32, error) {
	pageV := map[*spine.AtlasPage][2]float32{}
	if len(atlas.Pages) == 1 {
		p := atlas.Pages[0]
		raw, err := os.ReadFile(filepath.Join(dir, p.Name))
		if err != nil {
			return nil, nil, err
		}
		cfg, err := png.DecodeConfig(bytes.NewReader(raw))
		if err != nil {
			return nil, nil, fmt.Errorf("%s: %w", p.Name, err)
		}
		if p.Width == 0 {
			p.Width, p.Height = cfg.Width, cfg.Height
		}
		if p.Width != cfg.Width || p.Height != cfg.Height {
			return nil, nil, fmt.Errorf("%s is %dx%d, the atlas says %dx%d", p.Name, cfg.Width, cfg.Height, p.Width, p.Height)
		}
		pageV[p] = [2]float32{0, 1}
		return raw, pageV, nil
	}
	var imgs []image.Image
	w, h := 0, 0
	for _, p := range atlas.Pages {
		raw, err := os.ReadFile(filepath.Join(dir, p.Name))
		if err != nil {
			return nil, nil, err
		}
		img, err := png.Decode(bytes.NewReader(raw))
		if err != nil {
			return nil, nil, fmt.Errorf("%s: %w", p.Name, err)
		}
		if p.Width == 0 {
			p.Width, p.Height = img.Bounds().Dx(), img.Bounds().Dy()
		}
		imgs = append(imgs, img)
		w = max(w, p.Width)
		h += p.Height
	}
	out := image.NewNRGBA(image.Rect(0, 0, w, h))
	y := 0
	for i, p := range atlas.Pages {
		draw.Draw(out, image.Rect(0, y, p.Width, y+p.Height), imgs[i], imgs[i].Bounds().Min, draw.Src)
		pageV[p] = [2]float32{float32(y) / float32(h), float32(p.Height) / float32(h)}
		y += p.Height
	}
	// A region's u was relative to its own page's width.
	for _, p := range atlas.Pages {
		p.Width = w
	}
	var buf bytes.Buffer
	if err := png.Encode(&buf, out); err != nil {
		return nil, nil, err
	}
	return buf.Bytes(), pageV, nil
}

// looksPremultiplied guesses whether a texture's colours are premultiplied: in one that is, no
// colour channel is above alpha. A straight-alpha texture almost always has some.
func looksPremultiplied(raw []byte) (bool, error) {
	img, err := png.Decode(bytes.NewReader(raw))
	if err != nil {
		return false, err
	}
	nrgba := image.NewNRGBA(img.Bounds())
	draw.Draw(nrgba, img.Bounds(), img, img.Bounds().Min, draw.Src)
	translucent, over := 0, 0
	for i := 0; i < len(nrgba.Pix); i += 4 {
		r, g, b, a := nrgba.Pix[i], nrgba.Pix[i+1], nrgba.Pix[i+2], nrgba.Pix[i+3]
		if a == 255 {
			continue
		}
		translucent++
		if int(max(r, g, b)) > int(a)+1 {
			over++
		}
	}
	return translucent > 0 && over*1000 < translucent, nil
}

// repack keeps only the texture the parts use: each part's pixels (with a margin copied from
// around them, so filtering at the edges sees what it saw before) shelf-packed into a new
// image, the parts' coordinates moved to match. Parts using the same pixels share them.
func repack(raw []byte, parts []otanim.Part, margin int) ([]byte, error) {
	src, err := decodeNRGBA(raw)
	if err != nil {
		return nil, err
	}
	sw, sh := src.Bounds().Dx(), src.Bounds().Dy()
	type box struct {
		r      image.Rectangle // source, margin included
		dx, dy int             // where it goes
	}
	var boxes []*box
	byRect := map[image.Rectangle]*box{}
	owner := make([]*box, len(parts))
	for i, p := range parts {
		minU, minV, maxU, maxV := float32(1), float32(1), float32(0), float32(0)
		for j := 0; j < len(p.UVs); j += 2 {
			minU, maxU = min(minU, p.UVs[j]), max(maxU, p.UVs[j])
			minV, maxV = min(minV, p.UVs[j+1]), max(maxV, p.UVs[j+1])
		}
		r := image.Rect(int(math.Floor(float64(minU)*float64(sw)))-margin, int(math.Floor(float64(minV)*float64(sh)))-margin,
			int(math.Ceil(float64(maxU)*float64(sw)))+margin, int(math.Ceil(float64(maxV)*float64(sh)))+margin).Intersect(src.Bounds())
		b := byRect[r]
		if b == nil {
			b = &box{r: r}
			byRect[r] = b
			boxes = append(boxes, b)
		}
		owner[i] = b
	}
	// Boxes that overlap in the source are merged, so shared pixels are kept once.
	for merged := true; merged; {
		merged = false
		for i := 0; i < len(boxes) && !merged; i++ {
			for j := i + 1; j < len(boxes); j++ {
				if boxes[i].r.Overlaps(boxes[j].r) {
					gone := boxes[j]
					boxes[i].r = boxes[i].r.Union(gone.r)
					for k := range owner {
						if owner[k] == gone {
							owner[k] = boxes[i]
						}
					}
					boxes = append(boxes[:j], boxes[j+1:]...)
					merged = true
					break
				}
			}
		}
	}
	area := 0
	widest := 0
	for _, b := range boxes {
		area += b.r.Dx() * b.r.Dy()
		widest = max(widest, b.r.Dx())
	}
	width := max(widest, int(math.Ceil(math.Sqrt(float64(area))*1.15)))
	order := append([]*box(nil), boxes...)
	sort.Slice(order, func(i, j int) bool { return order[i].r.Dy() > order[j].r.Dy() })
	x, y, shelf := 0, 0, 0
	for _, b := range order {
		if x+b.r.Dx() > width {
			x, y, shelf = 0, y+shelf, 0
		}
		b.dx, b.dy = x, y
		x += b.r.Dx()
		shelf = max(shelf, b.r.Dy())
	}
	height := y + shelf
	if width*height >= sw*sh {
		return raw, nil // nothing gained
	}
	out := image.NewNRGBA(image.Rect(0, 0, width, height))
	for _, b := range boxes {
		draw.Draw(out, image.Rect(b.dx, b.dy, b.dx+b.r.Dx(), b.dy+b.r.Dy()), src, b.r.Min, draw.Src)
	}
	for i := range parts {
		b := owner[i]
		uvs := parts[i].UVs
		for j := 0; j < len(uvs); j += 2 {
			px := uvs[j]*float32(sw) - float32(b.r.Min.X) + float32(b.dx)
			py := uvs[j+1]*float32(sh) - float32(b.r.Min.Y) + float32(b.dy)
			uvs[j], uvs[j+1] = px/float32(width), py/float32(height)
		}
	}
	return encodePNG(out)
}

// shrink scales the texture down (averaging) so neither side is over limit. Coordinates are
// 0..1, so the parts need no change.
func shrink(raw []byte, limit int) ([]byte, error) {
	src, err := decodeNRGBA(raw)
	if err != nil {
		return nil, err
	}
	sw, sh := src.Bounds().Dx(), src.Bounds().Dy()
	if limit <= 0 || max(sw, sh) <= limit {
		return raw, nil
	}
	s := float64(limit) / float64(max(sw, sh))
	dw, dh := max(1, int(math.Round(float64(sw)*s))), max(1, int(math.Round(float64(sh)*s)))
	out := image.NewNRGBA(image.Rect(0, 0, dw, dh))
	for y := 0; y < dh; y++ {
		y0, y1 := y*sh/dh, max(y*sh/dh+1, (y+1)*sh/dh)
		for x := 0; x < dw; x++ {
			x0, x1 := x*sw/dw, max(x*sw/dw+1, (x+1)*sw/dw)
			// Average premultiplied, so transparent pixels do not darken edges.
			var r, g, b, a float64
			for yy := y0; yy < y1; yy++ {
				for xx := x0; xx < x1; xx++ {
					i := yy*src.Stride + xx*4
					pa := float64(src.Pix[i+3])
					r += float64(src.Pix[i]) * pa
					g += float64(src.Pix[i+1]) * pa
					b += float64(src.Pix[i+2]) * pa
					a += pa
				}
			}
			n := float64((x1 - x0) * (y1 - y0))
			i := y*out.Stride + x*4
			if a > 0 {
				out.Pix[i], out.Pix[i+1], out.Pix[i+2] = uint8(r/a+0.5), uint8(g/a+0.5), uint8(b/a+0.5)
			}
			out.Pix[i+3] = uint8(a/n + 0.5)
		}
	}
	return encodePNG(out)
}

func decodeNRGBA(raw []byte) (*image.NRGBA, error) {
	img, err := png.Decode(bytes.NewReader(raw))
	if err != nil {
		return nil, err
	}
	out := image.NewNRGBA(image.Rect(0, 0, img.Bounds().Dx(), img.Bounds().Dy()))
	draw.Draw(out, out.Bounds(), img, img.Bounds().Min, draw.Src)
	return out, nil
}

func encodePNG(img image.Image) ([]byte, error) {
	var buf bytes.Buffer
	e := png.Encoder{CompressionLevel: png.BestCompression}
	if err := e.Encode(&buf, img); err != nil {
		return nil, err
	}
	return buf.Bytes(), nil
}
