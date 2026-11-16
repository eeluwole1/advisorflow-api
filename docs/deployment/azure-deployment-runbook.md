# Runbook: Azure deployment (read-only, free tier)

First deployed 15 Nov 2026. This is the record of what exists in Azure and how to redeploy it. Run the commands from **PowerShell** on your own computer, signed in with `az login` to the **Azure for Students** subscription. No secret values appear in this document, in Git, or in App Service settings.

Decisions behind it: [ADR-009](../architecture/decisions.md) (read-only deployment) and [ADR-007](../architecture/decisions.md) (JWT bearer flow).

## 1. What exists

Everything lives in one resource group, so the whole project can be removed in one step without touching other projects.

| Resource | Name | Region | Tier / cost |
|---|---|---|---|
| Resource group | `rg-advisorflow` | Canada Central | free |
| Key Vault (RBAC) | `kv-advisorflow-ee1` | Canada Central | Standard, a few cents a month |
| App Service plan (Linux) | `asp-advisorflow` | Canada Central | **F1 Free** |
| Web App (API) | `app-advisorflow-api-ee1` | Canada Central | on the F1 plan |
| Static Web App (web) | `swa-advisorflow-web` | Central US | **Free** |

| | URL |
|---|---|
| Web | https://zealous-pebble-02abce610.4.azurestaticapps.net |
| API | https://app-advisorflow-api-ee1.azurewebsites.net (`/health`, `/api/households`) |

Allowed regions for this subscription (policy): eastus, southcentralus, westus2, canadacentral, centralus. Static Web Apps is not offered in Canada Central, hence Central US.

## 2. How the pieces connect

```
Browser ──> Static Web App (Angular build)
   │
   └──> App Service API ──(managed identity)──> Key Vault: Salesforce client id + private key
              │
              └──(JWT bearer, integration user)──> Salesforce Developer Edition
```

- The API's **system-assigned managed identity** has **Key Vault Secrets User** on the vault (read only). Your own account has **Key Vault Secrets Officer** (to set secrets).
- The web build calls the API by absolute URL (`src/environments/environment.ts` in advisorflow-web), and the API allows that site through CORS.

## 3. Key Vault secrets

| Secret | Holds |
|---|---|
| `salesforce-client-id` | Consumer Key of the External Client App `AdvisorFlow_API` |
| `salesforce-private-key` | Contents of the private key (`server.key`) |

To replace one (for example after rotating the certificate), set it again; the command prints only the name:

```powershell
az keyvault secret set --vault-name kv-advisorflow-ee1 --name salesforce-private-key --file "$env:USERPROFILE\.advisorflow\server.key" --query name -o tsv
az webapp restart --name app-advisorflow-api-ee1 --resource-group rg-advisorflow
```

## 4. API app settings

| Setting | Value |
|---|---|
| `Crm__Provider` | `Salesforce` |
| `Features__AllowWrites` | `false` (ADR-009) |
| `Salesforce__LoginUrl` | `https://orgfarm-c274b45e01-dev-ed.develop.my.salesforce.com` |
| `Salesforce__IntegrationUsername` | `advisorflow.integration@eeluwole.dev` |
| `Salesforce__ClientId` | `@Microsoft.KeyVault(VaultName=kv-advisorflow-ee1;SecretName=salesforce-client-id)` |
| `Salesforce__PrivateKeyPem` | `@Microsoft.KeyVault(VaultName=kv-advisorflow-ee1;SecretName=salesforce-private-key)` |
| `Cors__AllowedOrigins__0` | `https://zealous-pebble-02abce610.4.azurestaticapps.net` |

Other configuration: runtime `DOTNETCORE|10.0`, startup command `dotnet AdvisorFlow.Api.dll`, HTTPS only.

**Windows note:** `az` on Windows is a batch file and mangles `;` and `( )` in arguments, so values with Key Vault references must be passed from a JSON file:

```powershell
$settings = @(
  @{ name = "Salesforce__ClientId"; value = "@Microsoft.KeyVault(VaultName=kv-advisorflow-ee1;SecretName=salesforce-client-id)"; slotSetting = $false }
)
ConvertTo-Json @($settings) | Set-Content "$env:TEMP\advisorflow-settings.json"
az webapp config appsettings set --name app-advisorflow-api-ee1 --resource-group rg-advisorflow --settings "@$env:TEMP\advisorflow-settings.json" --query "[].name" -o tsv
```

Plain values work directly, for example `--settings "Features__AllowWrites=false"`.

## 5. Redeploy the API

From `C:\advisorflow-api`, after the tests pass:

```powershell
dotnet test
dotnet publish src\AdvisorFlow.Api -c Release -o "$env:TEMP\advisorflow-publish"
Compress-Archive -Path "$env:TEMP\advisorflow-publish\*" -DestinationPath "$env:TEMP\advisorflow-api.zip" -Force
az webapp deploy --name app-advisorflow-api-ee1 --resource-group rg-advisorflow --src-path "$env:TEMP\advisorflow-api.zip" --type zip
```

The first request after a deploy or restart can take 30-60 seconds: the F1 tier starts the app on demand.

## 6. Redeploy the web app

From `C:\advisorflow-web`:

```powershell
npm run build
$swaToken = az staticwebapp secrets list --name swa-advisorflow-web --resource-group rg-advisorflow --query properties.apiKey -o tsv
npx @azure/static-web-apps-cli deploy dist/advisorflow-web/browser --deployment-token $swaToken --env production
```

`public/staticwebapp.config.json` sends unknown paths to `index.html`, so deep links such as `/clients/{id}` survive a reload.

## 7. Smoke test

| Check | Expected |
|---|---|
| `GET /health` | `Healthy` |
| `GET /api/households` | the Salesforce households (Carter, Okafor, Williams) |
| `POST /api/households` (any body) | `403` problem response, "Changes are disabled in this environment." |
| `GET /swagger` | `404` (API docs are Development-only, ADR-009) |
| Web site | Clients list loads; opening a household shows its members |

## 8. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Web shows "Households couldn't be loaded" and the browser console mentions `Access-Control-Allow-Origin` | Site not in CORS | Set `Cors__AllowedOrigins__0`, then restart the API |
| API returns `503` | Key Vault references not resolved, or Salesforce sign-in failed | Check the identity's role on the vault; in the portal, **Environment variables** shows each Key Vault reference's status |
| `Forbidden` when setting a secret | RBAC role not yet effective | Wait a minute and retry |
| Web shows the Azure placeholder page | No upload yet | Run section 6 |

Logs: `az webapp log tail --name app-advisorflow-api-ee1 --resource-group rg-advisorflow`. The startup line reports the CRM provider and whether writes are allowed.

## 9. Cost and clean-up

- F1 and Static Web Apps Free cost nothing; Key Vault charges per operation (cents a month). The Azure for Students credit has its own spending cap. Check spend under **Subscriptions > Azure for Students > Overview**.
- To remove the whole deployment: `az group delete --name rg-advisorflow`. Key Vault keeps deleted vaults for 90 days (soft delete), so a new vault with the same name needs `az keyvault purge --name kv-advisorflow-ee1` first.

## 10. Switching writes on later (M7)

Only after Microsoft Entra sign-in protects the API (ADR-009): set `Features__AllowWrites=true`, add the write permission set to the integration user if it is not already assigned (runbook section 8), and record the acting advisor.
