"""Convert internal support procedure from PDF into knowledge/internal/.

This corpus must inform what an agent decides and **never** appear in what a customer reads:
it names the Asana projects used to track repairs, the Shopify steps for creating warranty
discount codes, the REPAIR1 code and warehouse routing. Every file it writes is
`exposure: internal`, and the retrieval path filters on that field.

Sections are listed explicitly rather than detected. The document is 19 pages of loosely
structured procedure where a heuristic would silently mis-split it, and a mis-split here means
internal text landing under the wrong heading — a one-off conversion is the right place to be
literal.

Passages in Lithuanian are preserved as written. They are a known retrieval-quality
limitation, recorded in knowledge/README.md.

Run from the repository root:  python tools/knowledge/convert_internal.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

PDF = Path("docs/sop") / "CS_ Internal Policies.pdf"
OUT = Path("knowledge/internal")
EFFECTIVE_DATE = "2026-06-22"

# Header text -> (topic slug, title). Order does not matter; position is found in the text.
SECTIONS = [
    ("REPAIR POLICY FOR INTERNAL USAGE", "repair-policy", "Repair policy"),
    ("WARRANTY DISCOUNTS", "warranty-discounts", "Warranty discounts"),
    ("REPLACEMENT FOR UK, US, CA ORDERS", "replacements-uk-us-ca", "Replacements for UK, US and CA orders"),
    ("RETURN LABELS FOR THE REPAIRS", "return-labels", "Return labels for repairs"),
    ("HOW TO SEND A REPLACEMENT", "sending-replacements", "How to send a replacement"),
    ("HOW TO CREATE NEW ORDER VIA ODOO", "odoo-orders", "Creating an order in Odoo"),
]


def read_layout_text(pdf: Path) -> str:
    try:
        from pypdf import PdfReader
    except ImportError:
        raise SystemExit("pypdf is required: pip install pypdf") from None

    reader = PdfReader(str(pdf))
    return "\n".join(page.extract_text(extraction_mode="layout") for page in reader.pages)


def front_matter(topic: str) -> str:
    return "\n".join([
        "---",
        "market: GLOBAL",
        f"topic: {topic}",
        "exposure: internal",
        f"effective_date: {EFFECTIVE_DATE}",
        "version: 1",
        "---",
        "",
    ])


def main() -> int:
    if not PDF.exists():
        print(f"error: {PDF} not found — run from the repository root", file=sys.stderr)
        return 1

    text = read_layout_text(PDF)

    found = []
    for header, slug, title in SECTIONS:
        match = re.search(rf"^\s*{re.escape(header)}\s*$", text, re.M)
        if match is None:
            print(f"error: section header not found: {header!r}", file=sys.stderr)
            return 1
        found.append((match.start(), header, slug, title))
    found.sort()

    OUT.mkdir(parents=True, exist_ok=True)
    written = 0
    for index, (start, header, slug, title) in enumerate(found):
        end = found[index + 1][0] if index + 1 < len(found) else len(text)
        body = text[start:end]
        body = re.sub(rf"^\s*{re.escape(header)}\s*$", "", body, count=1, flags=re.M).strip("\n")
        (OUT / f"{slug}.md").write_text(
            f"{front_matter(slug)}# {title}\n\n{body}\n", encoding="utf-8", newline="\n")
        written += 1
        print(f"  {slug}.md ({len(body):,} chars)")

    print(f"wrote {written} internal files into {OUT}/")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
