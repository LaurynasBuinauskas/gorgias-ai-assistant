"""Blind pairwise judging of two eval runs, to answer whether exemplars improve draft quality.

The eval suite cannot answer this. Its assertions are pass/fail on safety properties — leakage,
language, market, refusal — and a draft can satisfy every one of them while reading like a
form letter. Comparing the two runs mechanically showed nothing, but a control run proved why:
running the *same* configuration twice produces the same scale of difference, because the model
is nondeterministic. Text diffing measures noise.

So the drafts are judged instead, and the design exists to stop the judge measuring noise too:

**Blind and order-randomised.** The judge never learns which draft came from which run, and
which one is shown first is decided per case by a seeded coin flip. Judges have a well known
preference for whichever option they read first, so if position were fixed the winner would be
partly an artefact of layout.

**Ties are a first-class verdict.** Forcing a choice between two adequate drafts manufactures a
difference, and "no discernible difference" is the answer most worth being able to return.

**Position bias is measured, not assumed.** The summary reports how often the first-shown draft
won regardless of which run produced it. If that sits far from half, the verdict is unsafe and
the run should be repeated with more cases rather than reported.

    python tools/evals/judge_drafts.py drafts-off.json drafts-on.json
"""

from __future__ import annotations

import argparse
import json
import random
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

MODEL = "gpt-4.1-2025-04-14"

# A stronger model than the one that wrote the drafts (gpt-4.1-mini). A judge no better than
# the author mostly rewards its own habits.
RUBRIC = """You are comparing two draft replies written by customer support agents at a \
leather goods company, for the same customer message.

Judge only on these, in order of weight:

1. ANSWERS THE QUESTION. Does it address what the customer actually asked, specifically, \
rather than restating it and offering to help further?
2. CONCRETE AND ACTIONABLE. Does it tell the customer what will happen and what to do next, \
with specifics, rather than generalities?
3. TONE. Warm, direct, human. Not stiff, not padded with filler courtesy.
4. NO INVENTED COMMITMENTS. A draft that promises something it cannot know — a delivery date, \
a refund amount, a discount — is worse even if it reads better.

Ignore length unless one is padded. Ignore formatting differences. If the two are of \
comparable quality, say TIE — do not manufacture a preference.

Reply with exactly one line:
VERDICT: A or VERDICT: B or VERDICT: TIE
then one short sentence of reason."""


def secret(name: str) -> str:
    cli = shutil.which("az")
    if cli is None:
        raise SystemExit("the Azure CLI ('az') is not on PATH")
    result = subprocess.run(
        [cli, "keyvault", "secret", "show", "--vault-name", "gorgias-assistant-kv",
         "--name", name, "--query", "value", "-o", "tsv"],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"could not read {name}: {result.stderr.strip()[:200]}")
    return result.stdout.strip()


def ask(key: str, question: str, first: str, second: str) -> tuple[str, str]:
    body = {
        "model": MODEL,
        "temperature": 0,
        "messages": [
            {"role": "system", "content": RUBRIC},
            {"role": "user", "content":
                f"CUSTOMER MESSAGE\n{question}\n\n"
                f"--- DRAFT A ---\n{first}\n\n--- DRAFT B ---\n{second}"},
        ],
    }
    request = urllib.request.Request(
        "https://api.openai.com/v1/chat/completions",
        data=json.dumps(body).encode(), method="POST")
    request.add_header("Content-Type", "application/json")
    request.add_header("Authorization", f"Bearer {key}")

    with urllib.request.urlopen(request, timeout=180) as response:
        payload = json.loads(response.read())

    text = payload["choices"][0]["message"]["content"].strip()
    match = re.search(r"VERDICT:\s*(A|B|TIE)", text, re.IGNORECASE)
    return (match.group(1).upper() if match else "TIE"), text


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", help="drafts JSON from the run without exemplars")
    parser.add_argument("candidate", help="drafts JSON from the run with exemplars")
    parser.add_argument("--seed", type=int, default=4242)
    parser.add_argument("--out", default=None, help="write per-case verdicts here")
    args = parser.parse_args()

    baseline = {d["id"]: d for d in json.loads(Path(args.baseline).read_text(encoding="utf-8"))}
    candidate = {d["id"]: d for d in json.loads(Path(args.candidate).read_text(encoding="utf-8"))}

    key = secret("openai-apikey")
    rng = random.Random(args.seed)

    wins = {"baseline": 0, "candidate": 0, "tie": 0}
    first_shown_won = 0
    decided = 0
    rows = []

    for case_id, base in baseline.items():
        cand = candidate.get(case_id)
        if cand is None:
            continue
        # A declined draft is a policy decision, not prose, and judging it compares refusals.
        if base["outcome"] != "drafted" or cand["outcome"] != "drafted":
            continue
        if not base["body"].strip() or not cand["body"].strip():
            continue

        baseline_first = rng.random() < 0.5
        first, second = ((base, cand) if baseline_first else (cand, base))
        verdict, reason = ask(key, base.get("question") or case_id,
                              first["body"], second["body"])

        if verdict == "TIE":
            wins["tie"] += 1
            winner = "tie"
        else:
            chose_first = verdict == "A"
            decided += 1
            first_shown_won += 1 if chose_first else 0
            picked_baseline = chose_first == baseline_first
            winner = "baseline" if picked_baseline else "candidate"
            wins[winner] += 1

        rows.append({"id": case_id, "winner": winner,
                     "baselineShownFirst": baseline_first, "reason": reason})
        print(f"  {case_id:<32} {winner}")

    total = sum(wins.values())
    if total == 0:
        print("no comparable drafts", file=sys.stderr)
        return 1

    print(f"\ncases judged        {total}")
    print(f"  without exemplars {wins['baseline']:>3}  ({wins['baseline'] / total:.0%})")
    print(f"  with exemplars    {wins['candidate']:>3}  ({wins['candidate'] / total:.0%})")
    print(f"  tie               {wins['tie']:>3}  ({wins['tie'] / total:.0%})")

    if decided:
        bias = first_shown_won / decided
        print(f"\nposition bias       first-shown draft won {bias:.0%} of {decided} decided")
        if not 0.35 <= bias <= 0.65:
            print("  WARNING: the judge is picking by position, so the verdict above is unsafe")

    if args.out:
        Path(args.out).write_text(json.dumps(rows, indent=2), encoding="utf-8")
        print(f"\nper-case verdicts written to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
