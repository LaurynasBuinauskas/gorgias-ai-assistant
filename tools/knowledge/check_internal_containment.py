"""P-4 acceptance: internal procedure must be marked internal and must not leak into
customer-facing corpora.

The vocabulary below is drawn from the real internal document. If any of it appears in a file
an agent could quote to a customer, the exposure boundary the whole design rests on has
already failed at rest — before retrieval or prompting get a chance to.

Run from the repository root:
    python tools/knowledge/check_internal_containment.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

KNOWLEDGE = Path("knowledge")
INTERNAL = KNOWLEDGE / "internal"

# Terms that identify internal systems and process. A customer must never read any of these.
#
# Narrower than policy-adherence-eval-plan.md sec 2 Class A proposed, and deliberately so.
# That list also banned REPAIR1 and "warehouse" — but the team's own approved templates hand
# REPAIR1 to customers ("At checkout, use the code REPAIR1") and say "shipped from our
# warehouse". Banning them would fail drafts for correctly reproducing approved wording,
# which is a false-positive generator on a release-blocking class. See D-6 in
# docs/open-questions.md.
BANNED_OUTSIDE_INTERNAL = ["Asana", "Shopify", "CS: RETURNS", "Odoo", "kokybe"]


def main() -> int:
    internal_files = sorted(INTERNAL.rglob("*.md"))
    if not internal_files:
        print("error: no internal files — run convert_internal.py first", file=sys.stderr)
        return 1

    problems = []

    for path in internal_files:
        text = path.read_text(encoding="utf-8")
        if not re.search(r"^exposure:\s*internal\s*$", text, re.M):
            problems.append(f"{path}: not marked exposure: internal")

    customer_files = [
        p for p in KNOWLEDGE.rglob("*.md")
        if INTERNAL not in p.parents and p.name != "README.md"
    ]
    for path in customer_files:
        text = path.read_text(encoding="utf-8")
        for term in BANNED_OUTSIDE_INTERNAL:
            if re.search(rf"\b{re.escape(term)}", text, re.I):
                problems.append(f"{path}: contains internal term '{term}'")

    print(f"internal files: {len(internal_files)} (all exposure: internal = "
          f"{not any('not marked' in p for p in problems)})")
    print(f"customer-facing files scanned: {len(customer_files)}")
    print(f"vocabulary checked: {', '.join(BANNED_OUTSIDE_INTERNAL)}")
    print(f"problems: {len(problems)}")
    for problem in problems[:20]:
        print("  -", problem)

    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
