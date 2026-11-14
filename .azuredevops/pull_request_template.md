## What and why
<!-- One or two sentences. Link the work item. -->

## How it was tested
- [ ] `dotnet test` passes locally (unit, integration, in-process API)
- [ ] `bash scripts/run-api-tests.sh` passes (black-box API, smoke + regression)
- [ ] New behaviour has a test, or this PR explains why not
- [ ] Any new test creates its own data and does not assume exact totals

## Risk
- [ ] Touches access control, data residency, ingest or audit? If yes, a security-tagged test covers it
- [ ] No secrets, real patient data or real identifiers in code, config, tests or logs

## Reviewer notes
<!-- Anything specific to look at. -->
