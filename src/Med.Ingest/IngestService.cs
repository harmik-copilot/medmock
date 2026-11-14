using Med.Domain;

namespace Med.Ingest;

public sealed record RejectedStudy(string StudyInstanceUid, string Reason);

public sealed record IngestSummary(
    int Accepted,
    int Duplicates,
    IReadOnlyList<RejectedStudy> Rejected,
    IReadOnlyList<Guid> AcceptedStudyIds);

/// <summary>
/// ACQUIRE stage: validate -> de-duplicate -> checksum -> store -> audit.
/// Properties worth testing: idempotent (re-running adds nothing), one bad study never blocks
/// good ones, and the region always comes from the provider, never from the payload.
/// </summary>
public sealed class IngestService
{
    private static readonly HashSet<string> AllowedModalities =
        new(StringComparer.OrdinalIgnoreCase) { "CR", "DX", "CT", "MR", "US", "MG", "NM", "PT" };

    private readonly IStudyStore _store;
    private readonly IAuditLog _audit;
    private readonly TimeProvider _clock;

    public IngestService(IStudyStore store, IAuditLog audit, TimeProvider clock)
    {
        _store = store;
        _audit = audit;
        _clock = clock;
    }

    public IngestSummary Ingest(Provider provider, IEnumerable<IncomingStudy> incoming)
    {
        var accepted = 0;
        var duplicates = 0;
        var rejected = new List<RejectedStudy>();
        var acceptedIds = new List<Guid>();
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

        foreach (var item in incoming)
        {
            var header = item.Header;
            var problem = Validate(header, item, today);
            if (problem is not null)
            {
                rejected.Add(new RejectedStudy(header.StudyInstanceUid ?? "(missing)", problem));
                continue;
            }

            if (_store.Exists(header.StudyInstanceUid))
            {
                duplicates++;
                continue;
            }

            var study = new Study(
                Id: Guid.NewGuid(),
                StudyInstanceUid: header.StudyInstanceUid,
                PatientId: header.PatientId,
                PatientName: header.PatientName,
                Modality: header.Modality.ToUpperInvariant(),
                BodyPart: header.BodyPart,
                StudyDate: header.StudyDate,
                SeriesCount: header.SeriesCount,
                Region: provider.Region,            // residency comes from the provider
                ProviderId: provider.Id,
                ReferrerId: item.ReferrerId,
                Checksum: Hashing.Sha256Hex(item.Pixels),
                SizeBytes: item.Pixels.LongLength);

            _store.Save(study, item.Pixels);
            _audit.Write(new AuditEntry(_clock.GetUtcNow(), provider.Id, "INGEST_STUDY", study.Id, true, null));
            acceptedIds.Add(study.Id);
            accepted++;
        }

        return new IngestSummary(accepted, duplicates, rejected, acceptedIds);
    }

    private static string? Validate(DicomHeader h, IncomingStudy item, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(h.StudyInstanceUid)) return "missing StudyInstanceUID";
        if (string.IsNullOrWhiteSpace(h.PatientId)) return "missing PatientID";
        if (string.IsNullOrWhiteSpace(h.PatientName)) return "missing PatientName";
        if (string.IsNullOrWhiteSpace(item.ReferrerId)) return "missing referrer (from RIS order)";
        if (!AllowedModalities.Contains(h.Modality ?? string.Empty)) return $"unsupported modality '{h.Modality}'";
        if (h.StudyDate == default) return "missing study date";
        if (h.StudyDate > today) return "study date is in the future";
        if (h.SeriesCount < 1) return "series count must be at least 1";
        if (item.Pixels is null || item.Pixels.Length == 0) return "empty image payload";
        return null;
    }
}
