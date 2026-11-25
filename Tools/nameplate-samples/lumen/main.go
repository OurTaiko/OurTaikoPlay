// Encode the sampled meshes with the same v1 writer used by the other nameplate examples.
package main

import (
	"encoding/json"
	"fmt"
	"os"

	"ourtaiko.org/otanim"
)

func run() error {
	if len(os.Args) != 2 {
		return fmt.Errorf("usage: lumen <output.otanim> (JSON on stdin)")
	}
	var file otanim.File
	if err := json.NewDecoder(os.Stdin).Decode(&file); err != nil {
		return err
	}
	data, err := file.Encode()
	if err != nil {
		return err
	}
	roundtrip, err := otanim.Decode(data)
	if err != nil {
		return err
	}
	if len(roundtrip.Clips) != len(file.Clips) {
		return fmt.Errorf("clip count changed in round trip")
	}
	return os.WriteFile(os.Args[1], data, 0644)
}

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
