#!/usr/bin/env python3
"""Fetches the published nameplate set into a folder laid out as bake.py writes it (set/).

The set is not in the repository: the arcade's pictures stay out of it. Whoever has the decoded
arcade nameplate bakes it (bake.py); anyone else, and the Test workflow, fetches the one players
get: nameplate/lumen.json on the distribution root (put there by OurTaikoNet's
tools/nameplate/publish-lumen.sh). Each file is checked against its size and SHA-256 and written
to <folder>/<id>.otanim, and the catalog to <folder>/composition/catalog.json.

Usage: fetch.py <folder> [<root>]

The root defaults to https://distrib.ourtaiko.org/. The folder must not exist or be empty: a
baked set is never written over.
"""
import hashlib
import json
import os
import re
import sys
import urllib.request

ROOT = "https://distrib.ourtaiko.org/"
VERSION = 1
# As the game reads the list (Online/NameplateDownloads).
ITEM = re.compile(r"^(band|effect|icon|composition)/[a-z0-9][a-z0-9_.-]{0,63}$")
PATH = re.compile(r"^lumen/(band|effect|icon|composition)/[a-z0-9][a-z0-9_.-]{0,80}\.otanim$")
HASH = re.compile(r"^[0-9a-f]{64}$")
MOST_BYTES = 8 * 1024 * 1024
CATALOG = "composition/catalog"


def get(url):
    request = urllib.request.Request(url, headers={"User-Agent": "OurTaiko nameplate fetch"})
    with urllib.request.urlopen(request, timeout=60) as answer:
        return answer.read(MOST_BYTES + 1)


def main(folder, root=ROOT):
    if not root.endswith("/"):
        root += "/"
    if os.path.isdir(folder) and os.listdir(folder):
        sys.exit(f"{folder} is not empty: a baked set is never written over")
    listed = json.loads(get(root + "nameplate/lumen.json"))
    if listed.get("version") != VERSION:
        sys.exit(f"lumen.json is version {listed.get('version')}; this reads {VERSION}")
    catalog, files = listed.get("catalog"), listed.get("files")
    if not isinstance(catalog, dict) or not isinstance(files, dict) or not files:
        sys.exit("lumen.json has no catalog or no files")
    for item, entry in files.items():
        path, size, sha256 = entry.get("path"), entry.get("size"), entry.get("sha256")
        if (not ITEM.match(item) or item == CATALOG or not isinstance(path, str) or not PATH.match(path)
                or not isinstance(size, int) or not 0 < size <= MOST_BYTES
                or not isinstance(sha256, str) or not HASH.match(sha256)):
            sys.exit(f"lumen.json has an entry that cannot be used: {item}")

    for item, entry in sorted(files.items()):
        data = get(root + "nameplate/" + entry["path"])
        if len(data) != entry["size"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
            sys.exit(f"{item}: the file is not the one lumen.json names")
        target = os.path.join(folder, item + ".otanim")
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, "wb") as out:
            out.write(data)
    target = os.path.join(folder, CATALOG + ".json")
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "w", encoding="utf-8") as out:
        json.dump(catalog, out, ensure_ascii=False, indent=2)
        out.write("\n")
    print(f"{len(files)} files and the catalog from {root}nameplate/lumen.json in {folder}")


if __name__ == "__main__":
    if len(sys.argv) not in (2, 3):
        sys.exit(__doc__.split("\n\n")[2])
    main(*sys.argv[1:])
