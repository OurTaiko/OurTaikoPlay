package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	"ourtaiko.org/spine2otanim/spine"
)

// config is a character's conversion written down (spine2otanim -config): the files, the
// settings, and which animations make which clip. Paths are relative to the config file.
type config struct {
	Comment     string       `json:"comment,omitempty"`
	Skeleton    string       `json:"skeleton"`
	Atlas       string       `json:"atlas,omitempty"`
	Output      string       `json:"output,omitempty"`
	Name        string       `json:"name,omitempty"`
	Skin        string       `json:"skin,omitempty"`
	FPS         *float64     `json:"fps,omitempty"`
	Unit        *float64     `json:"unit,omitempty"`
	Pad         *float64     `json:"pad,omitempty"`
	View        *[4]float32  `json:"view,omitempty"`
	PMA         string       `json:"pma,omitempty"`
	MaxTexture  *int         `json:"maxTexture,omitempty"`
	KeepTexture bool         `json:"keepTexture,omitempty"`
	Preview     string       `json:"preview,omitempty"`
	PreviewSize *int         `json:"previewSize,omitempty"`
	Clips       []configClip `json:"clips"`
}

// configClip is one clip: "play" is an animation, "A+B", or a list; the animations play one
// after another and, when the clip loops (the default), the last one loops after the others
// played once.
type configClip struct {
	Name string   `json:"name"`
	Play playList `json:"play"`
	Loop *bool    `json:"loop,omitempty"`
}

type playList []string

func (p *playList) UnmarshalJSON(b []byte) error {
	var one string
	if err := json.Unmarshal(b, &one); err == nil {
		*p = nil
		for _, a := range strings.Split(one, "+") {
			if a = strings.TrimSpace(a); a != "" {
				*p = append(*p, a)
			}
		}
		return nil
	}
	var many []string
	if err := json.Unmarshal(b, &many); err != nil {
		return errors.New(`"play" is an animation's name, "A+B", or a list of names`)
	}
	*p = many
	return nil
}

func (p playList) MarshalJSON() ([]byte, error) { return json.Marshal(strings.Join(p, "+")) }

func loadConfig(path string) (*config, error) {
	raw, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	d := json.NewDecoder(bytes.NewReader(raw))
	d.DisallowUnknownFields()
	var c config
	if err := d.Decode(&c); err != nil {
		return nil, fmt.Errorf("%s: %w", path, err)
	}
	return &c, nil
}

func (c *config) apply(o *options, dir string) error {
	rel := func(p string) string {
		if p == "" || filepath.IsAbs(p) {
			return p
		}
		return filepath.Join(dir, p)
	}
	if c.Skeleton == "" {
		return errors.New(`"skeleton" is needed`)
	}
	o.Skeleton, o.Atlas, o.Output, o.Preview = rel(c.Skeleton), rel(c.Atlas), rel(c.Output), rel(c.Preview)
	o.Name, o.Skin = c.Name, c.Skin
	if c.PMA != "" {
		o.PMA = c.PMA
	}
	if c.FPS != nil {
		o.FPS = *c.FPS
	}
	if c.Unit != nil {
		o.Unit = *c.Unit
	}
	if c.Pad != nil {
		o.Pad = *c.Pad
	}
	if c.MaxTexture != nil {
		o.MaxTexture = *c.MaxTexture
	}
	if c.PreviewSize != nil {
		o.PreviewSize = *c.PreviewSize
	}
	o.View = c.View
	o.KeepTexture = c.KeepTexture
	return nil
}

func (c *config) clipSpecs() ([]clipSpec, error) {
	if len(c.Clips) == 0 {
		return nil, errors.New(`"clips" is empty`)
	}
	var specs []clipSpec
	for _, cc := range c.Clips {
		s := clipSpec{Name: cc.Name, Play: cc.Play, Loop: cc.Loop == nil || *cc.Loop}
		specs = append(specs, s)
	}
	return specs, nil
}

// writeInitialConfig writes a config to start from: every animation as a looping clip under
// its own name, to be renamed to the clips the game knows.
func writeInitialConfig(path, skeleton string, sk *spine.Skeleton) error {
	if _, err := os.Stat(path); err == nil {
		return fmt.Errorf("%s is already there", path)
	}
	abs, err := filepath.Abs(skeleton)
	if err != nil {
		return err
	}
	dir, err := filepath.Abs(filepath.Dir(path))
	if err != nil {
		return err
	}
	relSkel, err := filepath.Rel(dir, abs)
	if err != nil {
		relSkel = abs
	}
	base := strings.TrimSuffix(filepath.Base(skeleton), filepath.Ext(skeleton))
	maxTexture := 512
	c := config{
		Comment: "The game's clip names: idle (everything else), gogo (GOGO TIME), combo, fullgauge, happy (full combo, popped balloon), miss, clear (above the clear line), balloon. " +
			"play: an animation, \"A+B\" or a list (played in turn; when the clip loops, the last one loops). loop: false for a one-shot. Animations not wanted can be removed.",
		Skeleton:   relSkel,
		Output:     base + ".otanim",
		MaxTexture: &maxTexture,
		Preview:    base + "-preview",
	}
	for _, a := range sk.Animations {
		if a.Duration > 0 {
			c.Clips = append(c.Clips, configClip{Name: a.Name, Play: playList{a.Name}})
		}
	}
	out, err := json.MarshalIndent(c, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, append(out, '\n'), 0o644)
}
