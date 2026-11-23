"""Bake the YataiDON Nijiiro stage and inherited PyTaikoGreen collabs."""

import argparse
import subprocess
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("skin", type=Path, help="YataiDON/Skins/YataiDONNijiiro")
    parser.add_argument("output", type=Path, help="stage output directory")
    args = parser.parse_args()
    if not (args.skin / "Scripts/background/bg_data/collab_packs.lua").is_file():
        parser.error("expected a Nijiiro skin checkout including its Lua stage data")
    if not (args.skin.parent / "PyTaikoGreen").is_dir():
        parser.error("the inherited PyTaikoGreen skin must be beside the Nijiiro skin")
    for script in ("bake.py", "backgrounds.py", "other.py", "dan.py", "parent.py"):
        subprocess.run(
            [
                sys.executable,
                Path(__file__).with_name(script),
                args.skin.resolve(),
                args.output.resolve(),
            ],
            check=True,
        )


if __name__ == "__main__":
    main()
