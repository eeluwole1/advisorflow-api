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

## 6. Verification (16 Nov 2026)

| Check | Result |
|---|---|
| `dotnet test` (incl. 25 access tests: anonymous `401`, no role or scope `403`, reads public, advisor recorded) | 278/278 pass; CI #29 green incl. Salesforce validation, deployed |
| Web tests | 37/37 pass; CI #12 green, deployed |
| Local, real Entra token, mock data | Sign-in works; create household, create opportunity, move stage all accepted |
| Live, before `AllowWrites`: anonymous `POST /api/households` | `401` (new code live) |
| Live, `Features__AllowWrites=true`, signed in as the owner (Advisor) | Created **Patel Household** (High), added **Priya Patel** (became primary, BR-HH-003), created *Patel Household - Retirement Planning* ($5,000, Prospecting), moved it to **Qualification** |
| Salesforce Flow (ADR-011) | Created *Qualification follow-up: Patel Household - Retirement Planning*, due 19 Nov 2026, shown at once on the household as **Auto** |
| *Last changed by (AdvisorFlow)* (ADR-016), queried as the integration user | `elusiyan eluwole` on the Account, Contact and Opportunity |

Note: Entra issues **v1** access tokens to this API (the registration's default), which carry `name` but not `preferred_username`, so the field holds the advisor's display name only. The code falls back accordingly; switching the API registration to v2 tokens (`requestedAccessTokenVersion: 2` in its manifest) would add the sign-in name.
