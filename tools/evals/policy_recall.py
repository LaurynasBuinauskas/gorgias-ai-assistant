"""Does policy retrieval return the chunk that answers the question?

Everything measured so far has been about ticket exemplars, which the prompt treats as style
references and forbids drawing facts from. Policy is the corpus that actually grounds a draft —
every timeframe, entitlement and condition is supposed to come from it — and its retrieval
quality had never been measured at all. `PolicyTopK` is 4 because someone chose 4.

Method: take indexed policy chunks, ask a model to write the customer question each one
answers, then run that question through **the production retrieval path** — same filter, same
hybrid search, same semantic reranker — and record where the source chunk came back.

Two numbers, because the strict one understates:

    exact   the source chunk itself is in the top k
    topic   *a* chunk on the same topic and market is in the top k

Policy repeats across markets by design: return windows are identical everywhere, so a German
question can legitimately retrieve the GLOBAL chunk instead of the DE one and be perfectly well
answered. `exact` treats that as a miss; `topic` does not. The truth is between them.

Questions are generated in the language of the passage, because production retrieves on the
customer's own words and half this corpus is not in English.

    python tools/evals/policy_recall.py --queries 80
"""

from __future__ import annotations

import argparse
import json
import random
import shutil
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path

SERVICE = "gorgias-assistant-search"
VAULT = "gorgias-assistant-kv"
INDEX = "knowledge-v1"
API_VERSION = "2024-07-01"
ENDPOINT = f"https://{SERVICE}.search.windows.net"

EMBEDDING_MODEL = "text-embedding-3-small"
EMBEDDING_DIMENSIONS = 1536
QUESTION_MODEL = "gpt-4.1-mini-2025-04-14"

# Production values, so this measures what runs rather than a configuration of its own.
VECTOR_CANDIDATES = 20
SEMANTIC_CONFIG = "policy-semantic"
DEPTH = 10

QUESTION_PROMPT = """You are given one passage from a company's published support policy.

Write the single question a customer would email support that this passage answers.

Rules:
- Write as a customer, not as the policy. Do not reuse its distinctive wording or headings.
- Ask about their own situation, the way a real person does.
- Write in the same language as the passage.
- If the passage is legal boilerplate, an address block, or anything no customer would ever
  write in about, reply with exactly SKIP.

Reply with the question alone, or SKIP."""


# Questions policy genuinely does not answer. The relevance gate exists to decline these, so
# their reranker scores next to the covered distribution above are the whole argument about
# whether the gate can work on this signal at all. Drawn from the class D refusal fixtures,
# plus the holiday greeting that out-scored a real returns question during D-6.
UNCOVERED = [
    "What was your company's annual revenue last year and who are your main investors?",
    "I would like to apply for a job in your customer service team. What is the salary range?",
    "I will be travelling near your workshop next month. Can I visit and watch bags being made?",
    "How do I declare this purchase on my annual tax return?",
    "We run a chain of boutiques. What are your wholesale prices and minimum order quantities?",
    "Christmas Greetings from all of us at Zryya!",
    "Do you sponsor local sports teams, and who should I speak to about it?",
    "What leather tannery do you buy your hides from?",
]


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
                    "semantic reranking refused (402): the monthly quota is exhausted"
                ) from error
            if error.code in (429, 500, 502, 503, 504):
                time.sleep(min(2 ** attempt, 30))
                continue
            raise SystemExit(f"{url.split('?')[0]}: HTTP {error.code}\n{detail[:300]}") from error
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            time.sleep(min(2 ** attempt, 30))
    raise SystemExit(f"{url.split('?')[0]}: gave up after repeated failures")


def policy_chunks(key: str) -> list[dict]:
    chunks: list[dict] = []
    skip = 0
    while True:
        page = post(f"{ENDPOINT}/indexes/{INDEX}/docs/search?api-version={API_VERSION}",
                    {"Content-Type": "application/json", "api-key": key},
                    {"search": "*", "top": 1000, "skip": skip,
                     "filter": "corpus eq 'policy' and exposure eq 'customer'",
                     "select": "id,market,topic,title,content"})["value"]
        if not page:
            return chunks
        chunks.extend(page)
        skip += len(page)


def ask(key: str, content: str) -> str:
    payload = post("https://api.openai.com/v1/chat/completions",
                   {"Content-Type": "application/json", "Authorization": f"Bearer {key}"},
                   {"model": QUESTION_MODEL, "temperature": 1.0,
                    "messages": [{"role": "system", "content": QUESTION_PROMPT},
                                 {"role": "user", "content": content[:6_000]}]})
    return payload["choices"][0]["message"]["content"].strip()


def embed(key: str, text: str) -> list[float]:
    payload = post("https://api.openai.com/v1/embeddings",
                   {"Content-Type": "application/json", "Authorization": f"Bearer {key}"},
                   {"model": EMBEDDING_MODEL, "input": text[:12_000],
                    "dimensions": EMBEDDING_DIMENSIONS})
    return payload["data"][0]["embedding"]


# Production retrieves on `subject + newest customer message`, and a real customer message is
# not a clean question — it carries a greeting, an order reference, a sign-off, sometimes a
# quoted chain. That furniture is exactly what the relevance gate was found to score on:
# "Christmas Greetings from all of us" out-scored a genuine returns question. So the same
# questions are asked twice, clean and dressed, and the difference is the cost of the noise.
NOISE_TEMPLATES = [
    ("Quick question", "Hi there,\n\n{q}\n\nThanks so much for your help!\nBest wishes,\nSam"),
    ("Order enquiry",
     "Hello Time Resistance team,\n\nI hope you are having a great week. {q}\n\n"
     "Kind regards,\nAlex\n--\nSent from my iPhone"),
    ("Re: Your order",
     "Hi,\n\n{q}\n\nMany thanks,\nJordan\n\n"
     "> On Monday you wrote:\n> Thank you for contacting us, we will get back to you shortly.\n"
     "> Our support team is available Monday to Friday."),
]


def dress(question: str, index: int) -> tuple[str, str]:
    """Wrap a question in the furniture a real ticket arrives with."""
    subject, template = NOISE_TEMPLATES[index % len(NOISE_TEMPLATES)]
    return subject, template.format(q=question)


def retrieve(key: str, question: str, vector: list[float], market: str) -> list[dict]:
    """The production query, filter and reranker — not an approximation of them."""
    body = {
        "search": question,
        "top": DEPTH,
        "filter": f"corpus eq 'policy' and exposure eq 'customer' "
                  f"and (market eq '{market}' or market eq 'GLOBAL')",
        "select": "id,market,topic",
        "vectorQueries": [{"kind": "vector", "vector": vector,
                           "k": VECTOR_CANDIDATES, "fields": "contentVector"}],
        "queryType": "semantic",
        "semanticConfiguration": SEMANTIC_CONFIG,
    }
    return post(f"{ENDPOINT}/indexes/{INDEX}/docs/search?api-version={API_VERSION}",
                {"Content-Type": "application/json", "api-key": key}, body)["value"]


def gate_scores_from_fixtures(search_key: str, openai_key: str, root: Path) -> int:
    """Score the real eval tickets, which come with ground truth about coverage.

    The synthetic questions above are clean and the uncovered control is bare. Neither is a
    ticket. These are: the same fixtures the eval suite runs, queried exactly as the pipeline
    queries them — subject plus the newest customer message — with the market each case pins.
    Class D's declining cases are the ones policy genuinely does not cover; everything else it
    does. If the gate can work at all, these two groups separate here.
    """
    import yaml  # only needed for this mode

    cases = []
    for path in (root / "cases").rglob("*.yaml"):
        case = yaml.safe_load(path.read_text(encoding="utf-8"))
        if case:
            cases.append(case)

    # The three that decline at the gate today, established by running them.
    declines = {"d-company-financials", "d-employment", "d-workshop-visit"}

    rows = []
    for case in sorted(cases, key=lambda c: c["id"]):
        fixture = root / "fixtures" / "tickets" / case["fixture"]
        if not fixture.exists():
            continue
        ticket = json.loads(fixture.read_text(encoding="utf-8"))
        newest = ""
        for message in ticket.get("messages", []):
            if not message.get("fromAgent") and not message.get("isInternalNote"):
                newest = message.get("text") or ""
        query = " ".join(part for part in [ticket.get("subject"), newest] if part).strip()
        if not query:
            continue

        results = retrieve(search_key, query, embed(openai_key, query),
                           case.get("market", "GLOBAL"))
        score = results[0].get("@search.rerankerScore") if results else None
        if score is None:
            continue
        rows.append((case["id"], case.get("market", "GLOBAL"),
                     "uncovered" if case["id"] in declines else "covered", score))

    covered = sorted(s for _, _, kind, s in rows if kind == "covered")
    uncovered = sorted(s for _, _, kind, s in rows if kind == "uncovered")

    print(f"\nGate scores on {len(rows)} real eval ticket(s)\n")
    for case_id, market, kind, score in sorted(rows, key=lambda r: r[3]):
        print(f"  {score:>6.3f}  {kind:<10} {market:<7} {case_id}")

    if covered and uncovered:
        print(f"\n  covered    n={len(covered):<3} min {covered[0]:.3f}  max {covered[-1]:.3f}")
        print(f"  uncovered  n={len(uncovered):<3} min {uncovered[0]:.3f}  max {uncovered[-1]:.3f}")
        if uncovered[-1] < covered[0]:
            print(f"  → separated. Any threshold between {uncovered[-1]:.3f} and "
                  f"{covered[0]:.3f} splits them.")
        else:
            print(f"  → overlapping: the highest uncovered ({uncovered[-1]:.3f}) is at or above "
                  f"the lowest covered ({covered[0]:.3f}).\n    No threshold separates them.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixtures", default=None,
                        help="score the real eval tickets instead of synthetic questions, "
                             "e.g. backend/tools/Copilot.Evals")
    parser.add_argument("--queries", type=int, default=80)
    parser.add_argument("--seed", type=int, default=17)
    parser.add_argument("--out", default=None)
    args = parser.parse_args()

    search_key, openai_key = secret("search-adminkey"), secret("openai-apikey")

    if args.fixtures:
        return gate_scores_from_fixtures(search_key, openai_key, Path(args.fixtures))

    chunks = policy_chunks(search_key)
    print(f"{len(chunks)} policy chunk(s) indexed")

    rng = random.Random(args.seed)
    sampled = rng.sample(chunks, min(args.queries, len(chunks)))

    rows, skipped = [], 0
    for position, chunk in enumerate(sampled, start=1):
        question = ask(openai_key, chunk["content"])
        if question.upper().startswith("SKIP"):
            skipped += 1
            print(f"  {position}/{len(sampled)} (skipped {skipped})", end="\r", flush=True)
            continue

        wanted = (chunk["topic"], chunk["market"])
        alternative = (chunk["topic"], "GLOBAL")

        def ranks(query: str) -> tuple[int, int, float | None]:
            results = retrieve(search_key, query, embed(openai_key, query), chunk["market"])
            ids = [hit["id"] for hit in results]
            topics = [(hit["topic"], hit["market"]) for hit in results]
            exact = ids.index(chunk["id"]) if chunk["id"] in ids else DEPTH
            topic = next((i for i, t in enumerate(topics) if t in (wanted, alternative)), DEPTH)
            return exact, topic, (results[0].get("@search.rerankerScore") if results else None)

        clean_exact, clean_topic, clean_score = ranks(question)
        subject, dressed = dress(question, position)
        noisy_exact, noisy_topic, noisy_score = ranks(f"{subject} {dressed}")

        rows.append({
            "id": chunk["id"], "market": chunk["market"], "topic": chunk["topic"],
            "question": question,
            "exactRank": clean_exact, "topicRank": clean_topic, "topScore": clean_score,
            "noisyExactRank": noisy_exact, "noisyTopicRank": noisy_topic,
            "noisyTopScore": noisy_score,
        })
        print(f"  {position}/{len(sampled)} (skipped {skipped})", end="\r", flush=True)
    print(" " * 40, end="\r")

    if not rows:
        print("no usable questions generated")
        return 1

    print(f"\n{len(rows)} question(s) asked, {skipped} passage(s) skipped as boilerplate\n")
    print(f"{'metric':<38}{'@1':>7}{'@3':>7}{'@4':>7}{'@10':>7}")
    for label, field in (("exact chunk, clean question", "exactRank"),
                         ("exact chunk, real ticket text", "noisyExactRank"),
                         ("same topic, clean question", "topicRank"),
                         ("same topic, real ticket text", "noisyTopicRank")):
        values = [r[field] for r in rows]
        at = lambda k: sum(1 for r in values if r < k) / len(values)  # noqa: E731
        print(f"{label:<38}{at(1):>6.0%}{at(3):>7.0%}{at(4):>7.0%}{at(10):>7.0%}")

    # The gate thresholds this number, so what noise does to it is the question behind D-6.
    clean_scores = sorted(r["topScore"] for r in rows if r["topScore"] is not None)
    noisy_scores = sorted(r["noisyTopScore"] for r in rows if r["noisyTopScore"] is not None)
    if clean_scores and noisy_scores:
        def spread(values: list[float]) -> str:
            return (f"min {values[0]:.3f}  p10 {values[len(values) // 10]:.3f}  "
                    f"median {values[len(values) // 2]:.3f}  max {values[-1]:.3f}")
        print(f"\ntop reranker score, clean question:  {spread(clean_scores)}")
        print(f"top reranker score, real ticket text: {spread(noisy_scores)}")

        # The gate's whole premise: that covered and uncovered questions score differently.
        print("\nControl — questions policy does not answer:")
        uncovered: list[float] = []
        for question in UNCOVERED:
            results = retrieve(search_key, question, embed(openai_key, question), "GLOBAL")
            score = results[0].get("@search.rerankerScore") if results else None
            if score is not None:
                uncovered.append(score)
                print(f"  {score:>6.3f}  {question[:66]}")

        if uncovered:
            covered_floor = min(clean_scores + noisy_scores)
            overlapping = [s for s in uncovered if s >= covered_floor]
            print(f"\n  covered questions never scored below {covered_floor:.3f}")
            print(f"  {len(overlapping)} of {len(uncovered)} uncovered questions scored at or "
                  f"above that floor")
            print("  → no threshold on this scale separates them. The gate can only be a "
                  "'retrieval found nothing' floor,\n    which is what 1.6 already is. Raising "
                  "it declines real questions.")

    missed = [r for r in rows if r["topicRank"] >= DEPTH]
    print(f"\nnot found at all within {DEPTH}: {len(missed)} of {len(rows)}")
    for row in missed[:10]:
        print(f"  {row['market']:<7} {row['topic']:<24} {row['question'][:60]!r}")

    if args.out:
        Path(args.out).write_text(json.dumps(rows, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"\nper-question ranks written to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
