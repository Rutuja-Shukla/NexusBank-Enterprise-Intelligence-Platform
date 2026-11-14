#!/usr/bin/env bash
# Full verification: backend build + unit + integration tests, frontend build + tests.
# Exits non-zero on the first failure so CI and reviewers can trust the result.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "==> Backend build"
(cd "$root/backend" && dotnet build NexusBank.sln --configuration Release)

echo "==> Backend unit tests"
(cd "$root/backend" && dotnet test tests/NexusBank.Tests --configuration Release --no-build)

echo "==> Backend integration tests"
(cd "$root/backend" && dotnet test tests/NexusBank.IntegrationTests --configuration Release --no-build)

echo "==> Frontend install, build and tests"
(cd "$root/frontend" && npm ci && npm run build && npm test)

echo
echo "All checks passed."
