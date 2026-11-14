# ADR 0001: Test data strategy for shared environments

- **Status:** accepted
- **Date:** 2026-11-13

## Context

The first version of the black-box tests relied on seed data and fixed totals ("Dr Lee sees exactly one study").
That only works on a private, freshly started API. On a shared Test or Staging environment other people and other test
runs add data, and Staging has no seed data at all.

## Decision

- Every test creates the data it needs through the public ingest API (`POST /ingest/studies`), the same way a clinic connector does.
- Test data is clearly synthetic: patient names start with `QA^` and contain a per-run id, and study UIDs are unique per run.
- Assertions check that a specific study **is** or **is not** visible. They never assert on totals.
- QA accounts are pre-provisioned in each environment. Their shared password comes from `MED_QA_PASSWORD` (tests) and `Auth__QaPassword` (API). It is never committed.
- Tests do **not** delete what they create. Environment owners reset test data on a schedule (see `docs/environments.md`).

## Consequences

- Tests run safely in parallel and alongside other testers.
- Environments slowly accumulate `QA^` studies between resets. This is acceptable and easy to find and remove.
- The ingest API is itself exercised by every test, so a break there fails loudly and early (it is part of the smoke tests).
- A read-only environment (`allowMutations: false`) skips every test that creates data.
