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
- **API docs:** Swagger UI is mapped only in Development, so the deployed API does not publish its endpoint list (`/swagger` is `404`). The contract stays in `docs/api/openapi.v1.yaml`.
- **Deployment record:** resource names, settings and redeploy steps are in the [Azure deployment runbook](../deployment/azure-deployment-runbook.md).
- **Next:** writes are switched on in a deployed environment only once advisor sign-in (Microsoft Entra) protects them, together with recording the acting advisor (M7).

## ADR-010: Advisory opportunity pipeline (M4)
**Status:** Approved, 15 Nov 2026

The blueprint (§9) proposes the stages but leaves the stage-transition rule (BR-OP-004) to a business decision.

- **Own sales process.** Opportunities use the record type **Advisory Engagement**, whose sales process **AdvisorFlow Advisory** allows only the seven blueprint stages. Two values were added to the org's stage picklist (*Needs Assessment* 30% Pipeline, *Proposal Presented* 60% Best Case); the org's standard stages and defaults are untouched, as the blueprint asks. The API reads and writes only Advisory Engagement opportunities on household Accounts.
- **Stage rule (BR-OP-004).** An opportunity may move **forward any number of stages** (including straight to Closed Won or Closed Lost) or **back one stage** to correct a mistake. **Closed Won and Closed Lost are final:** their stage cannot change, though other fields can be corrected. Enforced in the API (`OpportunityStages.CheckChange`); an invalid change is `400` with a `stage` error. Alternatives considered: forward-only (too strict for honest mistakes) and no rule (leaves BR-OP-004 unenforced).
- **Loss reason (BR-OP-002).** New picklist `Loss_Reason__c`. Salesforce enforces it with the validation rule `Loss_Reason_Required_When_Closed_Lost`; the API checks first so callers get a field error instead of a `502`, and also refuses a loss reason on any other stage.
- **Amount (BR-OP-003).** The standard Amount field holds estimated advisory service revenue, never assets under management. The contract, API guide and UI labels say so.
- **Access.** `AdvisorFlow API Read` reads Opportunity (View All) and the fields used; `AdvisorFlow API Write` adds Create and Edit (no Delete, ADR-008), edit access to the fields the API writes, and the Advisory Engagement record type. Probability stays read-only: Salesforce sets it from the stage.
- **Automation (BR-ACT-001/002)** on stage changes, such as the Qualification follow-up task, is a Salesforce Flow in M5, not API code.

## ADR-011: Follow-up tasks are created by a Salesforce Flow (M5)
**Status:** Approved, 16 Nov 2026

BR-ACT-001/002 ask for one follow-up task when an opportunity enters Qualification, without duplicates on repeated edits. The blueprint assigns this to Salesforce Flow and says not to build the same task in both .NET and Flow.

- **Owner: Salesforce.** The record-triggered flow `AdvisorFlow_Opportunity_Follow_Ups` (after save, create and update) runs however the stage changes: through the API, the Salesforce UI or a data load. The API never creates these tasks.
- **When it fires.** Only when an Advisory Engagement opportunity is created in, or its stage *changes into*, **Qualification** (task "Qualification follow-up: …") or **Proposal Presented** (task "Proposal review: …", from the blueprint's automation plan). Edits that don't change the stage never fire it, and Closed Won/Lost never create tasks.
- **Duplicate rule (BR-ACT-002).** Because the stage rule allows one step back (ADR-010), an opportunity can enter a stage more than once. A new task is created on each entry **unless an open task from the same automation already exists** for that opportunity. Alternatives considered: once ever (misses a genuine second round of follow-up) and every entry (stacks open tasks after a correction).
- **Marker field.** The Task picklist `Automation__c` (*Qualification follow-up*, *Proposal review*) identifies the automation's tasks; tasks people log leave it blank. It is read-only for System Administrator so a manual edit can't defeat the duplicate check; it was deployed with no access for other profiles.
- **Task values.** Owner = opportunity owner, related to the opportunity (`WhatId`), status Not Started, priority Normal, due 3 days after the stage change.
- **Evidence.** The Apex test class `OpportunityFollowUpFlowTest` (10 tests: the nine scenarios plus a 200-record bulk insert that must stay within governor limits) runs in the org with `sf apex run test`; all passed on 16 Nov 2026. `scripts/verify-follow-up-flow.apex` is the earlier anonymous-Apex version of the same checks, kept for a quick look at a live org.
- **API access to tasks.** The activities API reads tasks as the integration user, which needed the *Access Activities* permission (in `AdvisorFlow API Read`) and read-only Task field access on the **Minimum Access - API Only Integrations** profile, because Salesforce keeps activity field access on profiles, not permission sets. Both are retrieved into the repo (runbook §10e). Edit access to tasks is not granted until the API logs tasks.

## ADR-012: Activities API is read-only and "overdue" uses the practice's time zone (M5)
**Status:** Approved, 16 Nov 2026

- **Read-only.** `GET /api/activities`, `/api/households/{id}/activities` and `/api/opportunities/{id}/activities` (contract 1.4.0) only read Salesforce Tasks. Follow-up tasks are created by the Flow (ADR-011), and logging tasks from the web comes later with the forms work. Being read-only, the endpoints work on the Azure deployment (ADR-009) without enabling writes.
- **Which tasks.** Tasks whose Account is a household (`Account.RecordType.DeveloperName = 'Household'`), attached either to the household or to one of its opportunities; Salesforce sets `Task.AccountId` for both. A household's list therefore includes its opportunities' tasks. Each record's list is capped at 200.
- **Overdue (BR-ACT-003).** Open with a due date before *today in America/Winnipeg*, not in the server's time zone: Azure App Service runs in UTC, so 21:00 in Winnipeg would otherwise already be "tomorrow". The API computes `isOverdue` itself (`ActivityService.TodayFor`, a `TimeProvider` for tests) and passes the date into the `state=overdue` SOQL filter. A task due today is not overdue; a task with no due date never is. Alternatives considered: let the browser decide (each list could disagree with the count) and a per-user time zone (no sign-in yet; revisit in M7).
- **Order.** Open before closed, then due date with undated tasks last, then subject and id, the same in Salesforce (`ORDER BY IsClosed, ActivityDate NULLS LAST, Subject, Id`) and the mock.
- **Mock data.** Eight fictional tasks with due dates relative to the day the API starts, so open, overdue and closed cases always exist; two carry the Flow's `Automation__c` values. The mock does not imitate the Flow.

## ADR-013: Dashboard totals come from aggregate SOQL and reconcile to the lists (M6)
**Status:** Approved, 16 Nov 2026

- **One endpoint, server-side aggregates.** `GET /api/dashboard` (contract 1.5.0) builds the summary from Salesforce aggregate queries: `SELECT StageName, COUNT(Id) n, SUM(Amount) fees … GROUP BY StageName`, the same for loss reasons and client statuses, plus `SELECT COUNT()` task counts. Salesforce does the counting, so the API never pages through every record and the numbers stay correct beyond a page. Alternative considered: computing totals in the browser from the list endpoints (capped at 100 records a page, and every client would repeat the work).
- **Same scope as the lists.** Advisory Engagement opportunities on household Accounts, tasks on households or their opportunities, household Accounts, so each dashboard figure reconciles with the matching list page. `DashboardServiceTests.Dashboard_reconciles_with_the_list_endpoints` checks this, and `Dashboard_matches_the_known_seed_totals` pins the seed totals (the blueprint's M6 exit criterion).
- **Weighted forecast.** Fees × each stage's default probability from the AdvisorFlow sales process, computed in the API, so no extra field access (such as ExpectedRevenue) is needed. Every open stage is listed, even with nothing in it, so the chart's axis never changes shape.
- **Dates.** "Overdue" and "due in the next 7 days" use today in America/Winnipeg (ADR-012); the two never overlap.
- **Web.** One headline number (open advisory fees), three supporting tiles, the pipeline as thin single-hue bars with values at the bar end and a hover description per bar; overdue is shown in red with a "Needs attention" label, never colour alone.

## ADR-014: Every push validates the Salesforce source against the org (CI)
**Status:** Approved, 16 Nov 2026

Until now CI only ran the LWC Jest tests; Apex tests and a deploy check ran by hand. A push could break the org-side build without anyone noticing.

- **What runs.** A CI job signs in to the Developer Edition org and runs `sf project deploy validate --manifest manifest/ci-validate-package.xml --test-level RunLocalTests`: a full deploy of the Apex classes, the LWC, the Flow and the `Automation__c` field, every local Apex test, the 75% coverage gate, then a rollback. Nothing is saved. The Azure deploy job waits for it. Pull requests from forks get no secrets, so the job is skipped there rather than failing.
- **What is validated.** Only the source this project builds and owns. Profiles, page layouts and standard value sets in `baseline-package.xml` are retrieved snapshots of clicked configuration; validating them would need profile-management rights for no real check.
- **A separate CI user, least privilege.** The API's integration user cannot do this: the Salesforce Integration license cannot deploy Apex or Flows, and it should stay read-only anyway. The CI user `advisorflow.ci@eeluwole.dev` is a Salesforce-licence user on the **Minimum Access - Salesforce** profile with one permission set kept as source, `AdvisorFlow_CI_Deploy`: API Enabled, **API Only User** (cannot log in to the UI), View Setup, Customize Application, Modify Metadata Through Metadata API Functions, Author Apex and Manage Flow, plus the setup-only permissions Salesforce makes them depend on (View Roles for Author Apex; Manage Custom Permissions and Manage Translation for Customize Application). It has no object or record access, no Modify All Data, no user or profile management. Author Apex is still powerful (Apex runs in system context), which is why the workflow only ever *validates*, and the key can be revoked on its own (next point).
- **Its own key and app.** JWT bearer flow (as in ADR-007) through a separate External Client App `AdvisorFlow CI` with its own certificate, pre-authorised for the CI permission set only. The private key lives only in the GitHub secret `SF_CI_JWT_KEY` and outside the repo; revoking CI access never touches the live API, and the reverse.
- **Tests declare their user.** Both Apex test classes run inside `System.runAs` as an active System Administrator (the advisor persona in this org) instead of as whoever started the run. The first CI-user run proved why: the deploy itself passed, but the Flow tests failed with *Access to entity 'Account' denied* (a user with no object access cannot create records even in a test), and `OpportunityFollowUpsControllerTest` queries `WITH USER_MODE`, so it would see no tasks. Giving the CI user record access instead would have defeated least privilege.
- **Alternatives considered.** An SFDX auth URL for the admin user (one secret, but a reusable admin refresh token in CI); a scratch org per run (needs a Dev Hub and recreating the household and pipeline setup as source; worth it later, not before the deadline).

## ADR-015: Advisors sign in with Microsoft Entra ID to make changes; reading stays public (M7)
**Status:** Approved, 16 Nov 2026

ADR-009 kept the deployed API read-only because nobody could prove who was changing data. M7 adds that proof, so changes can be switched on live.

- **Identity provider: Microsoft Entra ID**, in a directory the owner controls (created with a personal Microsoft account through the Azure free account). The Red River College directory that holds the Azure for Students subscription does not let students register apps, and a school-owned directory would end with the student account. The App Service stays on the student subscription; which directory issues the tokens does not matter to it.
- **Two app registrations.** *AdvisorFlow API* exposes the delegated scope `access_as_user` and defines the app role **Advisor**, assigned only to the owner. *AdvisorFlow Web* is a single-page app (redirect URIs: `http://localhost:4200` and the Static Web App) with permission to that scope. No client secret or certificate anywhere: a browser cannot keep one, so MSAL uses the authorization code flow with **PKCE**.
- **Reading is anonymous; every create/update needs an advisor.** The six write endpoints carry `[Authorize(Policy = "Advisor")]`: a valid Entra access token for this API (issuer = the directory, audience = the API's client id or `api://` URI, signature from Entra's published keys), the **Advisor** role in `roles`, and `access_as_user` in `scp`. No/invalid token → `401`; signed in without the role or scope → `403`. Recruiters can still browse the fictional data without an account.
- **Kill switch kept.** `Features:AllowWrites` (ADR-009) still answers `403` before anything else runs, so writes can be turned off in one setting without touching sign-in.
- **Web.** `@azure/msal-browser` with the redirect flow, session storage (nothing survives the tab), and an HTTP interceptor that attaches the token **only to non-GET calls to the API's own origin**. MSAL is loaded on demand, so visitors who only read never download it (the initial bundle stays under its 500 kB budget). Signed-out visitors see "View only · Sign in to make changes"; signed-in advisors see the forms.
- **Testing.** API tests replace Entra with a test authentication handler driven by a header, and cover anonymous `401`, missing role or scope `403`, reads still public, and the acting advisor on every write. The real JWT validation is configuration and is checked end to end against Entra by hand (runbook).
- **Alternatives considered.** Static Web Apps built-in auth (its identity only reaches a *linked* API, which needs the paid Standard plan); App Service Easy Auth (cookie-based, cross-site cookies from a separate SPA origin are unreliable); GitHub OAuth with API-issued tokens (no Microsoft dependency, but custom token issuance and a weaker fit for a Microsoft-centred employer).

## ADR-016: Changes record the acting advisor in Salesforce and in the API log (M7)
**Status:** Approved, 16 Nov 2026

The API writes to Salesforce as one integration user (ADR-007), so Salesforce's *Last Modified By* can never say which advisor acted (ADR-008 deferred this to M7).

- **In Salesforce:** a text field **Last changed by (AdvisorFlow)** (`AdvisorFlow_Changed_By__c`, 255) on Account, Contact and Opportunity. The API sets it on every create/update to the advisor's name and sign-in name from the token, e.g. `Ada Advisor (ada@example.com)`. Only `AdvisorFlow API Write` can edit it; `AdvisorFlow API Read` and System Administrator can read it. Changes made directly in Salesforce do not touch it, which is accurate: the field answers "who changed it *through AdvisorFlow*". Salesforce's own field history and *Last Modified* stay as they are.
- **In the API log:** one structured entry per change with the record id and the advisor's Entra **object id** (`oid`), never the email, e.g. `Household {HouseholdId} created by advisor {AdvisorId}`.
- **Scope.** The un-setting of a previous primary member (BR-HH-003) is a side effect of the member write and is not stamped separately.
- **Alternative considered:** a separate audit object (one row per change, full history). More complete, but more storage, permissions and UI than the prototype needs; the log plus the field answer "who last touched this".
