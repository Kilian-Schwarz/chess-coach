import json, re, sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
catalog = json.loads((root / "ChessCoach/Resources/Localizable.xcstrings").read_text(encoding="utf-8"))["strings"]
languages = sorted({lang for entry in catalog.values() for lang in entry.get("localizations", {})})
calls = re.compile(
    r'(?:String\(localized:|\bloc\(|\b(?:Text|Button|Toggle|Label|Section|Picker|TextField|SecureField|Menu|Stepper|'
    r'LabeledContent|Slider|DisclosureGroup|GroupBox)\(|\.(?:help|navigationTitle|accessibilityLabel|accessibilityHint)\()\s*"')
placeholder = re.compile(r'%(?:\d+\$)?(?:@|lld|d|lf|f|\.\d+f)')

def read_literal(source, start):
    out, i = [], start
    while i < len(source):
        c = source[i]
        if c == '"':
            return "".join(out), i + 1
        if c == "\\" and source[i + 1] == "(":
            depth, i = 1, i + 2
            while depth:
                if source[i] == '"':
                    _, i = read_literal(source, i + 1)
                    continue
                depth += (source[i] == "(") - (source[i] == ")")
                i += 1
            out.append("\x00")
            continue
        if c == "\\":
            out.append(source[i:i + 2].replace("\\n", "\n").replace('\\"', '"'))
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out), i

normalized = {placeholder.sub("\x00", k.replace("%%", "%")): k for k in catalog}
used = {}
for path in sorted((root / "ChessCoach").rglob("*.swift")):
    source = path.read_text(encoding="utf-8")
    for m in calls.finditer(source):
        text, _ = read_literal(source, m.end())
        used.setdefault(text, path.name)

failed = False
for text, name in sorted(used.items()):
    if not re.search(r"[A-Za-z]", text.replace("\x00", "")):
        continue
    if re.fullmatch(r"[a-z]+(\.[a-z]+)+", text):
        continue
    catalog_key = normalized.get(text)
    shown = text.replace("\x00", "%@")
    if catalog_key is None:
        print(f"missing key: {shown!r} ({name})")
        failed = True
        continue
    entry = catalog[catalog_key]
    absent = [l for l in languages if l != "en" and l not in entry.get("localizations", {})]
    if absent and entry.get("shouldTranslate") is not False:
        print(f"missing translation: {shown!r} {absent}")
        failed = True
print("strings FAILED" if failed else "strings ok", f"({len(used)} used, {len(catalog)} in catalog, {len(languages)} languages)")
sys.exit(1 if failed else 0)
