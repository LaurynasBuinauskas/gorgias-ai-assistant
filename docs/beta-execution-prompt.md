# Beta execution prompt

The prompt to hand a Claude Code agent (Opus 5 or Sonnet 5) that implements the beta plan
**one task at a time**. Section 1 starts a session; section 2 resumes one. Section 3 is the
order the tasks come in and why.

---

## 1. Session kickoff prompt

Paste this at the start of a session.

````text
You are implementing the Gorgias support-assistant Beta plan in this repository, one task
at a time. The plan already exists and was reviewed — your job is execution, not redesign.

## Read first (in this order, before touching any code)

1. `CLAUDE.md` — coding standards and non-negotiable principles. These override your
   defaults and are not open to interpretation.
2. `docs/beta-progress.md` — the task ledger. If it does not exist, create it from the task
   lists in the documents below, every task `todo`.
3. `docs/launch-plan.md` — scope, guardrails, sequencing, client decisions (§10), tasks L-*.
4. `docs/rag-pipeline-proposal.md` — retrieval architecture, index schema, tasks R-*.
5. `docs/policy-management-proposal.md` — knowledge layout, front-matter contract, tasks P-*.
6. `docs/policy-adherence-eval-plan.md` — eval classes, thresholds, tasks E-*.

The task's own **Do** and **Acceptance** lines in those documents are the specification.
Do not substitute your own idea of what the task should be.

## First, brief me — before you touch any code

Once you have read the documents, write a short briefing and **stop**. Keep the whole thing
under roughly 40 lines. It is a summary for someone who has read the plan before and wants
to confirm you understood it — not a restatement of the documents.

1. **The plan in five sentences or fewer.** What the beta changes, why the current
   assistant produces generic answers, and what "done" looks like.
2. **The three things that could sink it** — one line each, drawn from the documents, not
   invented.
3. **The task list**, grouped by wave, one line per task in the form
   `R-9 — input caps and output token limit (blocking for beta)`. No paragraphs.
4. **What is blocked and on whom**, with the question that unblocks it.
5. **The first task you will do**, and anything in it you read as ambiguous.

Then ask whether to proceed. After I say go, run the loop below continuously — the briefing
is a one-time checkpoint, not a per-task approval gate.

## The loop — repeat for one task at a time

1. **Pick** the next task from `docs/beta-progress.md` whose dependencies are all `done`.
   Announce the task ID and what you are about to do in two or three sentences.
2. **Check it is not blocked.** If it depends on a client answer that has not arrived
   (see "Hard stops" below), skip it, mark it `blocked` with the reason, and move to the
   next eligible task.
3. **Implement** it. Smallest correct change that satisfies the acceptance criteria.
4. **Test locally** — write the tests the acceptance criteria imply, then:
   ```bash
   dotnet test
   ```
   ```bash
   pnpm lint && pnpm check && pnpm -r test && pnpm build
   ```
   Run only what the change touches, but never skip tests for the layer you changed.
5. **Verify against the acceptance criteria literally**, line by line. If a criterion says
   "deliberately break it and prove the check fails", actually do that — a test that passes
   vacuously is worse than no test.
6. **Commit.** Imperative mood, one focused commit, and end the message with:
   ```
   Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
   ```
7. **Deploy, if the task changed deployable code.** Pushing to `main` triggers
   `deploy-api.yml` (backend) and `deploy-panel.yml` (panel). Watch the run, then verify
   the deployed build actually serves — `GET /health` reports the build version, and a
   passing workflow is not proof the new code is live.
8. **Update `docs/beta-progress.md`**: status, the commit SHA, and one line on anything a
   later task needs to know.
9. **Report** in the format below, then continue to the next task without waiting for
   permission — unless you hit a hard stop.

## Report format after each task

```
[TASK-ID] <title> — done | blocked | needs decision
Changed:    <files, one line>
Verified:   <which acceptance criteria, and how — not "tests pass">
Deployed:   <yes + version served | no + why not>
Next:       <task id>
Watch out:  <anything that surprised you, or "nothing">
```

## Hard stops — stop and ask the user

- **`P-1`** (authoritative policy markdown) and **`R-6`** (which signal determines a
  ticket's market). Both are unanswered client questions. Do not guess either. A wrong
  market is a wrong answer with legal weight; inventing a policy source is worse.
- Any acceptance criterion you cannot meet. Say so plainly and stop — do not weaken the
  criterion to make it pass.
- Any **blocking eval threshold** in `policy-adherence-eval-plan.md` §5 that fails. Those
  thresholds were fixed before any scores existed, deliberately. Moving one requires the
  user's explicit written decision, never yours.
- Anything requiring a new Azure resource beyond Azure AI Search Basic, or a new extension
  permission beyond `storage`.
- A task that turns out to be substantially larger than its description. Report the gap and
  propose a split rather than silently expanding scope.

## Rules that hold for every task

- **Stack:** C# + Azure for everything at runtime. Python **only** for offline
  transformation and ingestion under `tools/`. No Python in the request path, ever.
- **No vendor LLM SDK types outside `Copilot.Ai`.** All model access goes through
  `Microsoft.Extensions.AI` abstractions.
- **No database.** Retrieval is Azure AI Search; the backend stays stateless.
- **Contracts are append-only.** postMessage types and API DTOs get a new version rather
  than a changed shape.
- **Output language is English, unconditionally.** No configuration flag, no per-request
  override.
- **`exposure: internal` content informs decisions and is never quoted to a customer.**
  If you are unsure whether a change can leak internal procedure, assume it can and add the
  assertion.
- **Never run against live customer tickets.** Sandbox or synthetic fixtures only. Eval
  fixtures are synthetic with invented identities — the repo must not become another copy
  of customer data.
- **Never commit secrets.** Runtime secrets come from Key Vault via managed identity.
- Do not refactor code the task does not touch. Note it and move on; if it is genuinely
  worth doing, add it to `docs/next-tasks.md`.
- When you discover something that contradicts a planning document, **update the document
  in the same commit**. A stale plan is how the next session goes wrong.

Start by reading the documents, then give me the briefing described above and wait.
````

## 2. Resume prompt

For a later session, when the ledger already exists.

````text
Continue implementing the Gorgias beta plan. Read `CLAUDE.md` and `docs/beta-progress.md`,
then give me a five-line status — done so far, blocked and on whom, next task, anything
that changed the plan — and wait for my go-ahead. After that, run the loop and hard stops
in `docs/beta-execution-prompt.md` §1 continuously.
````

To pin a specific task instead of letting it choose:

````text
Do task R-5 only. Its spec is in `docs/rag-pipeline-proposal.md`. Follow the loop in
`docs/beta-execution-prompt.md` §1, stop after it, and report.
````

## 3. Execution order

Dependencies are recorded on each task in the source documents and are authoritative — this
is the order they produce, grouped by what can run at the same time.

### Wave 0 — nothing blocks these; start here

| Task | Why it goes first |
|---|---|
| `R-9` Input caps + `MaxOutputTokens` | Blocking for beta. Retrieval multiplies prompt size and there is no cap today |
| `L-1` English-only unconditional | Small, self-contained, and `R-8` depends on it |
| `L-3` Prove the kill switch | The rollback lever nobody has ever pulled. Cheap to verify, expensive to discover broken |
| `P-2` Knowledge layout + front-matter schema | Unblocks `R-3` design and every P-* conversion |
| `R-2` Provision Azure AI Search + index schema | Cost approved 2026-07-29; long-lead item |
| `R-1` Ticket extraction research (half a day, read-only) | Timeboxed. Answers volume sizing for `R-4` |

### Wave 1

`L-2` (after L-1) · `P-3` templates · `P-4` internal corpus · `P-5` validator ·
`R-5` `IKnowledgeStore` over Azure AI Search · `R-4` extraction + redaction (after R-1, R-2) ·
`L-4` feedback capture

`P-1` sits here and is **blocked on the client**. If it stays blocked, the fallback path in
its spec (PDF conversion with per-market human review) is a decision for the user, not the
agent.

### Wave 2

`P-6` CI validation · `P-7` manifest/provenance · `R-3` ingestion (needs P-2 plus real
content) · `R-6` market resolution (**blocked on the client**) · `R-7` retrieval + relevance
gate · `R-8` grounded prompt · `R-10` reindex/alias/rollback · `R-11` retrieval observability

### Wave 3 — evaluation

`E-1` harness · `E-2` assertions · then `E-3` … `E-8` and `E-11` in any order ·
`L-5` rollback runbook · `L-6` beta expectations document · `P-8` editing runbook

### Wave 4 — the gate

`E-9` go-live report · `E-10` reindex smoke subset · `L-7` go/no-go checklist

`L-7` is the last task and it is a gate, not a formality. If a blocking threshold is unmet,
the correct outcome is that the beta does not ship.

### One ordering note worth keeping in view

The policy track is the blocking path; the ticket track runs alongside and joins the beta
only if `R-1` and `R-4` land in time (`launch-plan.md` §5). If ticket exemplars are cut,
`E-11` is cut with them — but if they ship, `E-11` ships too. Redaction is the only control
standing between customer PII and the index, so it is verified twice: `R-4` fail-closed at
ingestion, `E-11` sweeping the live index. Neither may be dropped to save time.
