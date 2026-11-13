# API guide (contract v1.0.0, read-only)

The authoritative contract is [`openapi.v1.yaml`](openapi.v1.yaml), which `advisorflow-web` builds against. This page is a quick human-readable summary. If the two ever differ, the YAML wins, and `ContractTests` should catch the difference.

Base URL (local): `http://localhost:5080`. All responses are JSON. Errors use [RFC 9457 problem details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`).

| Method | Path | Success | Errors |
|---|---|---|---|
| GET | `/health` | `200` `Healthy` (text) | — |
| GET | `/api/households` | `200` `PagedResponse<HouseholdSummary>` | `400`, `502`, `503` |
| GET | `/api/households/{id}` | `200` `HouseholdDetail` | `400`, `404`, `502`, `503` |
| GET | `/api/households/{id}/contacts` | `200` `HouseholdMember[]` (primary first) | `400`, `404`, `502`, `503` |

## Query parameters for `GET /api/households`

| Name | Default | Rules |
|---|---|---|
| `page` | 1 | ≥ 1 |
| `pageSize` | 20 | 1–100 |
| `search` | — | Case-insensitive match on household name; ≤ 80 characters |
| `status` | — | `Prospect`, `Active` or `Inactive` (BR-CL-001) |

`{id}` must be a 15- or 18-character alphanumeric Salesforce record id; anything else returns `400`. This also keeps malformed input out of future SOQL queries.

## Status codes

| Code | Meaning |
|---|---|
| 400 | Invalid query parameter or id. `errors` lists each field. |
| 404 | Household does not exist (or is not visible to the caller, once authorization arrives in M3). |
| 502 | The CRM returned an error or unusable data. |
| 503 | The CRM is unreachable or the provider is not configured. |
| 401 / 403 | Reserved for M3, when user authentication is added. |

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

`riskProfile` is a CRM classification tag. It is **not** an investment suitability assessment. `isPrimary` is supplied by the mock provider. In Salesforce it depends on a custom field that is still pending (BR-HH-003); until that field exists, the Salesforce provider returns `false`.

POST and PUT endpoints are added in M3, only after authorization, validation and write behavior are tested.
