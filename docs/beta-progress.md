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
`→ R-5` (retrieval callable) `→ R-7 → R-8` (retrieval used). **`R-7` is blocked on `R-6`**, so
everything up to and including `R-5` is reachable now and the last two steps are not.

## Tasks

| Task | Title | Depends on | Status | Commit | Note |
|---|---|---|---|---|---|
| `R-9` | Input caps and output token limit | — | done | `092be5f` | Verified against production: 8/8 checks. Caps return 400 before the Gorgias lookup or any model call; `MaxOutputTokens` set on both call paths; transcript trimmed to newest messages; retrieval allowance reserved so `R-7` cannot silently blow the ceiling. Bodies over 128 KB surface as 502 (App Service reports Kestrel's abort, not 413) |
| `L-1` | Make English-only unconditional | — | **deferred** | | Client decision reopened 2026-08-01. Translation stays supported for now; see `open-questions.md` D-5 |
| `L-3` | Prove the kill switch works end to end | — | todo | | Never exercised |
| `P-2` | Knowledge layout and front-matter schema | — | done | | `_meta/markets.json` (14) + `topics.json` (10) generated from the corpus; all 99 policy files validate — market/topic resolve and match directory position. Generator asserts 1 storefront per market, independently confirming the `R-6` mapping |
| `R-2` | Provision Azure AI Search and define the index | — | done | | Basic, Sweden Central. `knowledge-v1` behind alias `knowledge`; smoke proves filtered hybrid retrieval and market exclusion. **Aliases need a preview api-version — see `open-questions.md` D-4** |
| `R-1` | Research ticket extraction path | — | todo | | Timeboxed half a day, read-only |
| `L-2` | Label the panel Beta | — | done | `59dc9d0` | Badge sits outside every conditional, so it renders in all states. Verified on the deployed panel: renders, CSP `frame-ancestors` intact. Authenticated states not exercised in a browser — entering the API token into a form is not something the agent does |
| `L-4` | In-panel feedback capture | — | **deferred** | | Postponed 2026-08-01 as late-game polish. Answer quality comes first; revisit before go-live since `L-6` promises a way to report a bad draft |
| `P-1` | Obtain authoritative policy markdown | — | in progress | | Fallback executed: 99 files reconstructed from the PDF, verified against its manifest. **Outstanding: human spot-check of one file per market (14)** |
| `P-3` | Convert the 162 templates | P-2 | done | | 162 files, 11 topics, every one tagged; 10 sampled bodies are exact substrings of the PDF text layer. **60 % are non-English** (14 each in DE/ES/FR/IT/NL/PL/SE) — material evidence for `open-questions.md` D-5 |
| `P-4` | Convert internal procedure, marked internal | P-2 | todo | | All `exposure: internal` |
| `P-5` | Content validator | P-2 | todo | | Python, shared by CI + ingestion |
| `R-5` | `IKnowledgeStore` over Azure AI Search | R-2 | todo | | |
| `R-4` | Extraction and redaction of closed tickets | R-1, R-2 | todo | | Fail-closed redaction check |
| `P-6` | Wire validation into CI | P-5, P-1 | todo | | |
| `P-7` | Manifest generation for provenance | P-5 | todo | | |
| `R-3` | Offline ingestion for policy/templates/internal | P-2, R-2 (+ content) | todo | | |
| `R-6` | Deterministic market resolution | client | blocked | | See table above |
| `R-7` | Retrieval step and relevance gate | R-5, R-6 | todo | | |
| `R-8` | Grounded prompt with citations | R-7, L-1 | todo | | |
| `R-10` | Reindex, alias swap and rollback | R-3, R-4 | todo | | |
| `R-11` | Retrieval observability | R-7 | todo | | |
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
| 2026-08-01 | **Priority set explicitly: answer quality first.** Policy grounding, retrieval over completed tickets, and better answers outrank launch polish. The plan's task order is re-sequenced around that | user |
