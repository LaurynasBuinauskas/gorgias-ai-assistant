"""Measure whether exemplar retrieval finds the right past exchange.

The draft-quality question — do exemplars make replies better — has no trustworthy instrument
here: blind pairwise judging produced a larger apparent gap between a configuration and
*itself* than between exemplars on and off, so it finds effects that do not exist. This script
measures something narrower and checkable instead: given a customer question, does retrieval
return the exchange that actually answers it?

The method is recall@k against a known answer. Take N exchanges as a pool, hold out a sample,
paraphrase each held-out customer question so it no longer shares the original's wording, then
ask which indexing strategy puts the original back in the top k:

    content  — embed "Customer asked: … Support replied: …", which is what `tickets-v1` does
    question — embed the customer's question alone

The hypothesis this exists to test is that the first is diluted. A retrieval unit that mixes
the question with its reply matches partly on agent phrasing and thread pleasantries, which is
why a warranty query returned an exchange opening "Thank you for your speedy reply".

Embeddings only — no search service and no index rebuild, so a design can be rejected before
anything is paid for.

    python tools/evals/exemplar_recall.py --pool 400 --queries 40
"""

from __future__ import annotations

import argparse
import json
import math
import random
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

VAULT = "gorgias-assistant-kv"
EMBEDDING_MODEL = "text-embedding-3-small"
EMBEDDING_DIMENSIONS = 1536
PARAPHRASE_MODEL = "gpt-4.1-mini-2025-04-14"
EMBED_BATCH = 96

PARAPHRASE_PROMPT = """Rewrite this customer support question as a different customer would \
ask the same thing.

Keep the substance identical — the same product issue, the same request. Change the wording, \
the sentence structure and the greeting. Do not reuse distinctive phrases from the original, \
and do not add details that are not there. Reply with the rewritten question only."""


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


def post(url: str, key: str, body: dict) -> dict:
    """POST with backoff. Rate limiting is a pause, not a failure."""
    for attempt in range(6):
        request = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST")
        request.add_header("Content-Type", "application/json")
        request.add_header("Authorization", f"Bearer {key}")
        try:
            with urllib.request.urlopen(request, timeout=300) as response:
                return json.loads(response.read())
        except urllib.error.HTTPError as error:
            if error.code in (429, 500, 502, 503, 504):
                time.sleep(min(2 ** attempt, 30))
                continue
            raise SystemExit(
                f"{url}: HTTP {error.code}\n{error.read().decode(errors='replace')[:300]}"
            ) from error
    raise SystemExit(f"{url}: gave up after repeated rate limiting")


def embed(key: str, texts: list[str]) -> list[list[float]]:
    vectors: list[list[float]] = []
    for start in range(0, len(texts), EMBED_BATCH):
        batch = [t[:12_000] for t in texts[start:start + EMBED_BATCH]]
        payload = post("https://api.openai.com/v1/embeddings", key,
                       {"model": EMBEDDING_MODEL, "input": batch,
                        "dimensions": EMBEDDING_DIMENSIONS})
        vectors.extend(item["embedding"] for item in payload["data"])
        print(f"  embedded {len(vectors):,}/{len(texts):,}", end="\r", flush=True)
    print(" " * 40, end="\r")
    return vectors


def paraphrase(key: str, question: str) -> str:
    payload = post("https://api.openai.com/v1/chat/completions", key,
                   {"model": PARAPHRASE_MODEL, "temperature": 1.0,
                    "messages": [{"role": "system", "content": PARAPHRASE_PROMPT},
                                 {"role": "user", "content": question[:4_000]}]})
    return payload["choices"][0]["message"]["content"].strip()


def normalise(vector: list[float]) -> list[float]:
    length = math.sqrt(sum(value * value for value in vector)) or 1.0
    return [value / length for value in vector]


def rank_of(query: list[float], pool: list[list[float]], target: int) -> int:
    """Position of the target in the pool, ranked by cosine similarity. 0 is the top hit."""
    scores = [(sum(q * p for q, p in zip(query, vector)), index)
              for index, vector in enumerate(pool)]
    scores.sort(key=lambda pair: (-pair[0], pair[1]))
    return next(position for position, (_, index) in enumerate(scores) if index == target)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--in", dest="source", default="data/exemplars.clean.jsonl")
    parser.add_argument("--pool", type=int, default=400, help="exchanges to search over")
    parser.add_argument("--queries", type=int, default=40, help="held-out questions to ask")
    parser.add_argument("--seed", type=int, default=4242)
    parser.add_argument("--out", default=None, help="write per-query ranks here")
    args = parser.parse_args()

    path = Path(args.source)
    if not path.exists():
        print(f"error: {path} not found", file=sys.stderr)
        return 1

    rows = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line]
    usable = [r for r in rows if r.get("question", "").strip() and r.get("answer", "").strip()]
    if len(usable) < args.pool:
        print(f"error: only {len(usable)} usable exchanges, need {args.pool}", file=sys.stderr)
        return 1

    rng = random.Random(args.seed)
    pool = rng.sample(usable, args.pool)
    targets = rng.sample(range(args.pool), args.queries)

    openai_key = secret("openai-apikey")

    print(f"pool {args.pool:,} exchange(s), {args.queries} held-out question(s)\n")

    print("paraphrasing held-out questions")
    paraphrases = []
    for position, index in enumerate(targets, start=1):
        paraphrases.append(paraphrase(openai_key, pool[index]["question"]))
        print(f"  {position}/{len(targets)}", end="\r", flush=True)
    print(" " * 40, end="\r")

    print("embedding pool as whole exchanges (today's tickets-v1)")
    content = [normalise(v) for v in embed(openai_key, [
        f"Customer asked: {r['question']}\n\nSupport replied: {r['answer']}" for r in pool])]

    print("embedding pool as customer questions only")
    questions = [normalise(v) for v in embed(openai_key, [r["question"] for r in pool])]

    print("embedding paraphrased queries")
    queries = [normalise(v) for v in embed(openai_key, paraphrases)]

    rows_out = []
    for query, target, text in zip(queries, targets, paraphrases, strict=True):
        rows_out.append({
            "ticketId": pool[target]["ticket_id"],
            "paraphrase": text,
            "contentRank": rank_of(query, content, target),
            "questionRank": rank_of(query, questions, target),
        })

    print(f"\n{'strategy':<34}{'recall@1':>10}{'recall@3':>10}{'recall@10':>11}{'median rank':>13}")
    for label, field in (("content (question + reply)", "contentRank"),
                         ("question only", "questionRank")):
        ranks = sorted(row[field] for row in rows_out)
        at = lambda k: sum(1 for r in ranks if r < k) / len(ranks)  # noqa: E731
        median = ranks[len(ranks) // 2]
        print(f"{label:<34}{at(1):>9.0%}{at(3):>10.0%}{at(10):>11.0%}{median:>13}")

    improved = sum(1 for r in rows_out if r["questionRank"] < r["contentRank"])
    worsened = sum(1 for r in rows_out if r["questionRank"] > r["contentRank"])
    print(f"\nquestion-only ranked the right exchange higher on {improved} of {len(rows_out)}, "
          f"lower on {worsened}")

    if args.out:
        Path(args.out).write_text(json.dumps(rows_out, indent=2), encoding="utf-8")
        print(f"per-query ranks written to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
