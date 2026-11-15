# M2 test evidence

**Exit gate (blueprint §11.8):** Salesforce household data displays in Angular. **Met on 13 Nov 2026.**

## Automated tests

`dotnet test AdvisorFlow.slnx`: **66 passed, 0 failed** (local, Windows, 13 Nov 2026). CI green for `f8749af`.

| Suite | Location | Count | What it covers |
|---|---|---|---|
| SOQL escaping | `SalesforceProviderTests.cs` (`SoqlTests`) | 5 tests | Quotes and backslashes escaped; injection attempt neutralised; `%` and `_` matched literally in `LIKE`; household record-type filter always present; search and canonical status added |
| JWT assertion | `SalesforceProviderTests.cs` (`SalesforceJwtTests`) | 1 test | `iss`, `sub`, `aud`, `exp` (+3 min) claims; RS256 signature verifies with the public key |
| Salesforce provider | `SalesforceProviderTests.cs` (`SalesforceProviderTests`) | 15 tests | JWT bearer token request; token reused, renewed after 30 min, renewed once after a 401; count + `LIMIT`/`OFFSET` paging; field mapping; one Contacts query per page; invalid Salesforce id → not found; rejected token, missing settings, missing key file, Salesforce 5xx → 503; rejected query, unreadable JSON → 502 |
| Existing M1 suites | `HouseholdServiceTests`, `HouseholdApiTests`, `ContractTests`, `ArchitectureTests`, `ApiDocsTests` | 45 | Unchanged behaviour. API tests now use `MockApiFactory`, which forces the mock and blank Salesforce settings so user-secrets never send tests to the real org |

The provider tests use `FakeSalesforce`, a fake HTTP handler, so CI needs no credentials or network access to Salesforce.

## Salesforce setup checks (CLI, as the integration user)

Setup is recorded in the [runbook](../deployment/salesforce-baseline-runbook.md), section 7.

| Check | Command | Result |
|---|---|---|
| JWT login, no password | `sf org login jwt ... --alias advisorflow-integration` | Authorized, org `00Dfj00000oMPSnEAO` |
| Read households | `sf data query` on Account, record type Household | 2 records, all custom fields readable |
| Read household fields used by the API | same, with `Phone` and `AccountNumber` | Readable |
| Read members | `sf data query` on Contact of households | 2 records: Email, Phone, Preferred Contact Method, AccountId |
| Read-only proof | `sf data update record` on an Account | Rejected: `entity type cannot be updated: Account` |

## End-to-end run against Salesforce (13 Nov 2026)

API started with `dotnet run --project src/AdvisorFlow.Api --launch-profile salesforce`.

| Request | Result |
|---|---|
| `GET /api/households` | `totalCount: 2`: Carter Household and Williams Household, real ids (`001fj…`), 1 member each |
| `GET /api/households/001fj00002Eg1LbAAJ` | Williams Household: Prospect, Moderate, Medium, phone 204-555-0101, account ADV-0001 |
| `GET /api/households/001fj00002Eg1LbAAJ/contacts` | James Williams (`003fj00001v8tUDAAY`), email, phone, preferred method Phone, `isPrimary: false` |
| advisorflow-web at http://localhost:4200 | Clients page lists "2 households" from Salesforce; Williams shows Medium priority. No frontend change was needed |

## Known gaps carried into M3

- No primary-member field in Salesforce yet (BR-HH-003): `isPrimary` is always false and the UI shows "No primary member".
- Daniel Carter shares James Williams's phone number (left over from the original test contact).
- All data is read as one integration user; per-advisor visibility and audit are deferred (ADR-007, ADR-008).
- `OFFSET` above 2,000 is not supported by SOQL; later pages come back empty. Not reachable with the current data.

## Manual acceptance checklist

- [x] `dotnet test AdvisorFlow.slnx` passes (66/66)
- [x] CI green for the Salesforce provider commit
- [x] JWT login works and the integration user cannot update records
- [x] With the `salesforce` launch profile, the API returns the two Salesforce households, detail and members
- [x] The web app lists the Salesforce households without code changes
