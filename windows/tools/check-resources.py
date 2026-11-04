import re
import sys
from pathlib import Path

folder = Path(__file__).resolve().parent.parent / "ChessCoach.App"
pattern = re.compile(r'<data name="([^"]+)"[^>]*><value>(.*?)</value>', re.S)
placeholders = lambda text: sorted(re.findall(r"\{\d+\}", text))

def load(path):
    return dict(pattern.findall(path.read_text(encoding="utf-8")))

source = load(folder / "Strings.resx")
failed = False
for path in sorted(folder.glob("Strings.*.resx")):
    entries = load(path)
    missing = sorted(source.keys() - entries.keys())
    extra = sorted(entries.keys() - source.keys())
    mismatched = [k for k in source.keys() & entries.keys() if placeholders(source[k]) != placeholders(entries[k])]
    if missing or extra or mismatched:
        failed = True
        print(f"{path.name}: missing={missing} extra={extra} placeholders={mismatched}")
    else:
        print(f"{path.name}: ok ({len(entries)} keys)")
sys.exit(1 if failed else 0)
