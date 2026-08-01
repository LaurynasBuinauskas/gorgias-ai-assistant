"""Ask the index a question the way R-7 will, to see what a draft would actually be grounded in.

Hybrid retrieval: BM25 over the text, vector over the embedding, fused and reranked
semantically, with the hard market and exposure filters applied as predicates rather than
after the fact.

    python tools/ingest/query.py "how long do I have to return an item" --market DE
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import urllib.error
import urllib.request

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
ALIAS = "knowledge"
API_VERSION = "2024-05-01-preview"
ENDPOINT = f"https://{SERVICE}.search.windows.net"
EMBEDDING_MODEL = "text-embedding-3-small"
EMBEDDING_DIMENSIONS = 1536


def secret(name: str) -> str:
    az = shutil.which("az")
    result = subprocess.run(
        [az, "keyvault", "secret", "show", "--vault-name", VAULT,
         "--name", name, "--query", "value", "-o", "tsv"],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"could not read {name}: {result.stderr.strip()}")
    return result.stdout.strip()


def post(url: str, headers: dict[str, str], body: dict) -> dict:
    req = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST")
    for key, value in headers.items():
        req.add_header(key, value)
    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        raise SystemExit(f"{url.split('?')[0]}: HTTP {error.code}\n"
                         f"{error.read().decode(errors='replace')[:300]}") from error


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("question")
    parser.add_argument("--market", default="GLOBAL")
    parser.add_argument("--corpus", default="policy")
    parser.add_argument("--top", type=int, default=4)
    args = parser.parse_args()

    vector = post("https://api.openai.com/v1/embeddings",
                  {"Content-Type": "application/json",
                   "Authorization": f"Bearer {secret('openai-apikey')}"},
                  {"model": EMBEDDING_MODEL, "input": [args.question],
                   "dimensions": EMBEDDING_DIMENSIONS})["data"][0]["embedding"]

    result = post(f"{ENDPOINT}/indexes/{ALIAS}/docs/search?api-version={API_VERSION}",
                  {"Content-Type": "application/json", "api-key": secret("search-adminkey")},
                  {
                      "search": args.question,
                      "filter": f"(market eq '{args.market}' or market eq 'GLOBAL') "
                                f"and corpus eq '{args.corpus}' and exposure eq 'customer'",
                      "vectorQueries": [{"kind": "vector", "vector": vector,
                                         "fields": "contentVector", "k": 20}],
                      "queryType": "semantic",
                      "semanticConfiguration": "policy-semantic",
                      "select": "market,topic,title,sourcePath,content",
                      "top": args.top,
                  })

    hits = result.get("value", [])
    print(f'"{args.question}"  market={args.market} corpus={args.corpus} -> {len(hits)} hits\n')
    for n, hit in enumerate(hits, 1):
        score = hit.get("@search.rerankerScore", hit.get("@search.score", 0))
        print(f"[{n}] {score:.3f}  {hit['market']}/{hit['topic']}  {hit['sourcePath']}")
        print(f"     {hit['title'][:100]}")
        snippet = " ".join(hit["content"].split())[:260]
        print(f"     {snippet}...\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
