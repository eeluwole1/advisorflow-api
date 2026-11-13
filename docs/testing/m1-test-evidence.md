# M1 test evidence

**Exit gate (blueprint §11.8):** the UI displays API-backed fictional households, and CI passes.

## Automated tests

| Suite | Location | Count | What it covers |
|---|---|---|---|
| API unit | `tests/AdvisorFlow.Api.Tests/HouseholdServiceTests.cs` | 19 cases | Query validation, record id shape (including an injection-style id), paging, search, status filter, primary-first ordering, empty and unknown households |
| API integration | `tests/AdvisorFlow.Api.Tests/HouseholdApiTests.cs` | 14 cases | All endpoints over HTTP; 400/404 problem responses; 502/503 mapping without leaking upstream detail; unconfigured Salesforce provider → 503 |
| API contract | `tests/AdvisorFlow.Api.Tests/ContractTests.cs` | 4 tests | Response fields of every endpoint equal the required fields in `docs/api/openapi.v1.yaml` |
| API architecture | `tests/AdvisorFlow.Api.Tests/ArchitectureTests.cs` | 4 tests | Domain ← Application ← Infrastructure ← Api layer rules |
| Web (advisorflow-web repo) | `src/app/app.spec.ts` | 6 tests | Shell, list request parameters, status filter resets paging, API failure message, detail with primary member, 404 state |

Run: `dotnet test AdvisorFlow.slnx` here, and `npx ng test --watch=false` in advisorflow-web.

## Initial verification (13 Nov 2026, during scaffolding)

- API built with 0 warnings (warnings are treated as errors).
- After the split into separate repositories and layered projects (blueprint v1.3), the service and architecture tests passed (23/23). They were executed with a minimal local xUnit stand-in because the build machine could not reach NuGet. **The first CI run is the first full `dotnet test` run, including the integration and contract tests.**
- Every integration-test scenario was also exercised by calling the running API directly: `/health` 200; list 200; `page=0`, `pageSize=500`, `status=VIP` and malformed ids → 400 `application/problem+json`; unknown ids → 404; `Crm:Provider=Salesforce` (unconfigured) → 503; CORS allows `http://localhost:4200`.
- Contract: `openapi.v1.yaml` lints clean with Redocly CLI 2. Live responses from all four schemas matched its required field lists.
- advisorflow-web: production and development builds succeeded; Vitest 6/6 passed; Prettier check clean; `npm audit` found 0 vulnerabilities.
- Headless browser run against API + Angular dev server: list renders 7 households; search "will" → 1 row; Prospect filter → 3 households; empty household shows its empty state; unknown id shows "Household not found"; no console errors; layout checked at 1360 px and 390 px widths.

## Manual acceptance checklist (run on your machine)

- [ ] `dotnet test AdvisorFlow.slnx` passes
- [ ] `npx ng test --watch=false` passes
- [ ] With both apps running, http://localhost:4200 lists 7 fictional households
- [ ] Selecting **Williams Household** shows James (Primary) and Sarah
- [ ] Stopping the API shows "Households couldn't be loaded" with a Retry button, and Retry works after restarting the API
- [ ] The first GitHub Actions run is green in both repositories
