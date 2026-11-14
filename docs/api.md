# API reference

Base URL: see `tests/environments/<env>.json`. All bodies are JSON unless noted. Authenticated calls send
`Authorization: Bearer <token>`. Every response carries `X-Correlation-ID` and `X-Content-Type-Options: nosniff`.

| Method and path | Who | Success | Errors |
|---|---|---|---|
| `GET /health/live` | anyone | 200 `Healthy` (text) | |
| `GET /health/ready` | anyone | 200 `Healthy` (text) | |
| `GET /version` | anyone | 200 `{ service, version, environment, region }` | |
| `POST /auth/login` | anyone | 200 `{ token }` | 401 wrong or unknown credentials (identical response for both) |
| `GET /studies` | any logged-in user | 200 list of `{ id, patientName, modality, bodyPart, studyDate }` that this user may see | 401 |
| `GET /studies/{id}` | any logged-in user | 200 `{ id, studyInstanceUid, patientId, patientName, modality, bodyPart, studyDate, seriesCount, sizeBytes, region }` | 401, **403** (empty body), 404 unknown id |
| `POST /ingest/studies` | provider | 200 `{ accepted, duplicates, rejected[], acceptedStudyIds[] }` | 400 bad batch or base64, 401, 403 |
| `POST /ingest/run` | provider | 200 same summary (pulls the clinic's simulated PACS feed) | 401, 403 |
| `GET /audit` | provider | 200 entries about that provider's own studies | 401, 403 |

## `POST /ingest/studies`

```json
{
  "studies": [
    {
      "studyInstanceUid": "1.2.999.9.123456789.1731456000000.1",
      "patientId": "P-1001",
      "patientName": "QA^A1B2C300",
      "modality": "DX",
      "bodyPart": "CHEST",
      "studyDate": "2026-11-12",
      "seriesCount": 1,
      "referrerId": "ref-001",
      "pixelsBase64": "c3ludGhldGljLXBpeGVscw=="
    }
  ]
}
```

- Between 1 and 100 studies per request, otherwise 400.
- A study is **rejected** (reported in `rejected`, others still accepted) for: missing UID, patient ID, patient name, referrer or date;
  modality not one of `CR DX CT MR US MG NM PT`; date in the future; `seriesCount` below 1; empty image data.
- Sending a UID that already exists is a **duplicate**, not an error, so retries are safe.
- The study's region always comes from the clinic that pushed it, never from the payload.

## Access rules (the only place they are defined: `AccessPolicy`)

1. The user's region must equal the study's region (data residency), otherwise 403.
2. Referrer: only the referrer of record. Patient: only their own patient id. Provider: only studies from their own clinic.

## Status codes in one line each

- **401** we do not know who you are. **403** we know who you are and you may not. **404** no such study. **400** the request itself is wrong.
- **500** must never be the answer to bad input.

## Known design questions

- A 403 versus a 404 reveals that a study id exists (risk R8 in the test plan).
- `GET /version` is public so smoke tests can confirm the environment. A real deployment may restrict it.
- `GET /audit` returns everything for the provider; a real one would page and filter.
