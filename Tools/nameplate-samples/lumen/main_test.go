package main

import (
	"errors"
	"io/fs"
	"os"
	"reflect"
	"testing"

	"ourtaiko.org/otanim"
	"ourtaiko.org/otanim/render"
)

// The set the bake writes and the game downloads (published from here). It is not in the
// repository: bake it (bake.py) or fetch the published one (fetch.py).
const collection = "set/"

// needSet skips a test when there is no set to check.
func needSet(t *testing.T) {
	t.Helper()
	if _, err := os.Stat(collection + "composition/catalog.json"); errors.Is(err, fs.ErrNotExist) {
		t.Skip("no set in " + collection + ": bake it (bake.py) or fetch the published one (fetch.py)")
	}
}

func example(t *testing.T, path string) (*otanim.File, *render.Renderer) {
	t.Helper()
	needSet(t)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	file, err := otanim.Decode(data)
	if err != nil {
		t.Fatal(err)
	}
	renderer, err := render.New(file)
	if err != nil {
		t.Fatal(err)
	}
	if file.View != [4]float32{0, 0, 408, 96} {
		t.Fatal(file.View)
	}
	return file, renderer
}

func TestMaskedEffect(t *testing.T) {
	f, r := example(t, collection+"effect/lumen-title-ai_3.otanim")
	c := f.Clip("idle")
	if c == nil || !c.Loop || c.FPS != 60 || len(c.Frames) != 2400 {
		t.Fatal("expected the complete glint and star loop")
	}
	// Both scrolling quads sit at depth 2 inside the title's sprite 98. The clip mask in
	// plate coordinates is centred at (204, 29.85) and measures 270 x 26.
	masked := 0
	for _, frame := range c.Frames {
		for _, draw := range frame.Draws {
			name := f.Parts[draw.Part].Name
			if name != "0/98/2/52/0/45/0" && name != "0/98/2/52/1/45/0" {
				continue
			}
			masked++
			for i := 0; i < len(draw.Vertices); i += 2 {
				x, y := draw.Vertices[i], 96-draw.Vertices[i+1]
				if x < 68.9 || x > 339.1 || y < 16.7 || y > 43.0 {
					t.Fatalf("glint outside mask: %v, %v", x, y)
				}
			}
		}
	}
	if masked == 0 {
		t.Fatal("no scrolling draws")
	}
	first := r.Draw(&c.Frames[0], 408, 96, false)
	later := r.Draw(&c.Frames[32], 408, 96, false)
	if reflect.DeepEqual(first, later) {
		t.Fatal("effect never moved")
	}
	// The band under it does not move: it is the one picture of its title that stays.
	band, _ := example(t, collection+"band/lumen-title-ai_3.otanim")
	if len(band.Clip("idle").Frames) != 1 {
		t.Fatal("the ai_3 band is a still picture")
	}
}

func TestRankupPlayback(t *testing.T) {
	f, r := example(t, collection+"icon/lumen-rankup.otanim")
	if len(f.Clips) != 58 || len(f.Clip("idle").Frames[0].Draws) != 0 {
		t.Fatal("expected idle and three phases per rank")
	}
	for _, rank := range []string{"5kyuu", "4kyuu", "3kyuu", "2kyuu", "1kyuu", "1dan", "2dan", "3dan", "4dan", "5dan", "6dan", "7dan", "8dan", "9dan", "10dan", "11dan", "12dan", "13dan", "14dan"} {
		start, wait, end := f.Clip(rank+"/rankup_start"), f.Clip(rank+"/rankup_wait"), f.Clip(rank+"/rankup_end")
		if start == nil || wait == nil || end == nil || len(start.Frames) != 42 || len(wait.Frames) != 1 || len(end.Frames) != 24 {
			t.Fatal(rank, "phases")
		}
		if start.FPS != 60 || start.Loop || len(start.Frames[0].Draws) != 0 || len(end.Frames[23].Draws) != 0 {
			t.Fatal(rank, "endpoints")
		}
		hold := r.Draw(&wait.Frames[0], 408, 96, false)
		if !reflect.DeepEqual(hold, r.Draw(&start.Frames[41], 408, 96, false)) || !reflect.DeepEqual(hold, r.Draw(&end.Frames[0], 408, 96, false)) {
			t.Fatal(rank, "phase discontinuity")
		}
		mid := r.Draw(&start.Frames[15], 408, 96, false)
		if reflect.DeepEqual(mid, hold) {
			t.Fatal(rank, "start did not animate")
		}
		lit := false
		for _, v := range mid {
			if v > 0 {
				lit = true
				break
			}
		}
		if !lit {
			t.Fatal(rank, "empty intermediate render")
		}
	}
}
