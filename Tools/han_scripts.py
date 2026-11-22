#!/usr/bin/env python3
"""Writes Assets/OurTaiko/Resources/UiFonts/HanScripts.txt, the ideographs that tell
Japanese, Simplified Chinese and Traditional Chinese apart (Runtime/Text/TextScripts.cs).

The sets come from the national character sets, through Python's own codecs, so the output only
changes with them:
  line 1  Japanese:    in JIS X 0208 (Shift_JIS) but neither GB 2312 nor Big5 (桜, 気, 込)
  line 2  Simplified:  in GB 2312 but neither Big5 nor JIS X 0208 (们, 说, 这)
  line 3  Traditional: in Big5 but not GB 2312 (們, 說, 國)
Shared forms count for no script. Simplified excludes JIS because Japanese uses many of the
same reduced forms (国, 学); Traditional keeps the old forms JIS also has, which modern
Japanese rarely writes (國, 龍) and which kana outweigh in Japanese text anyway.

Run from the repository root:  python3 -I Tools/han_scripts.py
"""
import pathlib

OUTPUT = pathlib.Path("Assets/OurTaiko/Resources/UiFonts/HanScripts.txt")
RANGES = [(0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xF900, 0xFAFF)]


def encodable(codec):
    chars = set()
    for lo, hi in RANGES:
        for code in range(lo, hi + 1):
            try:
                chr(code).encode(codec)
            except UnicodeEncodeError:
                continue
            chars.add(code)
    return chars


def main():
    gb, big5, jis = encodable("gb2312"), encodable("big5"), encodable("shift_jis")
    lines = [jis - gb - big5, gb - big5 - jis, big5 - gb]
    OUTPUT.write_text("".join("".join(map(chr, sorted(line))) + "\n" for line in lines), encoding="utf-8")
    print(OUTPUT, *(len(line) for line in lines))


if __name__ == "__main__":
    main()
