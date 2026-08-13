"""Staged client uploads: fetch, convert, validate, merge into an ingest run.

The upload API stores what a client worker sent; this module decides what the corpus makes
of it. Three responsibilities, kept separate from `ingest.py` so each is testable offline:

    fetch       download staged blobs and their attribution metadata (the only network here)
    build       convert .docx to markdown and write the front matter the system controls —
                a client upload can never choose its own `exposure`
    validate    the content checks that block a publish: PII, promo-code shapes, and
                percentage offers, reusing the exact patterns the exemplar corpus is swept
                with, negation handling included

A staged document *replaces* the git-managed policy files that share its (market, topic) —
replace-the-document is the unit of change, decided in `docs/policy-upload-plan.md` §8.
"""

from __future__ import annotations

import base64
import io
import re
import sys
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
# Private names imported deliberately: these patterns were measured and corrected against
# 17k real exchanges, and a fresh copy here would drift from those lessons. Same package,
# same maintainers, one source of truth.
from redaction import (  # noqa: E402
    _CODE_SHAPED,
    _LOGISTICS_ID,
    _NEGATION,
    _NOT_A_CODE,
    _PERCENTAGE_OFFER,
    _SENTENCE,
    residual_identifiers,
)

DRAFTS_CONTAINER = "knowledge-drafts"

HEADING = re.compile(r"^#\s+(.+)$", re.M)


@dataclass(frozen=True)
class StagedDoc:
    blob_name: str
    market: str
    topic: str
    file_name: str
    uploaded_by: str
    markdown: str


@dataclass(frozen=True)
class ValidationFinding:
    blob_name: str
    kind: str
    message: str


def fetch(blob_names: list[str], connection: str) -> list[StagedDoc]:
    """Download staged blobs. Import is local so everything else works without the SDK."""
    from azure.storage.blob import ContainerClient

    container = ContainerClient.from_connection_string(connection, DRAFTS_CONTAINER)
    docs = []
    for name in blob_names:
        blob = container.get_blob_client(name)
        metadata = blob.get_blob_properties().metadata or {}
        parts = name.split("/")
        docs.append(build(
            blob_name=name,
            market=metadata.get("market") or (parts[0] if len(parts) > 1 else "GLOBAL"),
            topic=metadata.get("topic") or (parts[1] if len(parts) > 2 else ""),
            file_name=metadata.get("fileName") or Path(name).name,
            uploaded_by=_decode_uploader(metadata.get("uploadedBy", "")),
            data=blob.download_blob().readall(),
        ))
    return docs


def build(blob_name: str, market: str, topic: str, file_name: str,
          uploaded_by: str, data: bytes) -> StagedDoc:
    markdown = (docx_to_markdown(data) if file_name.lower().endswith(".docx")
                else data.decode("utf-8-sig"))
    return StagedDoc(
        blob_name=blob_name,
        market=market.upper(),
        topic=topic.lower(),
        file_name=file_name,
        uploaded_by=uploaded_by,
        markdown=markdown.strip(),
    )


def docx_to_markdown(data: bytes) -> str:
    import mammoth

    value = mammoth.convert_to_markdown(io.BytesIO(data)).value
    # Mammoth escapes markdown punctuation — "within 30 days\." — which would put literal
    # backslashes into indexed policy text. Policy prose wants the characters back; a
    # genuine backslash-before-punctuation sequence in a Word policy is not a real case.
    return re.sub(r"\\([\\.*_#\-+\[\]()!`>])", r"\1", value)


def to_policy_text(doc: StagedDoc) -> tuple[str, str]:
    """The (source_path, full text) pair the ingest pipeline treats like any policy file.

    The front matter is written here, by the system, from the upload's form fields.
    `exposure: customer` is not a default — it is the only value this path can produce,
    because exposure is the boundary that keeps do-not-quote material out of drafts and a
    client upload must never be able to cross it.
    """
    title_match = HEADING.search(doc.markdown)
    title = title_match.group(1) if title_match else doc.topic.replace("-", " ").title()
    body = doc.markdown if title_match else f"# {title}\n\n{doc.markdown}"

    front = (f"---\nmarket: {doc.market}\ntopic: {doc.topic}\nexposure: customer\n---\n\n")
    source_path = (f"staged/policy/{doc.market}/{doc.topic}/"
                   + re.sub(r"\.docx$", ".md", doc.file_name, flags=re.I))
    return source_path, front + body


def validate(doc: StagedDoc) -> list[ValidationFinding]:
    findings: list[ValidationFinding] = []

    def flag(kind: str, message: str) -> None:
        findings.append(ValidationFinding(doc.blob_name, kind, message))

    text = doc.markdown
    if len(text) < 80:
        flag("too-short", "The document is shorter than a policy paragraph — is it the "
                          "right file?")

    for finding in residual_identifiers(text):
        flag("pii", f"Personal data must not appear in published policy — {finding.kind}: "
                    f"\"{finding.value}\"")

    for match in _CODE_SHAPED.finditer(text):
        token = match.group(0)
        if token in _NOT_A_CODE or _LOGISTICS_ID.match(token):
            continue
        flag("promo-code", f"'{token}' is shaped like a promo code. Published policy must "
                           "not hand out codes; remove it or spell the word out.")

    for sentence_match in _SENTENCE.finditer(text):
        sentence = sentence_match.group(0)
        if _PERCENTAGE_OFFER.search(sentence) and not _NEGATION.search(sentence):
            flag("discount-offer", "This sentence offers a percentage discount: "
                                   f"\"{sentence.strip()[:90]}\". Statements like a 50% "
                                   "deposit are fine; open-ended offers are not.")

    return findings


def _decode_uploader(encoded: str) -> str:
    try:
        return base64.b64decode(encoded).decode("utf-8")
    except (ValueError, UnicodeDecodeError):
        return encoded
