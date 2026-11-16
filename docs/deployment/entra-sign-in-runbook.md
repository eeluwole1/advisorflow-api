# Runbook: advisor sign-in with Microsoft Entra ID (M7)

Decisions: [ADR-015](../architecture/decisions.md) (sign-in, public reads) and [ADR-016](../architecture/decisions.md) (acting advisor). All values below are **identifiers, not secrets**; the web app has no secret at all (PKCE).

## 1. The directory

The Azure for Students subscription lives in the Red River College directory, which does not allow students to register apps (`az ad app create` → *Insufficient privileges*). Sign-in therefore uses a directory the owner controls: created by signing up for the **Azure free account** with a personal Microsoft account (no automatic charges; the free subscription itself is not used). The owner is Global Administrator there.

| Value | Where it is used |
|---|---|
| Directory (tenant) ID | API `Auth:TenantId`, web `environment.auth.tenantId` |
| AdvisorFlow API — Application (client) ID | API `Auth:ClientId`, web `apiScope` = `api://<id>/access_as_user` |
| AdvisorFlow Web — Application (client) ID | web `environment.auth.clientId` |

Values (16 Nov 2026), in `appsettings.json` (API) and `src/environments/environment*.ts` (web):

| | ID |
|---|---|
| Directory (tenant) | `b7695219-ac29-4031-9eec-798fb17de6ea` |
| AdvisorFlow API (client) | `bdb9c640-466c-4a1b-89ef-c354332cd93f` (`api://bdb9c640-466c-4a1b-89ef-c354332cd93f`) |
| AdvisorFlow Web (client) | `c300a82d-5810-4080-8b4d-3cf31a8cda7f` |

**Two Microsoft accounts, nothing moved.** All Azure resources (App Service, Static Web App, Key Vault) stay on the student subscription in the RRC directory and are managed with the RRC account (`az login`, portal.azure.com). The owner's personal directory holds only the two app registrations and the advisor's sign-in; it has no resources and no cost. The API trusts it purely through the `Auth` settings (tenant ID + API client ID) and Entra's published signing keys. Advisors sign in to AdvisorFlow with the personal account; app registrations are managed at entra.microsoft.com with that account.

## 2. App registrations (entra.microsoft.com → App registrations)

**A. AdvisorFlow API**
1. New registration, single tenant, no redirect URI.
2. Expose an API → Application ID URI `api://<client id>` → scope `access_as_user` (Admins and users; enabled).
3. App roles → `Advisor` (Users/Groups, value `Advisor`).
4. Enterprise applications → AdvisorFlow API → Users and groups → assign the owner the **Advisor** role.

**B. AdvisorFlow Web**
1. New registration, single tenant, platform **Single-page application**, redirect URI `http://localhost:4200`.
2. Authentication → add `https://zealous-pebble-02abce610.4.azurestaticapps.net`.
3. API permissions → My APIs → AdvisorFlow API → delegated `access_as_user` → **Grant admin consent**.

## 3. API configuration

The IDs are in `appsettings.json` (they are not secrets), so local runs and Azure use the same directory. On Azure the only switch is App Service → Environment variables → `Features__AllowWrites=true`, set once sign-in is verified. With `Auth` emptied, the API logs a warning and every write answers `401`.

## 4. Web configuration

`src/environments/environment.ts` and `environment.development.ts`:

```ts
auth: { tenantId: '<tenant id>', clientId: '<Web client id>', apiScope: 'api://<API client id>/access_as_user' }
```

## 5. Salesforce: Last changed by (AdvisorFlow)

`AdvisorFlow_Changed_By__c` on Account, Contact and Opportunity, editable through `AdvisorFlow API Write`, readable through `AdvisorFlow API Read` (see the Salesforce runbook §12).

## 6. Verification (to record)

| Check | Expected |
|---|---|
| Anonymous `POST /api/households` | `401` |
| Signed in, no Advisor role | `403` |
| Advisor creates a household on the live site | `201`; Salesforce shows *Last changed by (AdvisorFlow)* = the advisor |
| Advisor moves an opportunity to Qualification | Flow creates the follow-up; it appears on Activities and the Dashboard |
| Reads without signing in | still work |
