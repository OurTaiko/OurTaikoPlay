package main

import (
	"encoding/json"
	"fmt"
	"os"
	"testing"
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
		Titles, AiTitles     map[string]string
		Ranks, Badges, Blank []string
	}
	data, err := os.ReadFile("items/composition/catalog.json")
	if err != nil {
		t.Fatal(err)
	}
	if err = json.Unmarshal(data, &catalog); err != nil {
		t.Fatal(err)
	}
	if len(catalog.States) != 36 || len(catalog.Titles) != 88 || len(catalog.AiTitles) != 8 || len(catalog.Ranks) != 114 {
		t.Fatal("incomplete catalog")
	}
	base, renderer := example(t, "items/composition/plates.otanim")
	backing := base.Clip(catalog.BandBackingClip)
	if backing == nil {
		t.Fatal("missing standard band backing")
	}
	pixel := (45*408 + 300) * 4
	rgba := renderer.Draw(&backing.Frames[0], 408, 96, false)[pixel : pixel+4]
	if rgba[3] < 0.95 || rgba[0]+rgba[1]+rgba[2] > 0.1 {
		t.Fatal("expected opaque black divider at (300,45)", rgba)
	}
	band, bandRenderer := example(t, "items/"+catalog.Titles["aprilfool2025_01"]+".otanim")
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
	for _, titles := range []map[string]string{catalog.Titles, catalog.AiTitles} {
		for label, id := range titles {
			f, r := example(t, "items/"+id+".otanim")
			c := f.Clip("idle")
			if c == nil || c.FPS != 60 {
				t.Fatal(label)
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
				if !visible {
					t.Fatal(id, "transparent title at frame", i)
				}
			}
		}
	}
	for _, ring := range []string{"clear", "full", "donfull"} {
		f, _ := example(t, "items/composition/dan-"+ring+".otanim")
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
		f, _ := example(t, "items/composition/"+file+".otanim")
		for _, name := range labels {
			if f.Clip(name) == nil {
				t.Fatal(file, name)
			}
		}
	}
}
