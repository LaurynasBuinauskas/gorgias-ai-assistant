# Handoff — state of the project, 2026-08-02

Written by the outgoing agent for the incoming one. Everything here is either a **fact you can
re-verify** (command given) or is **labelled as a judgement**, because a judgement inherited
without its evidence is just a rumour with a good reputation.

**Assume the judgements may be wrong.** Several claims in this repo passed review for weeks
while being untrue — see §6. Re-verify anything a decision depends on.

---

## 1. What this is

A Chrome extension mounts a panel beside a Gorgias ticket. An agent asks for a reply draft; a
.NET API generates it from ticket content plus the company's published policy (RAG). The agent
edits and copies it themselves. **The system never sends anything.**

Read `CLAUDE.md` first, then `docs/gorgias-copilot-technical-reference.md`.

## 2. Verified state

| Fact | Re-verify with |
|---|---|
| HEAD `749601d`, 94 commits, branch `main`, tree clean | `git log --oneline -1` |
| API healthy, build `d037e25` | `curl -s https://gorgias-assistant-api.azurewebsites.net/health` |
| `knowledge-v1` 400 docs · `tickets-v1` 17,863 docs | see §7 snippet |
| 51 eval cases pass | `dotnet run --project backend/tools/Copilot.Evals -c Release -- --out eval-report.md` |
| 126 unit tests pass | `dotnet test backend/Copilot.Tests/Copilot.Tests.csproj` |
| 46 redaction tests pass | `python tools/ingest/test_redaction.py` |
| **Ticket exemplars are NOT used** — `TicketTopK` is 0 and `KnowledgeRetriever` returns early on `topK <= 0` | `grep -n "TicketTopK" backend/Copilot.Api/appsettings.json` |
| Kill switch off | `curl -s https://gorgias-assistant-api.azurewebsites.net/v1/config` |

The exemplar point matters most and is the easiest to get wrong: the corpus is **indexed but
never queried**. No exemplar text has ever reached a draft, including in eval runs. Anything
you read about ticket exemplars describes a switched-off feature.

## 3. Three kinds of "decision" in this repo — do not treat them alike

**Client decisions — binding.** In `docs/beta-progress.md` decisions log, marked `| client |`.
Examples: storefront ordered from wins on market conflict; drafts are always English because
agents review in English; redact-and-retain with no retention window; Azure AI Search Basic
approved. Do not relitigate without asking.

**Engineering decisions taken and implemented.** Marked `| investigation |`. Reasoning is
recorded; change them if you have better evidence, but read the reasoning first — some encode
a failure that already happened.

**Proposals — opinions, not approved.** `feedback-loop-proposal.md`,
`policy-management-proposal.md`, `rag-pipeline-proposal.md`. Written by an agent, not agreed by
the client. The feedback proposal in particular argues a strong line ("do not build the naive
learning loop"); treat that as an argument to evaluate, not a constraint.

## 4. What is open

**Needs the client:**
- **D-2** — acknowledge beta terms (`docs/beta-terms.md`, written and ready)
- **D-3** — privacy review of `data/exemplar-review-pack.md`, 400 exchanges, already drawn.
  Gates exemplars only, not the beta

**Needs a human, not an agent:**
- Whether drafts are worth editing — the beta's actual question
- First live drafts watched with a real agent in Gorgias

**Open engineering, no owner:**
- Panel E2E tests (mount, ticket switch, clipboard, floating fallback)
- Exercising the kill switch and an index rollback as drills
- Whether exemplars help at all (§5)

## 5. The exemplar question, stated neutrally

The client asked for retrieval over resolved tickets. It was built: 17,863 redacted exchanges
indexed. It is switched off.

**What was measured** (reproduce with `tools/evals/judge_drafts.py`, committed):
- Drafts with exemplars vs without, blind pairwise judging, order-randomised: **20 wins vs 11,
  15 ties**, position bias 42%
- The **same configuration judged against itself**: **23 vs 8** — a larger gap
- Mechanical text diffing: same result, no separation from the control

**What the outgoing agent concluded (judgement):** the effect is not detectable at 46 cases and
single runs, so switching exemplars on means accepting a privacy exposure for an unproven
benefit.

**What that does not establish:** that exemplars are useless. The eval suite tests safety, not
writing quality, and the experiment is small. A larger one — more cases, repeated runs
averaged, questions held out of the index — has not been run and could reverse the picture.

**Costs on the other side of the ledger,** for whoever decides: four independent 50-exchange
human reviews each found a personal-data leak class that no automated check could see; a fourth
sample of 400 found two more, both from small markets. All are fixed and are regression tests.
None of that proves the corpus is now clean.

## 6. Where this repo has been wrong before

Not anecdotes — each is a live reason to distrust a green check.

- **A PII eval class passed over an empty index** and stayed green through four rounds in which
  humans found four leak classes. It is still a weak check
- **A market test passed because its index was empty** and broke when data arrived
- **English-only output was reported fixed** while failing ~8% of the time
- **An injection vulnerability failed 1 run in 6** — a single green run would have missed it.
  Re-run safety classes more than once
- **Eval assertions twice failed correct refusals** by banning vocabulary rather than
  commitment. A refusal restates the demand in order to deny it
- **Semantic ranking is metered.** Repeated eval runs once exhausted the month's quota and
  500'd production. Billing is on now, but bulk eval runs cost money
- **A zip deploy can leave the previous build serving while routes throw.** Check the SHA in
  `/health`, not just `"status":"healthy"`

## 7. Operating

Runbooks: `docs/rollback-runbook.md` (four levers), `docs/exemplar-runbook.md` (rebuild,
removal, erasure, staleness). `docs/go-no-go.md` is the launch checklist.

Every app-setting change restarts App Service and takes **70–90 seconds**; `az` returns before
it is live. Poll for an observable.

```python
# index counts
import json, shutil, subprocess, urllib.request
key = subprocess.run([shutil.which("az"), "keyvault", "secret", "show", "--vault-name",
    "gorgias-assistant-kv", "--name", "search-adminkey", "--query", "value", "-o", "tsv"],
    capture_output=True, text=True).stdout.strip()
def count(index):
    r = urllib.request.Request(
        f"https://gorgias-assistant-search.search.windows.net/indexes/{index}/docs/search"
        "?api-version=2024-07-01",
        data=json.dumps({"search": "*", "count": True, "top": 0}).encode(), method="POST")
    r.add_header("Content-Type", "application/json"); r.add_header("api-key", key)
    return json.loads(urllib.request.urlopen(r, timeout=120).read())["@odata.count"]
print(count("knowledge-v1"), count("tickets-v1"))
```

## 8. Working agreement with Laurynas

From `~/.claude` memory and stated directly:

- **One task at a time, closed end to end** — build, test locally, commit, push to `main`
  (never a feature branch), check the pipeline, check Azure, test the live app, then next
- **Answer the prompt asked.** Do not drift back into previous work in the same reply
- **Plain language, not task IDs**
- **Investigate before declaring something blocked.** Escalate decisions; go and get facts

## 9. Local files holding customer data

Git-ignored, on this machine only. `data/exemplars.jsonl` (38 MB) is superseded and safe to
delete. `exemplars.deduped.jsonl` is the source for re-running redaction.
`exemplars.clean.jsonl` matches the index. `exemplar-review-pack.md` is awaiting the reviewer.
Do not move any of them into `docs/` or anywhere shareable.
