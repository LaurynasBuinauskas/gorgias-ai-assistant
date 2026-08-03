"""Does semantic reranking improve exemplar retrieval enough to pay for?

Exemplars are retrieved unranked. Only policy is reranked, because reranking is metered and
cutting it to one corpus took the cost of a draft from four semantic queries to one — the
change that made the free allowance cover roughly 1,000 drafts a month instead of 250. Turning
it on for exemplars puts that back to two per draft, halving the allowance again.

So the question is not "is reranking better" in the abstract. It is whether it retrieves the
right past exchange often enough to justify halving what the quota covers, and an early probe
was genuinely mixed: reranking helped a returns query and actively hurt a warranty one, because
`policy-semantic` ranks on `content` — the whole question-plus-reply blob — and latched onto
mid-thread pleasantries. This measures against `ticket-semantic`, which ranks on the customer's
question, so the comparison is between two designs that both make sense rather than against a
strawman.

Method mirrors `exemplar_recall.py`: hold out indexed exchanges, paraphrase their question so
no wording is shared, and ask where the original comes back. Unlike that script this runs
against the live index, because reranking cannot be simulated offline.

    python tools/evals/exemplar_rerank.py --queries 60
"""

from __future__ import annotations

import argparse
import json
import random
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
API_VERSION = "2024-07-01"
ENDPOINT = f"https://{SERVICE}.search.windows.net"

EMBEDDING_MODEL = "text-embedding-3-small"
EMBEDDING_DIMENSIONS = 1536
PARAPHRASE_MODEL = "gpt-4.1-mini-2025-04-14"
DEPTH = 10

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


def post(url: str, headers: dict[str, str], body: dict) -> dict:
    for attempt in range(6):
        request = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST")
        for key, value in headers.items():
            request.add_header(key, value)
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                return json.loads(response.read())
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")
            if error.code == 402:
                raise SystemExit(
                    "semantic reranking refused (402): the monthly quota is exhausted. "
                    "That is itself an answer about cost.") from error
            if error.code in (429, 500, 502, 503, 504):
                time.sleep(min(2 ** attempt, 30))
                continue
            raise SystemExit(f"{url.split('?')[0]}: HTTP {error.code}\n{detail[:300]}") from error
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            time.sleep(min(2 ** attempt, 30))
    raise SystemExit(f"{url.split('?')[0]}: gave up after repeated failures")


def embed(key: str, text: str) -> list[float]:
    payload = post("https://api.openai.com/v1/embeddings",
                   {"Content-Type": "application/json", "Authorization": f"Bearer {key}"},
                   {"model": EMBEDDING_MODEL, "input": text[:12_000],
                    "dimensions": EMBEDDING_DIMENSIONS})
    return payload["data"][0]["embedding"]


def paraphrase(key: str, question: str) -> str:
    payload = post("https://api.openai.com/v1/chat/completions",
                   {"content-type": "application/json", "Authorization": f"Bearer {key}"},
                   {"model": PARAPHRASE_MODEL, "temperature": 1.0,
                    "messages": [{"role": "system", "content": PARAPHRASE_PROMPT},
                                 {"role": "user", "content": question[:4_000]}]})
    return payload["choices"][0]["message"]["content"].strip()


def search(key: str, index: str, text: str, vector: list[float], semantic: bool) -> list[str]:
    body: dict = {
        "search": text,
        "top": DEPTH,
        "filter": "corpus eq 'ticket' and exposure eq 'customer'",
        "select": "id",
        "vectorQueries": [{"kind": "vector", "vector": vector, "k": 50,
                           "fields": "questionVector"}],
    }
    if semantic:
        body |= {"queryType": "semantic", "semanticConfiguration": "ticket-semantic"}

    payload = post(f"{ENDPOINT}/indexes/{index}/docs/search?api-version={API_VERSION}",
                   {"Content-Type": "application/json", "api-key": key}, body)
    return [hit["id"] for hit in payload["value"]]


def sample(key: str, index: str, count: int, seed: int) -> list[dict]:
    """Random exchanges with a usable question, drawn from the index rather than the file."""
    rng = random.Random(seed)
    picked: dict[str, dict] = {}
    while len(picked) < count:
        skip = rng.randrange(0, 17_000)
        payload = post(f"{ENDPOINT}/indexes/{index}/docs/search?api-version={API_VERSION}",
                       {"Content-Type": "application/json", "api-key": key},
                       {"search": "*", "top": 20, "skip": skip, "select": "id,ticketId,question"})
        for hit in payload["value"]:
            question = (hit.get("question") or "").strip()
            if len(question) >= 40 and hit["id"] not in picked:
                picked[hit["id"]] = hit
            if len(picked) >= count:
                break
    return list(picked.values())


def rank_of(results: list[str], target: str) -> int:
    """Zero-based position, or DEPTH when the target did not come back at all."""
    return results.index(target) if target in results else DEPTH


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--index", default="tickets-v2")
    parser.add_argument("--queries", type=int, default=60)
    parser.add_argument("--seed", type=int, default=71)
    parser.add_argument("--out", default=None)
    args = parser.parse_args()

    search_key, openai_key = secret("search-adminkey"), secret("openai-apikey")

    print(f"sampling {args.queries} exchange(s) from {args.index}")
    targets = sample(search_key, args.index, args.queries, args.seed)

    rows = []
    for position, target in enumerate(targets, start=1):
        text = paraphrase(openai_key, target["question"])
        vector = embed(openai_key, text)
        plain = search(search_key, args.index, text, vector, semantic=False)
        ranked = search(search_key, args.index, text, vector, semantic=True)
        rows.append({
            "ticketId": target.get("ticketId"),
            "unrankedRank": rank_of(plain, target["id"]),
            "rerankedRank": rank_of(ranked, target["id"]),
        })
        print(f"  {position}/{len(targets)}", end="\r", flush=True)
    print(" " * 30, end="\r")

    print(f"\n{'strategy':<24}{'recall@1':>10}{'recall@3':>10}{'recall@10':>11}")
    for label, field in (("unranked (today)", "unrankedRank"),
                         ("semantic rerank", "rerankedRank")):
        ranks = [r[field] for r in rows]
        at = lambda k: sum(1 for r in ranks if r < k) / len(ranks)  # noqa: E731
        print(f"{label:<24}{at(1):>9.0%}{at(3):>10.0%}{at(10):>11.0%}")

    better = sum(1 for r in rows if r["rerankedRank"] < r["unrankedRank"])
    worse = sum(1 for r in rows if r["rerankedRank"] > r["unrankedRank"])
    print(f"\nreranking ranked the right exchange higher on {better} of {len(rows)}, "
          f"lower on {worse}")
    print(f"cost if adopted: one extra semantic query per draft, roughly halving what the "
          f"free allowance covers")

    if args.out:
        import pathlib
        pathlib.Path(args.out).write_text(json.dumps(rows, indent=2), encoding="utf-8")
        print(f"per-query ranks written to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
