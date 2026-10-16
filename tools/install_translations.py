"""Copy the translation files into an installed game (for testing while translating).

Values are passed through xua_text.for_display, the same way build_package.py does,
so what you see in game matches the release.

    python -X utf8 tools/install_translations.py --game "E:/SteamLibrary/steamapps/common/CONVRGENCE"
"""
import argparse
import glob
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xua_text  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True)
    args = ap.parse_args()
    src = os.path.join(REPO, "translation", "zh-TW", "Text")
    dst = os.path.join(args.game, "BepInEx", "Translation", "zh-TW", "Text")
    os.makedirs(dst, exist_ok=True)
    n = 0
    for path in sorted(glob.glob(os.path.join(src, "*.txt"))):
        name = os.path.basename(path)
        target = os.path.join(dst, name)
        if name.startswith("convrgence"):
            raw = open(path, "rb").read()
            bom = raw.startswith(b"\xef\xbb\xbf")
            text = xua_text.display_file_text(raw.decode("utf-8-sig"))
            open(target, "wb").write((("\ufeff" if bom else "") + text).encode("utf-8"))
        else:
            shutil.copyfile(path, target)
        n += 1
    print("installed %d files -> %s" % (n, dst))


if __name__ == "__main__":
    main()
