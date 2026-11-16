# AdvisorFlow CRM: project status and hand-off

The single page to read first when picking the project up again (by you, a reviewer or a new assistant session). Last updated **16 Nov 2026**.

## What it is

A financial advisory CRM prototype: advisors see **households**, their **members**, the **advisory opportunity pipeline** and **follow-up tasks**. Salesforce Developer Edition is the system of record; an ASP.NET Core API mediates every Salesforce call; an Angular app is the advisor's screen. All data is fictional. Source of requirements: `docs/requirements/AdvisorFlow_CRM_Project_Blueprint_v1.3.pdf`.

| Part | Repo / place | Live |
|---|---|---|
| Web (Angular 22, Tailwind 4, Vitest) | `eeluwole1/advisorflow-web` (`C:\advisorflow-web`) | https://zealous-pebble-02abce610.4.azurestaticapps.net |
| API (.NET 10, layered) + Salesforce DX metadata + all docs | `eeluwole1/advisorflow-api` (`C:\advisorflow-api`) | https://app-advisorflow-api-ee1.azurewebsites.net (`/health`) |
| Salesforce Developer Edition | alias `advisorflow-dev` (admin), `advisorflow-integration` (API user) | orgfarm-c274b45e01-dev-ed.develop.my.salesforce.com |

## Milestones

| Milestone | Status | Notes |
|---|---|---|
| M1 Foundation | Done | Two repos, contract-first API, mock provider, CI |
| M2 Salesforce baseline | Done | Metadata retrieved into the repo; JWT bearer integration user (ADR-007) |
| M3 Households and members | Done (API writes + web read views) | Household record type, primary-member rule, no delete (ADR-008) |
| M4 Opportunities | **Done** | Own sales process + Advisory Engagement record type, stage rule (ADR-010), loss reason, pipeline page and household opportunities on the web |
| M5 Activities and automation | **In progress** | Flow done and verified (ADR-011); integration user can read tasks. Next: activities API, then web views |
| M6 Analytics | Not started | Pipeline/activity dashboard; totals must reconcile to seed data |
| M7 Azure hardening | Partly done early | Deployed read-only (ADR-009) with Key Vault, CI/CD on push; still to do: Entra sign-in, then enable writes, monitoring, smoke/security tests |

## Next steps (M5)

Decisions already made with the owner:
- Follow-up tasks: created on each entry into Qualification / Proposal Presented **unless an open task from the same automation exists**; due in 3 days (ADR-011).
- Web scope for M5: **task views only** — an Activities page (open and overdue, filterable) plus tasks on each household and opportunity. Events and task forms come later.

1. **Activities API** (contract 1.4.0, additive): `GET /api/activities` (open/overdue filters, paging), `GET /api/households/{id}/activities`, `GET /api/opportunities/{id}/activities`. Task fields: `Id, Subject, Status, Priority, ActivityDate, IsClosed, WhatId, What.Name, AccountId, Owner.Name, Automation__c` (household = `AccountId`). Mock seed tasks, Salesforce provider, contract tests, docs. Read-only, so it also works on Azure.
2. **Web**: Activities nav item (currently disabled, "M5"), Activities page, tasks sections on household detail and on pipeline cards or an opportunity view.
3. Test evidence docs for M3–M5 (`docs/testing/`), and BR-ACT rows already point at the verification script.

## How we work

- The owner commits and pushes; pushing to `main` runs CI and **auto-deploys** each repo after green checks (API: publish profile secret + `/health` smoke test; web: SWA token secret).
- Salesforce changes are made as source where possible (deploy with `sf project deploy start --dry-run` first), or clicked in Setup and then **retrieved** with `./scripts/retrieve-baseline.ps1` and reviewed before committing. See the Salesforce runbook.
- Contract-first: change `docs/api/openapi.v1.yaml` (minor version for additive changes), `ContractTests` keeps the API honest, web bumps `API_CONTRACT_VERSION`.
- Tests before handing over: `dotnet test AdvisorFlow.slnx`; web `npx ng test --watch=false`, `npx ng build`, `npx prettier --check "src/**/*.{ts,html,css}"`.

## Security rules (always)

- Never view, paste or commit `server.key`; no secrets in Git, chat, notes or screenshots. Secrets move only via clipboard into GitHub/Key Vault, then `Set-Clipboard -Value " "`.
- `az keyvault secret set ... --query name -o tsv` so values are never printed; never run `dotnet user-secrets list` unmasked; cover access tokens in `sf org display` screenshots.
- Fictional data only. Free tier where possible. Leave the unrelated `docsequence` Azure resources untouched.
- Deployed API stays read-only (`Features__AllowWrites=false`) until advisor sign-in exists; Swagger is Development-only.

## Gotchas learned

- A newly deployed Salesforce field is visible to **nobody** until field-level security is set (runbook §10c).
- Integration-license users need **Access Activities** to see Task at all, and Task field access lives on the **profile** (runbook §10e).
- Salesforce permission pages often need a second **Save** in a "Save changes?" dialog; an unsaved edit retrieves as no access.
- `az` on Windows mangles `;` and `()` in app settings: pass a JSON file (`--settings "@file.json"`).
- The deployed web needs the API's `Cors__AllowedOrigins__0` set to the SWA URL.
- `npm test` stays in watch mode: use `npm test -- --watch=false`.

## Related

- Interview script (story, architecture, war stories, Q&A, demo): https://claude.ai/artifact/6B9BkDMA7eoGR1r7GrY1bc

## Known gaps

- `primaryContactName` is empty for older households whose members predate `Is_Primary_Member__c` (data, not code).
- The `advisorflow-web` GitHub "About" text says "backend"; fix it on GitHub.
- No Azure budget alert yet (owner's choice for now).
