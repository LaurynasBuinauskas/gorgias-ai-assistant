# Policy management proposal

**Status:** proposal for review. Nothing here is implemented.
**Goal:** make the policy corpus machine-ingestible now, and make non-engineer editing
possible later without redesigning anything.

---

## 1. How policies are structured today

Three PDFs in `docs/sop`, and they are three different kinds of thing:

### `tr-cs-current-policies-2026-06-22.pdf` — 181 pages, ~473k chars
A **generated rollup**, not a source document. Its own first page says so:

> Source root: `data_reference/markets` · Scope: primary website-backed policy markdown
> only. No LLM summarization or rewriting was used. Source markdown remains authoritative.
> Included source files: 99 · Excluded policy-adjacent files: 74

Underneath it is a clean two-level structure — **market × topic**:

- **14 markets:** US, EU, UK, DE, FR, ES, IT, NL, PL, SE, CA, AU_NZ, SG, GLOBAL
- **10 topic files** per market (not all markets have all topics): `faqs-clean.md`,
  `shipping-and-returns-clean.md`, `warranty-clean.md`, `terms-and-conditions-clean.md`,
  `privacy-policy-clean.md`, `cookies-policy-clean.md`, `personalization-clean.md`,
  `international-clean.md`, `withdrawal-instructions-clean.md`, `impressum-clean.md`

That is already close to an ideal knowledge layout. **The problem is that we hold the
generated PDF and not the 99 markdown files it was generated from.**

### `CS_ Support's Templates.pdf` — 71 pages, 162 templates
Approved replies, each with a name (`Personalization: MISSING DETAILS - MONOGRAM`), body
text, and a trailing `TAGS:` line (`PERSONALIZATION, laser engraving`). This is the house
voice in written form — and much of what the stakeholder hopes to extract from historical
tickets is already here, curated.

### `CS_ Internal Policies.pdf` — 19 pages
Internal procedure: repair triage, the `CS: RETURNS/REPAIRS` Asana project, creating
warranty discount codes in Shopify per locale, warehouse routing, the `REPAIR1` code. Two
properties matter: it is **never customer-facing**, and it is **partly written in
Lithuanian** (mixed-language sections appear from page 2).

## 2. What is wrong with the current state

1. **The authoritative source is missing.** We have a lossy derivative. PDF extraction of
   this document already shows damage — words split across layout runs, bullets flattened,
   headings indistinguishable from body text. Chunking that faithfully is fighting the
   format rather than the problem.
2. **Exposure is implicit.** Nothing in the folder marks the internal document as
   never-quotable. One filename convention stands between internal Shopify steps and a
   customer reply.
3. **Market is implicit.** It exists as headings inside a single 181-page file, so any
   consumer must parse structure to recover the most safety-critical attribute.
4. **No versioning of what the assistant used.** "The assistant said X" cannot be traced to
   a policy version. The date lives only in a filename.
5. **Not editable by non-engineers**, and the current shape actively prevents it —
   editing a generated PDF is not a workflow.
6. **`docs/` conflates audiences.** `docs/` is engineering documentation; the policy corpus
   is business content with a different lifecycle, different owners, and a different review
   process.

## 3. Proposed structure

Move the corpus out of `docs/` into a top-level `knowledge/` tree — content, not
documentation:

```
knowledge/
  policy/
    US/faqs.md
    US/shipping-and-returns.md
    DE/warranty.md
    GLOBAL/personalization.md
    ...                                # 99 files, market × topic
  templates/
    personalization/missing-details-monogram.md
    returns/refund-approved.md
    ...                                # 162 files, one per template
  internal/
    repair-policy.md
    warranty-discounts.md
    ...
  _meta/
    markets.json                       # the 14 valid market codes
    topics.json                        # the valid topic slugs
    manifest.json                      # generated: file → hash, version, indexed-at
```

Three reasons for this shape over one big file per market:

- **One file is one editable unit.** A non-engineer changing the German warranty text
  should open one small file, not scroll a rollup.
- **Change detection is free.** Per-file hashes drive incremental reindexing and give
  "which policy version produced this draft" for nothing.
- **Directory position carries market and topic**, so metadata cannot silently drift from
  content the way it can when both live in one document.

### Front-matter contract

Every file starts with YAML front-matter. **This contract is the interface between content
and pipeline** — everything else in this proposal can change as long as it holds:

```yaml
---
market: DE                    # one of _meta/markets.json, or GLOBAL
topic: warranty               # one of _meta/topics.json
exposure: customer            # customer | internal   (required, never defaulted)
effective_date: 2026-06-22
source_url: https://timeresistance.de/policies/warranty
version: 3
---
```

Templates add `tags: [personalization, monogram]`, carried over from the existing `TAGS:`
lines rather than discarded.

`exposure` is **required with no default**. A missing value fails validation rather than
guessing, because the safe default and the useful default point in opposite directions.

### Keep the PDFs

`docs/sop/*.pdf` stay as the received artefacts, marked in a README as historical input
superseded by `knowledge/`. They are the provenance record for the one-time conversion.

## 4. Path to non-engineer editing

Not required for beta. The point of the structure above is that **each stage is additive
and none requires reworking the pipeline.**

### Stage 1 — Beta (now)
Files live in the repo. An engineer runs the ingestion pipeline. Validation runs in CI.
Editing requires a pull request.
*Non-engineers cannot edit. Accepted for beta.*

### Stage 2 — Git-based editing (natural next step)
Content owners edit markdown directly through the GitHub web UI: open file, edit, propose
change. CI validates front-matter and structure; merge to `main` triggers reindex; the
alias swaps only if the smoke gate passes.

Gets them: full history, review before anything goes live, instant revert, and no new
system to run. Costs them: learning a pull request. For a handful of policy owners this is
usually a smaller ask than it sounds, and it is by far the cheapest stage to reach —
CI-side work only.

### Stage 3 — Editing surface without Git (if Stage 2 proves too much friction)
Two credible options, deferred deliberately until there is evidence about who edits and how
often:

- **SharePoint / OneDrive sync** — content owners edit in a familiar place; a scheduled job
  pulls, validates, and reindexes. Fits an organisation already in Microsoft 365. Weaker
  review and history.
- **Git-backed CMS** (Decap, Sveltia, or similar) — a friendly web form that commits
  markdown to the same repo. Keeps every Stage 2 guarantee and hides Git entirely. More
  moving parts to host.

**Decision deferred on purpose.** Both consume the Stage 2 contract unchanged, so choosing
between them later costs nothing now — and choosing now would be guessing about users we
have not met.

### What must stay true for any of this to work
1. Front-matter remains the contract.
2. Validation runs before indexing, never after — bad content fails loudly at the edge.
3. Reindexing is idempotent and safely repeatable.
4. The alias swap gates on a smoke query, so a bad edit cannot become a live wrong answer.

## 5. Validation rules

Enforced in CI (Stage 1) and by the same code at ingestion time — one implementation, two
call sites:

| Rule | Failure mode it prevents |
|---|---|
| `market` present and in `markets.json` | A typo'd market silently becoming unretrievable |
| `topic` present and in `topics.json` | Inconsistent filters |
| `exposure` present and explicitly `customer` or `internal` | Internal procedure reaching a customer |
| Directory position matches front-matter | Metadata drifting from location |
| `effective_date` parseable | Future-dated policy handling |
| No secrets, keys, or admin URLs in `exposure: customer` files | Credential leakage into an index |
| File under a size ceiling (say 100 KB) | One giant file defeating chunking |
| Markdown parses and has at least one heading | Heading-aware chunking silently degrading |

## 6. Open questions

1. **Can we get the 99 markdown files** from `data_reference/markets`? Blocks everything
   downstream; by far the highest-value item to obtain.
2. **What are the 74 excluded "policy-adjacent" files?** They may contain real coverage.
3. **Who owns policy content**, and who approves a change before it reaches customers?
   Determines whether Stage 2 review is meaningful or ceremonial.
4. **Should the Lithuanian sections of the internal document be translated?** Mixed-language
   text retrieves less reliably. Lower priority since internal content is decision support
   only and never quoted, but it will degrade internal retrieval quality.
5. **How often does policy actually change?** Monthly justifies Stage 2; daily justifies
   Stage 3 sooner; yearly means Stage 1 is fine indefinitely.

---

## Task breakdown

### P-1 — Obtain the authoritative policy markdown
**Depends on:** client action. **Blocks R-3, and therefore the beta.**
**Do:** Request the 99 files under `data_reference/markets` (or repository access). If
unavailable, execute the fallback: a one-off PDF→markdown conversion, split by market and
topic, with per-market human review. Record which path was taken in `knowledge/README.md`.
**Acceptance:** `knowledge/policy/` contains one file per market×topic; total extracted text
is within 5 % of the PDF's ~473k characters; a reviewer confirms one spot-checked file per
market against the PDF.

### P-2 — Define the knowledge layout and front-matter schema
**Depends on:** none. **Can start immediately and unblocks R-3's design.**
**Do:** Create the `knowledge/` skeleton, `_meta/markets.json` (the 14 codes),
`_meta/topics.json` (the 10 topic slugs), and `knowledge/README.md` documenting the
front-matter contract and the rule that `docs/sop/*.pdf` are superseded historical input.
**Acceptance:** the schema is documented with a complete worked example per corpus; the
market and topic lists match those observed in the source PDF exactly.

### P-3 — Convert the 162 templates into structured files
**Depends on:** P-2.
**Do:** Extract each template from `CS_ Support's Templates.pdf` into
`knowledge/templates/<topic>/<slug>.md`: name as title, body verbatim, existing `TAGS:`
line into front-matter `tags`, `exposure: customer`.
**Acceptance:** 162 files exist; every one has at least one tag; a random sample of 10
matches the PDF body **verbatim** — these are approved wording and paraphrase is a defect.

### P-4 — Convert internal procedure, marked internal
**Depends on:** P-2.
**Do:** Extract `CS_ Internal Policies.pdf` into topic files under `knowledge/internal/`,
every file `exposure: internal`. Preserve the Lithuanian passages as-is and flag them in
`knowledge/README.md` as a known retrieval-quality limitation.
**Acceptance:** no file under `knowledge/internal/` carries `exposure: customer`; a grep for
`Asana`, `Shopify`, `REPAIR1` and `warehouse` returns hits **only** under `internal/`.

### P-5 — Content validator
**Depends on:** P-2.
**Do:** Implement the §5 rules as a single validator (Python, reused by CI and the
ingestion pipeline). Non-zero exit with a per-file, per-rule report.
**Acceptance:** a deliberately broken fixture set — missing `exposure`, unknown market,
front-matter contradicting directory, an admin URL in a customer file — fails with one
clear message each; the real corpus passes clean.

### P-6 — Wire validation into CI
**Depends on:** P-5, P-1.
**Do:** Add a CI job running the validator on every change touching `knowledge/`.
**Acceptance:** a PR introducing an invalid file fails CI with the specific rule named; a
valid content change passes.

### P-7 — Manifest generation for provenance
**Depends on:** P-5.
**Do:** Generate `_meta/manifest.json` mapping each file to a content hash, version and
last-indexed timestamp; the ingestion pipeline consumes it for incremental reindexing and
records the hash on every indexed chunk as `sourceVersion`.
**Acceptance:** changing one file changes exactly one hash; a reindex with no content change
performs zero writes; any retrieved chunk can be traced to the exact file version behind it.

### P-8 — Stage 2 editing runbook (documentation only)
**Depends on:** P-6.
**Do:** Write `knowledge/EDITING.md` for a non-engineer: how to find the right file by market
and topic, edit it via the GitHub web UI, what validation will check, who reviews, and how
long until the change is live.
**Acceptance:** someone who has never used Git can follow it to make one policy edit; it
states explicitly that changes go live only after review and a successful reindex.
