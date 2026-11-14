namespace Med.Domain;

/// <summary>Where data must live (data residency). Models typical regional deployments (Australia/NZ, Europe, South East Asia).</summary>
public enum Region { ANZ, EUR, SEA }

/// <summary>Who is using the platform.</summary>
public enum Role { Provider, Referrer, Patient }

/// <summary>A radiology provider (the clinic that owns the PACS/VNA). The customer in this model.</summary>
public sealed record Provider(string Id, string Name, Region Region);

/// <summary>A logged-in user. PatientId / ProviderId are set only for the matching role.</summary>
public sealed record AppUser(
    string Id,
    string DisplayName,
    Role Role,
    Region Region,
    string? PatientId = null,
    string? ProviderId = null);

/// <summary>
/// The handful of DICOM header fields this mock cares about.
/// Real DICOM has thousands of possible tags; see README for how to go further.
/// </summary>
public sealed record DicomHeader(
    string StudyInstanceUid,
    string PatientId,
    string PatientName,   // DICOM style: FAMILY^GIVEN
    string Modality,      // CR, DX, CT, MR, US, MG, NM, PT ...
    string BodyPart,
    DateOnly StudyDate,
    int SeriesCount);

/// <summary>What the clinic's PACS hands over: header + referrer (from the RIS order) + image bytes.</summary>
public sealed record IncomingStudy(DicomHeader Header, string ReferrerId, byte[] Pixels);

/// <summary>A study once accepted and stored by the platform.</summary>
public sealed record Study(
    Guid Id,
    string StudyInstanceUid,
    string PatientId,
    string PatientName,
    string Modality,
    string BodyPart,
    DateOnly StudyDate,
    int SeriesCount,
    Region Region,
    string ProviderId,
    string ReferrerId,
    string Checksum,
    long SizeBytes);

/// <summary>One line in the audit trail: who did what to which study, and was it allowed.</summary>
public sealed record AuditEntry(
    DateTimeOffset At,
    string UserId,
    string Action,
    Guid? StudyId,
    bool Allowed,
    string? Reason);
