package spine

import (
	"bufio"
	"bytes"
	"fmt"
	"strconv"
	"strings"
)

// AtlasPage is one texture of an atlas. Width and Height come from its "size:" line, or are 0
// when the atlas has none (older exports): the caller fills them from the image.
type AtlasPage struct {
	Name          string
	Width, Height int
	Premultiplied bool // "pma: true" (4.x) — 3.8 atlases do not say
}

// AtlasRegion is a packed picture. X, Y, Width, Height are where it is on the page (Width and
// Height before rotating: a rotated region takes Height×Width pixels on the page); the original
// picture was OrigW×OrigH with the packed part at OffsetX, OffsetY from its bottom left.
type AtlasRegion struct {
	Name                string
	Page                *AtlasPage
	X, Y, Width, Height int
	OrigW, OrigH        int
	OffsetX, OffsetY    int
	Rotate              bool
	Degrees             int
}

type Atlas struct {
	Pages   []*AtlasPage
	Regions map[string]*AtlasRegion
}

// ReadAtlas reads a libGDX/Spine text atlas.
func ReadAtlas(b []byte) (*Atlas, error) {
	a := &Atlas{Regions: map[string]*AtlasRegion{}}
	sc := bufio.NewScanner(bytes.NewReader(b))
	var page *AtlasPage
	var region *AtlasRegion
	line := 0
	ints := func(v string) ([]int, error) {
		parts := strings.Split(v, ",")
		out := make([]int, len(parts))
		for i, p := range parts {
			n, err := strconv.Atoi(strings.TrimSpace(p))
			if err != nil {
				return nil, fmt.Errorf("atlas line %d: %q is not a number", line, p)
			}
			out[i] = n
		}
		return out, nil
	}
	for sc.Scan() {
		line++
		raw := strings.TrimRight(sc.Text(), "\r")
		text := strings.TrimSpace(raw)
		if text == "" {
			page, region = nil, nil
			continue
		}
		key, value, isPair := strings.Cut(text, ":")
		key, value = strings.TrimSpace(key), strings.TrimSpace(value)
		switch {
		case page == nil:
			page = &AtlasPage{Name: text}
			a.Pages = append(a.Pages, page)
		case !isPair:
			region = &AtlasRegion{Name: text, Page: page, OffsetX: 0, OffsetY: 0}
			a.Regions[text] = region
		case region == nil: // a page's settings
			switch key {
			case "size":
				v, err := ints(value)
				if err != nil || len(v) != 2 {
					return nil, fmt.Errorf("atlas line %d: bad size", line)
				}
				page.Width, page.Height = v[0], v[1]
			case "pma":
				page.Premultiplied = value == "true"
			}
		default:
			switch key {
			case "rotate":
				switch value {
				case "true":
					region.Degrees = 90
				case "false":
					region.Degrees = 0
				default:
					n, err := strconv.Atoi(value)
					if err != nil {
						return nil, fmt.Errorf("atlas line %d: bad rotate", line)
					}
					region.Degrees = n
				}
				region.Rotate = region.Degrees == 90
				if region.Degrees != 0 && region.Degrees != 90 {
					return nil, fmt.Errorf("atlas line %d: region %q is rotated %d degrees; only 0 and 90 are supported", line, region.Name, region.Degrees)
				}
			case "xy", "size", "orig", "offset", "bounds", "offsets":
				v, err := ints(value)
				if err != nil {
					return nil, err
				}
				switch {
				case key == "xy" && len(v) == 2:
					region.X, region.Y = v[0], v[1]
				case key == "size" && len(v) == 2:
					region.Width, region.Height = v[0], v[1]
				case key == "orig" && len(v) == 2:
					region.OrigW, region.OrigH = v[0], v[1]
				case key == "offset" && len(v) == 2:
					region.OffsetX, region.OffsetY = v[0], v[1]
				case key == "bounds" && len(v) == 4:
					region.X, region.Y, region.Width, region.Height = v[0], v[1], v[2], v[3]
				case key == "offsets" && len(v) == 4:
					region.OffsetX, region.OffsetY, region.OrigW, region.OrigH = v[0], v[1], v[2], v[3]
				default:
					return nil, fmt.Errorf("atlas line %d: bad %s", line, key)
				}
			}
		}
	}
	for _, r := range a.Regions {
		if r.OrigW == 0 && r.OrigH == 0 {
			r.OrigW, r.OrigH = r.Width, r.Height
		}
	}
	return a, sc.Err()
}

// UV is a point on a page, 0..1 with v going down (image space).
type UV struct{ U, V float32 }

// RegionUVs are a region attachment's four corners (bottom left, top left, top right, bottom
// right of the picture as drawn) on the page.
func (r *AtlasRegion) RegionUVs() [4]UV {
	pw, ph := float32(r.Page.Width), float32(r.Page.Height)
	u, v := float32(r.X)/pw, float32(r.Y)/ph
	var u2, v2 float32
	if r.Rotate {
		u2, v2 = float32(r.X+r.Height)/pw, float32(r.Y+r.Width)/ph
		return [4]UV{{u2, v2}, {u, v2}, {u, v}, {u2, v}}
	}
	u2, v2 = float32(r.X+r.Width)/pw, float32(r.Y+r.Height)/ph
	return [4]UV{{u, v2}, {u, v}, {u2, v}, {u2, v2}}
}

// MeshUVs maps a mesh's region UVs (0..1 over the original picture, v down) onto the page.
func (r *AtlasRegion) MeshUVs(regionUVs []float32) []UV {
	pw, ph := float32(r.Page.Width), float32(r.Page.Height)
	u, v := float32(r.X)/pw, float32(r.Y)/ph
	out := make([]UV, len(regionUVs)/2)
	if r.Rotate {
		u -= float32(r.OrigH-r.OffsetY-r.Height) / pw
		v -= float32(r.OrigW-r.OffsetX-r.Width) / ph
		w, h := float32(r.OrigH)/pw, float32(r.OrigW)/ph
		for i := range out {
			out[i] = UV{u + regionUVs[2*i+1]*w, v + h - regionUVs[2*i]*h}
		}
		return out
	}
	u -= float32(r.OffsetX) / pw
	v -= float32(r.OrigH-r.OffsetY-r.Height) / ph
	w, h := float32(r.OrigW)/pw, float32(r.OrigH)/ph
	for i := range out {
		out[i] = UV{u + regionUVs[2*i]*w, v + regionUVs[2*i+1]*h}
	}
	return out
}

// RegionOffsets are a region attachment's four corners (as RegionUVs) in its bone's space.
func (r *AtlasRegion) RegionOffsets(a *Attachment) [4][2]float32 {
	rsx := a.Width / float32(r.OrigW) * a.ScaleX
	rsy := a.Height / float32(r.OrigH) * a.ScaleY
	lx := -a.Width/2*a.ScaleX + float32(r.OffsetX)*rsx
	ly := -a.Height/2*a.ScaleY + float32(r.OffsetY)*rsy
	lx2 := lx + float32(r.Width)*rsx
	ly2 := ly + float32(r.Height)*rsy
	cos, sin := cosDeg(a.Rotation), sinDeg(a.Rotation)
	pt := func(x, y float32) [2]float32 { return [2]float32{x*cos - y*sin + a.X, x*sin + y*cos + a.Y} }
	return [4][2]float32{pt(lx, ly), pt(lx, ly2), pt(lx2, ly2), pt(lx2, ly)}
}
