# Knowledge corpus

Business content, not engineering documentation — different owners, different lifecycle,
different review process. Everything the assistant is allowed to ground an answer in lives
here.

## How `policy/` was produced — the P-1 fallback path

The authoritative source named by the rollup PDF (`data_reference/markets`, 99 files) was
never handed over. `knowledge/policy/` was therefore **reconstructed from the PDF** by
`tools/policy-pdf-to-markdown/convert.py`, run against
`docs/sop/tr-cs-current-policies-2026-06-22.pdf`.

This was cheaper and far more faithful than `rag-pipeline-proposal.md` §2.1 assumed. That
section claimed PDF extraction "loses heading structure, mangles bullets, and splits words
across layout runs". It does not: the PDF carries a text layer in which font size encodes
structure, so file boundaries, heading hierarchy and per-file source URLs are all recovered
exactly.

Verified after conversion:

| Check | Result |
|---|---|
| Files vs the PDF's own `Source file:` manifest | 99 / 99, none missing, none extra |
| Markets | 14 — `AU_NZ CA DE ES EU FR GLOBAL IT NL PL SE SG UK US` |
| Section headings preserved | 756 |
| Front-matter complete (`market`, `topic`, `exposure`, `effective_date`, `source_url`) | 99 / 99 |
| Recovered text vs policy content in the PDF (442,429 chars, excluding footers, TOC and cover) | 446,158 chars, +0.8 % |

**Outstanding:** the P-1 acceptance criteria also require a human to spot-check one file per
market against the PDF. That review has **not** been done — 14 files, and it is the step that
catches anything the automated checks cannot see.

### What the PDF lost before we ever received it

These are defects in the client's generated rollup, not in the conversion. They are the real
argument for obtaining the original markdown — a stronger one than the heading-structure
argument the proposal made:

1. **German, French, Spanish and Italian diacritics are stripped.** The PDF contains `fur`
   and `hochster`, not `für` and `höchster`. `ß` and Spanish `¿`/`¡` survive (they are
   distinct characters rather than combining marks), which is consistent with the generator
   having applied Unicode decomposition and dropped the marks. Customer-facing German policy
   text without umlauts is visibly wrong to a native reader, and it will degrade retrieval.
2. **Some sub-lists were flattened into prose** — `"This includes: Stitching Issues: … Hardware
   Failure: …"` was clearly a bulleted list. Most bullets (884) do survive.
3. **Markdown links were flattened** to `text (url)`, so link syntax is not re-linkified.
4. **At least one duplicated fragment exists in the PDF itself** — see
   `DE/warranty.md`, "Schaden durch äußere EinflusseSchaden durch äußere Einflusse". Present
   in the source bytes; not introduced here.

If the original markdown arrives, re-run nothing — replace `policy/` wholesale and delete the
converter. The front-matter contract is what downstream code depends on, not the provenance.

## Layout

```
knowledge/
  policy/<MARKET>/<topic>.md     # 99 files, market × topic
  templates/                     # 162 approved replies (P-3, not yet converted)
  internal/                      # internal procedure, never quoted (P-4, not yet converted)
  _meta/                         # valid markets, topics, generated manifest (P-2, P-7)
```

Directory position carries market and topic, so metadata cannot silently drift from
location.

## Front-matter contract

The interface between content and pipeline. Everything else can change as long as this holds.

```yaml
---
market: DE                    # one of _meta/markets.json, or GLOBAL
topic: warranty               # one of _meta/topics.json
exposure: customer            # customer | internal — required, never defaulted
effective_date: 2026-06-22
source_url: https://timeresistance.de/pages/garantie
version: 1
---
```

`exposure` is required with no default. A missing value fails validation rather than being
guessed, because the safe default and the useful default point in opposite directions.

## The PDFs in `docs/sop/`

Retained as the received artefacts and the provenance record for this one-time conversion.
They are **historical input, superseded by this tree**. Do not ingest them directly.
