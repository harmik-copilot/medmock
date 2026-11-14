using System.Text;
using Med.Domain;

namespace Med.Ingest.Simulators;

/// <summary>
/// Synthetic data only. No real patient information is used anywhere in this repository.
/// prov-001 is an ANZ clinic; prov-eu1 is a European clinic (used for residency tests).
/// </summary>
public sealed class FakePacsSource : IPacsSource
{
    public IReadOnlyList<IncomingStudy> Fetch(string providerId) => providerId switch
    {
        "prov-001" => new[]
        {
            Make("1.2.999.1001", "P-1001", "EXAMPLE^SAM",  "DX", "CHEST", new DateOnly(2026, 11, 5), 1, "ref-001"),
            Make("1.2.999.1002", "P-1002", "MANTER^CHRIS", "MR", "KNEE",  new DateOnly(2026, 11, 9), 3, "ref-002"),
        },
        "prov-eu1" => new[]
        {
            Make("1.2.999.2001", "P-2001", "NOVAK^EVA", "CT", "HEAD", new DateOnly(2026, 11, 7), 2, "ref-eu-1"),
        },
        _ => Array.Empty<IncomingStudy>()
    };

    private static IncomingStudy Make(string uid, string patientId, string name, string modality,
        string bodyPart, DateOnly date, int series, string referrerId)
    {
        var header = new DicomHeader(uid, patientId, name, modality, bodyPart, date, series);
        var pixels = Encoding.UTF8.GetBytes($"synthetic-pixel-data-for-{uid}");
        return new IncomingStudy(header, referrerId, pixels);
    }
}
