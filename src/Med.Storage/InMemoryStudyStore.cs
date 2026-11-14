using System.Collections.Concurrent;
using Med.Domain;

namespace Med.Storage;

/// <summary>
/// STORE stage. Stands in for long-term, region-tagged cloud storage.
/// Holds study metadata and the image bytes separately, like a real system would.
/// </summary>
public sealed class InMemoryStudyStore : IStudyStore
{
    private readonly ConcurrentDictionary<Guid, Study> _studies = new();
    private readonly ConcurrentDictionary<Guid, byte[]> _payloads = new();
    private readonly ConcurrentDictionary<string, Guid> _byUid = new();

    public bool Exists(string studyInstanceUid) => _byUid.ContainsKey(studyInstanceUid);

    public void Save(Study study, byte[] payload)
    {
        if (!_byUid.TryAdd(study.StudyInstanceUid, study.Id))
            throw new InvalidOperationException($"Study {study.StudyInstanceUid} is already stored.");

        _studies[study.Id] = study;
        _payloads[study.Id] = (byte[])payload.Clone();
    }

    public Study? Get(Guid id) => _studies.TryGetValue(id, out var s) ? s : null;

    public IReadOnlyList<Study> All() => _studies.Values.OrderBy(s => s.StudyDate).ToList();

    public bool VerifyIntegrity(Guid id) =>
        _studies.TryGetValue(id, out var study)
        && _payloads.TryGetValue(id, out var bytes)
        && Hashing.Sha256Hex(bytes) == study.Checksum;

    /// <summary>Test hook: flips a byte so integrity checks can be proven to catch corruption.</summary>
    public void CorruptPayloadForTesting(Guid id)
    {
        if (_payloads.TryGetValue(id, out var bytes) && bytes.Length > 0)
            bytes[0] ^= 0xFF;
    }
}
