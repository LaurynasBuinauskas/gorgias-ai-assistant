# Handoff — state of the project, 2026-08-03

Written by the outgoing agent for the incoming one. It replaces the 2026-08-02 handoff
entirely; almost every number in that one is now wrong.

Everything here is either a **fact you can re-verify** (command given) or is **labelled as a
judgement**. Assume the judgements may be wrong.

**Re-verify before you build on it.** On 2026-08-03 the previous handoff's own figures turned
out to be wrong when checked — it claimed 46 redaction tests where there were 49, and claimed
51 eval cases passing from a single lucky run when the suite failed roughly three runs in four.
Both had passed review. Section 6 is a list of things this repo has been confidently wrong
about; treat it as a warning about this document too.

---

## 1. What this is

A Chrome extension mounts a panel beside a Gorgias ticket. An agent asks for a reply draft; a
.NET API generates it from ticket content plus the company's published policy and past resolved
tickets (two RAG corpora). The agent edits and copies it themselves. **The system never sends
anything.**

Read `CLAUDE.md` first, then `docs/gorgias-copilot-technical-reference.md`.

## 2. Verified state, 2026-08-03

| Fact | Re-verify with |
|---|---|
| HEAD `c2d8026`, branch `main` | `git log --oneline -1` |
| ~~One uncommitted change in `DraftPrompt.cs`~~ — **committed 2026-08-05 after the §3 measurement finished; see the resolution note in §3** | `git status --porcelain` |
| API healthy, serving `c2d8026` | `curl -s https://gorgias-assistant-api.azurewebsites.net/health` |
| **Exemplars are ON** — `Retrieval__TicketTopK=3` | `curl -s <api>/v1/config` → `"exemplars":true` |
| `knowledge-v1` **394** docs · `tickets-v3` **17,402** docs | §8 snippet |
| Full eval suite **53 cases** | `dotnet run --project backend/tools/Copilot.Evals -c Release -- --out eval-report.md` |
| **171 unit tests** pass | `dotnet test backend/Copilot.slnx` |
| **49 redaction assertions** over 48 cases | `python tools/ingest/test_redaction.py` |
| **7 shell E2E tests** pass | `pnpm --filter @copilot/extension test:e2e` |
| Kill switch off | `curl -s <api>/v1/config` |

Indexes: `tickets-v1` (17,863, superseded, no `questionVector`), `tickets-v2` (17,863, has
`questionVector`, still carries the goodwill content), `tickets-v3` (**17,402, live**).

## 3. The one uncommitted change — decide this first

`backend/Copilot.Pipeline/DraftPrompt.cs` has an uncommitted one-line addition: the sources
delimiter requirement repeated as the last thing the model reads.

**It is unproven.** Measured on the market eval class:

- before: **1 failing run in 12**
- after: **0 failing runs in 20**

Under the null that is p ≈ 0.19 — suggestive, not conclusive. A second batch of 18 runs was
started and never finished.

**Before committing it, do both:**
1. Finish the measurement — another ~18 runs of `--class market`, looking for
   `must_cite_market` failures.
2. Run the **full** suite several times. A prompt change on 2026-08-03 made a *different*
   defect five times more likely (§6), and only a full-suite comparison would have caught it.

If you cannot show it helps, revert it. An unproven prompt line is worse than none, because the
next person will assume it was measured.

> **Resolved 2026-08-05 — committed.** Both steps were done. (1) 18 further `--class market`
> runs: 0 failures, so **0 failing runs in 38** combined against the 1-in-12 base rate,
> p ≈ 0.036 under the null. (2) Four full-suite runs: **53/53 in all four**, every class 100%,
> class E 6/6 each time — no sign of the priming effect §6 warns about. The measured binary was
> byte-checked to actually contain the new line, and each report was checked to show a real
> 10/10 market row. Evidence in `docs/beta-progress.md`, 2026-08-05 entries.

## 4. What changed on 2026-08-03

Substantial. Read `docs/beta-progress.md` from the 2026-08-03 entries down for the evidence.

**Retrieval**
- Exemplars are matched **question-to-question** (`questionVector`), not against the whole
  question-plus-reply blob. Measured: recall@3 91% → 97% over 1,000 exchanges, replicated.
- **Policy retrieval measured for the first time.** `PolicyTopK: 4` validated — topic-level
  recall@4 is 100% on both clean questions and realistic ticket text.
- Exemplar **reranking measured and rejected**: worse (recall@3 75% → 72%) *and* double cost.
- Exemplar failures are contained: they cost the exemplars, not the draft. Policy failures
  still fail the draft.

**Content safety**
- `tickets-v3`: 461 exchanges withheld that hand out unpublished discount codes or percentages.
  Includes codes built from customers' surnames (PETER50, SMITH15) — a name surviving redaction
  inside a token no name rule could see.
- The **sold-out template** no longer offers 10% + `ITALY10`.
- The internal **warranty-discounts** document is excluded from retrieval (`retrieval: exclude`
  in front matter, honoured by `tools/ingest/ingest.py`).
- **`ITALY10` now appears in zero chunks of any corpus.** Verify: search `"ITALY10"` in
  `knowledge-v1` filtered by each corpus.

**The eval suite, which was substantially untrustworthy**
- **Class E was dead.** All five original fabrication cases had a literal backspace byte (0x08)
  where `\b` was intended, so 20 assertions could never match. Release-blocking, green for
  weeks.
- **Class J was vacuous** at `--ticket-topk 0` and reported as a passing blocking class.
- **Class D tested vocabulary**, failing correct refusals ~3 runs in 4.
- **`PiiSweep` had no tests at all**, so its "zero findings" meant nothing.
- New: `no_unsourced_numbers` — every figure in a draft must appear in the ticket or a
  retrieved chunk. This caught two fabrications no regex could.

**Infrastructure**
- Application Insights + Log Analytics created (Sweden Central, 30-day retention, 0.5 GB/day
  cap). Request and Information-level traces verified arriving.
- Deploys now run the tests first. Before this, a commit that failed CI still deployed.
- Redaction rules and shell E2E now run in CI.

## 5. Open issues — verify each before trusting it

**These are the outgoing agent's descriptions. Re-run the measurements; several of today's
"fixes" were wrong on the first attempt and one made things worse.**

- **#24 sources block, variant 2 — open.** The model sometimes emits no `---SOURCES---` block
  at all, finishing normally (`stopped because stop`, not truncation). ~1 run in 12 of the
  market class. `must_cite_market` is release-blocking, so the suite goes red, and the panel
  shows a grounded draft as unsourced. **Variant 1 — the delimiter split across a line break —
  is fixed and tested.** This task was closed once on 8 clean runs and had to be reopened;
  do not repeat that.
- **#21 stream the process — not started.** The user's outstanding feature request: stream
  what the app is doing (market resolved, corpora searched, counts, gate decision, drafting),
  not just draft text. The pipeline already computes all of it for `RetrievalLog`.
- **#19 dead-check audit — not started.** Five separate ways a check looked green while testing
  nothing were found in one day, all by accident. A harness self-test — any `must_not_match`
  that cannot match a deliberately bad string is itself a failure — would make the search
  systematic. Arguably the highest-value remaining task.
- **#22 retrieval query.** `BuildQuery` uses subject + newest customer message only. A
  follow-up like "yes please do that" gives retrieval almost no signal. Measure with
  `tools/evals/policy_recall.py` before changing.
- **#18, #14, #16, #12, #15** — see the task list in §7.
- **The relevance gate.** `docs/go-no-go.md` says it does not discriminate. On 2026-08-03 a
  measurement **disagreed**: uncovered questions scored 1.1–2.4, covered never below 2.1, and
  "Christmas Greetings" scored **1.340**, not the 2.923 on record. That contradiction is
  unresolved and the real-ticket run was never finished. Do not act on either number without
  re-measuring.

## 6. Where this repo has been wrong before

Each is a live reason to distrust a green check.

- **A release-blocking class had 20 assertions that could never fire** for weeks (class E,
  0x08 for `\b`). The C# YAML parser accepted the control character silently; Python refused
  it, which is the only reason it surfaced.
- **A prompt change made a defect five times worse.** A clause naming the forbidden remedy in
  detail ("a discount, a code, a refund") took class E from 0 failing runs in 10 to 5 in 10.
  Reverted and re-measured to confirm. **Naming a specific thing inside a prohibition appears
  to prime it.**
- **A diagnostic lied.** The instrumentation added to explain missing citations reported
  "sources block never emitted" when the model *had* emitted one — it just did not match. That
  sent the investigation at the prompt for hours.
- **Assertions have three times failed correct refusals** by banning vocabulary rather than
  commitment. A refusal restates the demand in order to deny it.
- **A PII eval class passed over an empty index** and stayed green through four rounds in which
  humans found four leak classes.
- **English-only was reported fixed** while failing ~8% of the time.
- **An injection vulnerability failed 1 run in 6.** Re-run safety classes more than once.
- **Semantic ranking is metered.** Bulk eval runs cost money.
- **A zip deploy can leave the previous build serving.** Check the SHA in `/health`.

## 7. Task list

Numbers refer to the in-session task tracker, which does not persist. Recreate as needed.

Open: **#24** (sources block variant 2), **#21** (stream the process), **#19** (dead-check
audit), **#22** (retrieval query), **#18** (widen class J to other commitment types), **#14**
(market-entitlement conflict cases), **#15** (relevance gate), **#16** (exemplar recency —
`resolvedAt` is indexed and unused), **#12** (panel clipboard E2E).

Suggested order: #24 → #19 → #21. #19 because five dead checks were found by accident and
nobody has looked systematically.

## 8. Operating

Runbooks: `docs/rollback-runbook.md` (five levers, lever 5 is exemplars off, drilled
2026-08-03 in both directions — 99 s off, 98 s on), `docs/exemplar-runbook.md` (rebuild,
removal, erasure, PII sweep, provenance queries), `docs/go-no-go.md` (launch checklist).

App-setting changes restart App Service and take **90–100 seconds**, not the 70–90 quoted
elsewhere. `az` returns first. Poll `/v1/config`.

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
print(count("knowledge-v1"), count("tickets-v3"))
```

Measurement tools written on 2026-08-03, all reusable:
`tools/evals/policy_recall.py`, `exemplar_recall.py`, `exemplar_rerank.py`,
`tools/ingest/survey_commitments.py`, and `--sweep-corpus` on the eval tool.

## 9. What the user (Laurynas) still owes

Tracked in `ACTION-REQUIRED.md` at the repo root — **git-ignored via `.git/info/exclude`,
deliberately not committed.** If it is missing, it was never in git; recreate it.

- **Client sign-off** on the sold-out template wording, where a sentence was removed
- **D-2** beta terms acknowledgement
- **D-3** privacy review of 400 exchanges — still unsigned, and exemplars are live
- **First supervised agent session** — also the only way to confirm a real draft's provenance
  reaches Application Insights, which has never carried one

## 10. Working agreement

- **One task at a time, closed end to end** — build, test locally, commit, push to `main`
  (never a feature branch), check the pipeline, check Azure, verify live, then next
- **Measure before and after.** Every fix on 2026-08-03 that was declared without a
  before/after comparison was wrong at least once
- **Answer the prompt asked.** Do not drift back into previous work
- **Plain language, not task IDs**
- **Investigate before declaring something blocked.** Escalate decisions; go and get facts

## 11. Local files holding customer data

Git-ignored, on this machine only. `data/exemplars.jsonl` (38 MB) is superseded.
`exemplars.deduped.jsonl` is the source for re-running sanitisation.
`exemplars.clean.jsonl` now matches `tickets-v3` (17,402 rows — it was regenerated, so it no
longer matches v2). `exemplar-review-pack.md` awaits the D-3 reviewer. Do not move any of them
into `docs/` or anywhere shareable.
