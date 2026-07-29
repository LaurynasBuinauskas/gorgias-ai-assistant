# Next tasks — a batch I can run autonomously

Ten stories, ordered. Every one is doable without SOP documents, without a DevTools
snippet, and without an Azure or client decision — so this batch can start immediately.

**Status:** stories 2–5 are done and deployed (2026-07-29); story 1 was deferred by
request. Stories 6–10 remain.

**Sizing:** 7 Easy, 3 Medium. Roughly 1.5–2 days end to end.
**Sources:** `code-audit-2026-07.md` (defects) and `project-status.md` (roadmap).
**Nothing here is Hard** — OIDC, the knowledge base, and docking are deliberately excluded
(see *Not in this batch* at the end).

| # | Story | Effort | Why now |
|---|---|---|---|
| 1 | Cap draft requests and model output | Easy | Protects the OpenAI balance |
| 2 | ~~Rate-limit per client~~ | ✅ Done | Removes an unauthenticated DoS |
| 3 | ~~Stop cross-ticket contamination~~ | ✅ Done | Prevents a customer-data leak |
| 4 | ~~Survive client disconnects mid-stream~~ | ✅ Done | Stops unhandled exceptions |
| 5 | ~~Harden the public telemetry endpoint~~ | ✅ Done | Closes log injection |
| 6 | Fail loudly on misconfiguration | Medium | No more silent breakage |
| 7 | Keep the debug harness out of production | Easy | Tightens the production CSP |
| 8 | Delete dead code and the unused endpoint | Easy | Removes an unguarded surface |
| 9 | Cover the auth boundary with tests | Medium | The riskiest code is untested |
| 10 | Add security scanning to CI | Easy | Catches the next issue early |

---

## 1. Cap draft requests and model output — **Easy**

*Audit #2, #6 · Critical*

**Why:** `Turns` has no cap on count or length, `Instruction` has no maximum, and no
`MaxOutputTokens` is set. Anyone with the token can post a 30 MB body and have it
forwarded verbatim to OpenAI — unbounded spend against a prepaid balance.

**Change:** `Copilot.Api/Contracts/DraftRequestV1.cs`,
`Copilot.Pipeline/DraftingPipeline.cs`, `Program.cs`

- Validate at the boundary: max 20 turns, max ~8 000 characters per turn, max ~2 000 for
  the instruction, and a total-character ceiling. Reject with `400` and a clear message.
- Validate `Role` strictly — today anything that isn't `"assistant"` silently becomes
  `Agent`.
- Set `ChatOptions.MaxOutputTokens` on both the streaming and non-streaming calls.
- Set an explicit request-body size limit rather than relying on Kestrel's 30 MB default.

**Done when:** an oversized request returns 400 without reaching OpenAI; a malformed role
is rejected; unit tests cover each limit.

## 2. Rate-limit per client, after auth — **Easy**

*Audit #3 · Critical*

**Why:** one global 100/min partition shared by everyone, and the limiter runs *before*
authentication — so an unauthenticated caller can exhaust it and lock out the whole team.

**Change:** `Copilot.Api/RateLimiting/RateLimitingExtensions.cs`, `Program.cs`

- Move `UseRateLimiter()` after `UseBearerTokenAuthentication()`.
- Partition by client (token hash, falling back to IP) instead of one `"global"` bucket.
- Give the LLM endpoints a much tighter limit than `/v1/config` and `/v1/telemetry/anchor`.

**Done when:** one client hitting its limit doesn't affect another, and unauthenticated
requests are rejected before consuming any budget.

## 3. Stop cross-ticket contamination — **Medium**

*Audit #1, #8 · Critical*

**Why:** the streaming loop never checks that the ticket is still current and nothing
aborts an in-flight request. Generate on ticket A → switch to B → generate again, and
ticket A's deltas land in ticket B's draft. Customer A's text ends up in Customer B's
panel, and drafts get pasted into real replies.

**Change:** `panel/src/App.svelte`, `panel/src/lib/stream.ts`

- Capture the ticket ID when a run starts; ignore any event whose ticket no longer matches.
- Add an `AbortController`, pass its signal to `fetch`, and abort on ticket switch and on
  component teardown. This also stops paying for abandoned generations.

**Done when:** a test drives generate → switch → generate and asserts no delta from the
first stream reaches the second conversation; aborting a run closes the connection.

## 4. Survive client disconnects mid-stream — **Easy**

*Audit #10*

**Why:** the first `ticket` event is written *outside* the `try`, so a disconnect there
throws unhandled; and the `catch` writes with `CancellationToken.None` onto a possibly
dead connection, which can throw again.

**Change:** `Copilot.Api/Endpoints/DraftEndpoints.cs`

- Move the `ticket` write inside the `try`.
- Guard the error-path write with `HttpContext.RequestAborted.IsCancellationRequested`.

**Done when:** cancelling a request mid-stream logs nothing unhandled.

## 5. Harden the public telemetry endpoint — **Easy**

*Audit #7*

**Why:** `/v1/telemetry/anchor` is unauthenticated by design, but `Account` and `Mode` are
attacker-controlled strings logged verbatim and unvalidated. Newlines allow log forging,
and anyone can flood App Insights ingestion.

**Change:** `Copilot.Api/Endpoints/TelemetryEndpoints.cs`,
`Copilot.Api/Contracts/AnchorTelemetryRequestV1.cs`

- Restrict `Mode` to `docked` / `floating`; reject anything else with 400.
- Cap and sanitise `Account` (strip control characters, clamp the length).

**Done when:** a payload with newlines or an unknown mode is rejected, and valid reports
still log cleanly.

## 6. Fail loudly on misconfiguration — **Medium**

*Audit #11, #12*

**Why:** `Api:AllowedOrigins` defaults to an empty array with no startup validation, so a
typo yields a healthy-looking API that rejects the panel with an opaque CORS error. And
with no global exception handler, `GorgiasApiException` surfaces as a bare 500 with no
correlation ID, while the two draft endpoints report errors differently.

**Change:** `Copilot.Api/Cors/CorsExtensions.cs`, `Program.cs`

- `ValidateOnStart` for `Api:AllowedOrigins` in non-development environments, matching
  every other setting.
- Narrow `AllowAnyMethod()`/`AllowAnyHeader()` to what's actually used.
- Add `UseExceptionHandler` with ProblemDetails and a correlation ID.

**Done when:** the API refuses to start in production without an allowed origin, and
unhandled failures return a consistent problem-details body.

## 7. Keep the debug harness out of production — **Easy**

*Audit #9*

**Why:** `harness.html` ships to Static Web Apps, and the production CSP was loosened to
`frame-ancestors 'self'` purely to keep it working. A debug tool shouldn't shape the
production security policy.

**Change:** `panel/vite.config.ts`, `panel/public/staticwebapp.config.json`,
`.github/workflows/deploy-panel.yml`

- Exclude `harness.html` from the production build (keep it in dev).
- Tighten CSP back to `frame-ancestors https://*.gorgias.com`.

**Done when:** the deployed site 404s on `/harness.html`, the CSP header no longer
includes `'self'`, and local development still works.

## 8. Delete dead code and the unused endpoint — **Easy**

*Audit #13*

**Why:** `panel/src/lib/api.ts` is imported by nothing, and the non-streaming
`POST /v1/tickets/{id}/drafts` it mirrored has no caller — an unvalidated, cost-bearing
endpoint still exposed. `Copilot.Knowledge` is empty and `tools/Copilot.Ingest` is still
`Hello, World!`.

**Change:** delete `panel/src/lib/api.ts`; remove the non-streaming endpoint (and its
`DraftResponseV1` usage if it becomes unreferenced). Leave the two Stage 2 projects in
place — they're placeholders with a clear purpose — but add a one-line README to each so
they don't read as abandoned.

**Done when:** no dead exports remain, the API surface is only what the panel calls, and
the build stays green.

## 9. Cover the auth boundary with tests — **Medium**

*Audit #19*

**Why:** 35 tests cover the pipeline, mapper, and state machine, but nothing asserts that
`/v1/config` is public while drafts are protected, or that a bad token 401s. The auth
middleware is the least-tested, highest-risk code in the repo — and stories 1, 2 and 6 all
modify the request pipeline.

**Change:** new `Copilot.Tests` integration tests using `WebApplicationFactory` with a
fake `IGorgiasTicketClient` and `IChatClient`.

Cover: no token → 401; wrong token → 401; valid token → 200; `/v1/config` and
`/v1/telemetry/anchor` reachable without a token; oversized request → 400; unknown ticket
→ 404.

**Done when:** the suite runs in CI with no network or API keys required.

## 10. Add security scanning to CI — **Easy**

*Audit #15*

**Why:** both scans pass today, but nothing enforces that. There's no CodeQL, no
`pnpm audit`, no `dotnet list package --vulnerable`, and no dependency review.

**Change:** `.github/workflows/ci.yml` (or a small `security.yml`)

- `dotnet list package --vulnerable --include-transitive`, failing on any hit.
- `pnpm audit --prod`, failing on high or critical.
- Optionally CodeQL for C# and TypeScript on a weekly schedule.

**Done when:** CI fails on a known-vulnerable dependency and passes on the current tree.

---

## Suggested sequencing

**Day 1 — the three criticals plus the quick wins:** stories 1, 2, 4, 5. Small, contained,
and they remove the sharpest edges (money, availability, log integrity).

**Day 2 — the data leak and the safety net:** story 3, then 9 so the pipeline changes from
day 1 are actually covered.

**Then, in any order:** 6, 7, 8, 10.

Each story is independently shippable — small commits, CI green between them.

## Not in this batch

**Blocked on you, not on effort:**

- **Stage 2 — knowledge base and retrieval gate.** Needs SOP/FAQ documents. I can write a
  realistic Time Resistance stand-in set to prove the mechanism if you'd rather not wait —
  say the word and it becomes a task.
- **Docking and insert-into-composer.** Both need one DevTools snippet of the container
  beside the Gorgias ticket view. Easy once that arrives.
- **LLM provider DPA confirmation.** A reading task, but it gates launch.

**Deliberately deferred (Hard, or low value right now):**

- **OIDC / PKCE sign-in** (audit #4) — the right fix for the shared token, but a genuine
  project. Schedule with P2.
- **Prompt-injection hardening** (audit #5) — needs adversarial test cases to prove it
  works, not just feels better.
- **Eval harness** — Medium and valuable, but its main job is making prompt tuning safe,
  and tuning starts in earnest once there's a knowledge base.
- **Health endpoint + App Insights** (audit #20), **token reset control** (#18), **docked
  hide button** (#17), **warnings-as-errors** (#14) — all fine follow-ups, none urgent.
