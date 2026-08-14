"""Offline tests for staged client uploads: conversion, front matter, validation, merge.

The properties that matter most are the boundary ones: a client upload can never set its
own exposure, a staged (market, topic) genuinely supersedes its git-managed file, and the
validation refuses the content classes that burned the exemplar corpus — while keeping the
refusal-shaped sentences and logistics identifiers that earlier pattern attempts wrongly
flagged. Run from anywhere:

    python tools/ingest/test_staged.py
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
os.chdir(REPO_ROOT)
sys.path.insert(0, str(REPO_ROOT / "tools" / "ingest"))

from staged import StagedDoc, build, to_policy_text, validate  # noqa: E402

failures: list[str] = []


def check(name: str, condition: bool, detail: str = "") -> None:
    print(f"  [{'ok' if condition else 'FAIL'}] {name}")
    if not condition:
        failures.append(f"{name}{': ' + detail if detail else ''}")


def doc(markdown: str, market: str = "DE", topic: str = "shipping-and-returns") -> StagedDoc:
    return StagedDoc(
        blob_name=f"{market}/{topic}/test.md", market=market, topic=topic,
        file_name="test.md", uploaded_by="Test", markdown=markdown)


POLICY_TEXT = (
    "# Shipping and returns\n\n"
    "Returns are accepted within 30 days of delivery. A 50% deposit is required for "
    "bespoke orders, and refunds are issued to the original payment method within 14 days. "
    "Send returns to Time Resistance, LT82229025, Vilnius."
)


def main() -> int:
    print("== front matter is written by the system ==")
    source_path, text = to_policy_text(doc(POLICY_TEXT))
    check("exposure is locked to customer", "exposure: customer" in text)
    check("market and topic come from the upload fields",
          "market: DE" in text and "topic: shipping-and-returns" in text)
    check("source path counts as policy for citation display",
          "/policy/" in source_path and source_path.endswith(".md"))

    headingless, _ = "", to_policy_text(doc("Just a paragraph of policy text."))[1]
    check("a heading is synthesized when the upload has none",
          "# Shipping And Returns" in headingless or "# Shipping And Returns" in _)

    print("\n== conversion ==")
    bom = build("DE/t/test.md", "de", "T", "test.md", "Test", b"\xef\xbb\xbf# Title\n\nBody")
    check("markdown decodes through a BOM and normalizes market case",
          bom.markdown.startswith("# Title") and bom.market == "DE" and bom.topic == "t")

    print("\n== validation keeps what earlier patterns wrongly flagged ==")
    check("clean policy text passes", validate(doc(POLICY_TEXT)) == [])
    check("a refusal of a discount passes",
          validate(doc(POLICY_TEXT + " We are unable to offer a 60% discount in any case."))
          == [])
    # The company's own contact identity is policy content, not a leak — the first live run
    # blocked the real DE policy on exactly these.
    check("the company's own email and links pass",
          validate(doc(POLICY_TEXT + " Write to kundenservice@timeresistance.com or see "
                                     "[returns](https://timeresistance.de/pages/returns)."))
          == [])

    print("\n== validation blocks what must never reach a draft ==")
    pii = validate(doc(POLICY_TEXT + " Contact jane.doe@example.com with questions."))
    check("a non-company email address is flagged as pii", any(f.kind == "pii" for f in pii))

    repeated = validate(doc(
        POLICY_TEXT + " Contact jane.doe@example.com now. Again: jane.doe@example.com."))
    check("repeated findings are reported once",
          len([f for f in repeated if f.kind == "pii"]) == 1)

    code = validate(doc(POLICY_TEXT + " Use code SUMMER25 at checkout."))
    check("a promo-code shape is flagged",
          any(f.kind == "promo-code" and "SUMMER25" in f.message for f in code))

    offer = validate(doc(POLICY_TEXT + " We offer a 10% discount on your next order."))
    check("an open-ended percentage offer is flagged",
          any(f.kind == "discount-offer" for f in offer))

    check("a stub upload is flagged as too short",
          any(f.kind == "too-short" for f in validate(doc("tiny"))))

    print("\n== staged uploads supersede their git-managed policy ==")
    import ingest

    merged = ingest.collect([doc(POLICY_TEXT)])
    staged_chunks = [d for d in merged if d.source_path.startswith("staged/")]
    git_same_slot = [d for d in merged
                     if d.source_path == "knowledge/policy/DE/shipping-and-returns.md"]
    git_other = [d for d in merged
                 if d.source_path == "knowledge/policy/DE/faqs.md"]
    check("staged chunks are present with customer exposure",
          len(staged_chunks) > 0 and all(d.exposure == "customer" for d in staged_chunks))
    check("the superseded git file is gone", git_same_slot == [])
    check("other documents in the same market are untouched", len(git_other) > 0)
    check("only the policy corpus is affected",
          any(d.corpus == "template" for d in merged)
          and any(d.corpus == "internal" for d in merged))

    print("\n== docx conversion ==")
    try:
        import mammoth  # noqa: F401

        import io
        import zipfile

        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr("[Content_Types].xml",
                             '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                             '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                             '<Default Extension="xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
            archive.writestr("_rels/.rels",
                             '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                             '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
            archive.writestr("word/document.xml",
                             '<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
                             '<w:body><w:p><w:r><w:t>Returns within 30 days.</w:t></w:r></w:p></w:body></w:document>')
        converted = build("DE/t/policy.docx", "DE", "t", "policy.docx", "Test",
                          buffer.getvalue())
        check("a Word file converts to markdown text",
              "Returns within 30 days." in converted.markdown)
    except ImportError:
        print("  [skip] mammoth not installed — conversion is covered in the publish flow")

    print(f"\n{len(failures)} failure(s)")
    for failure in failures:
        print(f"  - {failure}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
