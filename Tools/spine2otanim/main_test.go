package main

import (
	"os"
	"path/filepath"
	"reflect"
	"testing"
)

func TestParseClip(t *testing.T) {
	cases := map[string]clipSpec{
		"Idle":                            {Name: "Idle", Play: []string{"Idle"}, Loop: true},
		"idle=Idle":                       {Name: "idle", Play: []string{"Idle"}, Loop: true},
		"gogo=Skill_3_Begin+Skill_3_Loop": {Name: "gogo", Play: []string{"Skill_3_Begin", "Skill_3_Loop"}, Loop: true},
		"happy = Skill_1 : once":          {Name: "happy", Play: []string{"Skill_1"}, Loop: false},
		"x=A + B + C:once":                {Name: "x", Play: []string{"A", "B", "C"}, Loop: false},
	}
	for in, want := range cases {
		got, err := parseClip(in)
		if err != nil || !reflect.DeepEqual(got, want) {
			t.Errorf("parseClip(%q) = %+v, %v; want %+v", in, got, err, want)
		}
	}
	for _, bad := range []string{"A+B", "x=A:twice"} {
		if _, err := parseClip(bad); err == nil {
			t.Errorf("parseClip(%q) took it", bad)
		}
	}
}

func TestConfig(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "c.json")
	os.WriteFile(path, []byte(`{"skeleton":"s/a.skel","output":"out/a.otanim","fps":24,"maxTexture":256,
		"clips":[{"name":"idle","play":"Idle"},{"name":"gogo","play":["Skill_3_Begin","Skill_3_Loop"]},{"name":"happy","play":"Skill_1+Idle","loop":false}]}`), 0o644)
	c, err := loadConfig(path)
	if err != nil {
		t.Fatal(err)
	}
	o := defaultOptions()
	if err := c.apply(&o, dir); err != nil {
		t.Fatal(err)
	}
	if o.Skeleton != filepath.Join(dir, "s/a.skel") || o.Output != filepath.Join(dir, "out/a.otanim") || o.FPS != 24 || o.MaxTexture != 256 || o.Unit != 1.0/16 {
		t.Fatalf("options %+v", o)
	}
	specs, _ := c.clipSpecs()
	want := []clipSpec{{"idle", []string{"Idle"}, true}, {"gogo", []string{"Skill_3_Begin", "Skill_3_Loop"}, true}, {"happy", []string{"Skill_1", "Idle"}, false}}
	if !reflect.DeepEqual(specs, want) {
		t.Fatalf("clips %+v", specs)
	}
	os.WriteFile(path, []byte(`{"skeleton":"a.skel","clip":[]}`), 0o644)
	if _, err := loadConfig(path); err == nil {
		t.Fatal("a misspelt field was taken")
	}
}
