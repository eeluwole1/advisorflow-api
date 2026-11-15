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

## 7. Set up the OAuth integration (JWT bearer flow)

Done on 13 Nov 2026. The API signs in to Salesforce as a dedicated, read-only integration user, using the OAuth 2.0 JWT bearer flow. No password and no browser step are involved: the API signs a JWT with a private key, and Salesforce checks it against the uploaded certificate.

### 7a. Key pair (outside the repo)

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.advisorflow" | Out-Null
cd "$env:USERPROFILE\.advisorflow"
Set-Alias openssl "C:\Program Files\Git\usr\bin\openssl.exe"   # OpenSSL that ships with Git for Windows
openssl genrsa -out server.key 2048
openssl req -new -x509 -key server.key -out server.crt -days 365 -subj "/CN=AdvisorFlow API"
```

- `server.key` is the real credential. It never goes into Git, chat or screenshots. In Azure it moves to Key Vault.
- `server.crt` is public. It is uploaded to Salesforce and expires after 365 days (renew before 13 Nov 2027).

### 7b. Permission set `AdvisorFlow API Read` (`AdvisorFlow_API_Read`)

License: Salesforce API Integration. Read-only, nothing else.

| Object | Object permissions | Field read access |
|---|---|---|
| Account | Read, View All Records | Account Number, Phone, Client Status, Client Risk Profile, Customer Priority (Name, Owner, Record Type are automatic) |
| Contact | Read, View All Records | Account Name (`AccountId`), Email, Phone, Preferred Contact Method (Name is automatic) |

No tab settings, no record type assignments, no edit access.

### 7c. Integration user

| Setting | Value |
|---|---|
| Name | AdvisorFlow Integration |
| Username | `advisorflow.integration@eeluwole.dev` |
| User license | Salesforce Integration |
| Profile | Minimum Access - API Only Integrations (no UI login) |
| Role | None (`View All Records` already covers visibility) |
| Permission set license | Salesforce API Integration |
| Permission set | AdvisorFlow API Read |

### 7d. External Client App `AdvisorFlow API` (`AdvisorFlow_API`)

- Distribution state: Local.
- Callback URL: `http://localhost:1717/OauthRedirect` (required by the form; the JWT flow does not use it).
- OAuth scopes: `api`, `refresh_token, offline_access`.
- Flows: JWT bearer flow only, with `server.crt` uploaded.
- Policies: Permitted users = Admin approved users are pre-authorized; selected permission set = AdvisorFlow API Read.
- The Consumer Secret is not used by the JWT flow and is not stored anywhere.

### 7e. Verify with the CLI

```powershell
$clientId = Read-Host "Consumer Key"   # paste; keeps the key out of history and screenshots
sf org login jwt --client-id $clientId --jwt-key-file "$env:USERPROFILE\.advisorflow\server.key" `
  --username advisorflow.integration@eeluwole.dev `
  --instance-url https://orgfarm-c274b45e01-dev-ed.develop.my.salesforce.com --alias advisorflow-integration
```

| Check | Result |
|---|---|
| JWT login | Authorized, org `00Dfj00000oMPSnEAO` |
| Query households (Account, record type Household) | 2 records with all custom fields |
| Query contacts of households | 2 records with Email, Phone, Preferred Contact Method, AccountId |
| Update an Account | Rejected: `entity type cannot be updated: Account` (expected) |

Changes to an External Client App can take a few minutes to apply; if the first login returns `invalid_client_id`, wait and retry.

### 7f. Local configuration (`dotnet user-secrets`)

```powershell
cd C:\advisorflow-api
dotnet user-secrets init --project src\AdvisorFlow.Api      # adds <UserSecretsId> to the csproj (safe to commit)
dotnet user-secrets set "Salesforce:LoginUrl" "https://orgfarm-c274b45e01-dev-ed.develop.my.salesforce.com" --project src\AdvisorFlow.Api
dotnet user-secrets set "Salesforce:ClientId" $clientId --project src\AdvisorFlow.Api
dotnet user-secrets set "Salesforce:IntegrationUsername" "advisorflow.integration@eeluwole.dev" --project src\AdvisorFlow.Api
dotnet user-secrets set "Salesforce:PrivateKeyReference" "C:\Users\user\.advisorflow\server.key" --project src\AdvisorFlow.Api
```

The values live in `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, outside the repo, and override the empty placeholders in `appsettings.json` in Development. `Crm:Provider` stays `Mock` until the Salesforce provider is implemented.

To check without printing the Consumer Key:

```powershell
dotnet user-secrets list --project src\AdvisorFlow.Api | ForEach-Object { $_ -replace '^(Salesforce:ClientId = ).+$', '$1<hidden>' }
```

### Follow-ups

- ~~Daniel Carter shares James Williams's phone number.~~ Fixed on 14 Nov 2026: 204-555-0102 (see section 8d).
- Developer Edition API limits apply; the API should cache the access token rather than signing in per request.

## 8. Prepare Salesforce for writes (M3)

Done on 14 Nov 2026, following ADR-008 (no hard delete, separate write permission set, acting advisor deferred to M7).

### 8a. Primary member field

Contact → **Is Primary Member** (`Is_Primary_Member__c`), Checkbox, default unchecked. Field-level security: System Administrator only (other profiles explicitly have no access); added to the four Contact layouts. The API enforces at most one primary per household (BR-HH-003).

`AdvisorFlow API Read` now also reads `Is_Primary_Member__c`.

### 8b. Permission set `AdvisorFlow API Write` (`AdvisorFlow_API_Write`)

License: Salesforce API Integration. Assigned to the integration user (no expiry).

| Object | Object permissions | Field edit access |
|---|---|---|
| Account | Read, Create, Edit (no Delete, no View/Modify All) | Phone, Client Status, Client Risk Profile, Customer Priority (Name is automatic) |
| Contact | Read, Create, Edit (no Delete) | Account Name (`AccountId`), Email, Phone, Preferred Contact Method, Is Primary Member (First/Last Name are automatic) |

Record type: **Household** assigned, so the API can create household Accounts. Account Number stays read-only.

### 8c. Retrieve into the repo

Both permission sets were added to `manifest/baseline-package.xml`, then:

```powershell
./scripts/retrieve-baseline.ps1
```

Real changes: the new field, both permission sets, +4 lines in each Contact layout, and the new field's access in each of the 7 profiles (Admin: read/edit; others: none). The CLI labels every retrieved file "Changed"; `git diff --stat` shows what actually differs.

### 8d. Verify with the CLI (integration user)

| Check | Command | Result |
|---|---|---|
| Edit | `sf data update record --target-org advisorflow-integration --sobject Contact --record-id 003fj00001v2tLdAAI --values "Phone=204-555-0102"` | Success (also gives Daniel Carter his own number) |
| Create | `sf data create record --target-org advisorflow-integration --sobject Contact --values "FirstName=Delete LastName=Test AccountId=001fj00002Et3AfAAJ"` | Success: `003fj00001vIicLAAS` |
| Delete | `sf data delete record --target-org advisorflow-integration --sobject Contact --record-id 003fj00001vIicLAAS` | **Rejected:** `INSUFFICIENT_ACCESS_OR_READONLY` (expected) |
| Clean-up | same delete with `--target-org advisorflow-dev` (admin) | Test contact removed (Recycle Bin) |
