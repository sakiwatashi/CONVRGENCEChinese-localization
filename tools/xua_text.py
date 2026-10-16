"""Read XUnity.AutoTranslator translation files exactly the way XUA does.

XUA's TextHelper.ReadTranslationLineAndDecode is stricter than it looks:
- exactly one unescaped '=' (the separator); a second one makes XUA drop the
  whole line silently, so every '=' inside text must be written as '\\='
- '//' starts an inline comment and truncates the line
- '\\n', '\\r', '\\\\' are escapes; '%3D' decodes to '='
"""
import glob
import os

ESCAPES = {"=": "=", "n": "\n", "r": "\r", "\\": "\\", "/": "/"}


def parse_line(line):
    """Return (key, value), or None when XUA would ignore the line."""
    if line.startswith("//"):
        return None
    out, parts, esc, i = [], [], False, 0
    while i < len(line):
        c = line[i]
        if esc:
            out.append(ESCAPES.get(c, "\\" + c))
            esc = False
        elif c == "\\":
            esc = True
        elif c == "=":
            parts.append("".join(out))
            out = []
            if len(parts) > 1:
                return None
        elif c == "/" and i + 1 < len(line) and line[i + 1] == "/":
            break
        else:
            out.append(c)
        i += 1
    parts.append("".join(out))
    if len(parts) != 2:
        return None
    return parts[0].replace("%3D", "="), parts[1].replace("%3D", "=")


def encode(text):
    """Escape text for use as a key or value in a translation file."""
    if "//" in text:
        raise ValueError("'//' would be read as a comment: %r" % text)
    return (text.replace("\\", "\\\\").replace("=", "\\=")
                .replace("\r", "\\r").replace("\n", "\\n"))


# None of the game's CJK font atlases (NotoSansSC/JP/KR) contain these characters, so
# they would render as blanks. Their stand-ins are in the Japanese atlas (halfwidth forms)
# or in every Latin font. Applied to translations only when installing / packaging, so the
# source files keep proper punctuation. Measured in game with the plugin's CheckGlyphs.
DISPLAY_SUBSTITUTIONS = {
    "。": "｡",
    "、": "､",
    "「": "｢",
    "」": "｣",
    "—": "－",
    "…": "...",
}


def for_display(value):
    for a, b in DISPLAY_SUBSTITUTIONS.items():
        value = value.replace(a, b)
    return value


def separator_index(line):
    """Index of the '=' that separates key and value."""
    esc = False
    for i, c in enumerate(line):
        if esc:
            esc = False
        elif c == "\\":
            esc = True
        elif c == "=":
            return i
    raise ValueError("no separator: %r" % line)


def display_file_text(raw_text):
    """Rewrite a translation file so its values only use characters the game can draw."""
    nl = "\r\n" if "\r\n" in raw_text else "\n"
    out = []
    for line in raw_text.split(nl):
        kv = parse_line(line) if line.strip() and not line.startswith("//") else None
        if kv:
            key, val = kv
            new = for_display(val)
            if new != val:
                line = line[:separator_index(line) + 1] + encode(new)
                assert parse_line(line) == (key, new), line
        out.append(line)
    return nl.join(out)


def load(text_dir):
    """Yield (file name, line, key, value) for every translation entry.

    Lines XUA would silently drop are yielded with key None so callers can
    report them."""
    for path in sorted(glob.glob(os.path.join(text_dir, "convrgence*.txt"))):
        with open(path, encoding="utf-8-sig") as f:
            for line in f.read().splitlines():
                if not line.strip() or line.startswith("//"):
                    continue
                kv = parse_line(line)
                yield (os.path.basename(path), line) + (kv if kv else (None, None))
