# MedMock

A fictional learning project: a small **medical-imaging sharing platform** (Acquire, Share, Store), organised the way
a real .NET repository with several environments usually is. All data is synthetic, authentication is deliberately simple, and
it does not claim to show how any real company's product is built. The choice of .NET / C# is an assumption.

```
Scanner -> Clinic PACS/VNA -> [ACQUIRE] -> [STORE] -> [SHARE] -> Doctor / Patient
                               validate     region-tagged   access policy
                               dedupe       bytes +         audit trail
                               checksum     checksum
```

## Repository layout

```
src/
  Med.Domain      data model + AccessPolicy (the one place that decides who may see what)
  Med.Ingest      ACQUIRE: validation, de-duplication, idempotent ingest, fake PACS feed
  Med.Storage     STORE: image bytes + metadata, integrity check, audit log
  Med.Api         SHARE: HTTP API, per-environment settings, health and version endpoints
tests/
  Med.Tests       unit + integration + in-process API tests (no setup, run on every build)
  Med.ApiTests    black-box API tests, C# + Playwright (any environment)
  playwright-ts   the same black-box tests in TypeScript (any environment)
  environments/   local.json, test.json, staging.json shared by both black-box suites
  load/           k6 load smoke test
pipelines/        Azure DevOps templates (deploy stub, API test runner)
docs/             api.md, environments.md, test-plan.md, data-flow.md, adr/
scripts/          run-api-tests.sh
azure-pipelines.yml  Build -> Test -> Staging
Dockerfile, docker-compose.yml, global.json, Directory.Build.props, .editorconfig, CONTRIBUTING.md
```

## Quick start (local)

Needs the .NET 10 SDK (pinned in `global.json`). Node.js (current LTS) is only needed for the TypeScript suite.

```bash
dotnet test                          # 36 unit, integration and in-process API tests
dotnet run --project src/Med.Api     # API on http://localhost:5080, environment Development
bash scripts/run-api-tests.sh        # C# black-box tests against a freshly started API (44)

cd tests/playwright-ts && npm install && npx playwright test    # TypeScript version (44)
```

Local users all share the password `test-pass` (Development only): `dr.lee`, `dr.patel`, `dr.novak`, `sam`, `chris`, `clinic.admin`, `clinic.eu`.

## Test layers and tags

| Layer | Count | Needs a running API? |
|---|---|---|
| Unit, integration, in-process API (`tests/Med.Tests`) | 36 | no |
| Black-box API, C# (`tests/Med.ApiTests`) | 44 | yes |
| Black-box API, TypeScript (`tests/playwright-ts`) | 44 | yes |
| Load (`tests/load`, k6) | 1 scenario | yes |

The black-box tests are tagged **Smoke** (fast must-never-break checks), **Regression** (everything) and **Security**:

```bash
dotnet test tests/Med.ApiTests --filter "Category=Smoke"
bash scripts/run-api-tests.sh "Category=Security"
npx playwright test --grep @smoke          # inside tests/playwright-ts
```

## Environments

Choose one with `TARGET_ENV` (`local` default, `test`, `staging`). Its address and rules live in `tests/environments/<name>.json`.

```bash
TARGET_ENV=staging MED_QA_PASSWORD=... dotnet test tests/Med.ApiTests --filter "Category=Smoke"
TARGET_ENV=staging MED_QA_PASSWORD=... npx playwright test --grep @smoke
```

The tests create all the data they need through the ingest API, so they work on a shared environment and on Staging, which has no seed data.
Full detail, including the checklist for adding an environment: `docs/environments.md`.

## Configuration and secrets

- API settings layer as `appsettings.json` -> `appsettings.{Environment}.json` -> environment variables.
- **Nothing secret is committed.** Outside `Development` the API refuses to start without `Auth__QaPassword`, and the tests refuse to run without `MED_QA_PASSWORD`.
- `GET /version` reports the environment and build, and the first smoke test checks it, so tests cannot silently hit the wrong environment.
- Every response has an `X-Correlation-ID` header. Quote it in bug reports.

## Docker

```bash
MED_QA_PASSWORD=choose-one docker compose up --build      # a Test-like API on http://localhost:5080
TARGET_ENV=test MED_API_URL=http://localhost:5080 MED_QA_PASSWORD=choose-one dotnet test tests/Med.ApiTests --filter "Category=Smoke"
```

## CI/CD

`azure-pipelines.yml`: **Build** (restore, build, 36 tests, publish artifact) -> **Test** environment (deploy, smoke, regression) ->
**Staging** environment (approval, deploy, smoke, regression, k6 load smoke). The deploy step is a stub; the rest is wired as a real pipeline would be.
Run the TypeScript suite instead of the C# one with the pipeline parameter `testSuite`.

## Documentation

| File | What |
|---|---|
| `docs/api.md` | Endpoint reference, status codes, ingest rules |
| `docs/environments.md` | Environments, configuration, secrets, test data rules, adding an environment |
| `docs/test-plan.md` | Risk register, layers, tags, coverage gaps found, release checklist, defect template |
| `docs/data-flow.md` | Diagrams of the data flow and of opening a study |
| `docs/adr/0001-test-data-strategy.md` | Why tests create their own data |
| `CONTRIBUTING.md` | How to work in this repo |

## Deliberately simplified

- No real DICOM parsing (a real project would use a DICOM library and real PACS protocols), no HL7 or FHIR, no viewer.
- In-memory storage and audit log; no database, so no migrations.
- Simple token authentication, no multi-factor, no consent management.
- The deploy step in the pipeline is a stub, and no lockfile is committed for the TypeScript suite (commit `package-lock.json` after the first `npm install`, then use `npm ci`).
- Next steps worth adding: OpenAPI spec generation, secret scanning in CI, a UI viewer with browser tests, a database with migration tests.
