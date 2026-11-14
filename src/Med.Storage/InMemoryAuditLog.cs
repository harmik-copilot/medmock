using Med.Domain;

namespace Med.Storage;

/// <summary>Append-only audit trail. A real one would be durable and tamper-evident.</summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _gate = new();

    public void Write(AuditEntry entry)
    {
        lock (_gate) _entries.Add(entry);
    }

    public IReadOnlyList<AuditEntry> All()
    {
        lock (_gate) return _entries.ToList();
    }
}
