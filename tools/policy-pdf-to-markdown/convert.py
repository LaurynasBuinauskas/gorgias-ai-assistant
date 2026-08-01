"""Reconstruct the policy markdown corpus from the generated rollup PDF.

The authoritative source (`data_reference/markets`, 99 files) was never handed over, so
this recovers it from `docs/sop/tr-cs-current-policies-2026-06-22.pdf` — the P-1 fallback.

The PDF is a text-layer document whose generator emitted one `Tj` per rendered line with a
font size that encodes structure, so heading hierarchy, per-file boundaries and source URLs
are all recoverable exactly:

    F1 8.0   "Source file: data_reference/markets/<MARKET>/<topic>-clean.md"  file boundary
    F2 12.5  file title                                                       -> #
    F1 9.4   "Source: https://..."                                            -> source_url
    F2 11.2  section heading                                                  -> ##
    F2 10.2  subsection heading                                               -> ###
    F1 9.4   body text, wrapped at render width

What does not survive the round trip: inline emphasis, and markdown link syntax — the
generator flattened `[text](url)` to `text (url)`, which is faithful but not re-linkified.

Run from the repository root:  python tools/policy-pdf-to-markdown/convert.py
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass
from pathlib import Path

PDF = Path("docs/sop/tr-cs-current-policies-2026-06-22.pdf")
OUT = Path("knowledge/policy")
EFFECTIVE_DATE = "2026-06-22"

TEXT_OP = re.compile(
    r"BT /(F\d) ([0-9.]+) Tf [0-9.]+ ([0-9.]+) Td \((.*?)\) Tj ET"
)
FILE_MARKER = re.compile(r"^Source file: data_reference/markets/([A-Z_]+)/(.+?)-clean\.md$")
SOURCE_URL = re.compile(r"^Source: (https://\S+?)(?:\s|$)")

# Font size -> markdown heading prefix. Sizes above 12.5 label the rollup's own
# market/topic dividers rather than file content, so they are dropped.
HEADINGS = {12.5: "#", 11.2: "##", 10.2: "###"}
BODY_SIZE = 9.4
LINE_LEADING = 12.7
LEADING_TOLERANCE = 0.4


@dataclass(frozen=True)
class Op:
    font: str
    size: float
    y: float
    text: str


PDF_ESCAPE = re.compile(r"\\(?:([0-7]{3})|(.))")


def unescape(raw: str) -> str:
    """Resolve PDF string escapes. Octal escapes are WinAnsi (cp1252) code points."""

    def replace(match: re.Match[str]) -> str:
        octal, literal = match.groups()
        if octal:
            return bytes([int(octal, 8)]).decode("cp1252", errors="replace")
        return literal

    return PDF_ESCAPE.sub(replace, raw)


def read_ops(pdf: Path) -> list[Op]:
    raw = pdf.read_bytes().decode("latin-1")
    return [
        Op(font, float(size), float(y), unescape(text))
        for font, size, y, text in TEXT_OP.findall(raw)
    ]


def split_into_files(ops: list[Op]) -> list[tuple[str, str, list[Op]]]:
    """Slice the op stream at `Source file:` markers into (market, topic, body ops)."""
    starts = [
        (i, m)
        for i, op in enumerate(ops)
        if op.font == "F1" and op.size == 8.0 and (m := FILE_MARKER.match(op.text))
    ]
    files = []
    for n, (index, marker) in enumerate(starts):
        end = starts[n + 1][0] if n + 1 < len(starts) else len(ops)
        files.append((marker.group(1), marker.group(2), ops[index + 1 : end]))
    return files


def render(body: list[Op]) -> tuple[str | None, str]:
    """Return (source_url, markdown) for one file's ops."""
    source_url: str | None = None
    blocks: list[str] = []
    open_para: list[str] = []
    previous: Op | None = None

    def flush() -> None:
        nonlocal open_para
        if open_para:
            blocks.append(" ".join(open_para))
            open_para = []

    for op in body:
        # Page footers and the rollup's own market/topic dividers are not content.
        if op.size == 8.0 or op.size >= 15.0 or op.font == "F1" and op.size != BODY_SIZE:
            continue

        if op.font == "F2":
            flush()
            if prefix := HEADINGS.get(op.size):
                blocks.append(f"{prefix} {op.text}")
            previous = op
            continue

        if source_url is None and (m := SOURCE_URL.match(op.text)):
            source_url = m.group(1)
            previous = op
            continue

        starts_list_item = op.text.startswith("- ")
        wrapped = (
            previous is not None
            and previous.font == "F1"
            and abs(previous.y - op.y - LINE_LEADING) < LEADING_TOLERANCE
        )
        crossed_page = previous is not None and op.y > previous.y
        continues = (
            previous is not None
            and previous.font == "F1"
            and crossed_page
            and previous.text[-1:] not in {"", ".", "!", "?", ":", ";"}
        )

        if starts_list_item or not (wrapped or continues):
            flush()
        open_para.append(op.text)
        previous = op

    flush()
    return source_url, "\n\n".join(blocks) + "\n"


def front_matter(market: str, topic: str, source_url: str | None) -> str:
    lines = [
        "---",
        f"market: {market}",
        f"topic: {topic}",
        "exposure: customer",
        f"effective_date: {EFFECTIVE_DATE}",
    ]
    if source_url:
        lines.append(f"source_url: {source_url}")
    lines += ["version: 1", "---", ""]
    return "\n".join(lines)


def main() -> int:
    if not PDF.exists():
        print(f"error: {PDF} not found — run from the repository root", file=sys.stderr)
        return 1

    files = split_into_files(read_ops(PDF))
    if not files:
        print("error: no source-file markers found; the PDF layout has changed", file=sys.stderr)
        return 1

    written = 0
    body_chars = 0
    missing_url = []
    for market, topic, ops in files:
        source_url, markdown = render(ops)
        if source_url is None:
            missing_url.append(f"{market}/{topic}")
        target = OUT / market / f"{topic}.md"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(front_matter(market, topic, source_url) + markdown, encoding="utf-8")
        written += 1
        body_chars += len(markdown)

    markets = sorted({market for market, _, _ in files})
    print(f"wrote {written} files across {len(markets)} markets into {OUT}/")
    print(f"markets: {' '.join(markets)}")
    print(f"body characters: {body_chars:,}")
    if missing_url:
        print(f"warning: no source URL recovered for: {', '.join(missing_url)}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
