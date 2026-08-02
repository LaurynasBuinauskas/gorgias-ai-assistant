"""Re-apply the current redaction rules to an already-extracted exemplar file.

Exists because a human review of fifty indexed exchanges found leaks that every automated
check had passed — a street name the phone rule had orphaned, a per-recipient tracking link, a
third-party name inside an order-confirmation table. The rules were fixed; this re-applies them
without re-fetching 15,595 tickets from Gorgias.

Re-applying redaction to already-redacted text is safe in one direction only: the new rules can
remove more, never restore what the old ones took. The worst case was an address whose house
number a rule had already eaten — "Holunderweg 12 45143 Essen" reduced to "Holunderweg [PHONE]
Essen", where the street survived because the address rule no longer had a number to anchor to.
The new bare-street rule recovers those: nought of 18,555 exchanges still carry a compound
street name afterwards, measured rather than assumed.

What remains is a bare town — "[ADDRESS], [POSTCODE] Backnang". That is not withheld. A town
with the street and postcode already gone narrows a customer to some tens of thousands of
people, and discarding those exchanges cost a tenth of the corpus to identify nobody.

    python tools/ingest/sanitize_exemplars.py --in data/exemplars.deduped.jsonl \
        --out data/exemplars.clean.jsonl
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from redaction import engraved_third_party_name, redact, residual_identifiers  # noqa: E402

# Below this an exchange no longer carries a question and an answer worth retrieving.
MIN_QUESTION_CHARS = 20
MIN_ANSWER_CHARS = 40


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--in", dest="source", default="data/exemplars.deduped.jsonl")
    parser.add_argument("--out", dest="target", default="data/exemplars.clean.jsonl")
    args = parser.parse_args()

    rows = [json.loads(line) for line
            in Path(args.source).read_text(encoding="utf-8").splitlines() if line]
    print(f"read {len(rows):,} exchange(s)")

    kept: list[dict] = []
    changed = dropped_short = dropped_residual = dropped_engraved = 0

    for row in rows:
        question, answer = redact(row["question"]), redact(row["answer"])
        if (question, answer) != (row["question"], row["answer"]):
            changed += 1

        # Stripping a quoted chain often leaves a one-line courtesy. That is not an exemplar.
        if len(question.strip()) < MIN_QUESTION_CHARS or len(answer.strip()) < MIN_ANSWER_CHARS:
            dropped_short += 1
            continue

        combined = f"{question}\n{answer}"
        if engraved_third_party_name(combined):
            dropped_engraved += 1
            continue
        if residual_identifiers(combined):
            dropped_residual += 1
            continue

        kept.append(dict(row, question=question, answer=answer))

    print(f"  re-redacted        {changed:,}")
    print(f"  dropped, too short {dropped_short:,}  (quoted chain was the whole message)")
    print(f"  dropped, engraved  {dropped_engraved:,}  (third party's name on a gift)")
    print(f"  dropped, residual  {dropped_residual:,}  (fail-closed check)")
    print(f"  kept               {len(kept):,}")

    target = Path(args.target)
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("w", encoding="utf-8") as handle:
        for row in kept:
            handle.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(f"\nwrote {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
