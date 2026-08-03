"""Count the commitments agents made in past replies, before deciding what to withhold.

The corpus teaches phrasing. It also, unavoidably, teaches whatever the agent decided that
day — and agents decided things published policy does not offer. `ITALY10` reached a customer
who simply asked for a discount, because an agent had once issued it and that reply is now an
exemplar. In the knowledge corpus that code exists only inside `internal/warranty-discounts.md`,
a do-not-quote document, so the prompt's internal boundary never applied to it: by the time it
is an exemplar, it is customer-facing text.

That one was found by accident. This counts the rest, per class, so the withholding rules are
calibrated against what is actually there rather than against what the last incident happened
to be.

Reads the redacted file, writes nothing, and prints short excerpts. Excerpts are from already
redacted text but still describe real orders — keep the output local.

    python tools/ingest/survey_commitments.py --in data/exemplars.clean.jsonl
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# Each rule is (name, pattern, why it matters). Deliberately aimed at what an agent *granted*,
# not at what a customer asked for — the risk is a past decision being repeated as if it were
# policy, and a question carries no such weight.
RULES: list[tuple[str, re.Pattern[str], str]] = [
    ("promo code",
     re.compile(r"\b(?=[A-Z0-9]{4,20}\b)(?=[A-Z]*\d)[A-Z][A-Z0-9]{3,19}\b"),
     "a code issued to one customer works for anyone who is given it"),
    ("percentage offer",
     re.compile(r"\b\d{1,2}\s?%\s*(off|discount|reduction)\b|\b(discount|off)\b[^.]{0,20}\b\d{1,2}\s?%",
                re.I),
     "commits money on the strength of someone else's case"),
    # Dropped after measuring, and recorded so nobody adds them back:
    #
    # "free shipping or returns" — free returns and free delivery are *published* policy in
    # CA, US, UK and EU (11 chunks). Withholding those exchanges would delete correct content.
    #
    # "expedited upgrade" — every excerpt was a refusal: "even with expedited shipping, we are
    # unable to guarantee". The same trap the class D assertions fell into, where a rule fires
    # on a draft that restates the demand in order to deny it.
    ("free replacement or repair",
     re.compile(r"\b(free|no charge|at no cost|complimentary)\b[^.]{0,30}\b(replace|replacement|repair|exchange)\b",
                re.I),
     "a remedy granted case by case, not an entitlement"),
    ("fee waived",
     re.compile(r"\b(waive[d]?|waiving)\b[^.]{0,30}\b(fee|charge|cost|duty|duties|customs)\b", re.I),
     "waiving a charge is a decision, not a policy"),
    ("refund without return",
     re.compile(r"\b(refund|reimburse)[^.]{0,40}\b(without|no need to)\b[^.]{0,20}\breturn", re.I),
     "the most expensive goodwill there is"),
]

# Placeholders are the expected output of redaction, and shout in capitals.
PLACEHOLDER = re.compile(r"\[(?:CUSTOMER|AGENT|EMAIL|PHONE|ORDER|TRACKING|ADDRESS|POSTCODE|IBAN|CARD|SIGNATURE|LINK|QUOTED)\]")

# Words that look like a promo code but are not: shouted emphasis, common acronyms, and the
# company's own name. Without this the code rule reports mostly noise.
NOT_A_CODE = {
    "VAT", "EU", "UK", "USA", "US", "DHL", "UPS", "FEDEX", "USPS", "PDF", "URL", "ID", "OK",
    "ASAP", "FYI", "PS", "RE", "COD", "DPD", "GLS", "AM", "PM", "CET", "EST", "GMT",
}

# Warehouse and carrier identifiers, which share the shape of a promo code and are not one.
# `LT82229025` is part of a returns address and appears in published policy; `TBA…` is an
# Amazon tracking number. Counting them as codes made the first survey overstate by a third.
LOGISTICS_ID = re.compile(r"^(LT\d{6,}|TBA\d{8,}|\d{8,})$")

# Codes the company actually publishes, so an exemplar using one is repeating policy rather
# than inventing goodwill. Both appear in `knowledge/templates` and `knowledge/internal`:
# ITALY10 against a sold-out order, REPAIR1 against a warranty repair. Neither is a general
# discount, which is exactly how the ITALY10 incident happened — right code, wrong situation.
SANCTIONED_CODES = {"ITALY10", "REPAIR1"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--in", dest="source", default="data/exemplars.clean.jsonl")
    parser.add_argument("--samples", type=int, default=3)
    args = parser.parse_args()

    path = Path(args.source)
    if not path.exists():
        print(f"error: {path} not found", file=sys.stderr)
        return 1

    rows = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line]
    counts: dict[str, int] = {name: 0 for name, _, _ in RULES}
    samples: dict[str, list[str]] = {name: [] for name, _, _ in RULES}
    codes: dict[str, int] = {}
    flagged: set[int] = set()

    for index, row in enumerate(rows):
        # Only the agent's reply. What the customer asked for commits nobody.
        answer = PLACEHOLDER.sub(" ", row.get("answer", ""))

        for name, pattern, _ in RULES:
            match = pattern.search(answer)
            if not match:
                continue

            if name == "promo code":
                found = [c for c in pattern.findall(answer)
                         if c.upper() not in NOT_A_CODE
                         and not LOGISTICS_ID.match(c)
                         and c.upper() not in SANCTIONED_CODES]
                if not found:
                    continue
                for code in found:
                    codes[code] = codes.get(code, 0) + 1

            counts[name] += 1
            flagged.add(index)
            if len(samples[name]) < args.samples:
                start = max(0, match.start() - 55)
                samples[name].append(answer[start:match.end() + 35].replace("\n", " ").strip())

    total = len(rows)
    print(f"{total:,} exchanges\n")
    print(f"{'class':<28}{'replies':>9}{'share':>8}   why it matters")
    for name, _, why in RULES:
        n = counts[name]
        print(f"{name:<28}{n:>9,}{n / total:>8.1%}   {why}")

    print(f"\nexchanges matching at least one rule: {len(flagged):,} ({len(flagged) / total:.1%})")

    if codes:
        print(f"\nmost frequent code-shaped tokens ({len(codes):,} distinct):")
        for code, n in sorted(codes.items(), key=lambda kv: -kv[1])[:15]:
            print(f"  {code:<20} {n:>6,}")

    print("\nexcerpts (redacted text, still local-only):")
    for name, _, _ in RULES:
        for excerpt in samples[name]:
            print(f"  [{name}] …{excerpt[:120]}…")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
