namespace Med.Domain;

public interface IStudyStore
{
    bool Exists(string studyInstanceUid);
    void Save(Study study, byte[] payload);
    Study? Get(Guid id);
    IReadOnlyList<Study> All();

    /// <summary>Recomputes the checksum of the stored bytes and compares with the recorded one.</summary>
    bool VerifyIntegrity(Guid id);
}

public interface IAuditLog
{
    void Write(AuditEntry entry);
    IReadOnlyList<AuditEntry> All();
}
