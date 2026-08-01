"""Convert the approved reply templates from PDF into knowledge/templates/.

These are the house voice: 162 replies the support team has agreed on. Bodies are reproduced
**verbatim** — paraphrase is a defect, not a style choice — so this script normalises nothing
beyond stripping the layout artefacts the PDF itself introduces.

Unlike the policy rollup, this PDF has compressed streams and subset fonts, so its text layer
is read with pypdf in `layout` mode. That mode matters: the default mode explodes justified
lines into one word per line and doubles every space. Diacritics survive intact here.

Structure, one template per block:

    [SECTION HEADER]                      only at a category boundary
    Personalization: MISSING DETAILS      the template name
    ...body...
    TAGS: PERSONALIZATION, monogram       terminator; first tag is the category

Run from the repository root:  python tools/knowledge/convert_templates.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

PDF = Path("docs/sop") / "CS_ Support's Templates.pdf"
OUT = Path("knowledge/templates")
EFFECTIVE_DATE = "2026-06-22"
EXPECTED = 162

TAGS_LINE = re.compile(r"^TAGS:[ \t]*(.+)$", re.M)
# Category dividers printed between groups: short, no lowercase letters.
SECTION_HEADER = re.compile(r"^[^a-z]{2,40}$")


def slugify(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return slug or "untitled"


def read_layout_text(pdf: Path) -> str:
    try:
        from pypdf import PdfReader
    except ImportError:
        raise SystemExit("pypdf is required: pip install pypdf") from None

    reader = PdfReader(str(pdf))
    return "\n".join(page.extract_text(extraction_mode="layout") for page in reader.pages)


def split_templates(text: str) -> list[tuple[str, str, list[str]]]:
    """Return (name, body, tags) per template, split on the trailing TAGS: line."""
    tag_lines = TAGS_LINE.findall(text)
    blocks = TAGS_LINE.split(text)[::2][:-1]  # drop the trailing remainder

    templates = []
    for block, raw_tags in zip(blocks, tag_lines, strict=True):
        lines = block.strip("\n").split("\n")
        while lines and (not lines[0].strip() or SECTION_HEADER.match(lines[0].strip())):
            lines.pop(0)
        if not lines:
            continue
        name = lines[0].strip()
        body = "\n".join(lines[1:]).strip("\n")
        tags = [t.strip() for t in raw_tags.split(",") if t.strip()]
        templates.append((name, body, tags))
    return templates


def front_matter(topic: str, tags: list[str]) -> str:
    quoted = ", ".join(f'"{t}"' for t in tags)
    return "\n".join([
        "---",
        "market: GLOBAL",
        f"topic: {topic}",
        "exposure: customer",
        f"effective_date: {EFFECTIVE_DATE}",
        "version: 1",
        f"tags: [{quoted}]",
        "---",
        "",
    ])


def main() -> int:
    if not PDF.exists():
        print(f"error: {PDF} not found — run from the repository root", file=sys.stderr)
        return 1

    templates = split_templates(read_layout_text(PDF))
    if len(templates) != EXPECTED:
        print(f"error: found {len(templates)} templates, expected {EXPECTED}", file=sys.stderr)
        return 1

    used: set[Path] = set()
    untagged = []
    for name, body, tags in templates:
        if not tags:
            untagged.append(name)
            continue

        topic = slugify(tags[0])
        target = OUT / topic / f"{slugify(name)}.md"
        # Distinct templates occasionally share a name across categories.
        ordinal = 2
        while target in used:
            target = OUT / topic / f"{slugify(name)}-{ordinal}.md"
            ordinal += 1
        used.add(target)

        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(
            f"{front_matter(topic, tags)}# {name}\n\n{body}\n", encoding="utf-8")

    topics = sorted({p.parent.name for p in used})
    print(f"wrote {len(used)} templates across {len(topics)} topics into {OUT}/")
    print(f"topics: {' '.join(topics)}")
    if untagged:
        print(f"error: {len(untagged)} template(s) had no tags: {untagged}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
