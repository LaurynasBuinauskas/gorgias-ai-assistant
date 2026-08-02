# Rollback runbook

Four levers, fastest first. Each says what to run, what "it worked" looks like, and what it
costs — because the point of a lever is knowing which one to reach for while something is
already wrong.

**Read this first.** Every app-setting change restarts App Service and takes **70–90 seconds**,
not seconds. `az` returns as soon as Azure accepts the change, well before it is live. Poll for
the observable result rather than trusting the command's exit code. This applies in both
directions — turning something back on takes just as long.

Resource names: app `gorgias-assistant-api`, resource group `gorgias-assistant-rg`,
search service `gorgias-assistant-search`, vault `gorgias-assistant-kv`.

---

## Lever 1 — Kill switch

**When:** the assistant is doing something actively harmful and you want it gone from agents'
screens now. Drafting stops entirely; the shell mounts nothing.

```bash
az webapp config appsettings set --name gorgias-assistant-api --resource-group gorgias-assistant-rg --settings Shell__KillSwitch=true
```

**It worked when** `/v1/config` reports `"killSwitch":true`:

```bash
curl -s https://gorgias-assistant-api.azurewebsites.net/v1/config
```

Poll until it flips. Agents see the panel disappear on their next ticket; open panels persist
until reload, so it is not instant on screens already open.

**Cost:** total loss of the feature. Reverse by setting `false` and polling again.

---

## Lever 2 — Turn retrieval off, keep the assistant

**When:** drafts are wrong *because of what was retrieved* — a bad reindex, a policy document
that shouldn't have shipped, citations pointing somewhere embarrassing. Reverts to the
ticket-only prompt: worse drafts, but the known-acceptable behaviour from before grounding.

```bash
az webapp config appsettings set --name gorgias-assistant-api --resource-group gorgias-assistant-rg --settings Retrieval__Enabled=false
```

**It worked when** a generated draft carries no citations. `/health` stays `healthy` — this is
a normal mode, not a fault.

**Cost:** quality drops to ungrounded. Nothing breaks.

> `Retrieval__Enabled` is not currently present in the app settings, because the default is
> `true`. Setting it creates it; that is expected.

---

## Lever 3 — Roll back the index

**When:** a reindex shipped bad content and retrieval is otherwise fine.

**This is an app-setting change, not an alias swap.** The launch plan originally specified
aliases; that does not work here. Azure AI Search serves aliases **only on preview
api-versions** — a query through an alias returns 404 on the stable `2024-07-01` the app uses.
Aliases do exist on the service (`knowledge` → `knowledge-v1`, `tickets` → `tickets-v1`) and
are **unused**; do not build on them without moving the production request path onto a preview
contract Microsoft can retire. See `open-questions.md` D-4.

So: build the new version alongside the old, never over it.

```bash
# Build knowledge-v2 while knowledge-v1 keeps serving, then point the app at it.
az webapp config appsettings set --name gorgias-assistant-api --resource-group gorgias-assistant-rg --settings Knowledge__IndexName=knowledge-v2
```

**Rolling back is the same command with the old name.** The previous index is untouched and
still populated, which is the entire reason for versioning rather than rebuilding in place.

**It worked when** `/health` is `healthy` and a draft returns citations again. Confirm the
index holds what you expect *before* pointing at it:

```bash
python tools/ingest/verify.py
```

**Cost:** 70–90 seconds of restart. No data loss — nothing is deleted by a rollback.

> **Never rebuild in place under pressure.** A re-ingest is roughly an hour of embedding, and
> for all of it the assistant serves whatever the bad build produced. That hour is the reason
> this lever exists.

---

## Lever 4 — Redeploy the previous build

**When:** the API itself is broken — a bad release, an exception on every request.

```bash
gh run list --workflow deploy-api.yml --limit 5
gh run rerun <run-id>
```

**It worked when** `/health` reports the commit you expect:

```bash
curl -s https://gorgias-assistant-api.azurewebsites.net/health
```

The `version` field carries the git SHA, so a rollback is verifiable rather than assumed. Check
the SHA, not just `"status":"healthy"` — the known deploy defect below produces exactly that
combination while serving the wrong build.

**Known defect (audit #21):** a zip deploy can leave the previous build serving while new
routes throw. The deploy gate catches it, but **assume a restart may be needed**:

```bash
az webapp restart --name gorgias-assistant-api --resource-group gorgias-assistant-rg
```

Then poll `/health` again. A redeploy that reports the right SHA and still misbehaves is this
defect until proven otherwise.

**Cost:** several minutes. Highest-blast-radius lever; try 1–3 first unless the API is down.

---

## Choosing quickly

| Symptom | Lever |
|---|---|
| Drafts are harmful, unsure why | 1, then diagnose |
| Drafts cite the wrong or embarrassing content | 3 |
| Drafts are wrong in a way retrieval could cause, index looks fine | 2 |
| `/health` failing, 500s, or `degraded` populated | 4 |
| Exemplars misbehaving specifically | `Retrieval__TicketTopK=0` — off by default, so this is only relevant once they are enabled |

## After any rollback

1. Record what happened in `docs/beta-progress.md` — the decisions log is the incident record.
2. `/health` also reports `degraded` for retrieval health, including semantic-ranking quota
   exhaustion. A populated `degraded` with `healthy` status means the assistant is serving in a
   reduced mode — worth knowing before concluding the rollback fixed anything.
3. If the cause was content rather than code, the fix belongs in the ingest pipeline, not in a
   hand-edit of the index. A hand-edited index is undone by the next rebuild.
