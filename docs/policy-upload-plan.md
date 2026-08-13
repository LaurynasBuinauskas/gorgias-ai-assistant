# Client policy self-service — analysis and plan

**The request (2026-08-09):** the client's workers must be able to update the published
policy themselves, through an interface where they upload files, with no developer involved.

Everything below is either checkable against the repo or labelled as a judgement. Estimates
are judgements.

---

## 1. What already exists, and what is genuinely missing

The hard half of this feature is already built and battle-tested:

| Exists today | Where |
|---|---|
| Chunking, embedding, idempotent upsert into the search index | `tools/ingest/ingest.py` — content-hashed, so unchanged text costs nothing |
| Versioned-index promotion with one-setting rollback | `Knowledge__IndexName` app setting; the pattern shipped three ticket-corpus versions and the flip is drilled at 90–100 s |
| Live pickup of new content with no deploy | Retrieval hits the index per request; the next draft after ingest uses the new text |
| Content-quality gates | The 53-case eval suite (market separation, internal leakage, fabrication), `policy_recall.py`, banned-content patterns from the goodwill sweep, PII patterns from redaction |
| Authenticated, rate-limited API surface | `Copilot.Api` bearer auth + rate limiting |
| Audit sink | Application Insights, verified receiving Information-level traces |

What is missing is exactly the client-facing layer: **identity for client workers, an upload
UI, format conversion, a staging/approval step, and automation of the promote/rollback
levers** that today a developer pulls by hand.

## 2. Hard rules (these are the safety design, not preferences)

1. **The interface can only produce `exposure: customer` policy.** Front matter is a safety
   boundary: `exposure: internal` is what keeps do-not-quote material out of drafts. The
   upload UI never offers that choice; internal guidance and its ingest stay dev-managed.
2. **Templates are out of scope for v1.** Approved replies can establish entitlements — the
   `ITALY10` incident was a template problem. Client-editable templates are a separate,
   later decision with their own guardrails.
3. **No PDF ingestion.** The client's PDFs strip diacritics (`fur` for `für`) — a recorded,
   unfixed source defect. The UI accepts `.docx` and `.md` and rejects PDFs with a message
   saying why.
4. **Upload and publish are separate steps, both audited.** An upload lands in staging;
   nothing reaches a customer-facing draft until someone clicks Publish on a preview that
   shows what changed and what validation found.
5. **Publish is atomic and reversible.** Each publish builds a fresh versioned index and
   flips the app setting; the previous index is retained. Rollback is one click and
   ~100 seconds, same lever the runbooks already drill.
6. **Metadata is form fields, not front matter.** Workers pick market (fixed list) and
   topic (existing list + reviewed free text); they never write YAML. The system writes the
   front matter.

## 3. Architecture (judgement: the boring option at each fork)

```
Client worker (browser)
  └─ Admin page (new route in the panel SPA, same Static Web App)
       └─ HTTPS ──▶ Copilot.Api  /v1/admin/policy/*   (new endpoints, admin-only auth)
                       ├─ Azure Blob: knowledge-drafts/   (uploaded files, staging)
                       ├─ Azure Blob: knowledge-versions/ (immutable version ledger)
                       └─ Ingest runner (existing Python ingest wrapped in an Azure
                          Function, consumption plan) ──▶ staging index knowledge-vN+1
Publish:  validate ▶ build staging index ▶ targeted eval run ▶ flip Knowledge__IndexName
Rollback: flip back to knowledge-vN (retained)
```

Decisions taken and why (each reversible later):

- **Blob storage as the client-content source of truth**, not git-behind-a-UI and not
  PostgreSQL. Git write access driven by third-party uploads is a security surface into the
  codebase; Postgres is P2 machinery this feature does not need — a JSON ledger in blob
  covers versioning. Cost: pennies. The dev-managed corpora (templates, internal) stay in
  git; ingest merges both sources.
- **Reuse the Python ingest as an Azure Function** rather than porting chunking/embedding to
  C#. The Python code is proven and content-hash idempotent; a port would be a week of
  re-verification for zero user value. Consumption plan ≈ free at this volume.
- **`.docx` → markdown via `mammoth` inside the same Function**, so exactly one component
  understands content. Conversion output is shown to the uploader before publish — they see
  what the system read, not what they hoped it read.
- **Auth in two stages.** Pilot: a distinct admin bearer token (never the agent token),
  plus a mandatory "your name" field on every action, both logged. GA: Static Web Apps
  invitation-based auth (Entra/GitHub login for named client emails, role `policy-editor`),
  which gives real per-person identity. The pilot stage is honest about its weakness:
  a shared token attributes actions on trust.

## 4. The publish gate (what runs between Upload and Live)

Deterministic, < ~3 minutes, all automated:

1. **Structural validation** — file parses, size caps, market in the known set, topic
   resolves, resulting chunk count sane (a 40-page upload into one topic is flagged).
2. **Banned-content scan** — promo-code shapes and unpublished-discount patterns (from
   `survey_commitments.py`), PII patterns (from the redaction rules). Findings block
   publish with the offending line quoted.
3. **Staging index build** — full corpus (git tree + blob content) into `knowledge-vN+1`;
   the content hash makes unchanged chunks free.
4. **Targeted eval run against staging** — classes C (market separation) and A (internal
   leakage) plus the smoke cases: the content-sensitive classes, ~20 cases, ~2 minutes,
   cents. Red blocks publish. (The full 53-case suite stays a nightly/dev tool; requires a
   small `--knowledge-index` flag on the eval runner.)
5. **Human confirmation** — the preview shows converted text, chunk boundaries, a diff
   against the live version, markets touched, and validation results. Publish flips the
   setting; the UI polls `/v1/config` until the new version serves, same as the runbooks.

Judgement: this gate catches structure, leakage, and market mix-ups automatically. It cannot
catch a policy that is *factually wrong but well-formed* — that responsibility moves to the
client the moment they get the keys, and the beta terms should say so in one sentence.

## 5. Plan of action

**Phase 0 — decisions (client + us, before code):** see §7. Exit: the five questions
answered in writing.

**Phase 1 — pipeline without a UI (~3 days):** blob containers; admin endpoints
(`GET/POST /v1/admin/policy/files`, `POST .../publish`, `POST .../rollback`) with admin
token, rate limits, size caps, audit logging; ingest Function wrapping the existing tool
(+ docx conversion); staging-index build; `--knowledge-index` on the eval runner; targeted
eval gate wired into publish. Exit: a `.docx` uploaded by `curl` becomes a live, cited
policy chunk with no developer touching ingest, and rollback restores the prior version —
both demonstrated.

**Phase 2 — the interface (~3 days):** admin route in the panel SPA: current policy tree
grouped by market/topic; Replace / Add new with metadata form; conversion + chunk preview
with diff and validation results; Publish with progress; version history with one-click
rollback. Svelte 5, same state-machine discipline as the panel. Exit: a non-technical
person replaces a policy end to end with no instructions beyond the page itself.

**Phase 3 — hardening and handover (~2 days):** the client runbook (screenshots, plain
language); nightly scheduled full-suite eval against live config as the deep net;
go-no-go and technical-reference updates; a supervised first upload with the client's
worker driving. Exit: the client has done one real update themselves, and the audit trail
for it is queryable in Application Insights.

**Phase 4 — identity for GA (~2 days, before widening access):** SWA invitation auth with
named client emails replaces the shared admin token; optional maker–checker (uploader may
not publish their own change) if the client wants it. Exit: every action attributable to a
person without trusting a text field.

Total to a client-usable feature: **~8 working days** (phases 1–3), plus phase 4 before
general availability. Judgement, not a promise.

## 6. Cost

Blob < $1/mo; Function ≈ $0 at this volume; per-publish ≈ cents (embeddings for changed
chunks + ~20 eval drafts); one extra retained index within current Search tier limits.
Fits the ≤$30/month envelope with room.

## 7. Decisions needed from the client (Phase 0)

1. **Who** — the named people allowed to upload and publish (start with one or two).
2. **Formats** — is `.docx` + markdown acceptable, PDFs explicitly refused?
3. **Approval model** — is uploader-publishes acceptable for the pilot, or do they want
   maker–checker from day one (pushes phase 4 earlier)?
4. **Scope confirmation** — published policy only; templates and internal guidance remain
   dev-managed. Do they accept that boundary for v1?
5. **Responsibility line** — a sentence in the beta terms: content correctness of uploaded
   policy sits with the client; the system guards structure, leakage and market separation,
   not truth.

## 8. Explicitly out of scope for v1

Template editing, internal-guidance editing, exemplar management, PDF ingestion,
multi-tenant anything, and in-place chunk editing (replace-the-document is the unit of
change). Each can be revisited with its own justification.
