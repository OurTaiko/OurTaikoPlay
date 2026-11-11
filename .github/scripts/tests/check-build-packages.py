#!/usr/bin/env python3
"""Offline validation ONLY. Never builds/runs a Player or contacts a publishing service.

Requires Python 3 + PyYAML, bash, GNU tar and zip. Takes an existing ordinary Linux
Player, copies only build payload into an empty scratch workspace, and executes the
workflow's real Package run block. Windows/macOS/Android inputs are routing fixtures,
NOT native build evidence. sudo ownership changes and ALL git/gh publishing commands
are replaced by strict local recorders; tar/zip/cp themselves run normally.
"""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tarfile
import zipfile

import yaml

REPO = Path(__file__).resolve().parents[3]
DEBUG_SUFFIXES = ("_BurstDebugInformation_DoNotShip", "_BackUpThisFolder_ButDontShipItWithYourGame")


def excluded(path):
    return any(part.endswith(DEBUG_SUFFIXES) for part in path.parts)


def snapshot(root):
    result = {}
    for path in sorted(root.rglob("*")):
        relative = path.relative_to(root)
        if excluded(relative):
            continue
        if path.is_symlink():
            result[str(relative)] = ("link", os.readlink(path))
        elif path.is_file():
            result[str(relative)] = ("file", stat.S_IMODE(path.stat().st_mode), hashlib.sha256(path.read_bytes()).hexdigest())
        elif path.is_dir():
            result[str(relative)] = ("directory", stat.S_IMODE(path.stat().st_mode))
    return result


def write(path, data, mode=0o644):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(data)
    path.chmod(mode)


def elf64(path):
    with path.open("rb") as stream:
        header = stream.read(20)
    assert header[:5] == b"\x7fELF\x02" and int.from_bytes(header[18:20], "little") == 62, path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--linux-player", required=True, type=Path, help="ordinary isolated build, never an installed game")
    parser.add_argument("--work-dir", required=True, type=Path, help="new or empty scratch directory; outputs are retained here")
    parser.add_argument("--baseline-ref", default="HEAD^", help="reviewed pre-CI commit (default: parent of this standalone change)")
    args = parser.parse_args()
    source, work = args.linux_player.resolve(), args.work_dir.resolve()
    assert source.is_dir() and source != work and source not in work.parents and work not in source.parents
    work.mkdir(parents=True, exist_ok=True)
    assert not any(work.iterdir()), "Refusing to reuse a nonempty work directory"
    workflow = yaml.load((REPO / ".github/workflows/build.yml").read_text(), Loader=yaml.BaseLoader)
    jobs = workflow["jobs"]
    baseline = yaml.load(subprocess.check_output(["git", "show", args.baseline_ref + ":.github/workflows/build.yml"], cwd=REPO, text=True), Loader=yaml.BaseLoader)
    assert {key: value for key, value in workflow.items() if key != "jobs"} == {key: value for key, value in baseline.items() if key != "jobs"}
    for job in ("version", "ios", "publish"):
        assert jobs[job] == baseline["jobs"][job], "Existing job changed: " + job
    legacy, updated = copy.deepcopy(baseline["jobs"]["build"]), copy.deepcopy(jobs["build"])
    updated["strategy"]["matrix"]["include"] = [row for row in updated["strategy"]["matrix"]["include"] if row["platform"] != "StandaloneLinux64"]
    for job in (legacy, updated):
        job["steps"] = [step for step in job["steps"] if step.get("name") != "Package"]
    assert updated == legacy, "Non-packaging legacy build behavior changed"
    build = jobs["build"]
    rows = build["strategy"]["matrix"]["include"]
    assert len(rows) == 4
    methods = {row["platform"]: row["method"] for row in rows}
    assert methods == {
        "StandaloneOSX": "OurTaiko.Editor.PlayerBuilds.BuildMacOS",
        "StandaloneWindows64": "OurTaiko.Editor.PlayerBuilds.BuildWindows",
        "StandaloneLinux64": "OurTaiko.Editor.PlayerBuilds.BuildLinux",
        "Android": "OurTaiko.Editor.PlayerBuilds.BuildAndroid",
    }
    unity = next(s for s in build["steps"] if s.get("id") == "unity")
    retry = next(s for s in build["steps"] if s.get("name") == "Retry the Unity build")
    assert unity["uses"] == retry["uses"] == "game-ci/unity-builder@v4"
    assert unity["with"] == retry["with"]
    assert unity["with"]["targetPlatform"] == "${{ matrix.platform }}"
    assert unity["with"]["buildMethod"] == "${{ matrix.method }}"
    assert unity["with"]["version"] == build["env"]["DISPLAY_VERSION"] == "${{ needs.version.outputs.display }}"
    assert retry["if"] == "steps.unity.outcome == 'failure'"
    upload = next(s for s in build["steps"] if s.get("uses") == "actions/upload-artifact@v4")
    assert upload["with"]["name"] == "out-${{ matrix.platform }}" and upload["with"]["path"] == "out/*"
    rhythm = next(s for s in build["steps"] if s.get("id") == "rhythmhub")
    assert rhythm["if"] == "matrix.platform == 'StandaloneOSX' || matrix.platform == 'StandaloneWindows64'"
    assert next(s for s in build["steps"] if s.get("name") == "Publish to RhythmHub")["if"] == "steps.rhythmhub.outputs.channel != ''"
    publish = jobs["publish"]
    assert publish["needs"] == ["version", "build"]
    assert publish["if"] == "github.ref == 'refs/heads/main' || startsWith(github.ref, 'refs/tags/v')"
    download = next(s for s in publish["steps"] if s.get("uses") == "actions/download-artifact@v4")
    assert download["with"] == {"pattern": "out-*", "path": "collected", "merge-multiple": "true"}
    assert next(s for s in jobs["ios"]["steps"] if s.get("uses") == "actions/upload-artifact@v4")["with"]["name"] == "ipa-iOS"

    shell_count = 0
    for job in jobs.values():
        for step in job["steps"]:
            if "run" in step:
                script = re.sub(r"\$\{\{.*?\}\}", "EXPRESSION", step["run"])
                subprocess.run(["bash", "-n"], input=script, text=True, check=True)
                shell_count += 1
    production = REPO / ".github/scripts/package-linux.sh"
    subprocess.run(["bash", "-n", str(production)], check=True)
    print(f"PASS workflow routes, identical retry inputs, artifact/publish wiring, {shell_count} inline shell blocks + Linux script", flush=True)

    name = workflow["env"]["BUILD_NAME"]
    version = "1.2.3-456"
    workspace = work / "workspace with spaces"
    workspace.mkdir()
    package_script = workspace / ".github/scripts/package-linux.sh"
    package_script.parent.mkdir(parents=True)
    shutil.copy2(production, package_script)
    linux = workspace / "build/StandaloneLinux64"
    linux.mkdir(parents=True)
    # A run Player's root may contain private state; never copy it wholesale.
    for path in source.iterdir():
        if path.name in (name, name + "_Data", "UnityCrashHandler64") or ".so" in path.name or path.name.endswith(DEBUG_SUFFIXES):
            if path.is_dir() and not path.is_symlink():
                shutil.copytree(path, linux / path.name, symlinks=True)
            else:
                shutil.copy2(path, linux / path.name, follow_symlinks=False)
    elf64(linux / name)
    elf64(linux / "UnityPlayer.so")
    plugins = linux / (name + "_Data/Plugins/x86_64")
    for library in ("libbass.so", "libbassmix.so", "libbass_fx.so", "libbass_aac.so", "libbassopus.so", "libFFmpegUnityBridge.so"):
        elf64(plugins / library)
    for library in ("libavformat.so.*", "libavcodec.so.*", "libavutil.so.*", "libavfilter.so.*", "libavdevice.so.*", "libswscale.so.*", "libswresample.so.*"):
        assert list(plugins.glob(library)), library
    assert (linux / (name + "_Data/Managed/OurTaiko.Runtime.dll")).is_file()
    assert (linux / (name + "_Data/data.unity3d")).is_file()
    # Synthetic preservation/exclusion fixtures supplement, never replace, real Player contents.
    (plugins / "preservation-probe.so").symlink_to("libbass.so")
    for parent in (linux, linux / (name + "_Data")):
        for suffix in DEBUG_SUFFIXES:
            write(parent / ("Fixture" + suffix) / "do-not-ship.txt", "exclusion fixture")
    expected = snapshot(linux)

    fake_bin = work / "recorders"
    fake_bin.mkdir()
    recorder = """#!/usr/bin/env python3
import json, os, pathlib, sys
tool = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
if tool == 'sudo':
    assert len(args) == 4 and args[:2] == ['chown', '-R'], args
elif tool == 'git':
    assert args[:3] == ['tag','-f','nightly'] or args == ['push','origin','-f','nightly'], args
elif tool == 'gh':
    assert args[:2] in (['release','delete'], ['release','create']), args
else: raise AssertionError(tool)
with open(os.environ['COMMAND_RECORD'], 'a') as stream: stream.write(json.dumps([tool]+args)+'\\n')
"""
    for tool in ("sudo", "git", "gh"):
        write(fake_bin / tool, recorder, 0o755)
    env = os.environ.copy()
    env.update(PATH=str(fake_bin) + os.pathsep + env["PATH"], GITHUB_WORKSPACE=str(workspace),
               BUILDS_PATH="build", BUILD_NAME=name, DISPLAY_VERSION=version, USER=os.environ.get("USER", "runner"),
               COMMAND_RECORD=str(work / "commands.jsonl"), GH_CONFIG_DIR=str(work / "empty-gh-config"), GH_TOKEN="", GITHUB_TOKEN="")
    package = next(s for s in build["steps"] if s.get("name") == "Package")["run"]
    platforms = ["StandaloneLinux64", "StandaloneWindows64", "StandaloneOSX", "Android"]
    write(workspace / f"build/StandaloneWindows64/{name}.exe", "Windows route fixture")
    write(workspace / f"build/StandaloneWindows64/{name}_Data/native.dll", "Windows payload fixture")
    write(workspace / f"build/StandaloneWindows64/{name}_BurstDebugInformation_DoNotShip/skip.txt", "exclude")
    write(workspace / f"build/StandaloneOSX/{name}.app/Contents/MacOS/{name}", "macOS route fixture", 0o755)
    (workspace / f"build/StandaloneOSX/{name}.app/Contents/MacOS/alias").symlink_to(name)
    write(workspace / f"build/Android/{name}.apk", "Android route fixture")
    for platform in platforms:
        subprocess.run(["bash", "-c", package.replace("${{ matrix.platform }}", platform)], cwd=workspace, env=env, check=True)
    unknown = subprocess.run(["bash", "-c", package.replace("${{ matrix.platform }}", "UnsupportedFixture")], cwd=workspace, env=env, capture_output=True, text=True)
    assert unknown.returncode != 0 and "Unsupported package platform" in unknown.stdout
    archive = workspace / f"out/{name}-{version}-Linux-x64.tar.gz"
    extracted = work / "extracted"
    extracted.mkdir()
    subprocess.run(["tar", "-xzpf", str(archive), "-C", str(extracted)], check=True)
    assert snapshot(extracted) == expected, "Linux payload, permissions or symbolic links changed"
    with tarfile.open(archive) as packed:
        assert not any(excluded(Path(member.name)) for member in packed.getmembers())
    assert os.access(extracted / name, os.X_OK)
    assert os.readlink(extracted / (name + "_Data/Plugins/x86_64/preservation-probe.so")) == "libbass.so"
    with zipfile.ZipFile(workspace / f"out/{name}-{version}-Windows-x64.zip") as packed:
        assert name + ".exe" in packed.namelist() and name + "_Data/native.dll" in packed.namelist()
        assert not any(excluded(Path(path)) for path in packed.namelist())
    with zipfile.ZipFile(workspace / f"out/{name}-{version}-macOS-universal.zip") as packed:
        assert packed.getinfo(f"{name}.app/Contents/MacOS/{name}").external_attr >> 16 & 0o111
        assert stat.S_ISLNK(packed.getinfo(f"{name}.app/Contents/MacOS/alias").external_attr >> 16)
    assert (workspace / f"out/{name}-{version}-Android-arm64.apk").read_text() == "Android route fixture"
    write(linux / "settings.json", '{"synthetic":"must not ship"}')
    failed = subprocess.run(["bash", str(production), str(linux), name, version + "-dirty", str(work / "rejected")], capture_output=True, text=True)
    assert failed.returncode != 0 and "Unexpected Linux build-root entry" in failed.stderr
    assert not list(work.rglob("*-dirty-Linux-x64.tar.gz"))
    assert (linux / "settings.json").is_file(), "Packaging must not delete input state"
    (linux / "settings.json").unlink()
    print(f"PASS real Linux archive round-trip: {len(expected)} entries, all file hashes/modes/links, required native libraries, exclusions, dirty-root rejection", flush=True)
    print("PASS existing Windows/macOS/Android packaging routes (fixtures only); ownership operation recorded, not executed", flush=True)

    store = work / "artifacts"
    suffixes = {"StandaloneLinux64": "Linux-x64.tar.gz", "StandaloneWindows64": "Windows-x64.zip", "StandaloneOSX": "macOS-universal.zip", "Android": "Android-arm64.apk"}
    for platform, suffix in suffixes.items():
        target = store / ("out-" + platform)
        target.mkdir(parents=True)
        shutil.copy2(workspace / f"out/{name}-{version}-{suffix}", target)
    write(store / "ipa-iOS/should-not-publish.ipa", "excluded TestFlight fixture")
    collected = workspace / "collected"
    collected.mkdir()
    for artifact in store.glob("out-*/*"):
        shutil.copy2(artifact, collected)
    expected_assets = sorted("collected/" + path.name for path in collected.iterdir())
    assert len(expected_assets) == 4 and any(path.endswith("Linux-x64.tar.gz") for path in expected_assets)
    for step_name, ref, tag in (("Publish nightly", "refs/heads/main", "main"), ("Publish release", "refs/tags/v1.2.3", "v1.2.3")):
        step = next(s for s in publish["steps"] if s.get("name") == step_name)
        assert step["if"] == ("github.ref == 'refs/heads/main'" if tag == "main" else "startsWith(github.ref, 'refs/tags/v')")
        record = work / (tag + "-commands.jsonl")
        run_env = dict(env, COMMAND_RECORD=str(record), GITHUB_REF=ref, GITHUB_REF_NAME=tag, GITHUB_SHA="0" * 40)
        subprocess.run(["bash", "-c", step["run"]], cwd=workspace, env=run_env, check=True)
        calls = [json.loads(line) for line in record.read_text().splitlines()]
        create = next(call for call in calls if call[:3] == ["gh", "release", "create"])
        assert sorted(arg for arg in create if arg.startswith("collected/")) == expected_assets
        assert not any(arg.endswith(".ipa") for arg in create)
        if tag == "main":
            assert create[3] == "nightly" and "--prerelease" in create
            assert ["git", "push", "origin", "-f", "nightly"] in calls
        else:
            assert create[3] == tag and "--verify-tag" in create and not any(call[0] == "git" for call in calls)
    print("PASS nightly/tag attachment collection including Linux; git/gh calls recorded ONLY, no network, upload, tag or release operation", flush=True)
    print("ALL CI PACKAGING CHECKS PASS", flush=True)


if __name__ == "__main__":
    main()
