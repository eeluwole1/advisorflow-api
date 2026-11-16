# advisorflow-api

The backend for **AdvisorFlow CRM**, a financial advisory CRM prototype. It's an ASP.NET Core Web API that serves households and their members to [`advisorflow-web`](../advisorflow-web) and mediates all access to **Salesforce Developer Edition**, the CRM system of record. This repository also owns the Salesforce DX metadata and the project documentation.

> Portfolio prototype. All records are fictional.

```
advisorflow-web (Angular) ──HTTPS/JSON, contract v1──► AdvisorFlow.Api
                                                        │ controllers · error mapping
                                                   AdvisorFlow.Application
                                                        │ HouseholdService · validation · DTOs
                                                        │ ICrmDataProvider
                                                   AdvisorFlow.Infrastructure
                                                   ├─ MockCrmDataProvider        (local development)
                                                   └─ SalesforceCrmDataProvider  (M2, server-side OAuth) ──► Salesforce DE
                                                   AdvisorFlow.Domain  (Household, HouseholdMember, rules)
```

## Current status: milestone M2, Salesforce integration

- `GET /health`, `GET /api/households` (search, status filter, paging), `GET /api/households/{id}`, `GET /api/households/{id}/contacts`
- Mock provider with seven fictional households (default, and always used by the tests).
- Salesforce provider: signs in as a read-only integration user with the OAuth 2.0 JWT bearer flow, caches the access token, and reads households (Accounts with the `Household` record type) and their members (Contacts) with SOQL.
- Errors use problem details: 400 / 404 / 502 / 503
- Versioned OpenAPI contract: [`docs/api/openapi.v1.yaml`](docs/api/openapi.v1.yaml)

## Run locally

Prerequisites: .NET SDK 10. Salesforce CLI (`sf`) is needed from M2.

```bash
dotnet run --project src/AdvisorFlow.Api --launch-profile http    # http://localhost:5080
dotnet test AdvisorFlow.slnx
```

Try it:

- **Swagger UI:** http://localhost:5080/swagger (open it yourself; `dotnet run` does not start a browser). It renders the versioned contract and lets you call the live API with *Try it out*. Development only; not served in production.
- Raw contract: http://localhost:5080/openapi/v1.yaml
- Plain JSON: http://localhost:5080/api/households

`Cors:AllowedOrigins` in `appsettings.Development.json` allows `http://localhost:4200` (the Angular dev server). `Crm:Provider` chooses `Mock` (default) or `Salesforce`. `Features:AllowWrites` is `true` only in Development; deployed environments are read-only until advisor sign-in exists (ADR-009). Salesforce secrets never go in appsettings. Locally they go in `dotnet user-secrets`, and in Azure they go in Key Vault.

### Run against Salesforce

After the one-time setup in the [runbook](docs/deployment/salesforce-baseline-runbook.md) (section 7: integration user, External Client App, key pair and user-secrets):

```bash
dotnet run --project src/AdvisorFlow.Api --launch-profile salesforce   # same URL, Crm:Provider=Salesforce
```

The `salesforce` profile only sets `Crm__Provider=Salesforce`; the connection settings come from user-secrets. If something is missing or Salesforce rejects the sign-in, the API answers `503` and logs the reason.

## Tests

| Suite | Covers |
|---|---|
| `HouseholdServiceTests` | Validation, record id shape, paging, search, filtering, member ordering |
| `HouseholdApiTests` | Every endpoint over HTTP; 400/404/502/503; no upstream detail leaks |
| `ContractTests` | Response JSON fields equal the required fields in `openapi.v1.yaml` |
| `ArchitectureTests` | Layer rules: Domain ← Application ← Infrastructure ← Api |
| `ApiDocsTests` | Swagger UI and contract served in Development, 404 in Production |

CI (`.github/workflows/ci.yml`) runs the tests, a vulnerable-package check, an OpenAPI lint and a Salesforce project file check.

## API contract and versioning

`docs/api/openapi.v1.yaml` is the agreement with `advisorflow-web`. Its `info.version` follows semantic versioning:

- **1.0.x**: documentation only
- **1.x.0**: backward-compatible additions (new endpoints, optional fields)
- **2.0.0**: breaking changes, served under a new path while v1 keeps working

Change the contract and the code in the same pull request. `ContractTests` fails if they disagree.

## Repository layout

```
src/AdvisorFlow.Api/              HTTP surface, composition root
src/AdvisorFlow.Application/      Use cases, validation, DTOs, ICrmDataProvider
src/AdvisorFlow.Domain/           Household model and business values
src/AdvisorFlow.Infrastructure/   CRM providers (mock, Salesforce)
tests/AdvisorFlow.Api.Tests/      xUnit tests
force-app/main/default/           Salesforce metadata (filled by the M2 baseline retrieval)
manifest/  scripts/               Salesforce retrieval manifest and helper scripts
docs/                             Requirements, ADRs, API contract, testing, runbooks
```

## Documentation

- [Architecture decisions](docs/architecture/decisions.md)
- [API guide](docs/api/api-contracts.md) and [OpenAPI v1](docs/api/openapi.v1.yaml)
- [Business rules status](docs/requirements/business-rules.md)
- [Salesforce baseline and household migration runbook](docs/deployment/salesforce-baseline-runbook.md)
- [M1 test evidence](docs/testing/m1-test-evidence.md)
- Blueprint v1.3: `docs/requirements/AdvisorFlow_CRM_Project_Blueprint_v1.3.pdf`
