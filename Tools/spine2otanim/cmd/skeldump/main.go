// skeldump prints what a Spine 3.8 binary skeleton holds (a check of the reader).
package main

import (
	"fmt"
	"os"

	"ourtaiko.org/spine2otanim/spine"
)

func main() {
	b, err := os.ReadFile(os.Args[1])
	if err != nil {
		panic(err)
	}
	sk, err := spine.ReadBinary(b)
	if err != nil {
		fmt.Println("error:", err)
		os.Exit(1)
	}
	fmt.Printf("trailing %d version %s size %.0fx%.0f at %.0f,%.0f bones %d slots %d ik %d transform %d path %d skins %d events %d\n",
		sk.TrailingBytes, sk.Version, sk.Width, sk.Height, sk.X, sk.Y, len(sk.Bones), len(sk.Slots), len(sk.Ik), len(sk.Transforms), len(sk.Paths), len(sk.Skins), len(sk.Events))
	kinds := map[string]int{}
	blends := map[spine.BlendMode]int{}
	modes := map[spine.TransformMode]int{}
	for _, b := range sk.Bones {
		modes[b.Transform]++
	}
	for _, s := range sk.Slots {
		blends[s.Blend]++
		if s.Dark != nil {
			kinds["darkslot"]++
		}
	}
	for _, skin := range sk.Skins {
		for _, m := range skin.Attachments {
			for _, a := range m {
				k := a.Kind.String()
				if a.Vertices.Weighted() {
					k += "(weighted)"
				}
				kinds[k]++
			}
		}
		fmt.Println("skin", skin.Name)
	}
	fmt.Println("attachments", kinds, "blends", blends, "transform modes", modes)
	for _, ik := range sk.Ik {
		fmt.Printf("ik %s bones %d stretch %v compress %v softness %v\n", ik.Name, len(ik.Bones), ik.Stretch, ik.Compress, ik.Softness)
	}
	for _, t := range sk.Transforms {
		fmt.Printf("transform %s local %v relative %v mixes %v %v %v %v\n", t.Name, t.Local, t.Relative, t.RotateMix, t.TranslateMix, t.ScaleMix, t.ShearMix)
	}
	for _, a := range sk.Animations {
		tl := map[spine.TimelineKind]int{}
		for _, t := range a.Timelines {
			tl[t.Kind]++
		}
		fmt.Printf("anim %-12s %.3fs timelines %v\n", a.Name, a.Duration, tl)
	}
}
