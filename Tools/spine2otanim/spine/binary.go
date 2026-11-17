package spine

import (
	"encoding/binary"
	"errors"
	"fmt"
	"math"
	"strings"
)

type reader struct {
	b       []byte
	p       int
	strings []string
	err     error
}

var errShort = errors.New("unexpected end of the skeleton file")

func (r *reader) need(n int) bool {
	if r.err != nil {
		return false
	}
	if r.p+n > len(r.b) {
		r.err = errShort
		return false
	}
	return true
}

func (r *reader) byte_() byte {
	if !r.need(1) {
		return 0
	}
	v := r.b[r.p]
	r.p++
	return v
}

func (r *reader) bool_() bool { return r.byte_() != 0 }

func (r *reader) int32_() int32 {
	if !r.need(4) {
		return 0
	}
	v := binary.BigEndian.Uint32(r.b[r.p:])
	r.p += 4
	return int32(v)
}

func (r *reader) short() uint16 {
	if !r.need(2) {
		return 0
	}
	v := binary.BigEndian.Uint16(r.b[r.p:])
	r.p += 2
	return v
}

func (r *reader) float() float32 { return math.Float32frombits(uint32(r.int32_())) }

// varint reads Spine's variable-length int: 7 bits a byte, low first; optimizePositive false
// is zig-zag.
func (r *reader) varint(optimizePositive bool) int {
	var result uint32
	for shift := 0; shift < 35; shift += 7 {
		b := r.byte_()
		result |= uint32(b&0x7f) << shift
		if b&0x80 == 0 {
			break
		}
	}
	if !optimizePositive {
		return int(int32(result>>1) ^ -int32(result&1))
	}
	return int(int32(result))
}

func (r *reader) count() int {
	n := r.varint(true)
	if n < 0 || n > len(r.b) {
		if r.err == nil {
			r.err = fmt.Errorf("bad count %d at byte %d", n, r.p)
		}
		return 0
	}
	return n
}

// str reads a length-prefixed string: 0 is null, 1 is empty.
func (r *reader) str() (string, bool) {
	n := r.count()
	if n == 0 {
		return "", false
	}
	n--
	if !r.need(n) {
		return "", false
	}
	s := string(r.b[r.p : r.p+n])
	r.p += n
	return s, true
}

func (r *reader) s() string { s, _ := r.str(); return s }

func (r *reader) ref() (string, bool) {
	i := r.varint(true)
	if i == 0 {
		return "", false
	}
	if i-1 >= len(r.strings) {
		if r.err == nil {
			r.err = fmt.Errorf("bad string reference %d", i)
		}
		return "", false
	}
	return r.strings[i-1], true
}

func (r *reader) floats(n int) []float32 {
	out := make([]float32, n)
	for i := range out {
		out[i] = r.float()
	}
	return out
}

func (r *reader) shorts() []uint16 {
	n := r.count()
	out := make([]uint16, n)
	for i := range out {
		out[i] = r.short()
	}
	return out
}

// ReadBinary reads a Spine 3.8 binary skeleton.
func ReadBinary(b []byte) (*Skeleton, error) {
	r := &reader{b: b}
	sk := &Skeleton{}
	sk.Hash = r.s()
	sk.Version = r.s()
	if !strings.HasPrefix(sk.Version, "3.8") {
		return nil, fmt.Errorf("this is Spine %q; only 3.8 binary skeletons are read", sk.Version)
	}
	sk.X, sk.Y, sk.Width, sk.Height = r.float(), r.float(), r.float(), r.float()
	nonessential := r.bool_()
	if nonessential {
		sk.FPS = r.float()
		r.s()
		r.s()
	}
	r.strings = make([]string, r.count())
	for i := range r.strings {
		r.strings[i] = r.s()
	}

	sk.Bones = make([]*BoneData, r.count())
	for i := range sk.Bones {
		d := &BoneData{Index: i, Name: r.s()}
		if i > 0 {
			d.Parent = boneAt(r, sk, r.varint(true))
		}
		d.Rotation, d.X, d.Y = r.float(), r.float(), r.float()
		d.ScaleX, d.ScaleY, d.ShearX, d.ShearY = r.float(), r.float(), r.float(), r.float()
		d.Length = r.float()
		d.Transform = TransformMode(r.varint(true))
		d.SkinRequired = r.bool_()
		if nonessential {
			r.int32_()
		}
		sk.Bones[i] = d
	}

	sk.Slots = make([]*SlotData, r.count())
	for i := range sk.Slots {
		d := &SlotData{Index: i, Name: r.s()}
		d.Bone = boneAt(r, sk, r.varint(true))
		d.Color = colorFromRGBA8888(uint32(r.int32_()))
		if dark := r.int32_(); dark != -1 {
			c := colorFromRGBA8888(uint32(dark))
			d.Dark = &c
		}
		d.AttachmentName, _ = r.ref()
		d.Blend = BlendMode(r.varint(true))
		sk.Slots[i] = d
	}

	sk.Ik = make([]*IkData, r.count())
	for i := range sk.Ik {
		d := &IkData{Name: r.s(), Order: r.varint(true), SkinRequired: r.bool_()}
		d.Bones = make([]*BoneData, r.count())
		for j := range d.Bones {
			d.Bones[j] = boneAt(r, sk, r.varint(true))
		}
		d.Target = boneAt(r, sk, r.varint(true))
		d.Mix, d.Softness = r.float(), r.float()
		d.BendDirection = int(int8(r.byte_()))
		d.Compress, d.Stretch, d.Uniform = r.bool_(), r.bool_(), r.bool_()
		sk.Ik[i] = d
	}

	sk.Transforms = make([]*TransformData, r.count())
	for i := range sk.Transforms {
		d := &TransformData{Name: r.s(), Order: r.varint(true), SkinRequired: r.bool_()}
		d.Bones = make([]*BoneData, r.count())
		for j := range d.Bones {
			d.Bones[j] = boneAt(r, sk, r.varint(true))
		}
		d.Target = boneAt(r, sk, r.varint(true))
		d.Local, d.Relative = r.bool_(), r.bool_()
		d.OffsetRotation, d.OffsetX, d.OffsetY = r.float(), r.float(), r.float()
		d.OffsetScaleX, d.OffsetScaleY, d.OffsetShearY = r.float(), r.float(), r.float()
		d.RotateMix, d.TranslateMix, d.ScaleMix, d.ShearMix = r.float(), r.float(), r.float(), r.float()
		sk.Transforms[i] = d
	}

	sk.Paths = make([]*PathData, r.count())
	for i := range sk.Paths {
		d := &PathData{Name: r.s(), Order: r.varint(true), SkinRequired: r.bool_()}
		d.Bones = make([]*BoneData, r.count())
		for j := range d.Bones {
			d.Bones[j] = boneAt(r, sk, r.varint(true))
		}
		si := r.varint(true)
		if si < len(sk.Slots) {
			d.Target = sk.Slots[si]
		}
		d.PositionMode = PositionMode(r.varint(true))
		d.SpacingMode = SpacingMode(r.varint(true))
		d.RotateMode = RotateMode(r.varint(true))
		d.OffsetRotation, d.Position, d.Spacing = r.float(), r.float(), r.float()
		d.RotateMix, d.TranslateMix = r.float(), r.float()
		sk.Paths[i] = d
	}

	if def := readSkin(r, sk, true, nonessential); def != nil {
		sk.DefaultSkin = def
		sk.Skins = append(sk.Skins, def)
	}
	for i, n := 0, r.count(); i < n && r.err == nil; i++ {
		sk.Skins = append(sk.Skins, readSkin(r, sk, false, nonessential))
	}
	if r.err != nil {
		return nil, r.err
	}

	// Linked meshes take their parent's geometry.
	for _, skin := range sk.Skins {
		for slot, m := range skin.Attachments {
			for _, a := range m {
				if a.Kind != KindLinkedMesh {
					continue
				}
				from := sk.DefaultSkin
				if a.LinkSkin != "" {
					from = sk.FindSkin(a.LinkSkin)
				}
				parent := from.Get(slot, a.LinkParent)
				if parent == nil || parent.Kind != KindMesh {
					return nil, fmt.Errorf("linked mesh %q: no parent mesh %q", a.Name, a.LinkParent)
				}
				a.ParentMesh = parent
				a.Vertices = parent.Vertices
				a.RegionUVs = parent.RegionUVs
				a.Triangles = parent.Triangles
				a.HullLength = parent.HullLength
			}
		}
	}

	sk.Events = make([]*EventData, r.count())
	for i := range sk.Events {
		name, _ := r.ref()
		d := &EventData{Name: name, Int: r.varint(false), Float: r.float(), String: r.s(), AudioPath: r.s()}
		if d.AudioPath != "" {
			r.float()
			r.float()
		}
		sk.Events[i] = d
	}

	for i, n := 0, r.count(); i < n && r.err == nil; i++ {
		name := r.s()
		a, err := readAnimation(r, sk, name)
		if err != nil {
			return nil, err
		}
		sk.Animations = append(sk.Animations, a)
	}
	if r.err != nil {
		return nil, r.err
	}
	sk.TrailingBytes = len(r.b) - r.p
	return sk, nil
}

func boneAt(r *reader, sk *Skeleton, i int) *BoneData {
	if i < 0 || i >= len(sk.Bones) || sk.Bones[i] == nil {
		if r.err == nil {
			r.err = fmt.Errorf("bad bone index %d", i)
		}
		return &BoneData{}
	}
	return sk.Bones[i]
}

func readSkin(r *reader, sk *Skeleton, isDefault, nonessential bool) *Skin {
	var skin *Skin
	var slotCount int
	if isDefault {
		slotCount = r.count()
		if slotCount == 0 {
			return nil
		}
		skin = &Skin{Name: "default"}
	} else {
		name, _ := r.ref()
		skin = &Skin{Name: name}
		for i, n := 0, r.count(); i < n; i++ {
			skin.Bones = append(skin.Bones, boneAt(r, sk, r.varint(true)))
		}
		for i, n := 0, r.count(); i < n; i++ {
			if j := r.varint(true); j < len(sk.Ik) {
				skin.Ik = append(skin.Ik, sk.Ik[j])
			}
		}
		for i, n := 0, r.count(); i < n; i++ {
			if j := r.varint(true); j < len(sk.Transforms) {
				skin.Transforms = append(skin.Transforms, sk.Transforms[j])
			}
		}
		for i, n := 0, r.count(); i < n; i++ {
			if j := r.varint(true); j < len(sk.Paths) {
				skin.Paths = append(skin.Paths, sk.Paths[j])
			}
		}
		slotCount = r.count()
	}
	skin.Attachments = make([]map[string]*Attachment, len(sk.Slots))
	for i := 0; i < slotCount && r.err == nil; i++ {
		slot := r.varint(true)
		for j, n := 0, r.count(); j < n && r.err == nil; j++ {
			key, _ := r.ref()
			a := readAttachment(r, key, nonessential)
			if a == nil || slot >= len(sk.Slots) {
				continue
			}
			if skin.Attachments[slot] == nil {
				skin.Attachments[slot] = map[string]*Attachment{}
			}
			skin.Attachments[slot][key] = a
		}
	}
	return skin
}

func readVertices(r *reader, count int) Vertices {
	v := Vertices{Count: count}
	if !r.bool_() {
		v.Weights = r.floats(count * 2)
		return v
	}
	v.Bones = []int{}
	for i := 0; i < count && r.err == nil; i++ {
		n := r.count()
		v.Bones = append(v.Bones, n)
		for j := 0; j < n; j++ {
			v.Bones = append(v.Bones, r.varint(true))
			v.Weights = append(v.Weights, r.float(), r.float(), r.float())
		}
	}
	return v
}

func readAttachment(r *reader, key string, nonessential bool) *Attachment {
	name, ok := r.ref()
	if !ok {
		name = key
	}
	a := &Attachment{Kind: AttachmentKind(r.byte_()), Name: name}
	switch a.Kind {
	case KindRegion:
		path, ok := r.ref()
		if !ok {
			path = name
		}
		a.Path = path
		a.Rotation, a.X, a.Y = r.float(), r.float(), r.float()
		a.ScaleX, a.ScaleY, a.Width, a.Height = r.float(), r.float(), r.float(), r.float()
		a.Color = colorFromRGBA8888(uint32(r.int32_()))
	case KindBoundingBox:
		a.Vertices = readVertices(r, r.count())
		if nonessential {
			r.int32_()
		}
	case KindMesh:
		path, ok := r.ref()
		if !ok {
			path = name
		}
		a.Path = path
		a.Color = colorFromRGBA8888(uint32(r.int32_()))
		n := r.count()
		a.RegionUVs = r.floats(n * 2)
		a.Triangles = r.shorts()
		a.Vertices = readVertices(r, n)
		a.HullLength = r.varint(true)
		if nonessential {
			r.shorts()
			r.float()
			r.float()
		}
	case KindLinkedMesh:
		path, ok := r.ref()
		if !ok {
			path = name
		}
		a.Path = path
		a.Color = colorFromRGBA8888(uint32(r.int32_()))
		a.LinkSkin, _ = r.ref()
		a.LinkParent, _ = r.ref()
		a.InheritDeform = r.bool_()
		if nonessential {
			r.float()
			r.float()
		}
	case KindPath:
		a.Closed, a.ConstantSpeed = r.bool_(), r.bool_()
		n := r.count()
		a.Vertices = readVertices(r, n)
		a.Lengths = r.floats(n / 3)
		if nonessential {
			r.int32_()
		}
	case KindPoint:
		a.Rotation, a.X, a.Y = r.float(), r.float(), r.float()
		if nonessential {
			r.int32_()
		}
	case KindClipping:
		a.EndSlot = r.varint(true)
		a.Vertices = readVertices(r, r.count())
		if nonessential {
			r.int32_()
		}
	default:
		if r.err == nil {
			r.err = fmt.Errorf("unknown attachment type %d", a.Kind)
		}
		return nil
	}
	return a
}

func readCurve(r *reader, k *Key) {
	switch r.byte_() {
	case 1:
		k.Curve.Kind = 1
	case 2:
		k.Curve = bezierCurve(r.float(), r.float(), r.float(), r.float())
	}
}

// keys reads frameCount keys of n values each, a curve between every two.
func keys(r *reader, frameCount, n int) []Key {
	out := make([]Key, frameCount)
	for i := range out {
		out[i].Time = r.float()
		out[i].V = r.floats(n)
		if i < frameCount-1 {
			readCurve(r, &out[i])
		}
	}
	return out
}

func readAnimation(r *reader, sk *Skeleton, name string) (*Animation, error) {
	a := &Animation{Name: name}
	add := func(t *Timeline) {
		a.Timelines = append(a.Timelines, t)
		if n := len(t.Keys); n > 0 && t.Keys[n-1].Time > a.Duration {
			a.Duration = t.Keys[n-1].Time
		}
	}

	for i, n := 0, r.count(); i < n && r.err == nil; i++ {
		slot := r.varint(true)
		for j, m := 0, r.count(); j < m && r.err == nil; j++ {
			kind := r.byte_()
			frames := r.count()
			switch kind {
			case 0:
				t := &Timeline{Kind: TLAttachment, Index: slot, Keys: make([]Key, frames)}
				for f := range t.Keys {
					t.Keys[f].Time = r.float()
					t.Keys[f].Str, t.Keys[f].HasStr = r.ref()
				}
				add(t)
			case 1:
				t := &Timeline{Kind: TLColor, Index: slot, Keys: make([]Key, frames)}
				for f := range t.Keys {
					t.Keys[f].Time = r.float()
					c := colorFromRGBA8888(uint32(r.int32_()))
					t.Keys[f].V = []float32{c.R, c.G, c.B, c.A}
					if f < frames-1 {
						readCurve(r, &t.Keys[f])
					}
				}
				add(t)
			case 2:
				t := &Timeline{Kind: TLTwoColor, Index: slot, Keys: make([]Key, frames)}
				for f := range t.Keys {
					t.Keys[f].Time = r.float()
					c := colorFromRGBA8888(uint32(r.int32_()))
					d := colorFromRGBA8888(uint32(r.int32_()))
					t.Keys[f].V = []float32{c.R, c.G, c.B, c.A, d.R, d.G, d.B}
					if f < frames-1 {
						readCurve(r, &t.Keys[f])
					}
				}
				add(t)
			default:
				return nil, fmt.Errorf("animation %q: unknown slot timeline %d", name, kind)
			}
		}
	}

	for i, n := 0, r.count(); i < n && r.err == nil; i++ {
		bone := r.varint(true)
		for j, m := 0, r.count(); j < m && r.err == nil; j++ {
			kind := r.byte_()
			frames := r.count()
			switch kind {
			case 0:
				add(&Timeline{Kind: TLRotate, Index: bone, Keys: keys(r, frames, 1)})
			case 1, 2, 3:
				add(&Timeline{Kind: TLTranslate + TimelineKind(kind-1), Index: bone, Keys: keys(r, frames, 2)})
			default:
				return nil, fmt.Errorf("animation %q: unknown bone timeline %d", name, kind)
			}
		}
	}

	for i, n := 0, r.count(); i < n && r.err == nil; i++ {
		t := &Timeline{Kind: TLIk, Index: r.varint(true)}
		t.Keys = make([]Key, r.count())
		for f := range t.Keys {
			k := &t.Keys[f]
			k.Time = r.float()
			k.V = []float32{r.float(), r.float()}
			k.Bend = int(int8(r.byte_()))
			k.Flags = [2]bool{r.bool_(), r.bool_()}
			if f < len(t.Keys)-1 {
				readCurve(r, k)
			}
		}
		add(t)
	}

	for i, n := 0, r.count(); i < n && r.err == nil; i++ {
		index := r.varint(true)
		add(&Timeline{Kind: TLTransform, Index: index, Keys: keys(r, r.count(), 4)})
	}

	for i, n := 0, r.count(); i < n && r.err == nil; i++ {
		index := r.varint(true)
		for j, m := 0, r.count(); j < m && r.err == nil; j++ {
			kind := r.byte_()
			frames := r.count()
			switch kind {
			case 0, 1:
				add(&Timeline{Kind: TLPathPosition + TimelineKind(kind), Index: index, Keys: keys(r, frames, 1)})
			case 2:
				add(&Timeline{Kind: TLPathMix, Index: index, Keys: keys(r, frames, 2)})
			default:
				return nil, fmt.Errorf("animation %q: unknown path timeline %d", name, kind)
			}
		}
	}

	for i, n := 0, r.count(); i < n && r.err == nil; i++ {
		si := r.varint(true)
		if si >= len(sk.Skins) {
			return nil, fmt.Errorf("animation %q: bad skin %d", name, si)
		}
		skin := sk.Skins[si]
		for j, m := 0, r.count(); j < m && r.err == nil; j++ {
			slot := r.varint(true)
			for k, o := 0, r.count(); k < o && r.err == nil; k++ {
				an, _ := r.ref()
				att := skin.Get(slot, an)
				if att == nil {
					return nil, fmt.Errorf("animation %q: deform of a missing attachment %q", name, an)
				}
				vs := &att.Vertices
				length := vs.DeformLength()
				t := &Timeline{Kind: TLDeform, Index: slot, Attachment: att, Keys: make([]Key, r.count())}
				for f := range t.Keys {
					key := &t.Keys[f]
					key.Time = r.float()
					d := make([]float32, length)
					end := r.varint(true)
					if end == 0 {
						if !vs.Weighted() {
							copy(d, vs.Weights)
						}
					} else {
						start := r.varint(true)
						end += start
						for v := start; v < end; v++ {
							f := r.float()
							if v < length {
								d[v] = f
							}
						}
						if !vs.Weighted() {
							for v := range d {
								d[v] += vs.Weights[v]
							}
						}
					}
					key.V = d
					if f < len(t.Keys)-1 {
						readCurve(r, key)
					}
				}
				add(t)
			}
		}
	}

	if n := r.count(); n > 0 {
		slots := len(sk.Slots)
		t := &Timeline{Kind: TLDrawOrder, Keys: make([]Key, n)}
		for i := range t.Keys {
			t.Keys[i].Time = r.float()
			offsets := r.count()
			if offsets == 0 {
				continue // the setup order
			}
			order := make([]int, slots)
			for j := range order {
				order[j] = -1
			}
			unchanged := make([]int, 0, slots)
			orig := 0
			for j := 0; j < offsets; j++ {
				slot := r.varint(true)
				for orig != slot && orig < slots {
					unchanged = append(unchanged, orig)
					orig++
				}
				at := orig + r.varint(true)
				if at >= 0 && at < slots {
					order[at] = orig
				}
				orig++
			}
			for orig < slots {
				unchanged = append(unchanged, orig)
				orig++
			}
			u := len(unchanged)
			for j := slots - 1; j >= 0; j-- {
				if order[j] == -1 && u > 0 {
					u--
					order[j] = unchanged[u]
				}
			}
			t.Keys[i].Order = order
		}
		add(t)
	}

	if n := r.count(); n > 0 {
		t := &Timeline{Kind: TLEvent, Keys: make([]Key, n)}
		for i := range t.Keys {
			t.Keys[i].Time = r.float()
			ei := r.varint(true)
			r.varint(false)
			r.float()
			if r.bool_() {
				t.Keys[i].Str = r.s()
			}
			if ei < len(sk.Events) && sk.Events[ei].AudioPath != "" {
				r.float()
				r.float()
			}
		}
		add(t)
	}
	return a, r.err
}
