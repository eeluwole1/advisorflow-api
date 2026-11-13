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
   - [ ] Is the existing test Contact actually James Williams? If so, **reuse it** and link it to the household. Do not create a second James Williams Contact (BR-HH-004).
   - [ ] If the existing Contact is a different fictional person, keep it and decide which household it belongs to.

## 6. Migrate the sample to the household model

1. Choose how to identify households (ADR-002): a **Household** Account record type, or Type/classification = Household. Record the choice in `docs/architecture/decisions.md`.
2. Rename the Account **James Williams** → **Williams Household**, and apply the household record type or classification.
3. Make sure the James Williams Contact's Account is **Williams Household**.
4. Retrieve again (step 3) if any metadata changed, such as a new record type, and commit.

## 7. Prepare the OAuth integration (start of the M2 coding work)

This is done together in the next session:

- Create a dedicated integration user with a least-privilege permission set (read Account and Contact).
- Create an External Client App configured for the OAuth 2.0 JWT bearer flow, with a certificate. The private key stays outside the repo: in `dotnet user-secrets` locally, and in Azure Key Vault later.
- Confirm Developer Edition API limits and integration-user licensing before relying on them.
