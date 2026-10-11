"""List English display text in the game that has no translation yet.

Heuristic: the game stores each English line next to its Russian
counterpart, so English length-prefixed strings within a few KB of a
Cyrillic string are display text candidates. Object names, animator states
and bone paths are filtered out; review the rest by eye.

    python -X utf8 tools/find_new_strings.py --game "E:/SteamLibrary/steamapps/common/CONVRGENCE"
"""
import argparse
import bisect
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xua_text  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_TEXT = os.path.join(HERE, "..", "translation", "zh-TW", "Text")

CYRILLIC = re.compile("[\u0400-\u04ff]")
ASCII_TEXT = re.compile(r"^[\x09\x0a\x0d\x20-\x7e]+$")
NOISE = re.compile(r"UnityEngine|TMP SubMesh|Material|Shader|SDF|_LOD|Base Layer|->|"
                   r"\(\d+\)$|^CG/|Proximal|Intermediate|Distal")
SENTENCE = re.compile(r"\b[a-z]{3,}\b.*\s\b[a-z]{2,}\b.*\s\b[a-z]{2,}\b")


def strings(blob):
    i, end = 0, len(blob)
    while i + 4 < end:
        n = struct.unpack_from("<i", blob, i)[0]
        if 4 <= n <= 4000 and i + 4 + n <= end:
            try:
                s = blob[i + 4:i + 4 + n].decode("utf-8")
            except UnicodeDecodeError:
                s = None
            if s and "\x00" not in s:
                yield i, s
                i += 4 + n
                i += (-i) % 4
                continue
        i += 4


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True)
    ap.add_argument("--text", default=DEFAULT_TEXT)
    args = ap.parse_args()

    keys = set()
    for _, _, key, _ in xua_text.load(args.text):
        if key:
            keys.update({key, key.strip()})

    data = os.path.join(args.game, "CONVRGENCE_Data")
    seen = set()
    for name in sorted(os.listdir(data)):
        path = os.path.join(data, name)
        if not os.path.isfile(path) or name.endswith((".resS", ".resource")):
            continue
        with open(path, "rb") as f:
            blob = f.read()
        cyr, eng = [], []
        for pos, s in strings(blob):
            if CYRILLIC.search(s):
                cyr.append(pos)
            elif ASCII_TEXT.match(s) and " " in s:
                eng.append((pos, s))
        for pos, s in eng:
            if s in keys or s.strip() in keys or s in seen or NOISE.search(s):
                continue
            if "\n" not in s and not SENTENCE.search(s):
                continue
            j = bisect.bisect_left(cyr, pos)
            if any(0 <= k < len(cyr) and abs(cyr[k] - pos) < 5000 for k in (j - 1, j)):
                seen.add(s)
                print("%-22s | %r" % (name, s))
    print("candidates:", len(seen))


if __name__ == "__main__":
    main()
