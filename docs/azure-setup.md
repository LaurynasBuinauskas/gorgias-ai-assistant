# Azure setup — one-time runbook

Provisioning is done by hand (CLI) and captured here rather than in Bicep: it is a handful
of resources for a single pilot, and reproducing them is rare. Run this once, then the
GitHub Actions workflows deploy on every merge.

**Secrets never live in GitHub or in app config.** They sit in Key Vault; App Service
resolves them at startup through its managed identity. The deploy workflows never see them.

## 1. Variables

Pick globally-unique names for `API_APP`, `KV`, and `SWA`.

```sh
RG=gorgias-assistant-rg
LOC=westeurope
PLAN=gorgias-assistant-plan
API_APP=gorgias-assistant-api
KV=gorgias-assistant-kv
SWA=gorgias-assistant-panel
```

## 2. Resource group, App Service, Static Web App

```sh
az group create -n $RG -l $LOC

az appservice plan create -g $RG -n $PLAN --is-linux --sku B1

az webapp create -g $RG -p $PLAN -n $API_APP --runtime "DOTNETCORE:10.0"

az staticwebapp create -g $RG -n $SWA -l westeurope
```

> If `DOTNETCORE:10.0` is rejected, check what's offered with
> `az webapp list-runtimes --os linux | grep -i dotnet`. If .NET 10 isn't there yet,
> publish self-contained instead — add
> `--self-contained --runtime linux-x64` to the `dotnet publish` step in
> `.github/workflows/deploy-api.yml` and create the app with `--runtime "DOTNETCORE:8.0"`.

Note the two hostnames — you need them below:

```sh
az webapp show -g $RG -n $API_APP --query defaultHostName -o tsv
az staticwebapp show -g $RG -n $SWA --query defaultHostname -o tsv
```

## 3. Key Vault + managed identity

```sh
az keyvault create -g $RG -n $KV -l $LOC --enable-rbac-authorization true

az webapp identity assign -g $RG -n $API_APP

PRINCIPAL=$(az webapp identity show -g $RG -n $API_APP --query principalId -o tsv)
KV_ID=$(az keyvault show -g $RG -n $KV --query id -o tsv)

az role assignment create --assignee $PRINCIPAL --role "Key Vault Secrets User" --scope $KV_ID
```

Store the secrets (same values you have in local user-secrets; the bearer token should be
a fresh long random string, **not** `local-dev-token`):

```sh
az keyvault secret set --vault-name $KV -n gorgias-apikey  --value "<gorgias-api-key>"
az keyvault secret set --vault-name $KV -n openai-apikey   --value "<openai-api-key>"
az keyvault secret set --vault-name $KV -n api-bearertoken --value "<long-random-string>"
```

## 4. App settings

Non-secret values are set directly; secrets are **Key Vault references** that App Service
resolves via the managed identity.

```sh
SWA_HOST=$(az staticwebapp show -g $RG -n $SWA --query defaultHostname -o tsv)

az webapp config appsettings set -g $RG -n $API_APP --settings \
  Gorgias__Subdomain="timeresistance" \
  Gorgias__Email="<gorgias-login-email>" \
  Gorgias__ApiKey="@Microsoft.KeyVault(SecretUri=https://$KV.vault.azure.net/secrets/gorgias-apikey/)" \
  OpenAi__ApiKey="@Microsoft.KeyVault(SecretUri=https://$KV.vault.azure.net/secrets/openai-apikey/)" \
  Api__BearerToken="@Microsoft.KeyVault(SecretUri=https://$KV.vault.azure.net/secrets/api-bearertoken/)" \
  Api__AllowedOrigins__0="https://$SWA_HOST"
```

`Api__AllowedOrigins__0` is the **only** origin CORS will accept in production (dev allows
any loopback origin). Add more indexes (`__1`, `__2`) only with a reason.

Verify the references resolved — each should show `Status: Resolved`:

```sh
az webapp config appsettings list -g $RG -n $API_APP --query "[?contains(name,'ApiKey')]"
```

## 5. GitHub secrets and variables

Repo → Settings → Secrets and variables → Actions.

**Secrets:**

| Name | Value |
|---|---|
| `AZURE_API_PUBLISH_PROFILE` | output of `az webapp deployment list-publishing-profiles -g $RG -n $API_APP --xml` |
| `AZURE_SWA_TOKEN` | output of `az staticwebapp secrets list -g $RG -n $SWA --query "properties.apiKey" -o tsv` |

**Variables:**

| Name | Value |
|---|---|
| `AZURE_API_APP_NAME` | `$API_APP` |
| `API_ORIGIN` | `https://<api-hostname>` |
| `PANEL_ORIGIN` | `https://<swa-hostname>` |

## 6. Deploy

Push to `main` (or run the workflows manually from the Actions tab):

- **Deploy API** → App Service
- **Deploy panel** → Static Web Apps (bakes `API_ORIGIN` into the bundle)
- **Build extension** → downloadable zip artifact with the deployed origins baked in

## 7. Post-deploy checks

```sh
# 1. API is up (public endpoint, no token needed)
curl https://<api-hostname>/v1/config

# 2. Auth still guards drafts — expect 401
curl -s -o /dev/null -w "%{http_code}\n" -X POST https://<api-hostname>/v1/tickets/1/drafts

# 3. A real draft — expect 200 and a body
curl -X POST -H "Authorization: Bearer <api-bearertoken>" \
  https://<api-hostname>/v1/tickets/<ticket-id>/drafts

# 4. The CSP header that stops arbitrary sites framing the panel
curl -sI https://<swa-hostname> | grep -i content-security-policy
```

Check 4 must return `frame-ancestors 'self' https://*.gorgias.com`. `'self'` is there so the
bundled `harness.html` can still frame the panel for demos; Gorgias is the only external
site allowed to.

## Cost

| Resource | ~$/month |
|---|---|
| App Service B1 (Linux) | ~13 |
| Static Web Apps (free tier) | 0 |
| Key Vault | ~1 |
| Application Insights (sampled) | 1–5 |

To cut the largest line item, the App Service plan can be scaled to F1 (free) for a demo —
it sleeps when idle and has no custom-domain SSL, but it costs nothing.
