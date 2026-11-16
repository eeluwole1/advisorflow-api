# API guide (contract v1.4.0)

The authoritative contract is [`openapi.v1.yaml`](openapi.v1.yaml), which `advisorflow-web` builds against. This page is a quick human-readable summary. If the two ever differ, the YAML wins, and `ContractTests` should catch the difference.

Base URL (local): `http://localhost:5080`. All responses are JSON. Errors use [RFC 9457 problem details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`).

| Method | Path | Success | Errors |
|---|---|---|---|
| GET | `/health` | `200` `Healthy` (text) | — |
| GET | `/api/households` | `200` `PagedResponse<HouseholdSummary>` | `400`, `502`, `503` |
| GET | `/api/households/{id}` | `200` `HouseholdDetail` | `400`, `404`, `502`, `503` |
| GET | `/api/households/{id}/contacts` | `200` `HouseholdMember[]` (primary first) | `400`, `404`, `502`, `503` |
| POST | `/api/households` (1.1) | `201` `HouseholdDetail` + `Location` header | `400`, `502`, `503` |
| PUT | `/api/households/{id}` (1.1) | `200` `HouseholdDetail` | `400`, `404`, `502`, `503` |
| POST | `/api/households/{id}/contacts` (1.2) | `201` `HouseholdMember` + `Location` header | `400`, `404`, `502`, `503` |
| PUT | `/api/households/{id}/contacts/{contactId}` (1.2) | `200` `HouseholdMember` | `400`, `404`, `502`, `503` |
| GET | `/api/opportunities` (1.3) | `200` `PagedResponse<Opportunity>` (by close date) | `400`, `502`, `503` |
| GET | `/api/opportunities/{id}` (1.3) | `200` `Opportunity` | `400`, `404`, `502`, `503` |
| GET | `/api/households/{id}/opportunities` (1.3) | `200` `Opportunity[]` (by close date) | `400`, `404`, `502`, `503` |
| POST | `/api/households/{id}/opportunities` (1.3) | `201` `Opportunity` + `Location` header | `400`, `404`, `502`, `503` |
| PUT | `/api/opportunities/{id}` (1.3) | `200` `Opportunity` | `400`, `404`, `502`, `503` |
| GET | `/api/activities` (1.4) | `200` `PagedResponse<Activity>` (open first, by due date) | `400`, `502`, `503` |
| GET | `/api/households/{id}/activities` (1.4) | `200` `Activity[]` (incl. its opportunities' tasks) | `400`, `404`, `502`, `503` |
| GET | `/api/opportunities/{id}/activities` (1.4) | `200` `Activity[]` | `400`, `404`, `502`, `503` |

There is no `DELETE`. A household is deactivated by setting `clientStatus` to `Inactive`; an opportunity ends as Closed Won or Closed Lost (ADR-008).

## Query parameters for `GET /api/households`

| Name | Default | Rules |
|---|---|---|
| `page` | 1 | ≥ 1 |
| `pageSize` | 20 | 1–100 |
| `search` | — | Case-insensitive match on household name; ≤ 80 characters |
| `status` | — | `Prospect`, `Active` or `Inactive` (BR-CL-001) |

## Body for `POST` and `PUT /api/households`

```jsonc
{ "name": "Okafor Household",      // required, 1–255 characters (trimmed)
  "clientStatus": "Active",        // Prospect | Active | Inactive. POST: defaults to Prospect. PUT: required
  "riskProfile": "Conservative",   // Conservative | Moderate | Aggressive, or null
  "priority": "High",              // High | Medium | Low, or null
  "phone": "204-555-0199" }        // ≤ 40 characters, or null
```

- Picklist values are matched case-insensitively and returned in their exact spelling (`"active"` → `"Active"`).
- **PUT replaces every editable field:** an omitted or empty optional field is cleared. Send the whole form.
- `id` and `accountNumber` cannot be changed.

## Body for `POST` and `PUT /api/households/{id}/contacts`

```jsonc
{ "firstName": "Kofi",                    // ≤ 40 characters, or null
  "lastName": "Mensah",                   // required, 1–80 characters
  "email": "kofi.mensah@example.com",     // valid address, ≤ 80 characters, or null
  "phone": "204-555-0170",                // ≤ 40 characters, or null
  "preferredContactMethod": "Email",      // Email | Phone | SMS, or null. Email needs email; Phone/SMS need phone
  "isPrimary": false }                    // optional; omitted means false
```

**Primary member (BR-HH-003)**

- The household's first member becomes primary automatically.
- `isPrimary: true` makes that member the primary and clears the previous one. In Salesforce both changes are one all-or-nothing composite request, so a household never has two primaries.
- The current primary cannot be un-set: `400` with an `isPrimary` error. Make another member primary instead.
- Members cannot be moved to another household or removed (ADR-008).

## Query parameters for `GET /api/opportunities`

| Name | Default | Rules |
|---|---|---|
| `page`, `pageSize`, `search` | 1, 20, — | As for households; `search` matches the opportunity name |
| `stage` | — | One stage (see below), case-insensitive |
| `householdId` | — | A 15- or 18-character record id |

## Body for `POST /api/households/{id}/opportunities` and `PUT /api/opportunities/{id}`

```jsonc
{ "name": "Okafor Household - Investment Advisory", // required, 1–120 characters
  "stage": "Proposal Presented",   // POST: defaults to Prospecting. PUT: required
  "closeDate": "2026-12-01",       // required, yyyy-MM-dd (BR-OP-001)
  "amount": 12000,                 // advisory service revenue, NOT assets under management (BR-OP-003); ≥ 0, 2 decimals, or null
  "serviceType": "Investment Advisory", // Retirement Planning | Investment Advisory | Financial Review | Other, or null
  "engagementPriority": "Medium",  // Low | Medium | High, or null
  "nextStep": "Follow up on the proposal", // ≤ 255 characters, or null
  "lossReason": null }             // required for Closed Lost, not allowed otherwise (BR-OP-002)
```

**Stages and the stage rule (BR-OP-004, ADR-010)**

Prospecting → Qualification → Needs Assessment → Proposal Presented → Negotiation/Review → Closed Won / Closed Lost.

- Forward any number of stages, including straight to Closed Won or Closed Lost.
- Back **one** stage, to correct a mistake. Further back: `400` with a `stage` error.
- Closed Won and Closed Lost are final: changing the stage of a closed opportunity is `400`. Its other fields can still be edited.
- `probability` is set by Salesforce from the stage and is read-only. The household cannot be changed after creation.
- Salesforce also enforces BR-OP-002 with a validation rule, in case a change bypasses the API.

`{id}` and `{contactId}` must be 15- or 18-character alphanumeric Salesforce record ids; anything else returns `400`. This also keeps malformed input out of future SOQL queries.

## Query parameters for `GET /api/activities` (1.4)

| Name | Default | Rules |
|---|---|---|
| `page`, `pageSize`, `search` | 1, 20, — | As for households; `search` matches the task subject |
| `state` | `open` | `open`, `overdue`, `closed` or `all`, case-insensitive |
| `householdId` | — | A 15- or 18-character record id; includes tasks on the household's opportunities |

**Tasks are read-only in the API.** Advisors log them in Salesforce, and the follow-up Flow creates them when an opportunity enters Qualification or Proposal Presented (BR-ACT-001/002, ADR-011); `automation` says which. Every list puts open tasks first, then sorts by due date (tasks without one last), then subject. **Overdue** means open with a due date before today in the practice's time zone, America/Winnipeg (BR-ACT-003, ADR-012); a task due today is not overdue.

## Status codes

| Code | Meaning |
|---|---|
| 400 | Invalid query parameter, id or request body. `errors` lists each field. |
| 403 | Create/update on a read-only deployment (`Features:AllowWrites` is false, e.g. on Azure until sign-in exists; ADR-009). |
| 404 | Household does not exist (or is not a household), the member is not in that household, or the opportunity does not exist (or is not an advisory opportunity on a household). |
| 502 | The CRM returned an error or unusable data. |
| 503 | The CRM is unreachable or the provider is not configured. |
| 405 | Method not supported, for example `DELETE` (ADR-008). |
| 401 / 403 | Reserved for M7, when advisor sign-in is added (ADR-008). |

## Shapes

```jsonc
// HouseholdSummary
{ "id": "001MOCK00000000001", "name": "Williams Household", "clientStatus": "Prospect",
  "riskProfile": "Moderate", "priority": "Medium", "memberCount": 2, "primaryContactName": "James Williams" }

// HouseholdDetail
{ "id": "001MOCK00000000001", "name": "Williams Household", "clientStatus": "Prospect",
  "riskProfile": "Moderate", "priority": "Medium", "phone": "204-555-0101",
  "accountNumber": "ADV-0001", "memberCount": 2 }

// HouseholdMember
{ "id": "003MOCK00000000001", "householdId": "001MOCK00000000001", "firstName": "James",
  "lastName": "Williams", "fullName": "James Williams", "email": "james.williams@example.com",
  "phone": "204-555-0101", "preferredContactMethod": "Phone", "isPrimary": true }

// Opportunity
{ "id": "006MOCK00000000002", "householdId": "001MOCK00000000002", "householdName": "Okafor Household",
  "name": "Okafor Household - Investment Advisory", "stage": "Proposal Presented", "isClosed": false,
  "closeDate": "2026-12-01", "amount": 12000, "probability": 60, "serviceType": "Investment Advisory",
  "engagementPriority": "Medium", "nextStep": "Follow up on the proposal", "lossReason": null }

// Activity (1.4)
{ "id": "00TMOCK00000000002", "subject": "Proposal review: Okafor Household - Investment Advisory",
  "status": "Not Started", "priority": "Normal", "dueDate": "2026-11-19", "isClosed": false, "isOverdue": false,
  "householdId": "001MOCK00000000002", "householdName": "Okafor Household", "relatedToId": "006MOCK00000000002",
  "relatedToName": "Okafor Household - Investment Advisory", "relatedToType": "Opportunity",
  "contactName": null, "ownerName": "Demo Advisor", "automation": "Proposal review" }

// PagedResponse<T>
{ "items": [ /* T */ ], "page": 1, "pageSize": 20, "totalCount": 7, "totalPages": 1 }
```

`riskProfile` is a CRM classification tag. It is **not** an investment suitability assessment. `isPrimary` comes from the Contact field `Is_Primary_Member__c` in Salesforce (BR-HH-003).
