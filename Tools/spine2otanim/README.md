# spine2otanim

Bakes a Spine 3.8 binary skeleton (`.skel` + `.atlas` + page PNGs) into an `.otanim`: one
texture, textured meshes, and every chosen animation's vertex positions per frame. The game plays
an `.otanim` puchichara (`Assets/OurTaiko/Runtime/Chara/Otanim.cs`) with no skeleton runtime.

The format and the clip names the game uses are in `OurTaikoLogs/OurTaikoPlay/Documentation/Otanim.md`.

```sh
go run . -list character.skel
go run . -init character.json character.skel   # a config with every animation, to edit
go run . -config character.json
go run . -clips "idle=Relax,gogo=Move,happy=Interact:once,miss=Sleep" -max-texture 512 \
    -preview /tmp/preview -o 213.otanim character.skel
go test ./...
```

A clip may chain animations: `gogo=Skill_3_Begin+Skill_3_Loop` plays the first once, then
loops the last (in a config: `{"name": "gogo", "play": "Skill_3_Begin+Skill_3_Loop"}`).

`go run . -h` lists every flag. `cmd/skeldump` prints what a skeleton uses (constraints, attachment
kinds, timelines). The Spine reader is written from the published 3.8 format, not taken from
the Spine Runtimes.
