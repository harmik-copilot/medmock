#!/usr/bin/env bash
# Runs the C# Playwright API tests against a freshly started LOCAL API, then stops the API.
#   bash scripts/run-api-tests.sh                      # everything
#   bash scripts/run-api-tests.sh "Category=Smoke"     # only smoke tests
#   bash scripts/run-api-tests.sh "Category=Security"  # only security tests
# For Test or Staging you do not start anything: set TARGET_ENV and MED_QA_PASSWORD and run
#   dotnet test tests/Med.ApiTests/Med.ApiTests.csproj
set -euo pipefail

PORT="${PORT:-5080}"
URL="http://localhost:${PORT}"
FILTER="${1:-}"
cd "$(dirname "$0")/.."

echo "Building the API..."
dotnet build src/Med.Api/Med.Api.csproj -c Debug --nologo -v q

echo "Starting the API at ${URL} (environment: Development)..."
ASPNETCORE_ENVIRONMENT=Development dotnet src/Med.Api/bin/Debug/net10.0/Med.Api.dll --urls "${URL}" > /tmp/med-api.log 2>&1 &
API_PID=$!
trap 'kill ${API_PID} 2>/dev/null || true' EXIT

READY=0
for _ in $(seq 1 60); do
  if curl -s -f -o /dev/null "${URL}/health/ready"; then
    READY=1
    break
  fi
  sleep 1
done

if [ "${READY}" != "1" ]; then
  echo "The API did not become ready. Last lines of /tmp/med-api.log:"
  tail -n 20 /tmp/med-api.log
  exit 1
fi

echo "API is ready. Running Playwright API tests (TARGET_ENV=local)..."
if [ -n "${FILTER}" ]; then
  TARGET_ENV=local MED_API_URL="${URL}" dotnet test tests/Med.ApiTests/Med.ApiTests.csproj --nologo --filter "${FILTER}"
else
  TARGET_ENV=local MED_API_URL="${URL}" dotnet test tests/Med.ApiTests/Med.ApiTests.csproj --nologo
fi
