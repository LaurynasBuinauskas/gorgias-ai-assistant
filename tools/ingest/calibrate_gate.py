"""Score real tickets through the retrieval path to calibrate the relevance-gate threshold.

The threshold decides whether the assistant answers or declines, so it must be set against
the queries it will actually see. Idealised questions ("how long do I have to return an
item?") score far higher than real tickets, whose subject and message carry signatures,
quoted history and pleasantries that dilute the match — calibrating on the former produces a
threshold that declines genuine questions.

Read-only against Gorgias. Prints the score each ticket would be judged on, so a threshold can
be chosen from the distribution rather than from a handful of hand-written examples.

    python tools/ingest/calibrate_gate.py [count]
"""

from __future__ import annotations

import base64
import json
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.request

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
ALIAS = "knowledge"
API_VERSION = "2024-05-01-preview"
EMBEDDING_MODEL = "text-embedding-3-small"
EMBEDDING_DIMENSIONS = 1536
USER_AGENT = "gorgias-ai-assistant-devtool/1.0"


def az(args: list[str]) -> str:
    cli = shutil.which("az")
    if cli is None:
        raise SystemExit("the Azure CLI ('az') is not on PATH")
    result = subprocess.run([cli, *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"az failed: {result.stderr.strip()[:200]}")
    return result.stdout.strip()


def secret(name: str) -> str:
    return az(["keyvault", "secret", "show", "--vault-name", VAULT,
               "--name", name, "--query", "value", "-o", "tsv"])


def app_setting(name: str) -> str:
    return az(["webapp", "config", "appsettings", "list", "--name", "gorgias-assistant-api",
               "--resource-group", "gorgias-assistant-rg",
               "--query", f"[?name=='{name}'].value", "-o", "tsv"])


def post(url: str, headers: dict[str, str], body: dict) -> dict:
    req = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST")
    for key, value in headers.items():
        req.add_header(key, value)
    with urllib.request.urlopen(req, timeout=120) as response:
        return json.loads(response.read())


def gorgias(path: str, subdomain: str, email: str, key: str) -> dict:
    req = urllib.request.Request(f"https://{subdomain}.gorgias.com/api/{path}")
    req.add_header("User-Agent", USER_AGENT)
    req.add_header("Authorization",
                   "Basic " + base64.b64encode(f"{email}:{key}".encode()).decode())
    try:
        with urllib.request.urlopen(req, timeout=120) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        raise SystemExit(f"Gorgias HTTP {error.code}: {error.read().decode()[:200]}") from error


def main() -> int:
    count = int(sys.argv[1]) if len(sys.argv) > 1 else 15
    subdomain, email = app_setting("Gorgias__Subdomain"), app_setting("Gorgias__Email")
    gorgias_key, openai_key, search_key = (
        secret("gorgias-apikey"), secret("openai-apikey"), secret("search-adminkey"))

    listing = gorgias(
        f"tickets?limit={count}&order_by=created_datetime:desc", subdomain, email, gorgias_key)

    print(f"{'score':>7}  {'ticket':>10}  subject")
    print("-" * 78)
    scores: list[tuple[float, str]] = []

    for summary in listing.get("data", []):
        ticket = gorgias(f"tickets/{summary['id']}", subdomain, email, gorgias_key)
        messages = ticket.get("messages") or []
        customer = [m for m in messages
                    if not m.get("from_agent") and m.get("public")]
        if not customer:
            continue

        newest = customer[-1]
        text = newest.get("stripped_text") or newest.get("body_text") or ""
        # Mirrors KnowledgeRetriever.BuildQuery: subject plus the newest customer message.
        query = " ".join(p for p in [ticket.get("subject"), text] if p)
        if not query.strip():
            continue

        vector = post("https://api.openai.com/v1/embeddings",
                      {"Content-Type": "application/json",
                       "Authorization": f"Bearer {openai_key}"},
                      {"model": EMBEDDING_MODEL, "input": [query[:8000]],
                       "dimensions": EMBEDDING_DIMENSIONS})["data"][0]["embedding"]

        result = post(
            f"https://{SERVICE}.search.windows.net/indexes/{ALIAS}/docs/search"
            f"?api-version={API_VERSION}",
            {"Content-Type": "application/json", "api-key": search_key},
            {
                "search": query[:8000],
                "filter": "corpus eq 'policy' and exposure eq 'customer' "
                          "and (market eq 'GLOBAL' or market eq 'GLOBAL')",
                "vectorQueries": [{"kind": "vector", "vector": vector,
                                   "fields": "contentVector", "k": 20}],
                "queryType": "semantic",
                "semanticConfiguration": "policy-semantic",
                "select": "market,topic,sourcePath",
                "top": 4,
            })

        hits = result.get("value", [])
        best = max((h.get("@search.rerankerScore", 0) for h in hits), default=0.0)
        subject = re.sub(r"\s+", " ", ticket.get("subject") or "")[:52]
        scores.append((best, subject))
        print(f"{best:>7.3f}  {summary['id']:>10}  {subject}")

    if scores:
        ordered = sorted(s for s, _ in scores)
        print("-" * 78)
        print(f"n={len(ordered)}  min={ordered[0]:.3f}  "
              f"median={ordered[len(ordered) // 2]:.3f}  max={ordered[-1]:.3f}")
        print("Choose a threshold below the scores of tickets the corpus genuinely covers.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
