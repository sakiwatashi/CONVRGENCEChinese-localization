"""Check that every translation key still exists in the installed game.

Run this after every game update. A key only works if the game shows exactly
that text, so the check looks for Unity's length-prefixed string (4-byte
little-endian length followed by the UTF-8 bytes). A plain substring search is
not enough: when the developer appends " (FOREST)" to a line, the old line is
still a substring but XUA will no longer match it.

    python -X utf8 tools/check_keys.py --game "E:/SteamLibrary/steamapps/common/CONVRGENCE"
"""
import argparse
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xua_text  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_TEXT = os.path.join(HERE, "..", "translation", "zh-TW", "Text")


def load_blobs(game):
    data = os.path.join(game, "CONVRGENCE_Data")
    for name in sorted(os.listdir(data)):
        path = os.path.join(data, name)
        if os.path.isfile(path) and not name.endswith((".resS", ".resource")):
            with open(path, "rb") as f:
                yield f.read()


def in_assets(blobs, raw):
    n = len(raw)
    for blob in blobs:
        i = blob.find(raw)
        while i != -1:
            if i >= 4 and struct.unpack_from("<i", blob, i - 4)[0] == n:
                return True
            i = blob.find(raw, i + 1)
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="CONVRGENCE install folder")
    ap.add_argument("--text", default=DEFAULT_TEXT, help="translation Text folder")
    args = ap.parse_args()

    blobs = list(load_blobs(args.game))
    total, dropped, lost = 0, [], []
    for fname, line, key, _ in xua_text.load(args.text):
        if key is None:
            dropped.append((fname, line))
            continue
        total += 1
        if not any(in_assets(blobs, k.encode("utf-8")) for k in {key, key.strip()} if k):
            lost.append((fname, key))

    print("entries: %d   still in game: %d   not found: %d   unreadable lines: %d"
          % (total, total - len(lost), len(lost), len(dropped)))
    for fname, line in dropped:
        print("UNREADABLE", fname, "|", line[:120])
    for fname, key in lost:
        print("NOT FOUND ", fname, "|", repr(key)[:140])
    return 1 if dropped else 0


if __name__ == "__main__":
    sys.exit(main())
