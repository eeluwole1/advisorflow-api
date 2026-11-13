#!/usr/bin/env bash
# Retrieves the existing AdvisorFlow configuration from the Developer Edition org
# into force-app/ so it can be reviewed and committed as the M2 baseline.
# Usage (from the repo root):  ./scripts/retrieve-baseline.sh [alias]
set -euo pipefail
ALIAS="${1:-advisorflow-dev}"

if ! sf org display --target-org "$ALIAS" >/dev/null 2>&1; then
  echo "Org '$ALIAS' is not authorized yet. Opening the browser login..."
  sf org login web --alias "$ALIAS" --instance-url https://login.salesforce.com
fi

sf project retrieve start --manifest manifest/baseline-package.xml --target-org "$ALIAS"
echo
echo "Done. Review the changes with 'git status' and 'git diff', then commit:"
echo "  git add force-app manifest && git commit -m 'Retrieve Salesforce baseline metadata'"
