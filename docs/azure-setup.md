# Azure setup — one-time runbook

Everything needed to run the assistant in the cloud. Provisioning is CLI-by-hand and
captured here rather than in Bicep: it is a handful of resources for a single pilot and
reproducing them is rare.

**Secrets never live in GitHub or in app config.** They sit in Key Vault; App Service
resolves them at startup through its managed identity. The deploy workflows never see them.

**Order matters:** create *both* the App Service and the Static Web App before setting app
settings or GitHub variables — each needs the other's hostname.

---

## 0. Prerequisites

- An Azure subscription (a pay-as-you-go one is fine; this stack is ~$14/month, less on F1).
- Azure CLI: `az --version` — install from https://aka.ms/azcli if missing.
- Sign in and pick the subscription:

```sh
az login
az account set --subscription "<subscription-name-or-id>"
```

- Admin access to the GitHub repo (to add secrets and variables).

## 1. Variables

`API_APP`, `KV`, and `SWA` must be **globally unique** — add a suffix if creation fails.

```sh
RG=gorgias-assistant-rg
LOC=westeurope
PLAN=gorgias-assistant-plan
API_APP=gorgias-assistant-api
KV=gorgias-assistant-kv
SWA=gorgias-assistant-panel
```

## 2. Create the resources

```sh
az group create -n $RG -l $LOC

# B1 (~$13/mo) supports Always On, which avoids cold starts. For a throwaway demo
# swap --sku B1 for --sku F1 (free) and skip the Always On line below.
az appservice plan create -g $RG -n $PLAN --is-linux --sku B1

az webapp create -g $RG -p $PLAN -n $API_APP --runtime "DOTNETCORE:10.0"

az webapp update -g $RG -n $API_APP --https-only true
az webapp config set -g $RG -n $API_APP --always-on true   # skip on F1

# Static Web Apps exists in a limited set of regions.
az staticwebapp create -g $RG -n $SWA -l westeurope
```

> **If `DOTNETCORE:10.0` is rejected**, see what's available:
> `az webapp list-runtimes --os linux | grep -i dotnet`
> If .NET 10 isn't offered yet, create the app with `--runtime "DOTNETCORE:8.0"` and make
> the deploy self-contained: add `--self-contained --runtime linux-x64` to the
> `dotnet publish` line in `.github/workflows/deploy-api.yml`.

Capture both hostnames — you need them in steps 5 and 7:

```sh
API_HOST=$(az webapp show -g $RG -n $API_APP --query defaultHostName -o tsv)
SWA_HOST=$(az staticwebapp show -g $RG -n $SWA --query defaultHostname -o tsv)
echo "API:   https://$API_HOST"
echo "Panel: https://$SWA_HOST"
```

## 3. Key Vault + managed identity

```sh
az keyvault create -g $RG -n $KV -l $LOC --enable-rbac-authorization true

az webapp identity assign -g $RG -n $API_APP

PRINCIPAL=$(az webapp identity show -g $RG -n $API_APP --query principalId -o tsv)
KV_ID=$(az keyvault show -g $RG -n $KV --query id -o tsv)

az role assignment create --assignee $PRINCIPAL --role "Key Vault Secrets User" --scope $KV_ID
```

You also need permission to write secrets yourself:

```sh
ME=$(az ad signed-in-user show --query id -o tsv)
az role assignment create --assignee $ME --role "Key Vault Secrets Officer" --scope $KV_ID
```

## 4. Store the secrets

Same values as your local user-secrets, except the bearer token, which must be a **fresh
long random string** — never `local-dev-token`.

```sh
# Generate a production bearer token
openssl rand -base64 32     # or: pwsh -c "[guid]::NewGuid().ToString('N')+[guid]::NewGuid().ToString('N')"

az keyvault secret set --vault-name $KV -n gorgias-apikey  --value "<gorgias-api-key>"
az keyvault secret set --vault-name $KV -n openai-apikey   --value "<openai-api-key>"
az keyvault secret set --vault-name $KV -n api-bearertoken --value "<generated-token>"
```

## 5. App settings

Non-secrets set directly; secrets as **Key Vault references** resolved via managed identity.

```sh
az webapp config appsettings set -g $RG -n $API_APP --settings \
  Gorgias__Subdomain="timeresistance" \
  Gorgias__Email="<gorgias-login-email>" \
  Gorgias__ApiKey="@Microsoft.KeyVault(SecretUri=https://$KV.vault.azure.net/secrets/gorgias-apikey/)" \
  OpenAi__ApiKey="@Microsoft.KeyVault(SecretUri=https://$KV.vault.azure.net/secrets/openai-apikey/)" \
  Api__BearerToken="@Microsoft.KeyVault(SecretUri=https://$KV.vault.azure.net/secrets/api-bearertoken/)" \
  Api__AllowedOrigins__0="https://$SWA_HOST"
```

`Api__AllowedOrigins__0` is the **only** origin CORS accepts in production (dev allows any
loopback origin). Add `__1`, `__2` only with a reason.

Confirm the references resolved (RBAC can take a few minutes to propagate — if they show
as unresolved, wait, then `az webapp restart -g $RG -n $API_APP`):

```sh
az webapp config appsettings list -g $RG -n $API_APP -o table
```

## 6. Enable publish-profile deployments

New App Services often ship with **SCM basic authentication disabled**, which makes
`azure/webapps-deploy` fail with a 401 even though everything else is correct.

```sh
az resource update -g $RG --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies \
  --name scm --parent sites/$API_APP --set properties.allow=true
```

(Portal equivalent: App Service → Settings → Configuration → General settings →
**SCM Basic Auth Publishing Credentials** → On.)

## 7. GitHub secrets and variables

Repo → **Settings → Secrets and variables → Actions**.

**Secrets** (tab: Secrets):

| Name | Get it with |
|---|---|
| `AZURE_API_PUBLISH_PROFILE` | `az webapp deployment list-publishing-profiles -g $RG -n $API_APP --xml` (paste the whole XML) |
| `AZURE_SWA_TOKEN` | `az staticwebapp secrets list -g $RG -n $SWA --query "properties.apiKey" -o tsv` |

**Variables** (tab: Variables):

| Name | Value |
|---|---|
| `AZURE_API_APP_NAME` | `$API_APP` |
| `API_ORIGIN` | `https://$API_HOST` |
| `PANEL_ORIGIN` | `https://$SWA_HOST` |

## 8. Deploy

Actions tab → run each workflow (or push a change to its folder):

1. **Deploy API** → App Service.
2. **Deploy panel** → Static Web Apps. Bakes `API_ORIGIN` into the bundle, so **re-run this
   whenever the API URL changes**.
3. **Build extension** → downloadable zip with both deployed origins baked in.

## 9. Verify the deployment

```sh
# 1. API is up (public endpoint — the shell reads this without a token)
curl https://$API_HOST/v1/config

# 2. Auth still guards drafts — expect 401
curl -s -o /dev/null -w "%{http_code}\n" -X POST https://$API_HOST/v1/tickets/1/drafts

# 3. A real draft — expect 200 and a German reply
curl -X POST -H "Authorization: Bearer <api-bearertoken>" \
  https://$API_HOST/v1/tickets/<real-ticket-id>/drafts

# 4. The header that stops arbitrary sites framing the panel
curl -sI https://$SWA_HOST | grep -i content-security-policy
```

Check 4 must return `frame-ancestors 'self' https://*.gorgias.com`. `'self'` is present so
the bundled `harness.html` still works for demos; Gorgias is the only external site allowed.

Then the browser check: open `https://$SWA_HOST/harness.html`, paste the production bearer
token, enter a real ticket ID, and click **Generate reply**. That exercises the deployed
panel → deployed API → Gorgias → OpenAI path without the extension.

## 10. The extension, against the cloud

Manual install — no Chrome Web Store needed:

1. Actions → **Build extension** → open the run → download the
   `gorgias-ai-assistant-extension` artifact.
2. Unzip it somewhere permanent (the folder must stay on disk).
3. `chrome://extensions` → **Developer mode** ON → **Load unpacked** → select the folder.
4. Open a Gorgias ticket. The panel now talks to Azure, not localhost.
5. Paste the **production** bearer token into the panel once (kept in `sessionStorage`).

To repoint an already-installed build without rebuilding, run this in the extension's
console: `chrome.storage.local.set({ panelOrigin: '…', apiOrigin: '…' })`.

## 11. Gotchas

- **SCM basic auth** — step 6. The single most common cause of a failing deploy.
- **Do not set App Service → CORS in the portal.** The API handles CORS itself; the portal
  setting intercepts requests and will conflict.
- **Build-time URLs.** Both the panel (`VITE_API_URL`) and the extension
  (`VITE_PANEL_ORIGIN` / `VITE_API_ORIGIN`) bake origins at build time. Change a hostname →
  re-run the affected workflow.
- **Key Vault RBAC propagation** takes a few minutes; restart the web app if references
  show unresolved.
- **F1 free tier** has a 60-min/day CPU quota and no Always On — fine for a short demo,
  not for a pilot.
- **Cold start** on first request after idle: the first draft may take noticeably longer.
- **Application Insights is not wired into the code.** For the pilot, App Service log
  streaming is enough:
  `az webapp log tail -g $RG -n $API_APP`. Add the SDK when traces are actually needed.

## 12. Cost and teardown

| Resource | ~$/month |
|---|---|
| App Service B1 (Linux) | ~13 |
| Static Web Apps (free tier) | 0 |
| Key Vault | ~1 |
| **Total infrastructure** | **~14** |

LLM tokens are the only variable cost and are usage-proportional.

Set a budget alert (Cost Management → Budgets), and to stop all charges after a demo:

```sh
az group delete -n $RG --yes --no-wait
```

---

# Appendix — the same thing in the Azure Portal

Click-ops equivalent of sections 2–7, for anyone who prefers the UI. Same order applies:
create both apps before setting app settings.

### A. Resource group

portal.azure.com → search **Resource groups** → **Create** → name `gorgias-assistant-rg`,
region *West Europe* → **Review + create**.

### B. App Service (the API)

**Create a resource** → search **Web App** → **Create**:

- Resource group: `gorgias-assistant-rg`
- Name: `gorgias-assistant-api` (globally unique)
- Publish: **Code** · Runtime stack: **.NET 10** (if absent, pick .NET 8 and use the
  self-contained fallback in section 2) · OS: **Linux** · Region: *West Europe*
- Pricing plan: **Create new** → **B1** (or **F1 Free** for a throwaway demo)

→ **Review + create**. Then in the new app:

- **Settings → Configuration → General settings**: *Always On* → **On** (skip on F1), and
  **SCM Basic Auth Publishing Credentials** → **On** ← *deployments fail without this*
- **Settings → TLS/SSL settings**: *HTTPS Only* → **On**

### C. Static Web App (the panel)

**Create a resource** → **Static Web App** → **Create**:

- Name: `gorgias-assistant-panel` · Plan type: **Free** · Region: *West Europe*
- Deployment source: **Other** — do **not** connect GitHub here; our workflow deploys with
  a token, and letting the portal wire it up creates a second, conflicting workflow.

→ **Review + create**. Then: **Overview** → copy the URL; **Settings → Manage deployment
token** → copy the token (this is `AZURE_SWA_TOKEN`).

### D. Key Vault

**Create a resource** → **Key Vault** → **Create**:

- Name: `gorgias-assistant-kv` · Region *West Europe* · Standard
- **Access configuration** tab → Permission model: **Azure role-based access control (RBAC)**

### E. Managed identity + access

1. App Service → **Settings → Identity** → *System assigned* → **Status: On** → **Save**.
2. Key Vault → **Access control (IAM)** → **Add role assignment** →
   role **Key Vault Secrets User** → Members: **Managed identity** → pick your web app →
   **Review + assign**.
3. Repeat for yourself: role **Key Vault Secrets Officer** → Members: **User** → your
   account. (Without this you cannot add secrets in the next step.)

### F. Secrets

Key Vault → **Objects → Secrets** → **Generate/Import**, three times:

| Name | Value |
|---|---|
| `gorgias-apikey` | your Gorgias API key |
| `openai-apikey` | your OpenAI key |
| `api-bearertoken` | a long random string (not `local-dev-token`) |

Open each one → click its current version → copy the **Secret Identifier** URL for the next step.

### G. App settings

App Service → **Settings → Environment variables** → **App settings** → **+ Add** for each:

| Name | Value |
|---|---|
| `Gorgias__Subdomain` | `timeresistance` |
| `Gorgias__Email` | your Gorgias login email |
| `Gorgias__ApiKey` | `@Microsoft.KeyVault(SecretUri=<gorgias-apikey identifier>)` |
| `OpenAi__ApiKey` | `@Microsoft.KeyVault(SecretUri=<openai-apikey identifier>)` |
| `Api__BearerToken` | `@Microsoft.KeyVault(SecretUri=<api-bearertoken identifier>)` |
| `Api__AllowedOrigins__0` | `https://<your-static-web-app-url>` |

**Apply** → the app restarts. The three Key Vault rows should show a green
**Key Vault Reference** status; a red one means the role assignment (step E) hasn't
propagated — wait a few minutes and restart the app.

### H. Publish profile

App Service → **Overview** → **Download publish profile** (top toolbar). Open the file and
copy its entire contents — that is `AZURE_API_PUBLISH_PROFILE`.

### I. GitHub

Repo → **Settings → Secrets and variables → Actions**:

- **Secrets** tab → *New repository secret*: `AZURE_API_PUBLISH_PROFILE`, `AZURE_SWA_TOKEN`
- **Variables** tab → *New repository variable*: `AZURE_API_APP_NAME`, `API_ORIGIN`
  (`https://<api-url>`), `PANEL_ORIGIN` (`https://<swa-url>`)

### J. Deploy

Repo → **Actions** → run **Deploy API**, **Deploy panel**, **Build extension**.
Then run the verification in section 9.
