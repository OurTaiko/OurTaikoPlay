// Package otanim writes and reads .otanim files: baked 2D cut-out animation (one texture,
// textured triangle meshes whose vertices are stored per frame). The format is specified in
// OurTaikoLogs/OurTaikoPlay/Documentation/Otanim.md; this package is its reference.
package otanim

import (
	"bytes"
	"compress/flate"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"math"
)

const (
	Magic   = "OTAN"
	Version = 1

	FlagPremultiplied = 1 // the texture's colours are premultiplied by alpha

	ClipLoop = 1

	DrawDelta = 1 // the vertices are differences from this part's vertices in the previous frame
)

type Blend uint8

const (
	BlendNormal Blend = iota
	BlendAdditive
	BlendMultiply
	BlendScreen
)

// Part is a piece of the picture: triangles with fixed texture coordinates (0..1, v down from
// the image's top) whose vertex positions every frame gives.
type Part struct {
	Name    string
	Blend   Blend
	UVs     []float32 // u, v per vertex
	Indices []uint16
}

func (p *Part) VertexCount() int { return len(p.UVs) / 2 }

// Draw is one part drawn in a frame: its colour (straight RGBA, multiplying the texture) and
// its vertices (x, y in units, y up).
type Draw struct {
	Part     int
	Color    [4]uint8
	Vertices []float32
}

// Frame is what is drawn at one sample, back to front.
type Frame struct{ Draws []Draw }

type Clip struct {
	Name string
	FPS  float32
	Loop bool
	// LoopStart is the first frame a loop goes back to: the frames before it are an intro played
	// once (0: the whole clip loops). Ignored for a one-shot.
	LoopStart int
	Frames    []Frame
}

// Duration is how long the clip lasts the first time through: a loop's frames cover [0, n/fps)
// and it then wraps to LoopStart;
// first; a one-shot's last frame is its end.
func (c *Clip) Duration() float32 {
	n := len(c.Frames)
	if c.Loop {
		return float32(n) / c.FPS
	}
	if n == 0 {
		return 0
	}
	return float32(n-1) / c.FPS
}

type File struct {
	Name          string
	Premultiplied bool
	// View is the box the animation is shown in (units, y up): min x, min y, max x, max y. A
	// player fits it into the space it has, keeping its aspect.
	View    [4]float32
	Unit    float32 // vertex precision: positions are stored as multiples of Unit
	Texture []byte  // PNG
	Parts   []Part
	Clips   []Clip
}

func (f *File) Clip(name string) *Clip {
	for i := range f.Clips {
		if f.Clips[i].Name == name {
			return &f.Clips[i]
		}
	}
	return nil
}

type writer struct{ bytes.Buffer }

func (w *writer) u8(v uint8)    { w.WriteByte(v) }
func (w *writer) u16(v uint16)  { binary.Write(&w.Buffer, binary.LittleEndian, v) }
func (w *writer) i16(v int16)   { binary.Write(&w.Buffer, binary.LittleEndian, v) }
func (w *writer) u32(v uint32)  { binary.Write(&w.Buffer, binary.LittleEndian, v) }
func (w *writer) f32(v float32) { binary.Write(&w.Buffer, binary.LittleEndian, v) }
func (w *writer) str(s string) {
	w.u16(uint16(len(s)))
	w.WriteString(s)
}

func deflate(b []byte) []byte {
	var out bytes.Buffer
	z, _ := flate.NewWriter(&out, flate.BestCompression)
	z.Write(b)
	z.Close()
	return out.Bytes()
}

func chunk(out *bytes.Buffer, tag string, payload []byte) {
	out.WriteString(tag)
	binary.Write(out, binary.LittleEndian, uint32(len(payload)))
	out.Write(payload)
}

func quantize(v, unit float32) (int16, error) {
	q := math.Round(float64(v / unit))
	if q < math.MinInt16 || q > math.MaxInt16 {
		return 0, fmt.Errorf("a vertex at %.1f is out of range for a unit of %g (|x| ≤ %.0f); use a larger unit", v, unit, 32767*unit)
	}
	return int16(q), nil
}

// Encode writes the file.
func (f *File) Encode() ([]byte, error) {
	if len(f.Parts) > math.MaxUint16 || len(f.Clips) > math.MaxUint16 {
		return nil, errors.New("too many parts or clips")
	}
	if f.Unit <= 0 {
		return nil, errors.New("the unit must be positive")
	}
	var out bytes.Buffer
	out.WriteString(Magic)
	binary.Write(&out, binary.LittleEndian, uint16(Version))
	var flags uint16
	if f.Premultiplied {
		flags |= FlagPremultiplied
	}
	binary.Write(&out, binary.LittleEndian, flags)

	var info writer
	for _, v := range f.View {
		info.f32(v)
	}
	info.f32(f.Unit)
	info.u16(uint16(len(f.Parts)))
	info.u16(uint16(len(f.Clips)))
	info.str(f.Name)
	chunk(&out, "INFO", info.Bytes())
	chunk(&out, "TEXR", f.Texture)

	var parts writer
	for _, p := range f.Parts {
		if p.VertexCount() > math.MaxUint16 || len(p.Indices) > math.MaxUint16 {
			return nil, fmt.Errorf("part %q is too large", p.Name)
		}
		parts.str(p.Name)
		parts.u8(uint8(p.Blend))
		parts.u16(uint16(p.VertexCount()))
		parts.u16(uint16(len(p.Indices)))
		for _, uv := range p.UVs {
			parts.u16(uint16(math.Round(float64(clamp01(uv)) * 65535)))
		}
		for _, i := range p.Indices {
			parts.u16(i)
		}
	}
	chunk(&out, "PART", deflate(parts.Bytes()))

	for _, c := range f.Clips {
		var w writer
		w.str(c.Name)
		w.f32(c.FPS)
		var cflags uint8
		if c.Loop {
			cflags |= ClipLoop
		}
		w.u8(cflags)
		w.u32(uint32(len(c.Frames)))
		loopStart := 0
		if c.Loop {
			loopStart = c.LoopStart
		}
		if loopStart < 0 || (len(c.Frames) > 0 && loopStart >= len(c.Frames)) {
			return nil, fmt.Errorf("clip %q loops back to frame %d of %d", c.Name, loopStart, len(c.Frames))
		}
		w.u32(uint32(loopStart))
		prev := map[int][]int16{}
		for _, fr := range c.Frames {
			w.u16(uint16(len(fr.Draws)))
			now := map[int][]int16{}
			for _, d := range fr.Draws {
				if d.Part < 0 || d.Part >= len(f.Parts) {
					return nil, fmt.Errorf("clip %q draws part %d of %d", c.Name, d.Part, len(f.Parts))
				}
				if len(d.Vertices) != len(f.Parts[d.Part].UVs) {
					return nil, fmt.Errorf("clip %q: part %d has %d vertices, a frame gives %d", c.Name, d.Part, f.Parts[d.Part].VertexCount(), len(d.Vertices)/2)
				}
				q := make([]int16, len(d.Vertices))
				for i, v := range d.Vertices {
					var err error
					if q[i], err = quantize(v, f.Unit); err != nil {
						return nil, err
					}
				}
				now[d.Part] = q
				w.u16(uint16(d.Part))
				before, delta := prev[d.Part]
				var dflags uint8
				if delta {
					dflags |= DrawDelta
				}
				w.u8(dflags)
				w.Write(d.Color[:])
				for i, v := range q {
					if delta {
						v -= before[i] // wraps; the reader adds back with the same wrap
					}
					w.i16(v)
				}
			}
			prev = now
		}
		chunk(&out, "CLIP", deflate(w.Bytes()))
	}
	return out.Bytes(), nil
}

func clamp01(v float32) float32 {
	if v < 0 {
		return 0
	}
	if v > 1 {
		return 1
	}
	return v
}

type reader struct {
	b   []byte
	p   int
	err error
}

func (r *reader) take(n int) []byte {
	if r.err != nil || r.p+n > len(r.b) {
		r.err = io.ErrUnexpectedEOF
		return make([]byte, n)
	}
	s := r.b[r.p : r.p+n]
	r.p += n
	return s
}
func (r *reader) u8() uint8    { return r.take(1)[0] }
func (r *reader) u16() uint16  { return binary.LittleEndian.Uint16(r.take(2)) }
func (r *reader) u32() uint32  { return binary.LittleEndian.Uint32(r.take(4)) }
func (r *reader) f32() float32 { return math.Float32frombits(r.u32()) }
func (r *reader) str() string  { return string(r.take(int(r.u16()))) }

func inflate(b []byte) ([]byte, error) { return io.ReadAll(flate.NewReader(bytes.NewReader(b))) }

// Decode reads a file. Unknown chunks are skipped.
func Decode(b []byte) (*File, error) {
	if len(b) < 8 || string(b[:4]) != Magic {
		return nil, errors.New("not an otanim file")
	}
	if v := binary.LittleEndian.Uint16(b[4:]); v != Version {
		return nil, fmt.Errorf("otanim version %d is not supported", v)
	}
	f := &File{Premultiplied: binary.LittleEndian.Uint16(b[6:])&FlagPremultiplied != 0}
	var partCount, clipCount int
	haveInfo := false
	for p := 8; p < len(b); {
		if p+8 > len(b) {
			return nil, io.ErrUnexpectedEOF
		}
		tag := string(b[p : p+4])
		n := int(binary.LittleEndian.Uint32(b[p+4:]))
		p += 8
		if p+n > len(b) {
			return nil, io.ErrUnexpectedEOF
		}
		payload := b[p : p+n]
		p += n
		switch tag {
		case "INFO":
			r := &reader{b: payload}
			for i := range f.View {
				f.View[i] = r.f32()
			}
			f.Unit = r.f32()
			partCount, clipCount = int(r.u16()), int(r.u16())
			f.Name = r.str()
			if r.err != nil {
				return nil, r.err
			}
			haveInfo = true
		case "TEXR":
			f.Texture = append([]byte(nil), payload...)
		case "PART":
			raw, err := inflate(payload)
			if err != nil {
				return nil, err
			}
			r := &reader{b: raw}
			for i := 0; i < partCount && r.err == nil; i++ {
				part := Part{Name: r.str(), Blend: Blend(r.u8())}
				vc, ic := int(r.u16()), int(r.u16())
				part.UVs = make([]float32, vc*2)
				for j := range part.UVs {
					part.UVs[j] = float32(r.u16()) / 65535
				}
				part.Indices = make([]uint16, ic)
				for j := range part.Indices {
					part.Indices[j] = r.u16()
				}
				f.Parts = append(f.Parts, part)
			}
			if r.err != nil {
				return nil, r.err
			}
		case "CLIP":
			if !haveInfo || len(f.Parts) != partCount {
				return nil, errors.New("CLIP before INFO and PART")
			}
			raw, err := inflate(payload)
			if err != nil {
				return nil, err
			}
			r := &reader{b: raw}
			c := Clip{Name: r.str(), FPS: r.f32()}
			c.Loop = r.u8()&ClipLoop != 0
			frames := int(r.u32())
			c.LoopStart = int(r.u32())
			if frames > len(raw) || (frames > 0 && c.LoopStart >= frames) || (!c.Loop && c.LoopStart != 0) {
				return nil, errors.New("bad frame count or loop start")
			}
			prev := map[int][]int16{}
			for i := 0; i < frames && r.err == nil; i++ {
				var fr Frame
				now := map[int][]int16{}
				for j, n := 0, int(r.u16()); j < n && r.err == nil; j++ {
					d := Draw{Part: int(r.u16())}
					if d.Part >= len(f.Parts) {
						return nil, fmt.Errorf("clip %q draws part %d of %d", c.Name, d.Part, len(f.Parts))
					}
					flags := r.u8()
					copy(d.Color[:], r.take(4))
					q := make([]int16, len(f.Parts[d.Part].UVs))
					before := prev[d.Part]
					if flags&DrawDelta != 0 && before == nil {
						return nil, fmt.Errorf("clip %q frame %d: a delta for part %d, which the previous frame does not draw", c.Name, i, d.Part)
					}
					d.Vertices = make([]float32, len(q))
					for k := range q {
						q[k] = int16(r.u16())
						if flags&DrawDelta != 0 {
							q[k] += before[k]
						}
						d.Vertices[k] = float32(q[k]) * f.Unit
					}
					now[d.Part] = q
					fr.Draws = append(fr.Draws, d)
				}
				prev = now
				c.Frames = append(c.Frames, fr)
			}
			if r.err != nil {
				return nil, r.err
			}
			f.Clips = append(f.Clips, c)
		}
	}
	if !haveInfo || f.Texture == nil || len(f.Parts) != partCount || len(f.Clips) != clipCount {
		return nil, errors.New("the file is missing INFO, TEXR, PART or a CLIP")
	}
	return f, nil
}
