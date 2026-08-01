# Beta progress ledger

The single source of truth for **what is done**. The executing agent updates this after
every task; the specs live in the four planning documents and are not repeated here.

**Status values:** `todo` · `in progress` · `blocked` · `done`
**Rule:** a task may only start when every dependency is `done`. Never mark `done` without
the acceptance criteria in the source document having actually been checked.

Specs: `rag-pipeline-proposal.md` (R-*) · `policy-management-proposal.md` (P-*) ·
`launch-plan.md` (L-*) · `policy-adherence-eval-plan.md` (E-*).
Execution loop and hard stops: `beta-execution-prompt.md`.

## Blocked on the client — do not guess

| Task | Question | Asked | State |
|---|---|---|---|
| `R-6` | **Does each storefront have its own support email address, and does the Shopify integration expose which shop an order came from?** | not yet asked | **open — the only blocker** |

### `R-6` — what is actually needed, and why the question narrowed

The original question ("Shopify order locale, customer country, or Gorgias channel?") was
based on a field that does not exist. There is **no `locale`** anywhere in the Gorgias API
surface; what the payload carries, under `customer.integrations.<id>` with
`__integration_type__: "shopify"`, is `orders[].billing_address.country_code` and
`customer.default_address.country_code`.

Evidence from `docs/sop/tr-cs-current-policies-2026-06-22.pdf`: the 14 markets map **one to
one** onto 14 storefront domains, and the per-market file counts match the per-domain counts
exactly (EU 8/8, DE 8/8, US 6/6, all others 7/7).

| Market | Storefront | Market | Storefront |
|---|---|---|---|
| US | `timeresistance.com` | IT | `timeresistance.it` |
| EU | `eu.timeresistance.com` | NL | `timeresistance.nl` |
| GLOBAL | `global.timeresistance.com` | PL | `timeresistance.pl` |
| UK | `timeresistance.co.uk` | SE | `timeresistance.se` |
| DE | `timeresistance.de` | SG | `timeresistance.sg` |
| FR | `timeresistance.fr` | CA | `ca.timeresistance.com` |
| ES | `timeresistance.es` | AU_NZ | `au.timeresistance.com` |

So market is a property of **which storefront the ticket concerns**, not of where the
customer lives. A German tourist ordering from `timeresistance.com` is a US-market ticket,
and that is correct.

Intended signal order once confirmed — first match wins, log which signal decided:

1. Shopify shop domain on the order the ticket concerns → market (exact).
2. `message.source.to[].address` domain — the storefront inbox emailed → market.
3. `country_code` from the Shopify default/billing address → country→market map
   (`GB`→UK, `AU`/`NZ`→AU_NZ, other EU member states→EU, …).
4. `GLOBAL`.

**Never language.** Gorgias's own sample customer carries `"language": "fr"` alongside a `US`
billing address — their documentation illustrates the exact failure mode.

Signal 2 is already in the payload and currently discarded:
`backend/Copilot.Gorgias/GorgiasMessageSourceDto.cs` maps only `Type`, dropping `from` and
`to`. That is the `TicketContext` extension `R-6` anticipates.

**Not verified, and not to be guessed:** whether this tenant runs a separate support inbox per
storefront, and whether its Shopify integration is one multi-store connection or fourteen.
The storefront finding comes from repository artefacts, not from the live Gorgias account.

### Resolved

| Task | Question | Outcome |
|---|---|---|
| `P-1` | Where is the authoritative policy markdown (`data_reference/markets`, 99 files)? | **No longer blocking.** Never received; the fallback path was executed instead — `knowledge/policy/` reconstructed from the rollup PDF. Still worth requesting for a sane long-term edit path and to recover stripped diacritics. See `knowledge/README.md`. |

## Working priority (set 2026-08-01)

**Answer quality first.** The goal driving task order is making the assistant follow policy,
retrieve from completed tickets, and answer better. Launch polish — feedback widgets, badges,
copy changes — comes after, not before.

Concretely: the `P-*` content pipeline and `R-3`/`R-5` retrieval work outrank the remaining
`L-*` tasks. `L-1` and `L-4` are deferred for this reason.

The chain to "drafts are policy-grounded" is `P-2 → P-3/P-4 → R-3` (index populated)
`→ R-5` (retrieval callable) `→ R-7 → R-8` (retrieval used).

### `R-6` is a soft blocker, not a hard one (established 2026-08-01)

`R-6` supplies one thing: given a `TicketContext`, which market applies. Put that behind
`IMarketResolver` with a `GLOBAL`-fallback implementation and **everything downstream can be
built and evaluated without the client answer.**

The eval track needs it least of all. The harness supplies market per case
(`market: DE` is a field in the case format), and `E-5`'s own acceptance says *"deliberately
forcing market resolution to `US` on the DE case makes it fail"* — the harness overrides the
resolver by design. So even the market-divergence class tests **filtering**, not resolution.

What still genuinely needs `R-6`: a real ticket in production being assigned the right
market. Nothing else.

**This does not make it optional.** Under a `GLOBAL` fallback the assistant answers from
`GLOBAL` policy for everyone — substantively identical to every market on routine questions,
wrong on German statutory ones. Acceptable for building and measuring; **not acceptable for
go-live**, and `L-7` holds that line.

## Execution order

Phases are sequenced by what each one makes possible, not by task-ID grouping.

**Phase 1 — make the assistant use the index.** `R-5` → `R-6a` → `R-7` → `R-8` → `R-11`.
Ends with drafts grounded in policy and every retrieval traceable. Biggest single jump in
answer quality, and the whole point of the beta.

**Phase 2 — prove it works.** `E-1` → `E-2` → `E-3` → `E-4` → `E-6` → `E-7` → `E-5`.
Failures become mechanically detectable instead of a matter of impression. `E-4` needs
rewriting before it is built (see its row). `R-11` sits in Phase 1 deliberately: when an eval
fails you need to see what was retrieved.

**Phase 3 — ticket exemplars.** `R-1` → `R-4` → `E-11`. The parallel track from
`launch-plan.md` §5; joins the beta only if it lands in time. `R-1` is cheap, read-only and
decision-relevant, so it can move earlier on request. `E-11` follows `R-4` closely because
redaction is the only control between customer PII and the index, and `E-11` is its second,
independent verification.

**Phase 4 — governance and operations.** `P-5` → `P-6` → `P-7` → `R-10` → `E-10` → `L-3`
→ `L-5`. Content validation in CI, provenance, reindex with alias rollback, the smoke subset
gating that rollback, and the kill switch finally exercised.

**Phase 5 — ship.** `E-8` (advisory judge) → `E-9` (go-live report) → `L-6` (beta terms)
→ `P-8` (editing runbook) → `L-7` (the gate).

**`R-6` inserts wherever the client answer lands** and must be done before `L-7`.
Two other human actions also gate `L-7`: the `P-1` per-market spot check, and the 20-draft
human review.

## Tasks

| Task | Title | Depends on | Status | Commit | Note |
|---|---|---|---|---|---|
| `R-9` | Input caps and output token limit | — | done | `092be5f` | Verified against production: 8/8 checks. Caps return 400 before the Gorgias lookup or any model call; `MaxOutputTokens` set on both call paths; transcript trimmed to newest messages; retrieval allowance reserved so `R-7` cannot silently blow the ceiling. Bodies over 128 KB surface as 502 (App Service reports Kestrel's abort, not 413) |
| `L-1` | Make English-only unconditional | — | **deferred** | | Client decision reopened 2026-08-01. Translation stays supported for now; see `open-questions.md` D-5 |
| `L-3` | Prove the kill switch works end to end | — | todo | | Never exercised |
| `P-2` | Knowledge layout and front-matter schema | — | done | | `_meta/markets.json` (14) + `topics.json` (10) generated from the corpus; all 99 policy files validate — market/topic resolve and match directory position. Generator asserts 1 storefront per market, independently confirming the `R-6` mapping |
| `R-2` | Provision Azure AI Search and define the index | — | done | | Basic, Sweden Central. `knowledge-v1` behind alias `knowledge`; smoke proves filtered hybrid retrieval and market exclusion. **Aliases need a preview api-version — see `open-questions.md` D-4** |
| `R-1` | Research ticket extraction path | — | done | | **20,042 closed tickets in 12 months but only ~58% usable (~11,700); full backfill ~3.1 hours, not overnight.** Whole conversation returned, verified to 23 messages. The ~14 s per-ticket cost did not reproduce (0.20 s median; integrations blob is empty on this account). Rate limit, not latency, is the constraint — concurrency buys nothing. See `gorgias-extraction-findings.md` |
| `L-2` | Label the panel Beta | — | done | `59dc9d0` | Badge sits outside every conditional, so it renders in all states. Verified on the deployed panel: renders, CSP `frame-ancestors` intact. Authenticated states not exercised in a browser — entering the API token into a form is not something the agent does |
| `L-4` | In-panel feedback capture | — | **deferred** | | Postponed 2026-08-01 as late-game polish. Answer quality comes first; revisit before go-live since `L-6` promises a way to report a bad draft |
| `P-1` | Obtain authoritative policy markdown | — | in progress | | Fallback executed: 99 files reconstructed from the PDF, verified against its manifest. **Outstanding: human spot-check of one file per market (14)** |
| `P-3` | Convert the 162 templates | P-2 | done | | 162 files, 11 topics, every one tagged; 10 sampled bodies are exact substrings of the PDF text layer. **60 % are non-English** (14 each in DE/ES/FR/IT/NL/PL/SE) — material evidence for `open-questions.md` D-5 |
| `P-4` | Convert internal procedure, marked internal | P-2 | done | | 6 files, all `exposure: internal`; containment check passes over 261 customer-facing files. **Corrected the Class A banned vocabulary** — `REPAIR1` and `warehouse` appear in approved customer templates |
| `P-5` | Content validator | P-2 | todo | | Python, shared by CI + ingestion |
| `R-5` | `IKnowledgeStore` over Azure AI Search | R-2 | done | | Hybrid + semantic over the live index. 6 integration tests cover market filter, exposure filter, internal reachability, empty market, empty query. Removing the market predicate makes `NeverReturnsAnotherMarketsPolicy` fail, so the check is real. A reflection test asserts no Azure/OpenAI type reaches the contract |
| `R-4` | Extraction and redaction of closed tickets | R-1, R-2 | todo | | Fail-closed redaction check |
| `P-6` | Wire validation into CI | P-5, P-1 | todo | | |
| `P-7` | Manifest generation for provenance | P-5 | todo | | |
| `R-3` | Offline ingestion for policy/templates/internal | P-2, R-2 | done | | **400 chunks live in `knowledge-v1`** (224 policy, 162 template, 14 internal). All 14 markets present; DE-filtered query returns only DE; customer-filtered query returns no internal content; second run embeds and writes nothing |
| `R-6a` | `IMarketResolver` seam + `GLOBAL` fallback | — | done | | Placeholder that unblocks `R-7` onward. Returns `GLOBAL` and reports `MarketSignal.Fallback`, so a fallback is never mistaken for a resolved market |
| `R-6` | Deterministic market resolution | client | blocked | | See table above. `R-6a` removes it from the critical path; **still gates `L-7`** |
| `R-7` | Retrieval step and relevance gate | R-5, R-6a | done | | Retrieval runs ahead of the model; below threshold the pipeline declines with **zero chat calls**. Threshold calibrated against the live index (covered 2.71-2.89, uncovered 1.53-1.79) and set to 2.2 in config. Internal guidance kept in its own do-not-quote block. Rollback lever 2 (`Retrieval:Enabled=false`) bypasses retrieval and the gate |
| `R-8` | Grounded prompt with citations | R-7 | done | | Fenced labelled blocks; ticket marked untrusted; internal fenced do-not-quote **and excluded from citable labels**. Citations split off the reply body so the agent copies clean text. Verified on a real returns ticket: cited `GLOBAL/shipping-and-returns.md`. **Relevance-gate threshold lowered to a floor — see `open-questions.md` D-6** |
| `R-10` | Reindex, alias swap and rollback | R-3, R-4 | todo | | |
| `R-11` | Retrieval observability | R-7 | done | | Every line keyed by draft id: market + deciding signal, chunk ids and scores per corpus, gate decision, prompt size, usage, citations. Chunk ids decoded to readable natural keys. **Fixed: the streaming path minted its own draft id**, so feedback could never have been traced to a retrieval. Tests assert no customer or policy text reaches any log line |
| `E-1` | Eval harness skeleton | R-7 | todo | | `tools/Copilot.Evals` |
| `E-2` | Assertion library | E-1 | todo | | |
| `E-3` | Class A: internal leakage cases | E-2, P-4 | todo | | Blocking class |
| `E-4` | Class B: language cases | E-2 | todo | | **Needs rewriting before it is built.** It asserts every draft returns English even when an agent asks otherwise — no longer the intended behaviour now `L-1` is deferred |
| `E-5` | Class C: market divergence cases | E-2, R-6, R-3 | todo | | Blocking class |
| `E-6` | Class D and E: refusal and fabrication | E-2, R-7 | todo | | |
| `E-7` | Class F: injection cases | E-2, R-8 | todo | | Blocking class |
| `E-8` | LLM judge for faithfulness | E-1 | todo | | Advisory only, never blocks alone |
| `E-11` | Class I: PII leakage cases | E-2, R-4 | todo | | Blocking if ticket exemplars ship |
| `L-5` | Rollback runbook | R-10, L-3 | todo | | |
| `L-6` | Beta expectations document | L-3 | todo | | Client-facing. **`L-1` and `L-4` deferred**, so drop the "English only" line and the promise of in-panel feedback reporting, or restore those tasks first |
| `P-8` | Stage 2 editing runbook | P-6 | todo | | Documentation only |
| `E-9` | Go-live report and gate | E-3 … E-8, E-11 | todo | | |
| `E-10` | Reindex smoke subset | E-3, E-4, E-5, R-10 | todo | | |
| `L-7` | Go/no-go checklist | all E-*, L-1…L-6, R-7, R-9 | todo | | The gate |

## Decisions log

Append here whenever a task resolves a question or changes a planning document.

| Date | Decision | Source |
|---|---|---|
| 2026-07-29 | ~$90/month Azure AI Search Basic approved | client |
| 2026-07-29 | Redact personal data, retain everything else; no retention window for beta | client |
| 2026-07-29 | Per-ticket fetching acceptable; `R-1` becomes timeboxed research | client |
| 2026-08-01 | `P-1` unblocked by executing the fallback. The premise that PDF extraction is too lossy was wrong — font size encodes structure, so the 99 files were recovered exactly. `rag-pipeline-proposal.md` §2.1 corrected | investigation |
| 2026-08-01 | The PDF strips diacritics from all non-English markets (`fur`, not `für`). Pre-existing defect in the client's rollup, not the conversion. Now the strongest reason to still want the original markdown | investigation |
| 2026-08-01 | `R-6` question narrowed: "Shopify order locale" does not exist in the Gorgias API. Market maps 1:1 onto 14 storefront domains, so the question became a factual one about the tenant's inbox and Shopify setup | investigation |
| 2026-08-01 | Market divergence measured against the real corpus, not assumed. Return windows (30 days), duties guarantees and lifetime warranty are the **same in every market**; divergence is concentrated in statutory apparatus — DE-only Widerrufsbelehrung and Impressum, EU-only international page, ES data-protection references — plus the local language of DE/FR/ES/IT text. A wrong market on a routine question is largely harmless; on a statutory one it is not | investigation |
| 2026-08-01 | `L-1` deferred and translation kept. The English-only requirement goes back to the client as an open question | user |
| 2026-08-01 | `L-2` reduced to a Beta badge — no tooltip, nothing removed | user |
| 2026-08-01 | `L-4` deferred as late-game polish | user |
| 2026-08-01 | Template corpus is 60 % non-English — the team maintains approved replies in seven languages. Cuts against the recorded "client wants English always" premise; under English-only, 98 of 162 templates become unusable as customer-facing retrieval targets | investigation |
| 2026-08-01 | Class A (internal leakage) banned vocabulary was wrong. `REPAIR1` is handed to customers in five approved templates and `warehouse` is ordinary customer language; banning them would fail drafts for reproducing approved wording on a class that blocks release at 100 %. Corrected to `Asana`, `Shopify`, `CS: RETURNS`, `Odoo`, `kokybe` | investigation |
| 2026-08-01 | Topic vocabularies are per corpus, not global. Policy organises by published page, templates by support category, internal by procedure — one shared list would invent mappings nobody uses | investigation |
| 2026-08-01 | One chunk per heading retrieves badly — policy is full of two-line clauses, giving ~124-token chunks with too little context to interpret. Packing consecutive sections to the 500-800 token target cut policy chunks from 902 to 224 and raised the median to ~556 tokens | investigation |
| 2026-08-01 | The relevance gate does not discriminate on real tickets. Measured over 18: a genuine "Return Item" scored 2.186 while "Christmas Greetings" scored 2.923. A 2.2 threshold declined the real question and admitted the greeting. Lowered to a 1.6 floor; coverage now rests on the prompt rule and eval class D. See `open-questions.md` D-6 | investigation |
| 2026-08-01 | "Closed" is not "resolved": only 58% of closed tickets contain a customer message answered by an agent. Chat is 100% usable, email 44% — carrier notifications and marketing dominate the waste. Usable exemplars ~11,700, not 20,042 | investigation |
| 2026-08-01 | Ticket extraction is far cheaper than planned: 20,042 closed tickets in 12 months, ~3.1 hours to backfill, per-ticket fetch 0.20 s rather than the ~14 s the plan assumed. The naive walk is the recommended strategy; search, views and jobs are unnecessary | investigation |
| 2026-08-01 | **Priority set explicitly: answer quality first.** Policy grounding, retrieval over completed tickets, and better answers outrank launch polish. The plan's task order is re-sequenced around that | user |
