# Blockers, client questions and human actions

Everything the beta needs from a person rather than from code. The task ledger
(`beta-progress.md`) tracks what is built; this tracks what is **waiting on an answer or a
decision**.

Statuses: `blocking` — the beta cannot ship without it · `wanted` — improves quality or
lowers future cost, does not gate the launch · `decision` — someone must choose, and it is
not the agent's call.

Last reviewed: 2026-08-01.

---

## 1. Blocking

### B-1 · How is a ticket's market determined? — `R-6`

**Ask the client:**

> Does each storefront have its own support email address (for example a separate inbox
> behind `timeresistance.de` and `timeresistance.co.uk`), and does your Shopify integration
> tell us which shop an order came from?

**Why it blocks.** Market is a correctness boundary, not a ranking preference. Answering a
German customer with US return terms is a wrong answer with legal weight, and it reads
perfectly fluently — the failure is invisible without the right signal. `R-6` blocks `R-7`,
`R-8`, `E-5` and therefore the go-live gate.

**What we already know** (evidence in `beta-progress.md`): the 14 markets map one-to-one onto
14 storefront domains, and per-market file counts match per-domain counts exactly. So market
is a property of *which storefront the ticket concerns*, not of where the customer lives —
a German tourist buying from `timeresistance.com` is correctly a US-market ticket.

**A yes to either half unblocks it.** Shop domain on the order is the cleanest signal; the
storefront inbox in `message.source.to[].address` is the fallback and is already present in
the Gorgias payload, currently discarded by `GorgiasMessageSourceDto`.

**Do not guess.** Message language is specifically excluded — Gorgias's own sample data pairs
`"language": "fr"` with a `US` address.

---

## 2. Decisions the client must make

### D-1 · Policy first, or slip the date for ticket exemplars? — `launch-plan.md` §5

The stakeholder's stated priority is ticket exemplars first, policy second. The plan
deliberately inverts the engineering order: policy grounding fixes *whether the answer is
right*, ticket exemplars fix *how it is phrased*, and shipping tone improvements on top of
unpolicied answers makes the assistant more confidently wrong.

Current plan: policy is the blocking path; ticket exemplars join **only if** `R-1` and `R-4`
land in time, otherwise the beta ships without them.

**The decision:** accept that, or slip the launch date rather than launch without exemplars.
Either is legitimate — but it should be chosen explicitly rather than discovered on day 5.

### D-2 · Beta terms acknowledged before go-live — `L-6`, `L-7`

The client needs to read and accept what "Beta" means: every draft is a suggestion, nothing
sends autonomously, coverage is partial and it will say so, declining is correct behaviour,
answers are English only, accuracy is not guaranteed, and it can be switched off instantly.
Go-live is gated on this being agreed rather than implied.

### D-4 · How does the API reach the index — alias, or configured index name? — `R-5`, `R-10`, `L-5`

**Ours to decide, not the client's, but it changes three tasks so it should be decided
deliberately.**

Discovered while provisioning (2026-08-01): Azure AI Search **index aliases do not exist on
any stable api-version**. `2024-07-01` rejects the `aliases` endpoint *and* fails to resolve
an alias at query time — `indexes/knowledge/docs/$count` returns 404 on stable, while
`indexes/knowledge-v1/docs/$count` succeeds. Aliases work only on preview versions.

`launch-plan.md` §9 lever 3 assumes the API reads through the alias, so a bad reindex is
rolled back by swapping the alias with no deploy. That assumption only holds if the API talks
a preview api-version in the request path.

| Option | Rollback | Cost |
|---|---|---|
| **A — API reads the alias** | Alias swap, instant, no restart | A preview service contract, and a beta `Azure.Search.Documents` package, in the production request path. Preview APIs can change under us |
| **B — API reads a configured index name** (recommended) | Update one App Service app setting; the app restarts and picks up the new index. No deploy, seconds not minutes | Restart rather than instant swap. The alias stays useful for tooling and humans |

Option B keeps the request path on a stable, supported contract, which is the more
conservative reading of "keep it cheap and boring" and of the rule that contracts are pinned.
It preserves the property that actually matters — rollback without a deploy — and gives up
only instantaneity.

Whichever is chosen, `R-10` and the `L-5` runbook must describe *that* mechanism, not the
alias swap the plan currently assumes.

### D-3 · Redaction sample signed off — `L-7`, `R-4`

Only applies if ticket exemplars ship. The client chose to redact personal data and retain
everything else, with no retention window. That makes redaction the **only** control between
customer PII and the search index. Before go-live someone on their side reviews a sample of
indexed exemplars and signs off that no personal data survived.

---

## 3. Wanted — not blocking

### W-1 · The original policy markdown — `P-1`

Previously listed as the project's biggest risk. It is not: the 99 files were successfully
reconstructed from the rollup PDF and verified against its own manifest.

**Still worth asking for, for a different reason.** The PDF had already lost every diacritic
in the non-English markets before we received it — the German text reads `fur` and `hochster`
rather than `für` and `höchster`, and the same applies to French, Spanish and Italian. That
damage is irreversible from the PDF, and customer-facing German policy text without umlauts
looks broken to a native reader.

> Can you share the 99 markdown files under `data_reference/markets`, or repository access?
> The PDF we were given has had accented characters stripped, so the German, French, Spanish
> and Italian policy text is damaged.

Secondary benefit: per-file versioning and a real editing path for Stage 2.

### W-2 · The 74 excluded "policy-adjacent" files

The rollup's cover page states 99 files were included and 74 excluded as "policy-adjacent".

> What are the 74 excluded files, and is any of that content something support actually
> relies on when answering customers?

Possible coverage gaps. If they contain answerable material, the relevance gate will decline
questions it should have been able to answer.

### W-3 · Who owns policy content, and who approves a change?

Determines whether the Stage 2 review step is meaningful or ceremonial, and who receives the
editing runbook (`P-8`).

### W-4 · How often does policy actually change?

Monthly justifies Stage 2 (GitHub web-UI editing). Daily argues for bringing Stage 3 forward.
Yearly means Stage 1 is fine indefinitely. Cheap to ask, and it decides how much is worth
building after the beta.

### W-5 · Should the Lithuanian sections of the internal document be translated? — `P-4`

Mixed-language text retrieves less reliably. Low priority, since internal content is decision
support and is never quoted to a customer — but it will degrade internal retrieval quality.

---

## 4. Human actions — ours, not the client's

| # | Action | Task | Why it cannot be automated |
|---|---|---|---|
| H-1 | Spot-check one converted policy file per market (14) against the PDF | `P-1` | The automated checks confirm structure and counts, not that the text reads correctly. Start with DE and ES, where the stripped-accent damage is most visible |
| H-2 | Exercise the kill switch against production, end to end | `L-3` | Needs a real Gorgias ticket and production config. An untested kill switch is not a kill switch — this one has never been pulled |
| H-3 | Read-only research against the live Gorgias account | `R-1` | Needs account access. Strictly read-only: no writes, no ticket mutations, no messages created |
| H-4 | Review a 20-draft sample before go-live | eval plan §4 | Catches whole categories nobody thought to assert. Needs someone who knows the policies |
| H-5 | Rehearse the rollback, not just document it | `L-5`, `L-7` | A runbook nobody has followed is a guess |

---

## 5. Resolved — kept for the record

| Question | Outcome | Date |
|---|---|---|
| Approval for ~$90/month Azure AI Search Basic | **Approved**, proceed on Basic | 2026-07-29 |
| Retention and erasure for indexed ticket data | **Redact personal data, retain the rest.** No retention window for beta; redaction becomes the load-bearing control | 2026-07-29 |
| Is per-ticket fetching acceptable? | **Yes** — offline job, may run overnight. `R-1` dropped from gate to timeboxed research | 2026-07-29 |
| Where is the authoritative policy markdown? | **No longer blocking.** Fallback executed; 99 files recovered from the PDF and verified. Downgraded to W-1 | 2026-08-01 |
| Which signal determines market — locale, country, or channel? | **Question was malformed** — no `locale` field exists in the Gorgias API. Narrowed to the factual question in B-1 | 2026-08-01 |
