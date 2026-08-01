"""P-3 acceptance: sampled template bodies must appear in the PDF exactly as written.

These are approved wording. The risk this guards against is the conversion silently
reflowing, trimming or re-wrapping a body — so each sampled body is required to be an exact
substring of the PDF's own text layer, not merely similar to it.

Run from the repository root:
    python tools/knowledge/check_templates_verbatim.py [sample-size]
"""

from __future__ import annotations

import random
import re
import sys
from pathlib import Path

PDF = Path("docs/sop") / "CS_ Support's Templates.pdf"
TEMPLATES = Path("knowledge/templates")
SEED = 20260801


def pdf_text() -> str:
    from pypdf import PdfReader

    reader = PdfReader(str(PDF))
    return "\n".join(page.extract_text(extraction_mode="layout") for page in reader.pages)


def body_of(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    without_front_matter = re.sub(r"^---\n.*?\n---\n", "", text, flags=re.S)
    without_heading = re.sub(r"^# .*\n", "", without_front_matter)
    return without_heading.strip("\n")


def main() -> int:
    size = int(sys.argv[1]) if len(sys.argv) > 1 else 10
    files = sorted(TEMPLATES.rglob("*.md"))
    if not files:
        print("error: no templates found — run convert_templates.py first", file=sys.stderr)
        return 1

    source = pdf_text()
    sample = random.Random(SEED).sample(files, min(size, len(files)))

    failures = []
    for path in sample:
        body = body_of(path)
        if body and body in source:
            print(f"  [verbatim] {path.relative_to(TEMPLATES)}")
        else:
            failures.append(path)
            print(f"  [DIFFERS]  {path.relative_to(TEMPLATES)}")

    print(f"\n{len(sample) - len(failures)}/{len(sample)} sampled bodies match the PDF verbatim")

    tagless = [p for p in files if not re.search(r"^tags: \[.+\]$", p.read_text(encoding="utf-8"), re.M)]
    print(f"templates: {len(files)}; without tags: {len(tagless)}")
    if tagless:
        print("  " + ", ".join(str(p) for p in tagless[:5]), file=sys.stderr)

    return 1 if failures or tagless or len(files) != 162 else 0


if __name__ == "__main__":
    raise SystemExit(main())
