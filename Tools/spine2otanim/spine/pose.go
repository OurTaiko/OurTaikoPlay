package spine

import (
	"fmt"
	"math"
)

const degRad = math.Pi / 180

func cosDeg(d float32) float32   { return float32(math.Cos(float64(d) * degRad)) }
func sinDeg(d float32) float32   { return float32(math.Sin(float64(d) * degRad)) }
func atan2(y, x float32) float32 { return float32(math.Atan2(float64(y), float64(x))) }
func sqrt(v float32) float32     { return float32(math.Sqrt(float64(v))) }
func cos(r float32) float32      { return float32(math.Cos(float64(r))) }
func sin(r float32) float32      { return float32(math.Sin(float64(r))) }
func abs(v float32) float32 {
	if v < 0 {
		return -v
	}
	return v
}

// wrap180 brings an angle in degrees into -180..180 the way the runtimes do.
func wrap180(r float32) float32 {
	return r - float32(16384-int(16384.499999999996-float64(r)/360))*360
}

type Bone struct {
	Data                                           *BoneData
	Parent                                         *Bone
	Children                                       []*Bone
	X, Y, Rotation, ScaleX, ScaleY, ShearX, ShearY float32
	AX, AY, ARotation, AScaleX, AScaleY, AShearX   float32
	AShearY                                        float32
	AppliedValid                                   bool
	A, B, C, D, WorldX, WorldY                     float32
	sorted, active                                 bool
}

type Slot struct {
	Data       *SlotData
	Bone       *Bone
	Color      Color
	Attachment *Attachment
	Deform     []float32
}

type ikConstraint struct {
	data              *IkData
	bones             []*Bone
	target            *Bone
	mix, softness     float32
	bend              int
	compress, stretch bool
}

type transformConstraint struct {
	data                                        *TransformData
	bones                                       []*Bone
	target                                      *Bone
	rotateMix, translateMix, scaleMix, shearMix float32
}

// Pose is a skeleton posed: setup pose, then one animation applied at a time.
type Pose struct {
	Data       *Skeleton
	Skin       *Skin
	Bones      []*Bone
	Slots      []*Slot
	DrawOrder  []*Slot
	ik         []*ikConstraint
	transforms []*transformConstraint
	cache      []func()
	cacheReset []*Bone
	cacheBones []*Bone
}

// NewPose makes a pose of the skeleton wearing the skin (nil: the default skin only).
func NewPose(sk *Skeleton, skin *Skin) (*Pose, error) {
	if len(sk.Paths) > 0 {
		return nil, fmt.Errorf("the skeleton has %d path constraints, which this converter does not pose yet", len(sk.Paths))
	}
	p := &Pose{Data: sk, Skin: skin}
	for _, d := range sk.Bones {
		b := &Bone{Data: d}
		if d.Parent != nil {
			b.Parent = p.Bones[d.Parent.Index]
			b.Parent.Children = append(b.Parent.Children, b)
		}
		p.Bones = append(p.Bones, b)
	}
	for _, d := range sk.Slots {
		s := &Slot{Data: d, Bone: p.Bones[d.Bone.Index]}
		p.Slots = append(p.Slots, s)
	}
	for _, d := range sk.Ik {
		c := &ikConstraint{data: d, target: p.Bones[d.Target.Index]}
		for _, b := range d.Bones {
			c.bones = append(c.bones, p.Bones[b.Index])
		}
		p.ik = append(p.ik, c)
	}
	for _, d := range sk.Transforms {
		c := &transformConstraint{data: d, target: p.Bones[d.Target.Index]}
		for _, b := range d.Bones {
			c.bones = append(c.bones, p.Bones[b.Index])
		}
		p.transforms = append(p.transforms, c)
	}
	p.updateCache()
	p.SetupPose()
	return p, nil
}

func (p *Pose) skinHas(fn func(*Skin) bool) bool { return p.Skin != nil && fn(p.Skin) }

func (p *Pose) updateCache() {
	p.cache, p.cacheReset = nil, nil
	for _, b := range p.Bones {
		b.sorted = b.Data.SkinRequired
		b.active = !b.sorted
	}
	if p.Skin != nil {
		for _, bd := range p.Skin.Bones {
			for b := p.Bones[bd.Index]; b != nil && !b.active; b = b.Parent {
				b.sorted, b.active = false, true
			}
		}
	}
	contains := func(b *Bone) bool {
		for _, x := range p.cacheBones {
			if x == b {
				return true
			}
		}
		return false
	}
	p.cacheBones = nil
	n := len(p.ik) + len(p.transforms)
outer:
	for order := 0; order < n; order++ {
		for _, c := range p.ik {
			if c.data.Order != order {
				continue
			}
			if !c.target.active || (c.data.SkinRequired && !p.skinHas(func(s *Skin) bool { return hasIk(s, c.data) })) {
				continue outer
			}
			p.sortBone(c.target)
			parent := c.bones[0]
			p.sortBone(parent)
			if len(c.bones) > 1 {
				child := c.bones[len(c.bones)-1]
				if !contains(child) {
					p.cacheReset = append(p.cacheReset, child)
				}
			}
			c := c
			p.cache = append(p.cache, func() { p.applyIk(c) })
			sortReset(parent.Children)
			c.bones[len(c.bones)-1].sorted = true
			continue outer
		}
		for _, c := range p.transforms {
			if c.data.Order != order {
				continue
			}
			if !c.target.active || (c.data.SkinRequired && !p.skinHas(func(s *Skin) bool { return hasTransform(s, c.data) })) {
				continue outer
			}
			p.sortBone(c.target)
			if c.data.Local {
				for _, child := range c.bones {
					p.sortBone(child.Parent)
					if !contains(child) {
						p.cacheReset = append(p.cacheReset, child)
					}
				}
			} else {
				for _, b := range c.bones {
					p.sortBone(b)
				}
			}
			c := c
			p.cache = append(p.cache, func() { p.applyTransform(c) })
			for _, b := range c.bones {
				sortReset(b.Children)
			}
			for _, b := range c.bones {
				b.sorted = true
			}
			continue outer
		}
	}
	for _, b := range p.Bones {
		p.sortBone(b)
	}
}

func hasIk(s *Skin, d *IkData) bool {
	for _, x := range s.Ik {
		if x == d {
			return true
		}
	}
	return false
}

func hasTransform(s *Skin, d *TransformData) bool {
	for _, x := range s.Transforms {
		if x == d {
			return true
		}
	}
	return false
}

func (p *Pose) sortBone(b *Bone) {
	if b == nil || b.sorted {
		return
	}
	p.sortBone(b.Parent)
	b.sorted = true
	p.cacheBones = append(p.cacheBones, b)
	p.cache = append(p.cache, func() { b.updateWorldTransformWith(b.X, b.Y, b.Rotation, b.ScaleX, b.ScaleY, b.ShearX, b.ShearY) })
}

func sortReset(bones []*Bone) {
	for _, b := range bones {
		if !b.active {
			continue
		}
		if b.sorted {
			sortReset(b.Children)
		}
		b.sorted = false
	}
}

// SetupPose puts every bone, slot, constraint and the draw order back to the setup pose.
func (p *Pose) SetupPose() {
	for _, b := range p.Bones {
		d := b.Data
		b.X, b.Y, b.Rotation, b.ScaleX, b.ScaleY, b.ShearX, b.ShearY = d.X, d.Y, d.Rotation, d.ScaleX, d.ScaleY, d.ShearX, d.ShearY
	}
	for _, c := range p.ik {
		c.mix, c.softness, c.bend, c.compress, c.stretch = c.data.Mix, c.data.Softness, c.data.BendDirection, c.data.Compress, c.data.Stretch
	}
	for _, c := range p.transforms {
		d := c.data
		c.rotateMix, c.translateMix, c.scaleMix, c.shearMix = d.RotateMix, d.TranslateMix, d.ScaleMix, d.ShearMix
	}
	p.DrawOrder = append(p.DrawOrder[:0], p.Slots...)
	for _, s := range p.Slots {
		s.Color = s.Data.Color
		s.Attachment = nil
		s.Deform = s.Deform[:0]
		if s.Data.AttachmentName != "" {
			s.Attachment = p.attachment(s.Data.Index, s.Data.AttachmentName)
		}
	}
}

func (p *Pose) attachment(slot int, name string) *Attachment {
	if a := p.Skin.Get(slot, name); a != nil {
		return a
	}
	return p.Data.DefaultSkin.Get(slot, name)
}

func (p *Pose) setAttachment(s *Slot, a *Attachment) {
	if s.Attachment == a {
		return
	}
	s.Attachment = a
	s.Deform = s.Deform[:0]
}

// UpdateWorld computes every bone's world transform, constraints included.
func (p *Pose) UpdateWorld() {
	for _, b := range p.cacheReset {
		b.AX, b.AY, b.ARotation, b.AScaleX, b.AScaleY, b.AShearX, b.AShearY = b.X, b.Y, b.Rotation, b.ScaleX, b.ScaleY, b.ShearX, b.ShearY
		b.AppliedValid = true
	}
	for _, f := range p.cache {
		f()
	}
}

func (b *Bone) updateWorldTransformWith(x, y, rotation, scaleX, scaleY, shearX, shearY float32) {
	b.AX, b.AY, b.ARotation, b.AScaleX, b.AScaleY, b.AShearX, b.AShearY = x, y, rotation, scaleX, scaleY, shearX, shearY
	b.AppliedValid = true
	parent := b.Parent
	if parent == nil {
		ry := rotation + 90 + shearY
		b.A = cosDeg(rotation+shearX) * scaleX
		b.B = cosDeg(ry) * scaleY
		b.C = sinDeg(rotation+shearX) * scaleX
		b.D = sinDeg(ry) * scaleY
		b.WorldX, b.WorldY = x, y
		return
	}
	pa, pb, pc, pd := parent.A, parent.B, parent.C, parent.D
	b.WorldX = pa*x + pb*y + parent.WorldX
	b.WorldY = pc*x + pd*y + parent.WorldY
	switch b.Data.Transform {
	case TransformNormal:
		ry := rotation + 90 + shearY
		la, lb := cosDeg(rotation+shearX)*scaleX, cosDeg(ry)*scaleY
		lc, ld := sinDeg(rotation+shearX)*scaleX, sinDeg(ry)*scaleY
		b.A, b.B = pa*la+pb*lc, pa*lb+pb*ld
		b.C, b.D = pc*la+pd*lc, pc*lb+pd*ld
	case TransformOnlyTranslation:
		ry := rotation + 90 + shearY
		b.A, b.B = cosDeg(rotation+shearX)*scaleX, cosDeg(ry)*scaleY
		b.C, b.D = sinDeg(rotation+shearX)*scaleX, sinDeg(ry)*scaleY
	case TransformNoRotationOrReflection:
		s := pa*pa + pc*pc
		var prx float32
		if s > 0.0001 {
			s = abs(pa*pd-pb*pc) / s
			pb = pc * s
			pd = pa * s
			prx = atan2(pc, pa) / degRad
		} else {
			pa, pc = 0, 0
			prx = 90 - atan2(pd, pb)/degRad
		}
		rx := rotation + shearX - prx
		ry := rotation + shearY - prx + 90
		la, lb := cosDeg(rx)*scaleX, cosDeg(ry)*scaleY
		lc, ld := sinDeg(rx)*scaleX, sinDeg(ry)*scaleY
		b.A, b.B = pa*la-pb*lc, pa*lb-pb*ld
		b.C, b.D = pc*la+pd*lc, pc*lb+pd*ld
	case TransformNoScale, TransformNoScaleOrReflection:
		cs, sn := cosDeg(rotation), sinDeg(rotation)
		za := pa*cs + pb*sn
		zc := pc*cs + pd*sn
		s := sqrt(za*za + zc*zc)
		if s > 0.00001 {
			s = 1 / s
		}
		za *= s
		zc *= s
		s = sqrt(za*za + zc*zc)
		if b.Data.Transform == TransformNoScale && pa*pd-pb*pc < 0 {
			s = -s
		}
		r := float32(math.Pi/2) + atan2(zc, za)
		zb, zd := cos(r)*s, sin(r)*s
		la, lb := cosDeg(shearX)*scaleX, cosDeg(90+shearY)*scaleY
		lc, ld := sinDeg(shearX)*scaleX, sinDeg(90+shearY)*scaleY
		b.A, b.B = za*la+zb*lc, za*lb+zb*ld
		b.C, b.D = zc*la+zd*lc, zc*lb+zd*ld
	}
}

func (b *Bone) updateWorldTransform() {
	b.updateWorldTransformWith(b.AX, b.AY, b.ARotation, b.AScaleX, b.AScaleY, b.AShearX, b.AShearY)
}

// updateAppliedTransform recovers the local transform from the world one (after a constraint
// changed it).
func (b *Bone) updateAppliedTransform() {
	b.AppliedValid = true
	parent := b.Parent
	if parent == nil {
		b.AX, b.AY = b.WorldX, b.WorldY
		b.ARotation = atan2(b.C, b.A) / degRad
		b.AScaleX = sqrt(b.A*b.A + b.C*b.C)
		b.AScaleY = sqrt(b.B*b.B + b.D*b.D)
		b.AShearX = 0
		b.AShearY = atan2(b.A*b.B+b.C*b.D, b.A*b.D-b.B*b.C) / degRad
		return
	}
	pa, pb, pc, pd := parent.A, parent.B, parent.C, parent.D
	pid := 1 / (pa*pd - pb*pc)
	dx, dy := b.WorldX-parent.WorldX, b.WorldY-parent.WorldY
	b.AX = dx*pd*pid - dy*pb*pid
	b.AY = dy*pa*pid - dx*pc*pid
	ia, id, ib, ic := pid*pd, pid*pa, pid*pb, pid*pc
	ra := ia*b.A - ib*b.C
	rb := ia*b.B - ib*b.D
	rc := id*b.C - ic*b.A
	rd := id*b.D - ic*b.B
	b.AShearX = 0
	b.AScaleX = sqrt(ra*ra + rc*rc)
	if b.AScaleX > 0.0001 {
		det := ra*rd - rb*rc
		b.AScaleY = det / b.AScaleX
		b.AShearY = atan2(ra*rb+rc*rd, det) / degRad
		b.ARotation = atan2(rc, ra) / degRad
	} else {
		b.AScaleX = 0
		b.AScaleY = sqrt(rb*rb + rd*rd)
		b.AShearY = 0
		b.ARotation = 90 - atan2(rd, rb)/degRad
	}
}

func (p *Pose) applyIk(c *ikConstraint) {
	switch len(c.bones) {
	case 1:
		ik1(c.bones[0], c.target.WorldX, c.target.WorldY, c.compress, c.stretch, c.data.Uniform, c.mix)
	case 2:
		ik2(c.bones[0], c.bones[1], c.target.WorldX, c.target.WorldY, c.bend, c.stretch, c.softness, c.mix)
	}
}

func ik1(bone *Bone, targetX, targetY float32, compress, stretch, uniform bool, alpha float32) {
	if !bone.AppliedValid {
		bone.updateAppliedTransform()
	}
	p := bone.Parent
	pa, pb, pc, pd := p.A, p.B, p.C, p.D
	rotationIK := -bone.AShearX - bone.ARotation
	var tx, ty float32
	switch bone.Data.Transform {
	case TransformOnlyTranslation:
		tx, ty = targetX-bone.WorldX, targetY-bone.WorldY
	default:
		if bone.Data.Transform == TransformNoRotationOrReflection {
			rotationIK += atan2(pc, pa) / degRad
			ps := abs(pa*pd-pb*pc) / (pa*pa + pc*pc)
			pb = -pc * ps
			pd = pa * ps
		}
		x, y := targetX-p.WorldX, targetY-p.WorldY
		d := pa*pd - pb*pc
		tx = (x*pd-y*pb)/d - bone.AX
		ty = (y*pa-x*pc)/d - bone.AY
	}
	rotationIK += atan2(ty, tx) / degRad
	if bone.AScaleX < 0 {
		rotationIK += 180
	}
	if rotationIK > 180 {
		rotationIK -= 360
	} else if rotationIK < -180 {
		rotationIK += 360
	}
	sx, sy := bone.AScaleX, bone.AScaleY
	if compress || stretch {
		if bone.Data.Transform == TransformNoScale || bone.Data.Transform == TransformNoScaleOrReflection {
			tx, ty = targetX-bone.WorldX, targetY-bone.WorldY
		}
		b := bone.Data.Length * sx
		dd := sqrt(tx*tx + ty*ty)
		if (compress && dd < b) || (stretch && dd > b) && b > 0.0001 {
			s := (dd/b-1)*alpha + 1
			sx *= s
			if uniform {
				sy *= s
			}
		}
	}
	bone.updateWorldTransformWith(bone.AX, bone.AY, bone.ARotation+rotationIK*alpha, sx, sy, bone.AShearX, bone.AShearY)
}

func ik2(parent, child *Bone, targetX, targetY float32, bendDir int, stretch bool, softness, alpha float32) {
	if alpha == 0 {
		child.updateWorldTransform()
		return
	}
	if !parent.AppliedValid {
		parent.updateAppliedTransform()
	}
	if !child.AppliedValid {
		child.updateAppliedTransform()
	}
	bend := float32(bendDir)
	px, py, psx, psy, csx := parent.AX, parent.AY, parent.AScaleX, parent.AScaleY, child.AScaleX
	sx := psx
	var os1, os2, s2 float32
	if psx < 0 {
		psx, os1, s2 = -psx, 180, -1
	} else {
		os1, s2 = 0, 1
	}
	if psy < 0 {
		psy, s2 = -psy, -s2
	}
	if csx < 0 {
		csx, os2 = -csx, 180
	}
	cx := child.AX
	var cy, cwx, cwy float32
	a, b, c, d := parent.A, parent.B, parent.C, parent.D
	u := abs(psx-psy) <= 0.0001
	if !u {
		cy = 0
		cwx, cwy = a*cx+parent.WorldX, c*cx+parent.WorldY
	} else {
		cy = child.AY
		cwx, cwy = a*cx+b*cy+parent.WorldX, c*cx+d*cy+parent.WorldY
	}
	pp := parent.Parent
	a, b, c, d = pp.A, pp.B, pp.C, pp.D
	id := 1 / (a*d - b*c)
	x, y := cwx-pp.WorldX, cwy-pp.WorldY
	dx, dy := (x*d-y*b)*id-px, (y*a-x*c)*id-py
	l1, l2 := sqrt(dx*dx+dy*dy), child.Data.Length*csx
	var a1, a2 float32
	if l1 < 0.0001 {
		ik1(parent, targetX, targetY, false, stretch, false, alpha)
		child.updateWorldTransformWith(cx, cy, 0, child.AScaleX, child.AScaleY, child.AShearX, child.AShearY)
		return
	}
	x, y = targetX-pp.WorldX, targetY-pp.WorldY
	tx, ty := (x*d-y*b)*id-px, (y*a-x*c)*id-py
	dd := tx*tx + ty*ty
	if softness != 0 {
		softness *= psx * (csx + 1) / 2
		td := sqrt(dd)
		sd := td - l1 - l2*psx + softness
		if sd > 0 {
			p := float32(math.Min(1, float64(sd/(softness*2)))) - 1
			p = (sd - softness*(1-p*p)) / td
			tx -= p * tx
			ty -= p * ty
			dd = tx*tx + ty*ty
		}
	}
	if u {
		l2 *= psx
		cs := (dd - l1*l1 - l2*l2) / (2 * l1 * l2)
		if cs < -1 {
			cs = -1
		} else if cs > 1 {
			cs = 1
			if stretch {
				sx *= (sqrt(dd)/(l1+l2)-1)*alpha + 1
			}
		}
		a2 = float32(math.Acos(float64(cs))) * bend
		a = l1 + l2*cs
		b = l2 * sin(a2)
		a1 = atan2(ty*a-tx*b, tx*a+ty*b)
	} else {
		a = psx * l2
		b = psy * l2
		aa, bb, ta := a*a, b*b, atan2(ty, tx)
		c = bb*l1*l1 + aa*dd - aa*bb
		c1, c2 := -2*bb*l1, bb-aa
		d = c1*c1 - 4*c2*c
		solved := false
		if d >= 0 {
			q := sqrt(d)
			if c1 < 0 {
				q = -q
			}
			q = -(c1 + q) / 2
			r0, r1 := q/c2, c/q
			r := r1
			if abs(r0) < abs(r1) {
				r = r0
			}
			if r*r <= dd {
				y = sqrt(dd-r*r) * bend
				a1 = ta - atan2(y, r)
				a2 = atan2(y/psy, (r-l1)/psx)
				solved = true
			}
		}
		if !solved {
			minAngle, minX, minY := float32(math.Pi), l1-a, float32(0)
			minDist := minX * minX
			maxAngle, maxX, maxY := float32(0), l1+a, float32(0)
			maxDist := maxX * maxX
			c = -a * l1 / (aa - bb)
			if c >= -1 && c <= 1 {
				c = float32(math.Acos(float64(c)))
				x = a*cos(c) + l1
				y = b * sin(c)
				d = x*x + y*y
				if d < minDist {
					minAngle, minDist, minX, minY = c, d, x, y
				}
				if d > maxDist {
					maxAngle, maxDist, maxX, maxY = c, d, x, y
				}
			}
			if dd <= (minDist+maxDist)/2 {
				a1 = ta - atan2(minY*bend, minX)
				a2 = minAngle * bend
			} else {
				a1 = ta - atan2(maxY*bend, maxX)
				a2 = maxAngle * bend
			}
		}
	}
	os := atan2(cy, cx) * s2
	rotation := parent.ARotation
	a1 = (a1-os)/degRad + os1 - rotation
	if a1 > 180 {
		a1 -= 360
	} else if a1 < -180 {
		a1 += 360
	}
	parent.updateWorldTransformWith(px, py, rotation+a1*alpha, sx, parent.AScaleY, 0, 0)
	rotation = child.ARotation
	a2 = ((a2+os)/degRad-child.AShearX)*s2 + os2 - rotation
	if a2 > 180 {
		a2 -= 360
	} else if a2 < -180 {
		a2 += 360
	}
	child.updateWorldTransformWith(cx, cy, rotation+a2*alpha, child.AScaleX, child.AScaleY, child.AShearX, child.AShearY)
}

func (p *Pose) applyTransform(c *transformConstraint) {
	d := c.data
	t := c.target
	ta, tb, tc, td := t.A, t.B, t.C, t.D
	degRadReflect := float32(degRad)
	if ta*td-tb*tc <= 0 {
		degRadReflect = -degRadReflect
	}
	pi := float32(math.Pi)
	wrapPi := func(r float32) float32 {
		if r > pi {
			r -= 2 * pi
		} else if r < -pi {
			r += 2 * pi
		}
		return r
	}
	switch {
	case !d.Local && !d.Relative:
		offR, offSY := d.OffsetRotation*degRadReflect, d.OffsetShearY*degRadReflect
		for _, b := range c.bones {
			if c.rotateMix != 0 {
				a, bb, cc, dd := b.A, b.B, b.C, b.D
				r := wrapPi(atan2(tc, ta)-atan2(cc, a)+offR) * c.rotateMix
				cs, sn := cos(r), sin(r)
				b.A, b.B, b.C, b.D = cs*a-sn*cc, cs*bb-sn*dd, sn*a+cs*cc, sn*bb+cs*dd
			}
			if c.translateMix != 0 {
				x := d.OffsetX*ta + d.OffsetY*tb + t.WorldX
				y := d.OffsetX*tc + d.OffsetY*td + t.WorldY
				b.WorldX += (x - b.WorldX) * c.translateMix
				b.WorldY += (y - b.WorldY) * c.translateMix
			}
			if c.scaleMix > 0 {
				s := sqrt(b.A*b.A + b.C*b.C)
				if s != 0 {
					s = (s + (sqrt(ta*ta+tc*tc)-s+d.OffsetScaleX)*c.scaleMix) / s
				}
				b.A *= s
				b.C *= s
				s = sqrt(b.B*b.B + b.D*b.D)
				if s != 0 {
					s = (s + (sqrt(tb*tb+td*td)-s+d.OffsetScaleY)*c.scaleMix) / s
				}
				b.B *= s
				b.D *= s
			}
			if c.shearMix > 0 {
				bb, dd := b.B, b.D
				by := atan2(dd, bb)
				r := wrapPi(atan2(td, tb) - atan2(tc, ta) - (by - atan2(b.C, b.A)))
				r = by + (r+offSY)*c.shearMix
				s := sqrt(bb*bb + dd*dd)
				b.B, b.D = cos(r)*s, sin(r)*s
			}
			b.AppliedValid = false
		}
	case !d.Local && d.Relative:
		offR, offSY := d.OffsetRotation*degRadReflect, d.OffsetShearY*degRadReflect
		for _, b := range c.bones {
			if c.rotateMix != 0 {
				a, bb, cc, dd := b.A, b.B, b.C, b.D
				r := wrapPi(atan2(tc, ta)+offR) * c.rotateMix
				cs, sn := cos(r), sin(r)
				b.A, b.B, b.C, b.D = cs*a-sn*cc, cs*bb-sn*dd, sn*a+cs*cc, sn*bb+cs*dd
			}
			if c.translateMix != 0 {
				x := d.OffsetX*ta + d.OffsetY*tb + t.WorldX
				y := d.OffsetX*tc + d.OffsetY*td + t.WorldY
				b.WorldX += x * c.translateMix
				b.WorldY += y * c.translateMix
			}
			if c.scaleMix > 0 {
				s := (sqrt(ta*ta+tc*tc)-1+d.OffsetScaleX)*c.scaleMix + 1
				b.A *= s
				b.C *= s
				s = (sqrt(tb*tb+td*td)-1+d.OffsetScaleY)*c.scaleMix + 1
				b.B *= s
				b.D *= s
			}
			if c.shearMix > 0 {
				r := wrapPi(atan2(td, tb) - atan2(tc, ta))
				bb, dd := b.B, b.D
				r = atan2(dd, bb) + (r-pi/2+offSY)*c.shearMix
				s := sqrt(bb*bb + dd*dd)
				b.B, b.D = cos(r)*s, sin(r)*s
			}
			b.AppliedValid = false
		}
	case d.Local && !d.Relative:
		if !t.AppliedValid {
			t.updateAppliedTransform()
		}
		for _, b := range c.bones {
			if !b.AppliedValid {
				b.updateAppliedTransform()
			}
			rotation := b.ARotation
			if c.rotateMix != 0 {
				rotation += wrap180(t.ARotation-rotation+d.OffsetRotation) * c.rotateMix
			}
			x, y := b.AX, b.AY
			if c.translateMix != 0 {
				x += (t.AX - x + d.OffsetX) * c.translateMix
				y += (t.AY - y + d.OffsetY) * c.translateMix
			}
			sx, sy := b.AScaleX, b.AScaleY
			if c.scaleMix != 0 {
				if sx != 0 {
					sx = (sx + (t.AScaleX-sx+d.OffsetScaleX)*c.scaleMix) / sx
				}
				if sy != 0 {
					sy = (sy + (t.AScaleY-sy+d.OffsetScaleY)*c.scaleMix) / sy
				}
			}
			shearY := b.AShearY
			if c.shearMix != 0 {
				shearY += wrap180(t.AShearY-shearY+d.OffsetShearY) * c.shearMix
			}
			b.updateWorldTransformWith(x, y, rotation, sx, sy, b.AShearX, shearY)
		}
	default:
		if !t.AppliedValid {
			t.updateAppliedTransform()
		}
		for _, b := range c.bones {
			if !b.AppliedValid {
				b.updateAppliedTransform()
			}
			rotation := b.ARotation + (t.ARotation+d.OffsetRotation)*c.rotateMix
			x := b.AX + (t.AX+d.OffsetX)*c.translateMix
			y := b.AY + (t.AY+d.OffsetY)*c.translateMix
			sx := b.AScaleX * ((t.AScaleX-1+d.OffsetScaleX)*c.scaleMix + 1)
			sy := b.AScaleY * ((t.AScaleY-1+d.OffsetScaleY)*c.scaleMix + 1)
			shearY := b.AShearY + (t.AShearY+d.OffsetShearY)*c.shearMix
			b.updateWorldTransformWith(x, y, rotation, sx, sy, b.AShearX, shearY)
		}
	}
}

// keyAt finds the key at or before t: -1 before the first.
func keyAt(keys []Key, t float32) int {
	lo, hi := 0, len(keys)-1
	if t < keys[0].Time {
		return -1
	}
	if t >= keys[hi].Time {
		return hi
	}
	for lo < hi {
		mid := (lo + hi + 1) / 2
		if keys[mid].Time <= t {
			lo = mid
		} else {
			hi = mid - 1
		}
	}
	return lo
}

// sample answers the key before t, the eased fraction toward the next, and whether t is past
// the last key (fraction 0 then).
func sample(keys []Key, t float32) (i int, pct float32) {
	i = keyAt(keys, t)
	if i < 0 || i == len(keys)-1 {
		return i, 0
	}
	k, n := &keys[i], &keys[i+1]
	return i, k.Curve.Percent((t - k.Time) / (n.Time - k.Time))
}

func lerp(a, b, p float32) float32 { return a + (b-a)*p }

// Apply poses the setup pose with the animation at time t (seconds), as one track with alpha 1
// and the setup blend: what a runtime shows playing only this animation.
func (p *Pose) Apply(a *Animation, t float32) {
	p.SetupPose()
	for _, tl := range a.Timelines {
		keys := tl.Keys
		if len(keys) == 0 {
			continue
		}
		i, pct := sample(keys, t)
		val := func(j int) float32 {
			if i < 0 {
				return 0
			}
			if i == len(keys)-1 {
				return keys[i].V[j]
			}
			return lerp(keys[i].V[j], keys[i+1].V[j], pct)
		}
		switch tl.Kind {
		case TLRotate:
			b := p.Bones[tl.Index]
			if i < 0 {
				continue
			}
			r := keys[i].V[0]
			if i < len(keys)-1 {
				r += wrap180(keys[i+1].V[0]-r) * pct
			}
			b.Rotation = b.Data.Rotation + wrap180(r)
		case TLTranslate:
			if i < 0 {
				continue
			}
			b := p.Bones[tl.Index]
			b.X, b.Y = b.Data.X+val(0), b.Data.Y+val(1)
		case TLScale:
			if i < 0 {
				continue
			}
			b := p.Bones[tl.Index]
			b.ScaleX, b.ScaleY = b.Data.ScaleX*val(0), b.Data.ScaleY*val(1)
		case TLShear:
			if i < 0 {
				continue
			}
			b := p.Bones[tl.Index]
			b.ShearX, b.ShearY = b.Data.ShearX+val(0), b.Data.ShearY+val(1)
		case TLAttachment:
			if i < 0 {
				continue
			}
			s := p.Slots[tl.Index]
			if keys[i].HasStr {
				p.setAttachment(s, p.attachment(tl.Index, keys[i].Str))
			} else {
				p.setAttachment(s, nil)
			}
		case TLColor, TLTwoColor:
			if i < 0 {
				continue
			}
			p.Slots[tl.Index].Color = Color{val(0), val(1), val(2), val(3)}
		case TLIk:
			if i < 0 {
				continue
			}
			c := p.ik[tl.Index]
			c.mix, c.softness = val(0), val(1)
			c.bend, c.compress, c.stretch = keys[i].Bend, keys[i].Flags[0], keys[i].Flags[1]
		case TLTransform:
			if i < 0 {
				continue
			}
			c := p.transforms[tl.Index]
			c.rotateMix, c.translateMix, c.scaleMix, c.shearMix = val(0), val(1), val(2), val(3)
		case TLDeform:
			s := p.Slots[tl.Index]
			if s.Attachment == nil || s.Attachment.DeformSource() != tl.Attachment {
				continue
			}
			if i < 0 {
				s.Deform = s.Deform[:0]
				continue
			}
			n := len(keys[i].V)
			if cap(s.Deform) < n {
				s.Deform = make([]float32, n)
			}
			s.Deform = s.Deform[:n]
			for j := range s.Deform {
				s.Deform[j] = val(j)
			}
		case TLDrawOrder:
			if i < 0 || keys[i].Order == nil {
				continue
			}
			for j, si := range keys[i].Order {
				p.DrawOrder[j] = p.Slots[si]
			}
		}
	}
	p.UpdateWorld()
}

// WorldVertices answers where a slot's region or mesh vertices are (skeleton space, y up).
func (p *Pose) WorldVertices(s *Slot, region *AtlasRegion) []float32 {
	a := s.Attachment
	if a.Kind == KindRegion {
		b := s.Bone
		off := region.RegionOffsets(a)
		out := make([]float32, 8)
		for i, o := range off {
			out[2*i] = o[0]*b.A + o[1]*b.B + b.WorldX
			out[2*i+1] = o[0]*b.C + o[1]*b.D + b.WorldY
		}
		return out
	}
	vs := &a.Mesh().Vertices
	deform := s.Deform
	out := make([]float32, vs.Count*2)
	if !vs.Weighted() {
		v := vs.Weights
		if len(deform) == len(v) {
			v = deform
		}
		b := s.Bone
		for i := 0; i < vs.Count; i++ {
			x, y := v[2*i], v[2*i+1]
			out[2*i] = x*b.A + y*b.B + b.WorldX
			out[2*i+1] = x*b.C + y*b.D + b.WorldY
		}
		return out
	}
	hasDeform := len(deform) == vs.DeformLength()
	v, w, f := 0, 0, 0
	for i := 0; i < vs.Count; i++ {
		var wx, wy float32
		n := vs.Bones[v]
		v++
		for end := v + n; v < end; v, w, f = v+1, w+3, f+2 {
			b := p.Bones[vs.Bones[v]]
			x, y, weight := vs.Weights[w], vs.Weights[w+1], vs.Weights[w+2]
			if hasDeform {
				x += deform[f]
				y += deform[f+1]
			}
			wx += (x*b.A + y*b.B + b.WorldX) * weight
			wy += (x*b.C + y*b.D + b.WorldY) * weight
		}
		out[2*i], out[2*i+1] = wx, wy
	}
	return out
}
