"""Draw a random sample of indexed exemplars for a human to read before the corpus is trusted.

The automated checks answer "does any pattern still match?". They cannot answer "would a
customer be identifiable to someone who knows them?" — a redacted exchange can still name a
one-off product, a delivery dispute, a town with one stockist. That judgement is a person's,
so this produces something a person can actually sit and read.

The sample is drawn from the **index**, not the file, so what is reviewed is what is live.
It is stratified by length: short exchanges dominate the corpus (the median is 746
characters), and a uniform sample would be almost entirely one-liners while the long tail is
exactly where unusual identifiers survive.

The output is customer-derived text and lands under `data/`, which is git-ignored. Do not
move it into `docs/` to share it — send it to the reviewer directly and delete it afterwards.

    python tools/ingest/review_sample.py
"""

from __future__ import annotations

import argparse
import json
import random
import shutil
import subprocess
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from redaction import residual_identifiers  # noqa: E402

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
INDEX = "tickets-v1"
API_VERSION = "2024-07-01"

# Bands are by content length. The boundaries sit near the p50 and p99 of the corpus, so the
# long tail — a fraction of a percent of exchanges — gets a third of the reviewer's attention.
BANDS = [("short", 0, 750), ("medium", 750, 4_200), ("long", 4_200, 10**9)]


def secret(name: str) -> str:
    cli = shutil.which("az")
    if cli is None:
        raise SystemExit("the Azure CLI ('az') is not on PATH")
    result = subprocess.run(
        [cli, "keyvault", "secret", "show", "--vault-name", VAULT,
         "--name", name, "--query", "value", "-o", "tsv"],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"could not read {name}: {result.stderr.strip()[:200]}")
    return result.stdout.strip()


def fetch_all(key: str) -> list[dict]:
    """Page the whole ticket index. 18,555 documents of text is a few hundred megabytes at
    most, and holding it lets the sample be stratified rather than 'whatever came back first'.
    """
    documents: list[dict] = []
    skip = 0
    while True:
        body = {"search": "*", "top": 1000, "skip": skip,
                "select": "id,ticketId,title,content,resolvedAt"}
        request = urllib.request.Request(
            f"https://{SERVICE}.search.windows.net/indexes/{INDEX}/docs/search"
            f"?api-version={API_VERSION}",
            data=json.dumps(body).encode(), method="POST")
        request.add_header("Content-Type", "application/json")
        request.add_header("api-key", key)
        with urllib.request.urlopen(request, timeout=180) as response:
            page = json.loads(response.read())["value"]
        if not page:
            break
        documents.extend(page)
        skip += len(page)
        print(f"  fetched {len(documents):,}", flush=True)
        # Azure Search refuses skip beyond 100,000; well clear at this corpus size, but the
        # loop would silently truncate rather than error, so make the assumption explicit.
        if skip >= 100_000:
            raise SystemExit("corpus exceeds the skip ceiling — switch to a range-based scan")
    return documents


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default="data/exemplar-review-sample.md")
    parser.add_argument("--size", type=int, default=50)
    # Fixed so the sample is reproducible: a reviewer who finds a problem in item 31 and a
    # second reader asked to confirm it must be looking at the same item 31.
    parser.add_argument("--seed", type=int, default=20260801)
    args = parser.parse_args()

    print(f"reading {INDEX}")
    documents = fetch_all(secret("search-adminkey"))
    print(f"corpus {len(documents):,} document(s)")

    rng = random.Random(args.seed)
    per_band = args.size // len(BANDS)

    sample: list[tuple[str, dict]] = []
    for name, low, high in BANDS:
        band = [d for d in documents if low <= len(d["content"]) < high]
        drawn = rng.sample(band, min(per_band, len(band)))
        sample.extend((name, document) for document in drawn)
        print(f"  {name:<7} {len(band):>7,} available, {len(drawn)} drawn")

    # Top up from the whole corpus if a band was short, so the reviewer always gets --size.
    if len(sample) < args.size:
        chosen = {document["id"] for _, document in sample}
        remaining = [d for d in documents if d["id"] not in chosen]
        sample.extend(("topup", d) for d in rng.sample(remaining, args.size - len(sample)))

    rng.shuffle(sample)

    lines = [
        "# Exemplar review sample",
        "",
        f"{len(sample)} exchanges drawn at random from the {len(documents):,} indexed in "
        f"`{INDEX}`, stratified by length (seed `{args.seed}`, reproducible).",
        "",
        "## What you are checking",
        "",
        "The automated sweep already confirms no pattern matches an email address, phone "
        "number, order ID or postcode. It cannot judge **identifiability**: whether someone "
        "who knows this customer would recognise them from what is left. Read for that.",
        "",
        "Flag an exchange if it contains:",
        "",
        "- a name, place or company the redaction missed",
        "- a detail unusual enough to identify one person — a bespoke order, a named "
        "complaint, a single-stockist town",
        "- anything embarrassing or sensitive about a customer, redacted or not",
        "- internal commentary that should never be echoed toward a customer",
        "",
        "Anything flagged is removed from the index before beta. If more than two or three "
        "of fifty are flagged, the redaction rules need work rather than the individual "
        "documents being deleted.",
        "",
        "| | |",
        "|---|---|",
        "| Reviewer | _(name)_ |",
        "| Date | _(date)_ |",
        "| Flagged | _(list item numbers, or 'none')_ |",
        "| Verdict | _(approve / rework redaction)_ |",
        "",
        "---",
        "",
    ]

    for index, (band, document) in enumerate(sample, start=1):
        residual = residual_identifiers(document["content"])
        lines += [
            f"## {index}. ticket {document.get('ticketId', '?')} "
            f"({band}, {len(document['content']):,} chars)",
            "",
        ]
        if residual:
            # Should be unreachable — the ingest boundary refuses these. Shown rather than
            # asserted so a reviewer sees it instead of the script dying halfway through.
            lines += [f"> **Automated check flagged:** "
                      f"{[(f.kind, f.value[:30]) for f in residual]}", ""]
        lines += ["```", document["content"].strip(), "```", "", "**Flag?** _(yes / no)_", ""]

    output = Path(args.out)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines), encoding="utf-8")
    print(f"\nwrote {output} — {len(sample)} exchanges for review")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
