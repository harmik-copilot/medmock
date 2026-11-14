using Med.Domain;

namespace Med.Api;

public sealed record StudySummary(Guid Id, string PatientName, string Modality, string BodyPart, DateOnly StudyDate)
{
    public static StudySummary From(Study s) => new(s.Id, s.PatientName, s.Modality, s.BodyPart, s.StudyDate);
}

public sealed record StudyDetail(
    Guid Id, string StudyInstanceUid, string PatientId, string PatientName, string Modality,
    string BodyPart, DateOnly StudyDate, int SeriesCount, long SizeBytes, string Region)
{
    public static StudyDetail From(Study s) => new(
        s.Id, s.StudyInstanceUid, s.PatientId, s.PatientName, s.Modality,
        s.BodyPart, s.StudyDate, s.SeriesCount, s.SizeBytes, s.Region.ToString());
}

/// <summary>One study pushed by a clinic's connector. Image bytes travel as base64 text.</summary>
public sealed record IngestStudyRequest(
    string StudyInstanceUid,
    string PatientId,
    string PatientName,
    string Modality,
    string BodyPart,
    DateOnly StudyDate,
    int SeriesCount,
    string ReferrerId,
    string PixelsBase64);

public sealed record IngestStudiesRequest(List<IngestStudyRequest>? Studies);
