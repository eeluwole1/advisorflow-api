# Architecture Decision Records

## ADR-001: Full-stack application with Salesforce as system of record
**Status:** Approved, 13 Nov 2026 (blueprint §11.1)

Angular + TypeScript + Tailwind frontend, C# ASP.NET Core Web API backend, Salesforce Developer Edition as the CRM system of record, hosted on Azure. The browser never holds Salesforce credentials. All Salesforce access goes through the API using server-side OAuth. Salesforce Lightning remains the admin and verification interface. Salesforce Flow owns platform-native automation.

## ADR-002: Households are modeled as Accounts (MVP design decision)
**Status:** Approved for MVP, 13 Nov 2026 (blueprint §8)

- A household is a standard **Account**. Each individual member is a **Contact** with `AccountId` pointing to that household. Opportunities belong to the household Account.
- Person Accounts are **not** enabled; once enabled, they cannot be turned off.
- Households are identified by the **`Household` Account record type** (decided 13 Nov 2026, M2). It is active, the default for all profiles, and includes every Account picklist value. The Salesforce provider reads only Accounts with `RecordType.DeveloperName = 'Household'`, so business Accounts added later never appear as clients. The Account `Type` field keeps its standard meaning. Chosen over a `Type = Household` picklist value, which would have mixed household identity into a field already used for prospect/customer classification.
- A Contact has one primary household in the MVP. Multi-household membership is out of scope.
- **Scope:** this is a design decision for this MVP, not a claim about a universal standard for financial advisory CRMs. Revisit it if the business needs individual-client or business-client models.

## ADR-003: Provider abstraction between the API and the CRM
**Status:** Approved, 13 Nov 2026

The API depends on `ICrmDataProvider` (in `AdvisorFlow.Application`), never on Salesforce directly.

- `MockCrmDataProvider` (in `AdvisorFlow.Infrastructure`) serves fictional in-memory data for local development and tests.
- `SalesforceCrmDataProvider` (M2) implements the same interface against the Salesforce REST API.
- Configuration (`Crm:Provider`) selects the provider. The Angular app binds to the versioned contract (`docs/api/openapi.v1.yaml`), so switching providers needs no frontend change.
- Providers return `null` for "not found" and throw `CrmUnavailableException` (→ 503) or `CrmUpstreamException` (→ 502) for integration failures. Error responses never include upstream details.

## ADR-004: Local-first development, Azure deployment later
**Status:** Approved, 13 Nov 2026

Milestones M1–M6 run entirely on the developer's computer: the API on `localhost:5080`, and advisorflow-web on `localhost:4200` with a dev proxy. No Azure resources are provisioned until M7. Before anything is provisioned in M7, an Azure budget and cost alerts are set. This avoids cloud cost while the application is still changing quickly.

## ADR-005: Separate frontend and backend repositories
**Status:** Approved, 13 Nov 2026. Supersedes the monorepo layout. Blueprint v1.3 §11.5A records this decision as "ADR-002".

- `advisorflow-web` (Angular) and `advisorflow-api` (ASP.NET Core) are separate GitHub repositories. Each has its own versioning, CI, deployment, secrets and release history.
- Salesforce DX metadata lives in `advisorflow-api` for now, with its own validation step. It moves to a third repository only if release ownership requires it.
- The two repositories are bound by a versioned OpenAPI contract (`docs/api/openapi.v1.yaml`) with backward-compatible changes inside a major version. `ContractTests` checks the API against it on every build.
- The frontend never stores Salesforce secrets. The backend mediates Salesforce calls and enforces authorization.
- Deployment targets: Azure Static Web Apps (web) and Azure App Service (api). Because they are different origins, the API allows the web origin via `Cors:AllowedOrigins`, and the web production build sets an absolute `apiBaseUrl`.

## ADR-006: Layered backend projects
**Status:** Approved, 13 Nov 2026 (blueprint v1.3 §15)

`AdvisorFlow.Domain` ← `AdvisorFlow.Application` ← `AdvisorFlow.Infrastructure` ← `AdvisorFlow.Api`.

- **Domain:** the household model and business values. No dependencies.
- **Application:** use cases, validation, DTOs and the `ICrmDataProvider` abstraction. It depends only on Domain and has no ASP.NET Core dependency.
- **Infrastructure:** CRM providers and their configuration.
- **Api:** controllers, error mapping and the composition root.

`ArchitectureTests` enforces these rules.

## ADR-007: Server-to-server Salesforce access with the JWT bearer flow
**Status:** Approved, 13 Nov 2026 (milestone M2)

The API reads Salesforce as one dedicated **integration user**, signing in with the **OAuth 2.0 JWT bearer flow** through an External Client App.

- **Why JWT bearer:** no password and no browser step. The API proves its identity by signing a short-lived JWT with a private key; Salesforce verifies it with the uploaded certificate. Nothing reusable travels with the request, and the Consumer Secret is not used at all.
- **Least privilege:** the integration user has the Salesforce Integration license, the *Minimum Access - API Only Integrations* profile (no UI login) and one permission set, `AdvisorFlow API Read` (read Account and Contact, selected fields only). It is pre-authorized for the app through that permission set. A test update is rejected by Salesforce.
- **Secrets:** the private key and connection settings live outside the repository: `dotnet user-secrets` locally, Azure Key Vault when deployed (M7).
- **Token reuse:** the access token is cached and renewed after 30 minutes or on a 401, to stay within Developer Edition API limits.
- **Trade-off:** every API caller sees the same data, because Salesforce sees one user. Per-advisor visibility (each advisor sees their own households) would need user-level OAuth or filtering in the API, and is out of scope for the MVP.
- **Failure mapping:** sign-in or network failures and Salesforce 5xx/429 become `503`; rejected queries or unreadable responses become `502`. Salesforce error details are logged, never returned.

## ADR-008: Write scope and access for M3 (household management)
**Status:** Approved, 14 Nov 2026

- **No hard delete.** Households are deactivated by setting Client Status to Inactive, which keeps history and the audit trail. The API exposes no `DELETE` for households. Removing a member from a household is decided with the member endpoints.
- **Separate write permission set.** Writes use a new permission set, `AdvisorFlow API Write` (Create and Edit on Account and Contact, only the fields the API writes; no Delete, no Modify All). `AdvisorFlow API Read` stays read-only, so write access can be revoked on its own.
- **Acting advisor deferred to M7.** M3 has no app sign-in, so every change reaches Salesforce as the integration user and Salesforce's *Last Modified By* shows that user. Recording the acting advisor (Microsoft Entra sign-in and audit fields) is added with deployment in M7. Until then the prototype is single-user and uses fictional data only.

## ADR-009: Read-only public deployment until advisor sign-in exists
**Status:** Approved, 15 Nov 2026 (first Azure deployment)

The API can create and edit Salesforce records (ADR-008), but advisor sign-in is deferred to M7. A public deployment without sign-in would let anyone who finds the URL change the org.

- **Decision:** the deployed API is **read-only**. `Features:AllowWrites` is `false` in `appsettings.json` and `true` only in `appsettings.Development.json`, so the safe value is the default and a missing setting can never enable writes.
- **How:** a resource filter on every create/update endpoint answers `403` with a problem response before model binding, whatever the body. Reads are unaffected. Documented in the contract (1.2.1) and covered by `ReadOnlyModeApiTests`.
- **Defence in depth:** Salesforce still enforces the integration user's permission sets (no delete, ADR-008), and the private key lives in Azure Key Vault, read by the App Service's managed identity (`Salesforce:PrivateKeyPem` as a Key Vault reference).
- **Next:** writes are switched on in a deployed environment only once advisor sign-in (Microsoft Entra) protects them, together with recording the acting advisor (M7).
