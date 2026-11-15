# API guide (contract v1.2.0)

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

There is no `DELETE`. A household is deactivated by setting `clientStatus` to `Inactive` (ADR-008).

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

`{id}` and `{contactId}` must be 15- or 18-character alphanumeric Salesforce record ids; anything else returns `400`. This also keeps malformed input out of future SOQL queries.

## Status codes

| Code | Meaning |
|---|---|
| 400 | Invalid query parameter, id or request body. `errors` lists each field. |
| 404 | Household does not exist (or is not a household), or the member is not in that household. |
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

// PagedResponse<T>
{ "items": [ /* T */ ], "page": 1, "pageSize": 20, "totalCount": 7, "totalPages": 1 }
```

`riskProfile` is a CRM classification tag. It is **not** an investment suitability assessment. `isPrimary` comes from the Contact field `Is_Primary_Member__c` in Salesforce (BR-HH-003).
