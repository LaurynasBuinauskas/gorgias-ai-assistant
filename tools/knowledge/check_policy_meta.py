"""P-2 acceptance: every policy file resolves against the generated _meta lists."""
import json, re
from pathlib import Path

meta = Path("knowledge/_meta")
markets = {m["code"] for m in json.loads((meta / "markets.json").read_text())["markets"]}
topics = {t["slug"] for t in json.loads((meta / "topics.json").read_text())["topics"]}

problems = []
checked = 0
for path in sorted(Path("knowledge/policy").rglob("*.md")):
    checked += 1
    text = path.read_text(encoding="utf-8")
    fm = re.match(r"^---\n(.*?)\n---\n", text, re.S)
    if not fm:
        problems.append(f"{path}: no front-matter")
        continue
    fields = dict(re.findall(r"^(\w+):\s*(.+)$", fm.group(1), re.M))

    for key in ("market", "topic", "exposure", "effective_date", "version"):
        if key not in fields:
            problems.append(f"{path}: missing '{key}'")

    if fields.get("market") not in markets:
        problems.append(f"{path}: market '{fields.get('market')}' not in markets.json")
    if fields.get("topic") not in topics:
        problems.append(f"{path}: topic '{fields.get('topic')}' not in topics.json")
    if fields.get("exposure") not in {"customer", "internal"}:
        problems.append(f"{path}: exposure '{fields.get('exposure')}' must be customer|internal")

    # Directory position must agree with front-matter, or metadata drifts from location.
    if fields.get("market") != path.parent.name:
        problems.append(f"{path}: market '{fields.get('market')}' != directory '{path.parent.name}'")
    if fields.get("topic") != path.stem:
        problems.append(f"{path}: topic '{fields.get('topic')}' != filename '{path.stem}'")

print(f"checked {checked} policy files against {len(markets)} markets / {len(topics)} topics")
print(f"problems: {len(problems)}")
for p in problems[:20]:
    print("  -", p)
raise SystemExit(1 if problems else 0)
