# Test plan (risk-based)

## Approach

Test hardest where failure hurts most. For a platform that shares medical images, the worst failure is
**the wrong person seeing a patient's scan**, so access control and data residency come first. Next come
**data integrity** (the image must not change), **completeness** (nothing silently missing) and **auditability**.

Severity: **S1** patient safety or privacy breach · **S2** wrong or missing data, workflow blocked ·
**S3** degraded experience · **S4** cosmetic.

## Test layers

| Layer | Where | Count | Needs | Runs |
|---|---|---|---|---|
| Unit | `tests/Med.Tests` (`AccessPolicyTests`, `StoreIntegrityTests`) | 8 | nothing | every build |
| Integration | `tests/Med.Tests` (`IngestTests`) | 12 | nothing | every build |
| In-process API | `tests/Med.Tests` (`ApiTests`) | 16 | nothing; app hosted in the test | every build |
| Black-box API (C#, Playwright) | `tests/Med.ApiTests` | 44 | a running API | local, Test, Staging |
| Black-box API (TypeScript, Playwright) | `tests/playwright-ts` | 44 | a running API | local, Test, Staging |
| Load | `tests/load/k6-studies.js` | 1 scenario | a running API | Staging |

The two black-box suites contain the same 44 checks, so a team needs only one of them in its pipeline.
The first three layers are the "100% local, no setup" safety net and run in the Build stage.

## Tags

| Tag (C# `Category` / TypeScript) | Meaning | Where it runs |
|---|---|---|
| `Smoke` / `@smoke` | A handful of must-never-break checks: health, version, login, create + open a study, one permission denial | everywhere, first, fail fast |
| `Regression` / `@regression` | The whole black-box suite (includes every smoke test) | Test and Staging, every build |
| `Security` / `@security` | Access control, residency, leakage, headers, auth behaviour | Test and Staging, every build |

## Test data strategy

See `docs/adr/0001-test-data-strategy.md`. In short: each test creates its own synthetic study through the ingest API,
names start with `QA^` plus a run id, assertions are "this study is / is not visible" and never totals, nothing is deleted by tests,
Staging has no seed data. This is what lets the same tests run on a laptop, on a shared Test environment, and in parallel.

## Risk register

| ID | Risk | Sev | Covered by | Status |
|---|---|---|---|---|
| R1 | A referrer, patient or clinic opens a study that is not theirs | S1 | `AccessPolicyTests`; black-box permission and ingest tests tagged `@R1` | Automated |
| R2 | Study served across regions (data residency breach) | S1 | `AccessPolicyTests.Region_mismatch...`; black-box residency tests tagged `@R2`, including the cross-border referral case | Automated |
| R3 | An error response leaks patient data | S1 | black-box 403 response test tagged `@R3` (empty body and no patient name) | Automated |
| R4 | Unauthenticated or forged-token access | S1 | black-box auth tests tagged `@R4`, plus unauthenticated API endpoint tests | Automated |
| R5 | Stored image altered or corrupted without detection | S1/S2 | `StoreIntegrityTests` | Automated |
| R6 | Ingest duplicates, drops or accepts bad studies | S2 | `IngestTests`; black-box ingest tests tagged `@R6` (duplicate, bad modality, future date, bad base64, batch limits, no bad study blocks good ones) | Automated |
| R7 | Allowed or denied access not audited, or audit visible to the wrong clinic | S2 | black-box audit tests tagged `@R7`; `IngestTests.Ingest_is_audited` | Automated |
| R8 | 403 versus 404 reveals that a study id exists (enumeration) | S3 | Design discussion | **Open** |
| R9 | Slow under many concurrent users | S2 | `tests/load/k6-studies.js` in the Staging stage | Automated, thresholds to tune |
| R10 | Viewer renders wrong orientation or markers | S1 | Needs a real viewer and real DICOM samples | Not built |
| R11 | Odd DICOM headers (missing tags, non-ASCII names) break ingest | S2 | Needs real DICOM parsing | Not built |
| R12 | HL7 or FHIR order message malformed or missing referrer | S2 | Needs HL7 parsing | Not built |
| R13 | Failed logins and unauthenticated requests are not audited | S3 | Not covered; product and security decision | **Open** |
| R14 | Tests pointed at the wrong environment | S3 | Smoke test tagged `@R14` compares `GET /version` with the expected environment | Automated |
| R15 | Secret committed, or a default password used outside local | S2 | The API refuses to start without `Auth__QaPassword` outside Development; tests refuse to run without `MED_QA_PASSWORD` outside local. Add secret scanning to the repo | Partly automated |
| R16 | Unsafe client-supplied header reflected (header or log injection) | S3 | Contract test tagged `@R16` verifies unsafe `X-Correlation-ID` values are replaced | Automated |

## Broader risk areas of an imaging-sharing platform (modelled or not)

The register above covers what MedMock implements. A real platform of this kind carries more risk than MedMock models.
This table lists those areas so the gaps are explicit, with the idea of how each would be tested for real.

| Area | Sev | In MedMock? | How it would be tested in a real system |
|---|---|---|---|
| Wrong patient or wrong study (merged or duplicate patients, similar names, wrong context passed by a practice system) | S1 | Partly: ingest validation only | Similar-name and merged-record cases; launch from the practice system for several patients |
| Report and images out of step (wrong study, amended report not shown) | S1 | No reports modelled | Amend a report and confirm every client updates |
| Viewer renders images wrongly (laterality markers, orientation, window and level, series order) | S1 | No viewer | Reference studies with known markers, compared across devices |
| Incomplete, late or duplicated ingest; odd DICOM headers; very large studies | S1/S2 | Partly: validation, duplicates, batch limits | Interrupted transfers, updates after first sync, non-ASCII headers |
| Unauthorised access by changing an id or link | S1 | Yes | Same checks through web, mobile and API |
| Sharing abuse (guessable, non-expiring, forwarded or mis-addressed links) | S1 | No share links | Expiry, revocation, link entropy, wrong-recipient flows |
| Cross-region and cross-tenant leakage (including per-provider branding) | S1 | Yes for region and clinic; branding not modelled | Residency tests per region; tenant isolation checks |
| Patient identity, account recovery, shared devices, notifications revealing health details | S1 | No patient accounts beyond a login | Registration and recovery flows, lock-screen content |
| Search returns more than the user may see | S1/S2 | No search | Search as users with different permissions; boundary and wildcard queries |
| Audit completeness, retention and deletion | S2 | Audit yes; retention and deletion no | Deletion and retention scenarios, backups |
| Scale and availability at peak | S2 | Load smoke only | Load, soak and failover tests |
| Practice-system integration breaking after vendor updates | S2 | No | Compatibility matrix, contract checks on the launch link |
| Mobile apps (OS updates, poor networks, old app versions) | S2/S3 | No | Device and OS matrix, upgrade paths, network throttling |
| Booking and follow-up requests misrouted or lost | S2 | No | End-to-end request flow, notification content |
| Old and new code side by side (regressions during refactors, slow queries as data grows) | S2 | The break-it exercises show the approach | Regression suite around the oldest paths; query-time checks on large data |
| Medical-device obligations (traceability, change records, verification evidence) | S1 (compliance) | Risk register and release checklist only | Traceable test cases and release evidence |
| Public website and contact form (personal data, mis-routing, stale legal links) | S3 | No | Form validation, spam handling, routing, link checks |
| Support staff access to patient data | S2 | No | Role checks on support tooling, redaction in support channels |

Candidate next features to model, in order of value for effort: search with permission filtering, share links with expiry and revocation,
and patient-identity conflicts at ingest.

## Coverage gaps found by breaking things on purpose

| Planted bug | Result | Fix |
|---|---|---|
| Referrer rule returns allow | Caught by unit and black-box tests | none needed |
| No audit entry when opening a study | Caught by the two audit tests | none needed |
| **Region check deleted** | **Nothing failed in the black-box suites.** Role rules alone happened to deny every cross-region case in the fixed data | Added the cross-border referral test: a European GP is the referrer of record for a study at an ANZ clinic. Only the region rule denies it |

Repeat this exercise whenever a rule is added: break it, and confirm a test fails for the right reason.

## Release readiness checklist

- [ ] Build stage green: unit, integration and in-process API tests
- [ ] Smoke and regression green on Test, then on Staging, for the same build
- [ ] `GET /version` on Staging shows this build number
- [ ] No open S1 or S2 defects; S3 defects reviewed and accepted
- [ ] Security-tagged tests green after any change touching access, residency, ingest or audit
- [ ] Load smoke within thresholds on Staging
- [ ] Known gaps (R8, R10 to R13) listed and accepted by product and engineering
- [ ] Rollback plan confirmed; approval recorded with date and build number

## Defect report template

**Title:** short, specific · **Environment and build:** environment name and `GET /version` output ·
**Steps:** numbered · **Expected vs actual:** one line each · **Evidence:** response body, status, the
`X-Correlation-ID` header, log excerpt · **Severity / priority:** with reasoning · **Notes:** frequency, workaround, suspected area
