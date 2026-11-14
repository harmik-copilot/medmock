using Xunit;
using Med.Domain;
using Med.Storage;

namespace Med.Tests;

public class StoreIntegrityTests
{
    [Fact]
    public void Corrupted_payload_fails_the_integrity_check()
    {
        var store = new InMemoryStudyStore();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var study = new Study(Guid.NewGuid(), "uid-1", "P-1", "EXAMPLE^SAM", "DX", "CHEST",
            new DateOnly(2026, 11, 5), 1, Region.ANZ, "prov-001", "ref-001",
            Hashing.Sha256Hex(bytes), bytes.Length);

        store.Save(study, bytes);
        Assert.True(store.VerifyIntegrity(study.Id));

        store.CorruptPayloadForTesting(study.Id);
        Assert.False(store.VerifyIntegrity(study.Id));
    }

    [Fact]
    public void Saving_the_same_study_uid_twice_is_refused()
    {
        var store = new InMemoryStudyStore();
        var bytes = new byte[] { 1 };
        Study Make() => new(Guid.NewGuid(), "uid-dup", "P-1", "EXAMPLE^SAM", "DX", "CHEST",
            new DateOnly(2026, 11, 5), 1, Region.ANZ, "prov-001", "ref-001", Hashing.Sha256Hex(bytes), 1);

        store.Save(Make(), bytes);
        Assert.Throws<InvalidOperationException>(() => store.Save(Make(), bytes));
    }
}
