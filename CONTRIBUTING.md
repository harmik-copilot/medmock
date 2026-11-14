# Contributing

## Ground rules

- **Synthetic data only.** Never put real patient data, real names or real identifiers anywhere in this repository, its tests or its logs.
- **No secrets in the repo.** Passwords and keys come from environment variables or the pipeline's secret store (see `docs/environments.md`).
- **Small, short-lived branches** off `main`. Open a pull request early; `main` is protected and needs a green build plus a review.

## Before you open a pull request

```bash
dotnet test                       # unit + integration + in-process API tests
bash scripts/run-api-tests.sh     # black-box API tests against a freshly started local API
```

If you touch the TypeScript suite as well: `cd tests/playwright-ts && npx playwright test`.

## Adding or changing tests

1. **Pick the lowest level that can catch the bug.** A rule in `AccessPolicy` gets a unit test in `tests/Med.Tests`. Only add a black-box API test when the behaviour is about the HTTP contract or about environments.
2. **Create your own data** with the helpers (`CreateStudyAsync` in C#, `createStudy` in TypeScript). Never rely on seed data, and assert that something *is* or *is not* visible rather than "exactly N in total".
3. **Tag it:** `Smoke` (a handful of must-never-break checks, fast, safe everywhere), `Regression` (the full suite), `Security` (access control, residency, leakage). A smoke test is also a regression test.
4. **Keep each test independent.** No test may depend on another having run first.
5. **Name tests as sentences** that read like requirements, for example `Another_referrer_gets_403_with_no_patient_data_in_the_response`.

## Prove a new test is worth having

Break the behaviour on purpose, run the test, and check that it fails for the right reason. Then undo the change. A test that cannot fail is not protecting anything. The cross-border residency test exists because this exercise showed that deleting the region check broke nothing.

## Bugs

Use the defect template in `docs/test-plan.md`. Always include the environment, the build version (`GET /version`) and the `X-Correlation-ID` from the failing response.
