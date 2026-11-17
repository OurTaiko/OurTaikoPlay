// spine2otanim bakes a Spine 3.8 skeleton (.skel + .atlas + page PNGs) into an .otanim file:
// one texture and, for every chosen clip, the meshes' vertex positions per frame. See
// OurTaikoLogs/OurTaikoPlay/Documentation/Otanim.md for the format and README.md for use.
package main

import (
	"errors"
	"flag"
	"fmt"
	"math"
	"os"
	"path/filepath"
	"strconv"
	"strings"

	"ourtaiko.org/spine2otanim/otanim"
	"ourtaiko.org/spine2otanim/spine"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "spine2otanim:", err)
		os.Exit(1)
	}
}

func run() error {
	o := defaultOptions()
	configPath := flag.String("config", "", "a JSON file with every setting and the clips (see README.md); flags given as well override it")
	initPath := flag.String("init", "", "write a config for the skeleton to this path, one looping clip per animation, and stop")
	flag.StringVar(&o.Atlas, "atlas", "", "the .atlas (default: the skeleton's name with .atlas)")
	flag.StringVar(&o.Output, "o", "", "the .otanim to write (default: the skeleton's name with .otanim)")
	clipsFlag := flag.String("clips", "", "clips to bake, comma separated: name=Animation, name=Intro+Animation (the intro plays once, then the last one loops), :once at the end for a one-shot, or just Animation (default: every animation that lasts, looping, under its own name)")
	flag.Float64Var(&o.FPS, "fps", o.FPS, "frames per second to sample at")
	flag.StringVar(&o.Skin, "skin", "", "the skin to wear (default: the default skin)")
	flag.Float64Var(&o.Unit, "unit", o.Unit, "vertex precision in skeleton units")
	flag.Float64Var(&o.Pad, "pad", o.Pad, "margin around the animation's bounds, in skeleton units, for the view")
	viewFlag := flag.String("view", "", "the view box minX,minY,maxX,maxY in skeleton units (y up), instead of the bounds")
	flag.StringVar(&o.PMA, "pma", o.PMA, "whether the texture is premultiplied: auto, yes or no")
	flag.StringVar(&o.Name, "name", "", "a name kept in the file (default: the skeleton's file name)")
	flag.StringVar(&o.Preview, "preview", "", "a folder to write each clip's frames (PNG) and playback (GIF) into")
	flag.IntVar(&o.PreviewSize, "preview-size", o.PreviewSize, "preview frame size in pixels")
	flag.BoolVar(&o.KeepTexture, "keep-texture", false, "carry the atlas texture as it is instead of only the parts the clips use")
	flag.IntVar(&o.MaxTexture, "max-texture", o.MaxTexture, "scale the texture down so neither side is over this many pixels (0: never)")
	list := flag.Bool("list", false, "only list the skeleton's animations and skins")
	flag.Usage = func() {
		fmt.Fprintln(os.Stderr, "usage: spine2otanim [flags] skeleton.skel")
		fmt.Fprintln(os.Stderr, "       spine2otanim -config character.json [flags]")
		fmt.Fprintln(os.Stderr, "       spine2otanim -init character.json skeleton.skel")
		flag.PrintDefaults()
	}
	flag.Parse()

	// The config first, then whatever was given on the command line over it.
	given := map[string]bool{}
	flag.Visit(func(f *flag.Flag) { given[f.Name] = true })
	var clips []clipSpec
	if *configPath != "" {
		if *initPath != "" {
			return errors.New("-config and -init do not go together")
		}
		cfg, err := loadConfig(*configPath)
		if err != nil {
			return err
		}
		flagged := o
		if err := cfg.apply(&o, filepath.Dir(*configPath)); err != nil {
			return fmt.Errorf("%s: %w", *configPath, err)
		}
		o.override(flagged, given)
		if clips, err = cfg.clipSpecs(); err != nil {
			return fmt.Errorf("%s: %w", *configPath, err)
		}
	}
	if given["view"] {
		v, err := parseView(*viewFlag)
		if err != nil {
			return err
		}
		o.View = &v
	}
	switch {
	case flag.NArg() == 1:
		o.Skeleton = flag.Arg(0)
	case flag.NArg() > 1 || o.Skeleton == "":
		flag.Usage()
		return errors.New("one skeleton file is needed (or a config that names one)")
	}
	if given["clips"] {
		var err error
		if clips, err = parseClips(*clipsFlag); err != nil {
			return err
		}
	}

	base := strings.TrimSuffix(o.Skeleton, filepath.Ext(o.Skeleton))
	if o.Atlas == "" {
		o.Atlas = base + ".atlas"
	}
	if o.Output == "" {
		o.Output = base + ".otanim"
	}
	if o.Name == "" {
		o.Name = filepath.Base(base)
	}

	if strings.HasSuffix(strings.ToLower(o.Skeleton), ".json") {
		return errors.New("JSON skeletons are not read yet; export binary (.skel) from Spine 3.8 (a config goes after -config)")
	}
	raw, err := os.ReadFile(o.Skeleton)
	if err != nil {
		return err
	}
	sk, err := spine.ReadBinary(raw)
	if err != nil {
		return fmt.Errorf("%s: %w", o.Skeleton, err)
	}
	if sk.TrailingBytes != 0 {
		fmt.Fprintf(os.Stderr, "warning: %d bytes after the last animation were not read\n", sk.TrailingBytes)
	}
	if *list {
		fmt.Printf("Spine %s, %d bones, %d slots\n", sk.Version, len(sk.Bones), len(sk.Slots))
		for _, a := range sk.Animations {
			fmt.Printf("  animation %-24s %.3f s\n", a.Name, a.Duration)
		}
		for _, s := range sk.Skins {
			fmt.Printf("  skin %s\n", s.Name)
		}
		return nil
	}
	if *initPath != "" {
		if err := writeInitialConfig(*initPath, o.Skeleton, sk); err != nil {
			return err
		}
		fmt.Println("wrote", *initPath, "- rename the clips to the game's names and run: spine2otanim -config", *initPath)
		return nil
	}
	if clips == nil {
		clips = everyAnimation(sk)
	}
	if err := checkClips(clips, sk); err != nil {
		return err
	}
	return bake(o, sk, clips)
}

// options are every setting of a run, from the defaults, a config and the flags.
type options struct {
	Skeleton, Atlas, Output, Name, Skin, PMA, Preview string
	FPS, Unit, Pad                                    float64
	View                                              *[4]float32
	MaxTexture, PreviewSize                           int
	KeepTexture                                       bool
}

func defaultOptions() options {
	return options{PMA: "auto", FPS: 30, Unit: 1.0 / 16, Pad: 4, MaxTexture: 1024, PreviewSize: 256}
}

// override takes the settings given as flags from f.
func (o *options) override(f options, given map[string]bool) {
	set := map[string]func(){
		"atlas": func() { o.Atlas = f.Atlas }, "o": func() { o.Output = f.Output }, "name": func() { o.Name = f.Name },
		"skin": func() { o.Skin = f.Skin }, "pma": func() { o.PMA = f.PMA }, "preview": func() { o.Preview = f.Preview },
		"fps": func() { o.FPS = f.FPS }, "unit": func() { o.Unit = f.Unit }, "pad": func() { o.Pad = f.Pad },
		"max-texture": func() { o.MaxTexture = f.MaxTexture }, "preview-size": func() { o.PreviewSize = f.PreviewSize },
		"keep-texture": func() { o.KeepTexture = f.KeepTexture },
	}
	for name, apply := range set {
		if given[name] {
			apply()
		}
	}
}

func parseView(s string) ([4]float32, error) {
	var v [4]float32
	parts := strings.Split(s, ",")
	if len(parts) != 4 {
		return v, errors.New("-view is minX,minY,maxX,maxY")
	}
	for i, p := range parts {
		f, err := strconv.ParseFloat(strings.TrimSpace(p), 32)
		if err != nil {
			return v, fmt.Errorf("-view: %w", err)
		}
		v[i] = float32(f)
	}
	return v, nil
}

func bake(o options, sk *spine.Skeleton, clips []clipSpec) error {
	atlasRaw, err := os.ReadFile(o.Atlas)
	if err != nil {
		return err
	}
	atlas, err := spine.ReadAtlas(atlasRaw)
	if err != nil {
		return fmt.Errorf("%s: %w", o.Atlas, err)
	}
	texture, pageV, err := loadPages(atlas, filepath.Dir(o.Atlas))
	if err != nil {
		return err
	}
	premultiplied := false
	switch o.PMA {
	case "yes":
		premultiplied = true
	case "no":
	case "auto":
		if premultiplied, err = looksPremultiplied(texture); err != nil {
			return err
		}
		if !premultiplied {
			for _, p := range atlas.Pages {
				premultiplied = premultiplied || p.Premultiplied
			}
		}
	default:
		return errors.New("pma is auto, yes or no")
	}

	var skin *spine.Skin
	if o.Skin != "" {
		if skin = sk.FindSkin(o.Skin); skin == nil {
			return fmt.Errorf("the skeleton has no skin %q", o.Skin)
		}
	}
	pose, err := spine.NewPose(sk, skin)
	if err != nil {
		return err
	}

	b := &baker{sk: sk, atlas: atlas, pose: pose, fps: float32(o.FPS), partKeys: map[string]int{}, pageV: pageV, warnings: map[string]bool{},
		minX: math.MaxFloat32, minY: math.MaxFloat32, maxX: -math.MaxFloat32, maxY: -math.MaxFloat32}
	file := &otanim.File{Name: o.Name, Premultiplied: premultiplied, Unit: float32(o.Unit), Texture: texture}
	for _, spec := range clips {
		c, err := b.clip(spec)
		if err != nil {
			return err
		}
		file.Clips = append(file.Clips, c)
	}
	file.Parts = b.parts
	if !o.KeepTexture {
		if file.Texture, err = repack(file.Texture, file.Parts, 2); err != nil {
			return err
		}
	}
	if file.Texture, err = shrink(file.Texture, o.MaxTexture); err != nil {
		return err
	}
	if o.View != nil {
		file.View = *o.View
	} else {
		if b.minX > b.maxX {
			return errors.New("nothing is drawn in any clip")
		}
		p := float32(o.Pad)
		file.View = [4]float32{b.minX - p, b.minY - p, b.maxX + p, b.maxY + p}
	}
	if file.View[2] <= file.View[0] || file.View[3] <= file.View[1] {
		return errors.New("the view is empty")
	}

	encoded, err := file.Encode()
	if err != nil {
		return err
	}
	if dir := filepath.Dir(o.Output); dir != "" {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return err
		}
	}
	if err := os.WriteFile(o.Output, encoded, 0o644); err != nil {
		return err
	}
	for _, w := range b.sortedWarnings() {
		fmt.Fprintln(os.Stderr, "warning:", w)
	}
	fmt.Printf("%s: %d bytes (texture %d), %d parts, view %.1f,%.1f to %.1f,%.1f, premultiplied %v\n",
		o.Output, len(encoded), len(file.Texture), len(file.Parts), file.View[0], file.View[1], file.View[2], file.View[3], premultiplied)
	for i, c := range file.Clips {
		kind := "once"
		if c.Loop {
			kind = "loop"
			if c.LoopStart > 0 {
				kind = fmt.Sprintf("loop from frame %d", c.LoopStart)
			}
		}
		fmt.Printf("  clip %-10s %4d frames at %.2f fps (%.3f s, %s) <- %s\n", c.Name, len(c.Frames), c.FPS, c.Duration(), kind, strings.Join(clips[i].Play, " + "))
	}

	if o.Preview != "" {
		// Preview what was written, read back.
		back, err := otanim.Decode(encoded)
		if err != nil {
			return fmt.Errorf("reading the written file back: %w", err)
		}
		if err := writePreviews(back, o.Preview, o.PreviewSize); err != nil {
			return err
		}
		fmt.Println("previews in", o.Preview)
	}
	return nil
}

// everyAnimation is the clips when none are named: every animation that lasts, looping, under its
// own name (the first one when none lasts).
func everyAnimation(sk *spine.Skeleton) []clipSpec {
	var specs []clipSpec
	for _, a := range sk.Animations {
		if a.Duration > 0 {
			specs = append(specs, clipSpec{Name: a.Name, Play: []string{a.Name}, Loop: true})
		}
	}
	if len(specs) == 0 && len(sk.Animations) > 0 {
		a := sk.Animations[0]
		specs = append(specs, clipSpec{Name: a.Name, Play: []string{a.Name}, Loop: true})
	}
	return specs
}

// parseClip reads one "name=A+B:once" (name= may be left out when there is one animation).
func parseClip(item string) (clipSpec, error) {
	spec := clipSpec{Loop: true}
	name, play, named := strings.Cut(item, "=")
	if !named {
		play = name
	}
	if p, mode, ok := strings.Cut(play, ":"); ok {
		play = p
		switch strings.TrimSpace(mode) {
		case "once":
			spec.Loop = false
		case "loop":
		default:
			return spec, fmt.Errorf("clip %q: %q is neither loop nor once", item, mode)
		}
	}
	for _, a := range strings.Split(play, "+") {
		if a = strings.TrimSpace(a); a != "" {
			spec.Play = append(spec.Play, a)
		}
	}
	spec.Name = strings.TrimSpace(name)
	if !named {
		if len(spec.Play) != 1 {
			return spec, fmt.Errorf("clip %q: a clip of several animations needs a name (name=A+B)", item)
		}
		spec.Name = spec.Play[0]
	}
	return spec, nil
}

func parseClips(s string) ([]clipSpec, error) {
	var specs []clipSpec
	for _, item := range strings.Split(s, ",") {
		if item = strings.TrimSpace(item); item == "" {
			continue
		}
		spec, err := parseClip(item)
		if err != nil {
			return nil, err
		}
		specs = append(specs, spec)
	}
	return specs, nil
}

// checkClips: names are there and different, every animation is in the skeleton.
func checkClips(specs []clipSpec, sk *spine.Skeleton) error {
	if len(specs) == 0 {
		return errors.New("no clips to bake")
	}
	seen := map[string]bool{}
	for _, s := range specs {
		if s.Name == "" || seen[s.Name] {
			return fmt.Errorf("clip %q: empty or repeated name", s.Name)
		}
		seen[s.Name] = true
		if len(s.Play) == 0 {
			return fmt.Errorf("clip %q plays no animation", s.Name)
		}
		for _, a := range s.Play {
			if sk.FindAnimation(a) == nil {
				return fmt.Errorf("clip %q: the skeleton has no animation %q (spine2otanim -list shows them)", s.Name, a)
			}
		}
	}
	return nil
}
