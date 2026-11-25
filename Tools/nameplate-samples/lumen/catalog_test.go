package main

import (
	"encoding/json"
	"fmt"
	"os"
	"strings"
	"testing"

	"ourtaiko.org/otanim"
)

func TestCompleteCollection(t *testing.T) {
	var catalog struct {
		States map[string][]struct {
			Role, Clip  string
			Character   int
			Width, Size float64
			RGBA        []float64
		}
		BandBackingClip      string
		Titles, AiTitles     map[string]map[string]string
		Unclipped            []string
		Ranks, Badges, Blank []string
	}
	data, err := os.ReadFile(collection + "composition/catalog.json")
	if err != nil {
		t.Fatal(err)
	}
	if err = json.Unmarshal(data, &catalog); err != nil {
		t.Fatal(err)
	}
	if len(catalog.States) != 36 || len(catalog.Titles) != 88 || len(catalog.AiTitles) != 8 || len(catalog.Ranks) != 114 {
		t.Fatal("incomplete catalog")
	}
	base, renderer := example(t, collection+"composition/plates.otanim")
	backing := base.Clip(catalog.BandBackingClip)
	if backing == nil {
		t.Fatal("missing standard band backing")
	}
	pixel := (45*408 + 300) * 4
	rgba := renderer.Draw(&backing.Frames[0], 408, 96, false)[pixel : pixel+4]
	if rgba[3] < 0.95 || rgba[0]+rgba[1]+rgba[2] > 0.1 {
		t.Fatal("expected opaque black divider at (300,45)", rgba)
	}
	band, bandRenderer := example(t, collection+""+catalog.Titles["aprilfool2025_01"]["band"]+".otanim")
	pixels := bandRenderer.Draw(&band.Clip("idle").Frames[0], 408, 96, false)
	if pixels[pixel+3] > 0.05 {
		t.Fatal("April Fool band covers its divider")
	}
	texts := map[int]bool{}
	for name, rows := range catalog.States {
		if len(rows) == 0 {
			t.Fatal(name)
		}
		for _, row := range rows {
			switch row.Role {
			case "base":
				if base.Clip(row.Clip) == nil {
					t.Fatal(name, row.Clip)
				}
			case "text_name", "text_title":
				texts[row.Character] = true
				if row.Width <= 0 || row.Size <= 0 || len(row.RGBA) != 4 {
					t.Fatal(name, "invalid live text")
				}
			case "title_instance", "ai_title_instance", "blank_instance", "plate_player_no", "rankup_instance", "dani":
			default:
				t.Fatal(name, row.Role)
			}
		}
	}
	if len(texts) != 45 {
		t.Fatalf("exported %d text definitions", len(texts))
	}
	checkTitles(t, catalog.Titles, catalog.AiTitles, catalog.Unclipped)
	for _, ring := range []string{"clear", "full", "donfull"} {
		f, _ := example(t, collection+"composition/dan-"+ring+".otanim")
		for index := 0; index < 19; index++ {
			rank := fmt.Sprintf("%ddan", index-4)
			if index < 5 {
				rank = fmt.Sprintf("%dkyuu", 5-index)
			}
			for _, colour := range []string{"red", "gold"} {
				c := f.Clip(rank + "_" + colour)
				if c == nil || len(c.Frames[0].Draws) == 0 {
					t.Fatal(ring, rank, colour)
				}
			}
		}
	}
	for file, labels := range map[string][]string{"badges": {"1p", "2p"}, "blank": {"off", "on", "on_ai"}} {
		f, _ := example(t, collection+"composition/"+file+".otanim")
		for _, name := range labels {
			if f.Clip(name) == nil {
				t.Fatal(file, name)
			}
		}
	}
}

// A title is the band, effect and icon it wears: each an item of its slot's folder, every item
// used by some title, and each part drawn where its slot draws.
func checkTitles(t *testing.T, normal, ai map[string]map[string]string, unclipped []string) {
	t.Helper()
	// The plain title's band: nothing of a band, and no clipped effect, leaves it.
	const left, top, right, bottom = 44, 8, 364, 48
	const slack = 1.0
	used := map[string]bool{}
	for _, titles := range []map[string]map[string]string{normal, ai} {
		for label, parts := range titles {
			if parts["band"] == "" {
				t.Fatal(label, "has no band")
			}
			for slot, id := range parts {
				if slot != "band" && slot != "effect" && slot != "icon" {
					t.Fatal(label, "unknown layer", slot)
				}
				if !strings.HasPrefix(id, slot+"/") {
					t.Fatal(label, slot, "is not in its own folder:", id)
				}
				used[id] = true
				f, r := example(t, collection+id+".otanim")
				c := f.Clip("idle")
				if c == nil || c.FPS != 60 || len(f.Clips) != 1 || c.Loop != (len(c.Frames) > 1) {
					t.Fatal(id, "is not one looping idle clip")
				}
				for _, i := range []int{0, len(c.Frames) / 2, len(c.Frames) - 1} {
					pixels := r.Draw(&c.Frames[i], 408, 96, false)
					visible := false
					for j := 3; j < len(pixels); j += 4 {
						if pixels[j] > 0.05 {
							visible = true
							break
						}
					}
					// An effect is lit only some of the time.
					if !visible && slot == "band" {
						t.Fatal(id, "transparent band at frame", i)
					}
				}
				if slot == "band" {
					box := bounds(c)
					if box[0] < left-slack || box[1] < top-slack || box[2] > right+slack || box[3] > bottom+slack {
						t.Fatal(id, "band leaves the band:", box)
					}
				}
				if slot == "icon" && bounds(c)[2] < 280 {
					t.Fatal(id, "icon does not reach the band's right end")
				}
			}
		}
	}
	// No item is left unused, and the catalog tells exactly which effects leave the band.
	spills := map[string]bool{}
	for _, id := range unclipped {
		spills[id] = true
	}
	for _, folder := range []string{"band", "effect", "icon"} {
		entries, err := os.ReadDir(collection + folder)
		if err != nil {
			t.Fatal(err)
		}
		for _, entry := range entries {
			name := entry.Name()
			if !strings.HasSuffix(name, ".otanim") {
				continue
			}
			id := folder + "/" + strings.TrimSuffix(name, ".otanim")
			if id == "icon/lumen-rankup" {
				continue
			}
			if !used[id] {
				t.Fatal(id, "is not part of any title")
			}
			if folder != "effect" {
				continue
			}
			f, _ := example(t, collection+id+".otanim")
			box := bounds(f.Clip("idle"))
			leaves := box[0] < left-slack || box[1] < top-slack || box[2] > right+slack || box[3] > bottom+slack
			if leaves != spills[id] {
				t.Fatal(id, "leaves the band:", leaves, "but the catalog says", spills[id], box)
			}
			delete(spills, id)
		}
	}
	if len(spills) != 0 {
		t.Fatal("unclipped effects without a file:", spills)
	}
}

// What a clip draws, over all its frames, in plate pixels from the top left: left, top, right, bottom.
func bounds(c *otanim.Clip) [4]float32 {
	box := [4]float32{1e9, 1e9, -1e9, -1e9}
	for _, frame := range c.Frames {
		for _, draw := range frame.Draws {
			for i := 0; i < len(draw.Vertices); i += 2 {
				x, y := draw.Vertices[i], 96-draw.Vertices[i+1]
				box = [4]float32{min(box[0], x), min(box[1], y), max(box[2], x), max(box[3], y)}
			}
		}
	}
	return box
}
