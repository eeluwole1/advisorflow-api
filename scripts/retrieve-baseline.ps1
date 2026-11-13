# Retrieves the existing AdvisorFlow configuration from the Developer Edition org
# into force-app/ so it can be reviewed and committed as the M2 baseline.
# Usage (from the repo root):  ./scripts/retrieve-baseline.ps1 [-Alias advisorflow-dev]
param([string]$Alias = "advisorflow-dev")
$ErrorActionPreference = "Stop"

$orgs = sf org list --json | ConvertFrom-Json
$known = @($orgs.result.nonScratchOrgs) | Where-Object { $_.alias -eq $Alias }
if (-not $known) {
    Write-Host "Org '$Alias' is not authorized yet. Opening the browser login..."
    sf org login web --alias $Alias --instance-url https://login.salesforce.com
}

sf project retrieve start --manifest manifest/baseline-package.xml --target-org $Alias
Write-Host ""
Write-Host "Done. Review the changes with 'git status' and 'git diff', then commit:"
Write-Host "  git add force-app manifest && git commit -m 'Retrieve Salesforce baseline metadata'"
