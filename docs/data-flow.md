# Data flow

All of this is a model for learning. Boxes marked *(simulated)* stand in for real clinic systems.

## 1. The big picture

```mermaid
flowchart LR
    S["Scanner / modality<br/>(simulated)"] -- DICOM --> P[("Clinic PACS / VNA<br/>(simulated: FakePacsSource)")]
    R["RIS order: who referred?<br/>(simulated: ReferrerId field)"] -.-> P
    P -- "pull: Fetch(providerId)<br/>push: POST /ingest/studies" --> I["ACQUIRE<br/>Med.Ingest<br/>validate, dedupe, checksum"]
    I -- Save --> ST[("STORE<br/>Med.Storage<br/>metadata + bytes + region")]
    I -- "INGEST_STUDY" --> A[("Audit log")]
    ST --> API["SHARE<br/>Med.Api<br/>auth + AccessPolicy"]
    API -- "200 / 403" --> D["Referring doctor"]
    API -- "200 / 403" --> PT["Patient"]
    API -- "OPEN_STUDY (allowed or not)" --> A
```

## 2. Opening a study (the most important request)

```mermaid
sequenceDiagram
    participant U as Doctor or patient
    participant API as Med.Api
    participant Auth as AuthService
    participant Store as StudyStore
    participant Pol as AccessPolicy
    participant Log as AuditLog

    U->>API: GET /studies/{id} + Bearer token
    API->>Auth: Resolve(token)
    Auth-->>API: user, or nothing
    alt no valid token
        API-->>U: 401
    else token ok
        API->>Store: Get(id)
        alt study not found
            API->>Log: OPEN_STUDY denied (not found)
            API-->>U: 404
        else found
            API->>Pol: Evaluate(user, study)
            Pol-->>API: allowed or denied + reason
            API->>Log: OPEN_STUDY (allowed?, reason)
            alt allowed
                API-->>U: 200 + study detail
            else denied
                API-->>U: 403 (no reason in body)
            end
        end
    end
```

## 3. Access rules (AccessPolicy)

1. **Region first.** User's region must equal the study's region (data residency).
2. **Referrer:** only the referrer of record.
3. **Patient:** only their own studies.
4. **Provider:** only studies from their own clinic.

Anything else is denied. Rules are checked in that order, so a region mismatch beats everything.

## 4. Where trust boundaries sit

| Boundary | Why it matters |
|---|---|
| Clinic network → platform | Data leaves a closed network. Must be authenticated and encrypted in a real system |
| Internet → API | Every endpoint must check identity (401) and permission (403) |
| Region → region | Data residency: studies must not be served across regions |
| Stored bytes → viewer | Integrity: what the doctor sees must be exactly what the scanner produced |

## 5. Where bugs would hurt most (feeds the test plan)

- Ingest assigns the wrong region, or the wrong referrer, so the wrong person sees the study.
- Re-running ingest duplicates studies, so a doctor sees the same scan twice and reports get confused.
- An error response leaks patient details.
- Stored bytes change (silent corruption) and nobody notices.
- A denied attempt is not logged, so a breach cannot be investigated.
