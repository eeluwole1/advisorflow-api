# Business rules: implementation status

The full rule definitions are in blueprint v1.3 §11.6. This table records where each rule is enforced and tested as the milestones progress.

| Rule | Summary | Owner | Status | Evidence |
|---|---|---|---|---|
| BR-HH-001 | Household name required | API + Salesforce | Planned (M3, writes) | — |
| BR-HH-002 | Contact belongs to one household | API + Salesforce | **Read side done (M1):** members are listed per household; unknown household → 404 | `GetMembers_returns_null_for_unknown_household`, `Unknown_household_returns_404` |
| BR-HH-003 | At most one primary member | API (custom field pending) | **Display done (M1):** primary member listed first; enforcement in M3 | `GetMembers_lists_primary_member_first` |
| BR-HH-004 | No duplicate members; don't dedupe by name alone | API | Planned (M3) | — |
| BR-CL-001 | Approved Client Status values only | Salesforce picklist + API | **Done for filters (M1):** unknown status → 400. Values must be verified against the org picklist in M2 | `ValidateQuery_rejects_unapproved_status`, `Invalid_requests_return_400` |
| BR-OP-001 | Opportunity needs household, stage, close date | Salesforce + API | Planned (M4) | — |
| BR-OP-002 | Closed Lost requires a loss reason | Salesforce validation | Planned (M4) | — |
| BR-OP-003 | Amount = advisory revenue, not AUM | Angular + API | Planned (M4) | — |
| BR-OP-004 | Allowed stage transitions | Business decision pending | Blocked on decision | — |
| BR-ACT-001 | One follow-up task per Qualification entry | Salesforce Flow | Planned (M5) | — |
| BR-ACT-002 | No duplicate generated tasks | Salesforce Flow | Planned (M5) | — |
| BR-SEC-001 | Respect Salesforce sharing + app authorization | API + Salesforce | **Partly done (M1):** upstream errors don't leak record details. Authentication arrives in M3 | `Provider_failures_map_to_gateway_errors_without_leaking_details` |
| BR-SEC-002 | Contact preference ≠ marketing consent | Salesforce + Angular | Planned (M3): preference shown read-only; opt-out flags kept separate | — |
