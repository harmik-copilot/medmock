# Playwright API tests (TypeScript)

The same 44 black-box API checks as `tests/Med.ApiTests` (C#), in TypeScript. No browser is needed: only Playwright's
`request` fixture is used, which sends HTTP requests and lets you assert on the responses.

## One-time setup

1. Install **Node.js** (current LTS) from https://nodejs.org and check `node --version`.
2. For the **local** environment you also need the .NET 10 SDK, because Playwright starts the API for you.
3. In this folder: `npm install`. (No `npx playwright install` is needed: API tests use no browsers.)

## Run

```bash
npx playwright test                       # local: starts the API if needed, runs all 44, stops it
npx playwright test --grep @smoke         # only smoke tests
npx playwright test --grep @security      # only security tests
npx playwright test --grep @R2            # tests tagged for risk R2 in docs/test-plan.md
npx playwright test permissions.spec.ts   # one file
npx playwright show-report                # HTML report
```

Risk-based tags use the registry IDs directly (`@R1`, `@R2`, etc.) and can be combined with the existing smoke, regression, and security tags.

## Other environments

```bash
TARGET_ENV=test    MED_QA_PASSWORD=...  npx playwright test --grep @regression
TARGET_ENV=staging MED_QA_PASSWORD=...  npx playwright test
MED_API_URL=http://localhost:5080 TARGET_ENV=test MED_QA_PASSWORD=... npx playwright test   # override the address
```

Settings per environment: `../environments/<name>.json` (shared with the C# suite). Nothing is started for test or staging.
The password is never stored in the repo: without `MED_QA_PASSWORD` the suite refuses to run outside `local`.

## Files

| File | Tests | Covers |
|---|---|---|
| `tests/health.spec.ts` | 3 | live, ready, and `/version` matches the expected environment |
| `tests/auth.spec.ts` | 7 | login, 401s, no user enumeration |
| `tests/permissions.spec.ts` | 9 | who may see what, 403s, data residency incl. cross-border referral |
| `tests/ingest.spec.ts` | 12 | push and pull ingest, validation, duplicates, batch limits |
| `tests/audit.spec.ts` | 6 | allowed and denied attempts recorded, audit scoping |
| `tests/contract.spec.ts` | 7 | JSON shape, no internal fields, correlation id, nosniff |
| `tests/helpers/env.ts` | | loads the environment file, password rules |
| `tests/helpers/api.ts` | | `login` (cached per worker), `auth`, `visibleStudyIds` |
| `tests/helpers/data.ts` | | unique synthetic data: `createStudy`, `newStudyPayload`, `pushStudies` |
| `playwright.config.ts` | | base URL, reporters (list, HTML, JUnit), auto-start of the local API |

## Test data rules

Each test creates the studies it needs through `POST /ingest/studies`, names them `QA^<run id>...`, and asserts that a study **is** or
**is not** visible, never on totals. That is why the suite works on a shared environment and on Staging, which has no seed data.
Tests that create data are skipped automatically when an environment file says `"allowMutations": false`.

## C# to TypeScript cheat sheet

| C# (xUnit) | TypeScript (Playwright Test) |
|---|---|
| `[Fact]` | `test('title', { tag: '@smoke' }, async ({ request }) => { ... })` |
| `[Trait("Category","Smoke")]` | `{ tag: '@smoke' }` |
| `[Theory]` + `[InlineData]` | a `for` loop that creates one `test(...)` per row |
| `[MutatingFact]` | `test.skip(!environment.allowMutations, ...)` in the describe block |
| `Assert.Equal(403, result.Status)` | `expect(res.status()).toBe(403)` |
| `ApiFixture`, `TestData` | `helpers/api.ts`, `helpers/data.ts` |
