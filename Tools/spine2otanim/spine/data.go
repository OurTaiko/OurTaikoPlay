// Package spine reads Spine 3.8 skeletons (binary .skel and .atlas) and poses them.
//
// It is written from the published format (the 3.8 SkeletonBinary layout and the runtimes'
// documented behaviour), not copied from the Spine Runtimes, and covers what a converter needs:
// bones, slots, region and mesh attachments (weighted or not, linked meshes), IK, transform and
// path constraints, and every 3.8 timeline except events (which carry no pose).
package spine

type TransformMode int

const (
	TransformNormal TransformMode = iota
	TransformOnlyTranslation
	TransformNoRotationOrReflection
	TransformNoScale
	TransformNoScaleOrReflection
)

type BlendMode int

const (
	BlendNormal BlendMode = iota
	BlendAdditive
	BlendMultiply
	BlendScreen
)

type Color struct{ R, G, B, A float32 }

func colorFromRGBA8888(v uint32) Color {
	return Color{float32(v>>24&0xff) / 255, float32(v>>16&0xff) / 255, float32(v>>8&0xff) / 255, float32(v&0xff) / 255}
}

type BoneData struct {
	Index                                          int
	Name                                           string
	Parent                                         *BoneData
	Length                                         float32
	X, Y, Rotation, ScaleX, ScaleY, ShearX, ShearY float32
	Transform                                      TransformMode
	SkinRequired                                   bool
}

type SlotData struct {
	Index          int
	Name           string
	Bone           *BoneData
	Color          Color
	Dark           *Color
	AttachmentName string
	Blend          BlendMode
}

type IkData struct {
	Name                       string
	Order                      int
	SkinRequired               bool
	Bones                      []*BoneData
	Target                     *BoneData
	Mix, Softness              float32
	BendDirection              int
	Compress, Stretch, Uniform bool
}

type TransformData struct {
	Name                                                         string
	Order                                                        int
	SkinRequired                                                 bool
	Bones                                                        []*BoneData
	Target                                                       *BoneData
	Local, Relative                                              bool
	OffsetRotation, OffsetX, OffsetY, OffsetScaleX, OffsetScaleY float32
	OffsetShearY                                                 float32
	RotateMix, TranslateMix, ScaleMix, ShearMix                  float32
}

type PositionMode int

const (
	PositionFixed PositionMode = iota
	PositionPercent
)

type SpacingMode int

const (
	SpacingLength SpacingMode = iota
	SpacingFixed
	SpacingPercent
)

type RotateMode int

const (
	RotateTangent RotateMode = iota
	RotateChain
	RotateChainScale
)

type PathData struct {
	Name                    string
	Order                   int
	SkinRequired            bool
	Bones                   []*BoneData
	Target                  *SlotData
	PositionMode            PositionMode
	SpacingMode             SpacingMode
	RotateMode              RotateMode
	OffsetRotation          float32
	Position, Spacing       float32
	RotateMix, TranslateMix float32
}

type AttachmentKind int

const (
	KindRegion AttachmentKind = iota
	KindBoundingBox
	KindMesh
	KindLinkedMesh
	KindPath
	KindPoint
	KindClipping
)

func (k AttachmentKind) String() string {
	return [...]string{"region", "boundingbox", "mesh", "linkedmesh", "path", "point", "clipping"}[k]
}

// Vertices are a vertex attachment's setup positions: plain (x, y pairs in the slot bone's
// space) when Bones is nil, otherwise weighted: for each vertex its influence count in Bones
// followed by that many bone indices, and Weights holds x, y, weight per influence.
type Vertices struct {
	Count   int
	Bones   []int
	Weights []float32 // weighted: x, y, w per influence; plain: x, y per vertex
}

func (v *Vertices) Weighted() bool { return v.Bones != nil }

// DeformLength is how many floats a deform key holds for these vertices.
func (v *Vertices) DeformLength() int {
	if v.Weighted() {
		return len(v.Weights) / 3 * 2
	}
	return len(v.Weights)
}

type Attachment struct {
	Kind  AttachmentKind
	Name  string
	Path  string // the atlas region
	Color Color

	// Region.
	X, Y, Rotation, ScaleX, ScaleY, Width, Height float32

	// Vertex attachments (mesh, linked mesh after resolving, bounding box, path, clipping).
	Vertices   Vertices
	RegionUVs  []float32 // mesh: 0..1 in the region
	Triangles  []uint16
	HullLength int

	// Linked mesh.
	ParentMesh    *Attachment
	LinkSkin      string
	LinkParent    string
	InheritDeform bool

	// Path.
	Closed, ConstantSpeed bool
	Lengths               []float32

	// Clipping.
	EndSlot int
}

// DeformSource is the attachment whose deform keys move this one (a linked mesh that inherits
// deform answers its parent).
func (a *Attachment) DeformSource() *Attachment {
	if a.Kind == KindLinkedMesh && a.ParentMesh != nil && a.InheritDeform {
		return a.ParentMesh
	}
	return a
}

// Mesh is the attachment that holds the geometry (a linked mesh's parent).
func (a *Attachment) Mesh() *Attachment {
	if a.Kind == KindLinkedMesh && a.ParentMesh != nil {
		return a.ParentMesh
	}
	return a
}

type Skin struct {
	Name        string
	Attachments []map[string]*Attachment // by slot
	Bones       []*BoneData
	Ik          []*IkData
	Transforms  []*TransformData
	Paths       []*PathData
}

func (s *Skin) Get(slot int, name string) *Attachment {
	if s == nil || slot < 0 || slot >= len(s.Attachments) || s.Attachments[slot] == nil {
		return nil
	}
	return s.Attachments[slot][name]
}

type EventData struct {
	Name      string
	Int       int
	Float     float32
	String    string
	AudioPath string
}

type Skeleton struct {
	Hash, Version       string
	X, Y, Width, Height float32
	FPS                 float32
	Bones               []*BoneData
	Slots               []*SlotData
	Ik                  []*IkData
	Transforms          []*TransformData
	Paths               []*PathData
	Skins               []*Skin
	DefaultSkin         *Skin
	Events              []*EventData
	Animations          []*Animation
	TrailingBytes       int
}

func (s *Skeleton) FindSkin(name string) *Skin {
	for _, k := range s.Skins {
		if k.Name == name {
			return k
		}
	}
	return nil
}

func (s *Skeleton) FindAnimation(name string) *Animation {
	for _, a := range s.Animations {
		if a.Name == name {
			return a
		}
	}
	return nil
}

// Curve is how a key eases into the next: linear, stepped, or a cubic Bézier sampled the way
// the 3.8 runtimes do (ten segments).
type Curve struct {
	Kind    int // 0 linear, 1 stepped, 2 bezier
	Samples [18]float32
}

func bezierCurve(cx1, cy1, cx2, cy2 float32) Curve {
	c := Curve{Kind: 2}
	tmpx := (-cx1*2 + cx2) * 0.03
	tmpy := (-cy1*2 + cy2) * 0.03
	dddfx := ((cx1-cx2)*3 + 1) * 0.006
	dddfy := ((cy1-cy2)*3 + 1) * 0.006
	ddfx := tmpx*2 + dddfx
	ddfy := tmpy*2 + dddfy
	dfx := cx1*0.3 + tmpx + dddfx*0.16666667
	dfy := cy1*0.3 + tmpy + dddfy*0.16666667
	x, y := dfx, dfy
	for i := 0; i < 18; i += 2 {
		c.Samples[i], c.Samples[i+1] = x, y
		dfx += ddfx
		dfy += ddfy
		ddfx += dddfx
		ddfy += dddfy
		x += dfx
		y += dfy
	}
	return c
}

// Percent eases p (0..1 between this key and the next).
func (c *Curve) Percent(p float32) float32 {
	if p < 0 {
		p = 0
	} else if p > 1 {
		p = 1
	}
	switch c.Kind {
	case 0:
		return p
	case 1:
		return 0
	}
	var x float32
	for i := 0; i < 18; i += 2 {
		x = c.Samples[i]
		if x >= p {
			if i == 0 {
				return c.Samples[1] * p / x
			}
			px, py := c.Samples[i-2], c.Samples[i-1]
			return py + (c.Samples[i+1]-py)*(p-px)/(x-px)
		}
	}
	y := c.Samples[17]
	return y + (1-y)*(p-x)/(1-x)
}

// Key is one key of a timeline: its time, its values and how it eases to the next key.
type Key struct {
	Time   float32
	V      []float32
	Curve  Curve
	Str    string  // attachment name
	HasStr bool    // the attachment key names one (false: no attachment)
	Bend   int     // IK
	Flags  [2]bool // IK compress, stretch
	Order  []int   // draw order (nil: the setup order)
}

type TimelineKind int

const (
	TLAttachment TimelineKind = iota
	TLColor
	TLTwoColor
	TLRotate
	TLTranslate
	TLScale
	TLShear
	TLIk
	TLTransform
	TLPathPosition
	TLPathSpacing
	TLPathMix
	TLDeform
	TLDrawOrder
	TLEvent
)

type Timeline struct {
	Kind       TimelineKind
	Index      int         // slot, bone or constraint
	Attachment *Attachment // deform
	Keys       []Key
}

type Animation struct {
	Name      string
	Duration  float32
	Timelines []*Timeline
}
