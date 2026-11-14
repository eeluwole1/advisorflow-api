# Runbook: Salesforce baseline retrieval and household migration (M2)

Run these steps on your own computer from the root of **advisorflow-api**, which owns the Salesforce metadata. They need your Salesforce login. Use fictional data only.

## 1. Install tools (once)

- Salesforce CLI: https://developer.salesforce.com/tools/salesforcecli. Check with `sf --version`.
- VS Code + Salesforce Extension Pack (optional, but useful for browsing metadata)

## 2. Log in to the Developer Edition org

```bash
sf org login web --alias advisorflow-dev --instance-url https://login.salesforce.com
sf org display --target-org advisorflow-dev     # note the API version shown
```

The CLI stores the login in your user profile, outside the repo. Never commit `.sf/` or `.sfdx/` (they are already in `.gitignore`).

If `sf org display` shows a newer API version than the one in `sfdx-project.json` (currently `68.0`, matching the org on 13 Nov 2026), update `sourceApiVersion` in `sfdx-project.json` and `<version>` in `manifest/baseline-package.xml` to match.

## 3. Retrieve the existing configuration

```bash
./scripts/retrieve-baseline.sh            # macOS / Linux / Git Bash
./scripts/retrieve-baseline.ps1           # Windows PowerShell
```

Both run `sf project retrieve start --manifest manifest/baseline-package.xml`. This retrieves:

- the Account, Contact and Opportunity objects, including custom fields such as `Preferred_Contact_Method__c`, Client Status, Client Risk Profile and Customer Priority
- the 12 Account, Contact and Opportunity page layouts
- 7 profiles (Admin, Standard, Read Only, Marketing, and Custom: Marketing/Sales/Support), which carry the field-level security for those fields
- the `AccountType` and `OpportunityStage` standard value sets

## 4. Review before committing

```bash
git status
git diff --stat
```

Check:
- [ ] `Preferred_Contact_Method__c` values are Email, Phone and SMS
- [ ] The Client Status picklist values match the API's approved list (`Prospect`, `Active`, `Inactive`). If they don't, update `ClientStatuses` in `src/AdvisorFlow.Domain/Households/Household.cs`, the `ClientStatus` enum in `docs/api/openapi.v1.yaml` (a contract change; tell advisorflow-web), and the docs.
- [ ] FLS matches blueprint §4 (Sales/Support/Standard/Admin edit; Marketing/Read Only read-only)
- [ ] No credentials, usernames or org ids appear in the retrieved files

Then commit: `git add force-app manifest sfdx-project.json && git commit -m "Retrieve Salesforce baseline metadata"`.

## 5. Review existing records before migrating

Do this in Salesforce Lightning before changing anything:

1. Open the **James Williams** Account. Record its saved values: Type, Client Status, Client Risk Profile, Customer Priority, Phone and Account Number.
2. Open **Contacts** and look for the test Contact created earlier. Note its name, which Account it is linked to (if any), and its Preferred Contact Method.
3. Decide, and write down here:
   - [x] Is the existing test Contact actually James Williams? **No.** The only Contact was **Daniel Carter** (`003fj00001v2tLdAAI`, no email or contact preference), linked to the James Williams Account for testing only. Creating James Williams therefore is not a duplicate (BR-HH-004).
   - [x] If the existing Contact is a different fictional person, keep it and decide which household it belongs to. **Decision (13 Nov 2026):** Daniel gets his own **Carter Household**. The original link to the Williams Account was a testing artifact, not evidence that he belongs to that family (BR-HH-002: one primary household per Contact).

## 6. Migrate the sample to the household model

1. [x] Choose how to identify households (ADR-002). **Decision:** a `Household` Account record type, created in Setup (Object Manager → Account → Record Types) from `--Master--` so it inherits every picklist value, available and default for all profiles. Page layouts per profile match each profile's existing Account layout (Admin/Standard/Read Only/Marketing User → Account Layout; Custom: Sales → Account (Sales); Custom: Support → Account (Support); Custom: Marketing → Account (Marketing)).
2. [x] Rename the Account **James Williams** → **Williams Household** and set its record type to Household.
3. [x] Create **James Williams** as a Contact in Williams Household (fictional: 204-555-0101, james.williams@example.com, prefers Phone).
4. [x] Create **Carter Household** (Household, Prospect) and move **Daniel Carter** into it.
5. [x] Retrieve again and commit (`Add Household record type for Accounts`).

Commands used (record ids are specific to this Developer Edition org):

```powershell
sf data query  --target-org advisorflow-dev --query "SELECT Id FROM RecordType WHERE SobjectType = 'Account' AND DeveloperName = 'Household'"
sf data update record --target-org advisorflow-dev --sobject Account --record-id <williamsAccountId> --values "Name='Williams Household' RecordTypeId=<householdRecordTypeId>"
sf data create record --target-org advisorflow-dev --sobject Contact --values "FirstName=James LastName=Williams AccountId=<williamsAccountId> Phone='204-555-0101' Email=james.williams@example.com Preferred_Contact_Method__c=Phone"
sf data create record --target-org advisorflow-dev --sobject Account --values "Name='Carter Household' RecordTypeId=<householdRecordTypeId> Client_Status__c=Prospect"
sf data update record --target-org advisorflow-dev --sobject Contact --record-id <danielContactId> --values "AccountId=<carterAccountId>"
```

Result (verified 13 Nov 2026):

| Household | Record type | Client Status | Members |
|---|---|---|---|
| Carter Household | Household | Prospect | Daniel Carter (no preference yet) |
| Williams Household | Household | Prospect | James Williams (Phone) |

The primary-member flag (BR-HH-003) has no Salesforce field yet; it is added in M3. Until then the Salesforce provider reports `isPrimary: false`.

## 7. Prepare the OAuth integration (start of the M2 coding work)

This is done together in the next session:

- Create a dedicated integration user with a least-privilege permission set (read Account and Contact).
- Create an External Client App configured for the OAuth 2.0 JWT bearer flow, with a certificate. The private key stays outside the repo: in `dotnet user-secrets` locally, and in Azure Key Vault later.
- Confirm Developer Edition API limits and integration-user licensing before relying on them.
