using Xunit;
using Med.Domain;
using Med.Ingest;
using Med.Storage;

namespace Med.Tests;

internal sealed class FixedClock : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedClock(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>Integration level: the ACQUIRE stage with the real store, no web layer.</summary>
public class IngestTests
{
    private static readonly Provider Anz = new("prov-001", "Example Radiology", Region.ANZ);
    private static readonly Provider Eu = new("prov-eu1", "Example Imaging EU", Region.EUR);

    private readonly InMemoryStudyStore _store = new();
    private readonly InMemoryAuditLog _audit = new();
    private readonly IngestService _ingest;

    public IngestTests()
    {
        // "Today" is fixed so date rules are deterministic.
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero));
        _ingest = new IngestService(_store, _audit, clock);
    }

    private static IncomingStudy Make(
        string uid = "uid-1", string modality = "DX", DateOnly? date = null,
        byte[]? pixels = null, string referrer = "ref-001", int series = 1) =>
        new(new DicomHeader(uid, "P-1", "EXAMPLE^SAM", modality, "CHEST",
                date ?? new DateOnly(2026, 11, 5), series),
            referrer, pixels ?? new byte[] { 1, 2, 3, 4 });

    [Fact]
    public void Valid_study_is_accepted_stored_and_checksummed()
    {
        var pixels = new byte[] { 9, 8, 7 };
        var summary = _ingest.Ingest(Anz, new[] { Make(pixels: pixels) });

        Assert.Equal(1, summary.Accepted);
        var stored = Assert.Single(_store.All());
        Assert.Equal(Hashing.Sha256Hex(pixels), stored.Checksum);
        Assert.Equal(stored.Id, Assert.Single(summary.AcceptedStudyIds));
        Assert.True(_store.VerifyIntegrity(stored.Id));
    }

    [Fact]
    public void Running_the_same_feed_twice_is_idempotent()
    {
        var feed = new[] { Make("uid-a"), Make("uid-b") };
        _ingest.Ingest(Anz, feed);
        var second = _ingest.Ingest(Anz, feed);

        Assert.Equal(0, second.Accepted);
        Assert.Equal(2, second.Duplicates);
        Assert.Equal(2, _store.All().Count);
    }

    [Fact]
    public void Unsupported_modality_is_rejected_with_a_reason()
    {
        var summary = _ingest.Ingest(Anz, new[] { Make(modality: "XX") });
        var rejected = Assert.Single(summary.Rejected);
        Assert.Contains("modality", rejected.Reason);
        Assert.Empty(_store.All());
    }

    [Fact]
    public void Future_study_date_is_rejected()
    {
        var summary = _ingest.Ingest(Anz, new[] { Make(date: new DateOnly(2027, 1, 1)) });
        Assert.Contains("future", Assert.Single(summary.Rejected).Reason);
    }

    [Fact]
    public void Empty_image_payload_is_rejected()
    {
        var summary = _ingest.Ingest(Anz, new[] { Make(pixels: Array.Empty<byte>()) });
        Assert.Contains("payload", Assert.Single(summary.Rejected).Reason);
    }

    [Fact]
    public void Missing_referrer_is_rejected()
    {
        var summary = _ingest.Ingest(Anz, new[] { Make(referrer: "") });
        Assert.Contains("referrer", Assert.Single(summary.Rejected).Reason);
    }

    [Fact]
    public void Missing_study_date_is_rejected()
    {
        var summary = _ingest.Ingest(Anz, new[] { Make(date: default(DateOnly)) });
        Assert.Contains("date", Assert.Single(summary.Rejected).Reason);
    }

    [Fact]
    public void Zero_series_is_rejected()
    {
        var summary = _ingest.Ingest(Anz, new[] { Make(series: 0) });
        Assert.Single(summary.Rejected);
    }

    [Fact]
    public void Lowercase_modality_is_accepted_and_normalised()
    {
        _ingest.Ingest(Anz, new[] { Make(modality: "ct") });
        Assert.Equal("CT", Assert.Single(_store.All()).Modality);
    }

    [Fact]
    public void One_bad_study_does_not_block_the_good_ones()
    {
        var summary = _ingest.Ingest(Anz, new[] { Make("uid-bad", modality: "XX"), Make("uid-good") });
        Assert.Equal(1, summary.Accepted);
        Assert.Single(summary.Rejected);
    }

    [Fact]
    public void Region_comes_from_the_provider()
    {
        _ingest.Ingest(Eu, new[] { Make() });
        Assert.Equal(Region.EUR, Assert.Single(_store.All()).Region);
    }

    [Fact]
    public void Ingest_is_audited()
    {
        _ingest.Ingest(Anz, new[] { Make() });
        Assert.Contains(_audit.All(), e => e.Action == "INGEST_STUDY" && e.UserId == "prov-001");
    }
}
