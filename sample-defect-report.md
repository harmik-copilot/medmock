**Title:** Cross-region referrer can access an ANZ study

**Environment and Build:** TEST — `GET /version`:
```json
{"service":"Med.Api","version":"1.1.987","environment":"Test","region":"ANZ"}
```

**Steps:**
1. Log in as `dr.lee`, whose region is EUR and who is the referrer of record for a synthetic ANZ study.
2. Request `GET /studies/7f92b93d-3e6e-4e31-bd5a-ef15282dc918` using the returned token.
3. Observe that the API returns the study instead of denying access.

**Expected vs Actual:** Expected HTTP `403 Forbidden` because the user’s region differs from the study’s region.  
Actual HTTP `200 OK`; the study details are returned.

**Evidence:**  

- Response body:
  ```json
  {"id":"7f92b93d-3e6e-4e31-bd5a-ef15282dc918","patientId":"QA-P-104","patientName":"QA^Synthetic","modality":"DX","region":"ANZ"}
  ```

- Application log:
  ```text
  2026-11-15T08:42:17Z Information Study access allowed for user ref-001, study 7f92b93d-3e6e-4e31-bd5a-ef15282dc918. Reason: referrer of record.
  ```

- Actual supplied test output: `Region_mismatch_is_denied_even_for_the_referrer_of_record` failed at `AccessPolicyTests.cs:50`: expected `False`, actual `True`.

**Severity / Priority:** S1 / P1 — Cross-region access is allowed despite the intended data-residency restriction.
