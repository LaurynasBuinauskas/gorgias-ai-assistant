"""Split knowledge files into the units retrieval should return.

The unit of meaning differs per corpus, so the strategy does too:

* **policy** and **internal** — heading-aware. A clause is never separated from the heading
  that scopes it, and every chunk carries a breadcrumb (`DE > Warranty > Garantieausschlusse`)
  in its embedded text so the vector knows its own scope.
* **template** — never split. A template already *is* the unit a hit should return: one
  complete, approved reply.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

# ~4 characters per token, so this lands around 500-800 tokens as the pipeline design asks.
MAX_CHUNK_CHARS = 2_800
OVERLAP_CHARS = 400
HEADING = re.compile(r"^(#{1,6})\s+(.*)$")


@dataclass(frozen=True)
class Chunk:
    title: str
    content: str
    ordinal: int


def split(markdown: str, corpus: str, breadcrumb_root: str) -> list[Chunk]:
    if corpus == "template":
        body = markdown.strip()
        return [Chunk(breadcrumb_root, body, 0)] if body else []
    return _heading_aware(markdown, breadcrumb_root)


def _heading_aware(markdown: str, root: str) -> list[Chunk]:
    """Pack consecutive sections up to the target size, splitting only oversized ones.

    One chunk per heading sounds tidy and retrieves badly: policy documents are full of
    two-line clauses, and a 120-token chunk arrives at the model without enough around it to
    interpret. Packing neighbours keeps chunks near the 500-800 token target while never
    merging across a file boundary, since each file is a different market or topic.
    """
    chunks: list[Chunk] = []
    pending: list[str] = []
    pending_title = root
    pending_length = 0

    def flush() -> None:
        nonlocal pending, pending_title, pending_length
        if pending:
            chunks.append(Chunk(pending_title, "\n\n".join(pending), len(chunks)))
            pending, pending_length = [], 0

    for breadcrumb, body in _sections(markdown, root):
        for part in _fit(body):
            # The breadcrumb is part of the embedded text, not just metadata: a chunk read in
            # isolation must still say which market and topic it belongs to.
            block = f"{breadcrumb}\n\n{part}"
            if pending and pending_length + len(block) > MAX_CHUNK_CHARS:
                flush()
            if not pending:
                pending_title = breadcrumb
            pending.append(block)
            pending_length += len(block) + 2

    flush()
    return chunks


def _sections(markdown: str, root: str) -> list[tuple[str, str]]:
    trail: dict[int, str] = {}
    current = root
    buffer: list[str] = []
    sections: list[tuple[str, str]] = []

    for line in markdown.split("\n"):
        if match := HEADING.match(line):
            if body := "\n".join(buffer).strip():
                sections.append((current, body))
            buffer = []
            level, text = len(match.group(1)), match.group(2).strip()
            trail = {k: v for k, v in trail.items() if k < level}
            # The file's H1 is already the last component of the root breadcrumb; repeating
            # it would put "DE > Warranty > Warranty" into every embedded chunk.
            if not (level == 1 and root.endswith(f"> {text}")):
                trail[level] = text
            current = " > ".join([root, *(trail[k] for k in sorted(trail))])
        else:
            buffer.append(line)

    if body := "\n".join(buffer).strip():
        sections.append((current, body))
    return sections


def _fit(body: str) -> list[str]:
    """Split an oversized section at paragraph boundaries, with overlap for continuity."""
    if len(body) <= MAX_CHUNK_CHARS:
        return [body]

    parts: list[str] = []
    current: list[str] = []
    length = 0
    for paragraph in body.split("\n\n"):
        if current and length + len(paragraph) > MAX_CHUNK_CHARS:
            parts.append("\n\n".join(current))
            tail = "\n\n".join(current)[-OVERLAP_CHARS:]
            current, length = [tail], len(tail)
        current.append(paragraph)
        length += len(paragraph) + 2

    if current:
        parts.append("\n\n".join(current))
    return parts
