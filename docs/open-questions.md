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

### B-1 · ~~Which storefront's terms apply when signals disagree?~~ **ANSWERED 2026-08-01**

**Client decision: the storefront the customer ordered from wins.** A customer who bought
from the German store gets German terms, whichever inbox they wrote to. This is what
`StorefrontMarketResolver` already implements — order storefront outranks support inbox —
so no change was needed. Retained below for the reasoning.

### Original question — Which storefront's terms apply when signals disagree? — `R-6`

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

### D-4 · ~~How does the API reach the index — alias, or configured index name?~~ **ANSWERED 2026-08-02**

**Option B. The API reads a configured index name; rollback is an app-setting change.**

Re-verified empirically before closing, rather than taken on the earlier note: a query through
the `knowledge` alias returns **404 on stable `2024-07-01`** and succeeds on
`2024-05-01-preview` and `2025-05-01-preview`. Alias *management* is equally preview-only.
Aliases do exist on the service — `knowledge` → `knowledge-v1`, `tickets` → `tickets-v1` — and
are **unused**; they are a trap for anyone who assumes otherwise, so `rollback-runbook.md` says
so explicitly.

`launch-plan.md` §9 lever 3 has been corrected, and the mechanism is written up in
`rollback-runbook.md` lever 3: build the new version alongside the old, point
`Knowledge__IndexName` at it, roll back with the same command and the old name. Costs a 70–90
second restart; keeps the production request path on a supported contract.

A tool for driving aliases was written and then deleted — building on a rejected design is
worse than not building.

<details>
<summary>Original question, kept for the reasoning</summary>

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

</details>

### D-5 · ~~Is English-only actually required?~~ **ANSWERED 2026-08-01**

**Client decision: yes — drafts are always English.** The reason is not about customers,
it is about reviewers: the support agents primarily read English, so a draft in German is
one they cannot check before sending. English-first is what makes human-in-the-loop real
rather than ceremonial.

That resolves the tension with the 60 %-non-English template corpus below. Those templates
are what agents *send*, after review and translation. They are not what the assistant
should *draft*.

**Consequences:**

- The prompt already writes English by default for a non-English ticket, so no behaviour
  change is required for the default path.
- The translate quick action **stays** (client decision, 2026-08-01), so an explicit agent
  request is still honoured. "Always English" governs what the assistant produces
  unprompted, not what an agent may deliberately ask for.
- `E-4` (language class) is unblocked and asserts: a non-English ticket yields an English
  draft.
- `L-6` may state "drafts arrive in English so you can review them" as agreed.

### Original question — Is English-only actually required? — `L-1`, `L-2`, `E-4`, `L-6`

**Reopened 2026-08-01.** The plan recorded "the client wants English always" and built three
tasks on it. That is now on hold: `L-1` is deferred, the translate quick-action stays, and
the assistant behaves as it does today — English by default, another language on request.

**Ask the client:**

> Do you want drafts to always come back in English, or should agents keep the option to
> request another language? The panel currently offers a one-tap translate action and we have
> left it in place.

**New evidence, found while converting the templates (`P-3`, 2026-08-01).** The approved
template corpus is **60 % non-English**:

| Language | Templates | Language | Templates |
|---|---|---|---|
| English / unmarked | 64 | Italian | 14 |
| German | 14 | Dutch | 14 |
| Spanish | 14 | Polish | 14 |
| French | 14 | Swedish | 14 |

96 of the 162 carry a `language` tag. The support team **maintains approved replies in seven
languages** and keeps them current enough to sit in the same curated document as the English
ones. That is not the artefact of a team that answers only in English.

This cuts against the premise the plan recorded. It does not settle the question — the client
may be deliberately changing practice, and drafting in English for an agent to translate is a
coherent workflow — but "the client wants English always" should be re-confirmed rather than
assumed, because the evidence in their own content points the other way.

It also has a concrete cost: under English-only, **98 of 162 approved templates become
unusable** as customer-facing retrieval targets, and template grounding (`E-7`, class G) loses
most of its corpus.

**Why it needs a real answer rather than drift.** Four things hang off it. `E-4` is a
release-blocking eval class asserting every draft is English — as written it now tests
behaviour we are not building. `L-6` promises the client "answers are English only" as part
of the beta terms. Template retrieval quality depends on it, per the table above. And leaving
a translate button that a future `L-1` would contradict is the exact confusion the original
plan wanted to avoid.

Nothing is blocked while this sits open, but it should not reach go-live undecided.

### D-6 · ~~The relevance gate does not discriminate on real tickets~~ **LARGELY RESOLVED 2026-08-01**

**Measured, now that eval class D exists.** Lowering the threshold to a 1.6 floor did
**not** make the gate inert. Genuinely uncovered questions — company financials,
recruitment, workshop visits — are still declined *before the model is called*,
returning in ~330 ms against ~2,700 ms for questions that reach the model. The
"no spend on uncovered questions" property survives for the cases that matter.

What remains true is that the score does not discriminate on *ambiguous* real tickets,
so option B (cleaning the query before retrieval) is still worth doing eventually.
It is no longer urgent, and no longer a weakened guarantee — the floor plus the prompt
rule are together doing the job. Original analysis retained below.

### Original finding — the gate does not discriminate on real tickets — `R-7`, `E-6`

**Ours to decide. Found by measurement on 2026-08-01, and it contradicts a design assumption.**

`rag-pipeline-proposal.md` §8 step 4 assumes a calibrated reranker-score threshold separates
questions the policy corpus covers from ones it does not. `launch-plan.md` §3 calls the
resulting gate "the mechanism that converts 'confidently wrong' into 'honestly silent'" and
"the most important guardrail in the beta".

On clean hand-written questions the assumption holds — covered 2.71-2.89, uncovered 1.53-1.79.
**On 18 real tickets it does not:**

| Ticket | Score | Covered by policy? |
|---|---|---|
| `Christmas Greetings from all of us at Zryya!` | **2.923** | No — not a question at all |
| `Re: Your timeresistance.com return request has expired` | 2.676 | Yes |
| `Logo Gravur Bestellung #DE#4145` | 2.404 | Yes |
| `Return Item` | **2.186** | **Yes** |
| `New customer message on 1 August 2026` | 2.060 | Unknown — no subject content |
| `how did I check a gift card balance?` | 1.677 | No |

Range 1.677–2.923, median 2.248. A threshold of 2.2 **declined a genuine returns question and
admitted a holiday greeting.** Real ticket text carries signatures, quoted history and
pleasantries, and the reranker scores similarity to *some* policy chunk rather than whether
policy answers the question.

**Interim position:** the threshold is lowered to 1.6, below the lowest observed real ticket,
making it a floor that fires only when retrieval found essentially nothing. Coverage is carried
by the prompt rule added in `R-8` — "if the policy shown does not cover the question, say
plainly that you cannot confirm it". Tooling to re-measure is `tools/ingest/calibrate_gate.py`.

**What this costs:** uncovered questions now reach the model, so they cost tokens. The
"no spend, no invented answer" property from §8 is weakened to "no invented answer".

**Options, none free:**

| Option | Trade |
|---|---|
| **A — keep the floor, rely on the prompt** (current) | Simple, no extra cost per draft. Coverage depends on model compliance, measured by eval class D rather than guaranteed |
| **B — clean the query before retrieving** | Strip signatures, quoted history and boilerplate so the score reflects the question. Likely improves separation; needs its own calibration run and does not obviously fix "Christmas Greetings" |
| **C — second-stage check** | Ask a cheap model "does this policy answer this question?" before drafting. Restores the hard gate; adds a call per draft and a second thing to evaluate |

I would not choose between these before eval class D exists, because D is what measures whether
the current behaviour is actually a problem. Flagging it now so the weakened guarantee is a
decision rather than a silent regression.

### D-3 · Redaction sample signed off — `L-7`, `R-4`

Only applies if ticket exemplars ship. The client chose to redact personal data and retain
everything else, with no retention window. That makes redaction the **only** control between
customer PII and the search index. Before go-live someone on their side reviews a sample of
indexed exemplars and signs off that no personal data survived.

**This is now the highest-value open item, and the evidence says fifty is not enough.**

Four samples of fifty were drawn from the live index on 2026-08-02, each after the previous
round's fixes had been applied and reindexed. **Every round found a leak class the automated
checks had passed**, and the eval PII class stayed green throughout:

| Round | Found | Why no check could catch it |
|---|---|---|
| 1 | Street name orphaned when the phone rule ate the house number; per-recipient tracking links (213); quoted boilerplate | A bare street name matches no pattern; a URL token is an identifier that looks like a URL |
| 2 | Corporate signatures written inline with pipes (426) | Job title + employer + city is often one person and matches nothing |
| 3 | Third-party names in gift engraving; regulated-industry disclaimers | Names are *matched*, not detected — an engraved name is neither customer nor agent |
| 4 | Customer-announced address blocks; `Chase` absent from street types | Street-type enumeration always lags reality |

Each is now a regression fixture and the measurable classes sit at zero, but **the rate of
discovery is the finding**. A sample of fifty is 0.3 % of 17,892: a class appearing once is
likely present in dozens unseen, and a class absent from fifty says almost nothing. Iterating
sample-fix-resample converges slowly and has no defined end.

What would actually settle it: a person reading **300–500** exchanges, weighted toward the long
tail, rather than fifty. `tools/ingest/review_sample.py --size 400` produces it; the output
lands under the git-ignored `data/` and must be sent to the reviewer directly, not committed.

**What is not at stake:** the beta. `TicketTopK` is 0 and `KnowledgeRetriever` short-circuits
on `topK <= 0`, so no exemplar text reaches a draft today and none reached the eval drafts
either. The exposure is customer-derived data at rest in the search index. Enabling exemplars
is a deliberate one-setting change that should follow the sign-off, not precede it.

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

---

## D-7 · ~~Semantic ranking is metered, and the meter ran out~~ **ANSWERED 2026-08-02**

**Option 1 and option 3, together: billing enabled, and reranking cut to policy only.**

Verified live rather than assumed — the service reports `semanticSearch: standard` on the
`basic` SKU, and `KnowledgeRetriever` passes `rerank: true` for exactly one corpus.

The fourfold cut was worth taking on its own terms, independent of billing. The gate scores
policy and nothing else, so reranking templates, internal guidance and exemplars was buying
rankings that nothing read. **One semantic query per draft, not four.**

What that means for cost, arithmetically:

| | |
|---|---|
| Semantic queries per draft | 1 (was 4) |
| Free allowance | 1,000/month — now ~1,000 drafts rather than 250 |
| Beyond free | roughly $1 per 1,000 queries, against the ~$75/month Basic tier already approved |

At any plausible pilot volume this is single-digit dollars a month, and below the free
allowance it is nothing. The exposure that mattered was never the price — it was an
unhandled 402 taking drafting down, and that now has a fallback, a `/health` signal, and a
gate that stands down rather than declining everything when it cannot score.

**One risk stays open and is accepted:** eval runs spend the same meter as production. A full
suite is ~51 semantic queries, and it was repeated eval runs that exhausted the month in the
first place. Billing means this now costs money instead of causing an outage, which is the
right trade, but bulk eval runs should not be treated as free. Separating them would mean a
second search service — more standing cost than the problem justifies.

<details>
<summary>Original decision, kept for the reasoning</summary>

### Semantic ranking is metered, and the meter ran out

**Found the hard way on 2026-08-02: production drafting returned HTTP 500 for a period.**

Azure AI Search semantic reranking on the **free** tier allows 1,000 queries per month. Each
draft spends **four** — one per corpus. Repeated eval runs exhausted the month's allowance,
Search began returning `402 Payment Required`, and every draft failed.

### What it cost, and what it revealed

Two things were wrong beyond the quota itself:

- **No fallback.** A metered dependency failing in a way that is neither transient nor
  retryable had no handling at all. There is now a switch (`Knowledge:UseSemanticRanking`).
- **The gate does not survive without it.** Measured: with reranking off, Search returns
  fusion scores clustered around 0.032 which do **not** separate covered from uncovered
  questions — "wholesale prices" scored 0.0333 against "how long do I have to return an item"
  at 0.0325. Turning reranking off does not degrade the relevance gate, it **removes** it.

### Where production stands right now

Running **without semantic reranking**, and therefore **without the relevance gate**:

```
Knowledge__UseSemanticRanking = false
Retrieval__MinimumPolicyScore = 0
Retrieval__SemanticRankingEnabled = false
```

Drafting works and citations are correct. What is lost is ranking quality and the honest
decline — coverage now rests entirely on the prompt rule, which eval class D was measuring as
a *second* line rather than the only one.

### The decision

| Option | Cost | Consequence |
|---|---|---|
| **Enable semantic billing** | Metered per query, on top of the ~$75/month Basic tier | Restores ranking quality and the relevance gate. Every draft spends 4 queries, so cost scales with usage |
| **Stay without reranking** | Nothing extra | Weaker ranking, no gate. The prompt still instructs the model to decline, but nothing enforces it |
| **Reduce queries per draft** | Nothing extra | Retrieving fewer corpora, or only reranking policy, cuts spend roughly fourfold and keeps the gate |

The third is worth considering regardless: reranking only the policy corpus would have made the
free allowance last four times longer, and policy is the only corpus the gate scores.

**Until this is decided, eval runs consume the same meter as production.** That is the
underlying problem — a test suite and the live product sharing a quota with no separation.

</details>
