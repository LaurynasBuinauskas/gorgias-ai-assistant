"""Does the retrieval query survive a conversation?

Production retrieves on `subject + newest customer message` (`BuildQuery`). That is right for
a fresh ticket and wrong the moment the customer answers with "yes please, go ahead" — the
question that needs policy is now an older message and the query carries almost no signal.
The code's own comment defends newest-only by warning that whole-thread queries dilute with
already-resolved topics. That warning was asserted, never measured. This measures both.

Two scenarios, each the failure mode of one candidate fix:

    followup   message 1 is a real question, the newest is a low-signal confirmation.
               Ground truth: the chunk answering message 1. Newest-only should collapse here.
    shift      message 1 asks topic X, it gets resolved, the newest asks topic Y.
               Ground truth: the chunk answering Y. Whole-thread should suffer here if the
               dilution warning is true.

Four query constructions, run through the production retrieval path per scenario:

    newest     subject + newest customer message            (production today)
    all        subject + every customer message
    last2      subject + the two newest customer messages
    signal     newest-first, taking messages until the query holds enough words (>= 12),
               at most three messages — the candidate rule

    python tools/evals/followup_recall.py --queries 40
"""

from __future__ import annotations

import argparse
import json
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from policy_recall import DEPTH, ask, dress, embed, policy_chunks, retrieve, secret  # noqa: E402

# The furniture a real confirmation arrives with, not bare stubs: the gate work showed
# greetings and sign-offs are exactly what scores when nothing else does.
FOLLOWUPS = [
    "Yes please, go ahead!",
    "Ok thank you.",
    "Yes, that would be great. Thanks so much!",
    "Hi, any update on this?\n\nBest,\nSam",
    "Hello, did you have a chance to look into it?\n\nKind regards,\nAlex",
    "Sounds good, please do that.",
    "Yes.",
    "Thanks! And happy holidays to the whole team!",
]

# What the customer says once the first topic is settled, before asking something new.
SETTLED = [
    "Thank you, that answered my question.",
    "Great, that worked. Thanks!",
    "Ok understood, thanks for explaining.",
]

MAX_MESSAGES = 3
MIN_WORDS = 12


def query_newest(subject: str, messages: list[str]) -> str:
    return " ".join([subject, messages[-1]])


def query_all(subject: str, messages: list[str]) -> str:
    return " ".join([subject, *messages])


def query_last2(subject: str, messages: list[str]) -> str:
    return " ".join([subject, *messages[-2:]])


def query_signal(subject: str, messages: list[str]) -> str:
    taken: list[str] = []
    words = 0
    for message in reversed(messages):
        taken.insert(0, message)
        words += len(message.split())
        if words >= MIN_WORDS or len(taken) == MAX_MESSAGES:
            break
    return " ".join([subject, *taken])


VARIANTS = {
    "newest": query_newest,
    "all": query_all,
    "last2": query_last2,
    "signal": query_signal,
}


def topic_rank(results: list[dict], chunk: dict) -> int:
    wanted = (chunk["topic"], chunk["market"])
    alternative = (chunk["topic"], "GLOBAL")
    topics = [(hit["topic"], hit["market"]) for hit in results]
    return next((i for i, t in enumerate(topics) if t in (wanted, alternative)), DEPTH)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--queries", type=int, default=40, help="conversations per scenario")
    parser.add_argument("--seed", type=int, default=17)
    parser.add_argument("--out", default=None)
    args = parser.parse_args()

    search_key, openai_key = secret("search-adminkey"), secret("openai-apikey")
    chunks = policy_chunks(search_key)
    print(f"{len(chunks)} policy chunk(s) indexed")

    rng = random.Random(args.seed)
    rows: list[dict] = []

    def measure(scenario: str, subject: str, messages: list[str], truth: dict) -> None:
        row: dict = {"scenario": scenario, "market": truth["market"], "topic": truth["topic"]}
        for name, build in VARIANTS.items():
            query = build(subject, messages)
            results = retrieve(search_key, query, embed(openai_key, query), truth["market"])
            row[name] = topic_rank(results, truth)
        rows.append(row)

    # Scenario 1: the question is old, the newest message confirms.
    print("scenario: followup")
    sampled = rng.sample(chunks, min(args.queries, len(chunks)))
    for position, chunk in enumerate(sampled, start=1):
        question = ask(openai_key, chunk["content"])
        if question.upper().startswith("SKIP"):
            continue
        subject, dressed = dress(question, position)
        measure("followup", subject, [dressed, rng.choice(FOLLOWUPS)], chunk)
        print(f"  {position}/{len(sampled)}", end="\r", flush=True)
    print(" " * 30, end="\r")

    # Scenario 2: topic X settled, the newest message asks topic Y. The subject still names
    # the thread's original concern, as it would in a real ticket.
    print("scenario: shift")
    pairs = []
    pool = [c for c in chunks if c["content"].strip()]
    while len(pairs) < args.queries and len(pool) >= 2:
        x, y = rng.sample(pool, 2)
        if x["topic"] != y["topic"]:
            pairs.append((x, y))
    for position, (x, y) in enumerate(pairs, start=1):
        question_x = ask(openai_key, x["content"])
        question_y = ask(openai_key, y["content"])
        if question_x.upper().startswith("SKIP") or question_y.upper().startswith("SKIP"):
            continue
        subject, dressed_x = dress(question_x, position)
        _, dressed_y = dress(question_y, position + 1)
        measure("shift", subject, [dressed_x, rng.choice(SETTLED), dressed_y], y)
        print(f"  {position}/{len(pairs)}", end="\r", flush=True)
    print(" " * 30, end="\r")

    print()
    for scenario in ("followup", "shift"):
        subset = [r for r in rows if r["scenario"] == scenario]
        if not subset:
            continue
        print(f"\n{scenario}: topic recall over {len(subset)} conversation(s)")
        print(f"{'variant':<10}{'@1':>7}{'@4':>7}{'@10':>7}")
        for name in VARIANTS:
            ranks = [r[name] for r in subset]
            at = lambda k: sum(1 for r in ranks if r < k) / len(ranks)  # noqa: E731
            marker = "  <- production" if name == "newest" else ""
            print(f"{name:<10}{at(1):>6.0%}{at(4):>7.0%}{at(10):>7.0%}{marker}")

    if args.out:
        Path(args.out).write_text(json.dumps(rows, indent=2, ensure_ascii=False),
                                  encoding="utf-8")
        print(f"\nper-conversation ranks written to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
