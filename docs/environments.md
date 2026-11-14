# Environments

| | Local | Test | Staging | Production |
|---|---|---|---|---|
| **Purpose** | Develop and debug on one machine | First stop for every new build; find bugs fast | Production-like final check before release | Real customers |
| **ASPNETCORE_ENVIRONMENT** | `Development` | `Test` | `Staging` | `Production` |
| **Settings file (API)** | `appsettings.Development.json` | `appsettings.Test.json` | `appsettings.Staging.json` | not in this repo |
| **Test settings file** | `tests/environments/local.json` | `tests/environments/test.json` | `tests/environments/staging.json` | none: the QA suites are not run here |
| **Seed data at startup** | Yes | Yes | **No** (data arrives through the ingest API) | No |
| **Data allowed** | Synthetic | Synthetic | Synthetic or de-identified | Real |
| **QA password** | `test-pass` default, local only | From a secret, required | From a secret, required | n/a |
| **Who deploys** | You | Pipeline, automatically | Pipeline, after approval | Release process |
| **Reset** | Restart the API | Scheduled, owner decides | Scheduled, owner decides | never |

## How configuration is layered

The API reads, lowest to highest priority: `appsettings.json`, then `appsettings.{Environment}.json`, then **environment variables**.
Anything secret is supplied as an environment variable and never written in a file:

| Setting | Environment variable | Notes |
|---|---|---|
| `Auth:QaPassword` | `Auth__QaPassword` | The API **refuses to start** outside `Development` if it is missing |
| `Seed:OnStartup` | `Seed__OnStartup` | `true` locally and in Test, `false` in Staging |
| `Deployment:Region` | `Deployment__Region` | Reported by `GET /version` |
| (build id) | `APP_VERSION` | Reported by `GET /version`; the pipeline sets it to the build number |

The test suites read, again from the environment and never from files in the repo:

| Variable | Meaning |
|---|---|
| `TARGET_ENV` | `local` (default), `test` or `staging`; selects the file in `tests/environments` |
| `MED_API_URL` | Optional: overrides `baseUrl` from that file |
| `MED_QA_PASSWORD` | The QA accounts' password. Required for anything except `local` |

The two passwords must match: the API's `Auth__QaPassword` and the tests' `MED_QA_PASSWORD` are the same secret in the environment's variable group.

## GitHub Actions configuration

The workflow uses GitHub Environments named `med-test` and `med-staging`. Create both under **Settings -> Environments** and configure each with:

- Environment variable `MED_API_URL`: the deployed API base URL.
- Environment secret `MED_QA_PASSWORD`: the same QA password configured on that API as `Auth__QaPassword`.

Add required reviewers to `med-staging` to require approval before Staging tests run. The workflow tests existing deployments; it does not deploy the API. Pull requests run only the build and unit/integration tests; pushes to `main` run both API suites through Test and Staging, then the read-only k6 smoke test. A manually selected smoke-only run uses `@smoke` for TypeScript (or `Category=Smoke` for C#) and skips k6.

## Running the tests against an environment

```bash
# Local (the API is started for you)
bash scripts/run-api-tests.sh                       # C#
cd tests/playwright-ts && npx playwright test       # TypeScript

# Test or Staging (already deployed)
TARGET_ENV=staging MED_QA_PASSWORD=... dotnet test tests/Med.ApiTests/Med.ApiTests.csproj --filter "Category=Smoke"
TARGET_ENV=staging MED_QA_PASSWORD=... npx playwright test --grep @smoke        # inside tests/playwright-ts
```

The first smoke test calls `GET /version` and compares the reported environment with the one the tests expect.
If you point `TARGET_ENV=test` at the Staging URL, that test fails with a message saying so, before anything else runs.

## What each suite may do where

| Suite | Local | Test | Staging | Production |
|---|---|---|---|---|
| Unit, integration, in-process API (`tests/Med.Tests`) | yes (and in the build) | no need | no need | no |
| Smoke (`Category=Smoke` / `@smoke`) | yes | yes | yes | read-only checks only, if ever, with separate approval |
| Regression (`Category=Regression` / `@regression`) | yes | yes | yes | **never** |
| Security (`Category=Security` / `@security`) | yes | yes | yes | **never** |
| Tests that create data (all but a few) | yes | yes | yes (unique `QA^` data, no deletes) | **never** |
| Load (`tests/load`, k6) | small | rarely | **yes**, read-only | **never** |

An environment file with `"allowMutations": false` makes every test that creates data skip itself.

## Test data rules (see also ADR 0001)

- Tests create their own studies through `POST /ingest/studies` and never assume seed data or totals.
- Patient names start with `QA^` followed by a per-run id, and study UIDs are unique per run.
- Tests do not delete. The environment owner removes `QA^` data on the reset schedule.
- No real patient data in Test or Staging, ever. Anything derived from production must be de-identified first.

## Promotion flow

```
pull request -> Build (unit + integration) -> Test (smoke, regression) -> approval -> Staging (smoke, regression, load) -> release
```

The same build artifact moves through every stage. Only configuration changes between environments, never the code.

## Adding another environment (checklist)

1. Add `tests/environments/<name>.json` (URL, `allowMutations`, expected environment name).
2. Add `src/Med.Api/appsettings.<Name>.json` with its non-secret settings.
3. Create the hosting target, set `ASPNETCORE_ENVIRONMENT`, `APP_VERSION` and the `Auth__QaPassword` secret on it.
4. Create an Azure DevOps **environment** (with approvals if needed) and a **variable group** holding `MED_API_URL` and the secret `MED_QA_PASSWORD`.
5. Add a stage to `azure-pipelines.yml`, reusing the two templates in `pipelines/templates`.
6. Run the smoke tests against it. The `/version` check proves you are testing the right place.

## Ownership (fill in)

| Environment | Owner | Reset schedule | Contact |
|---|---|---|---|
| Test | | | |
| Staging | | | |
