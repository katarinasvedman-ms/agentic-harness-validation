using System.Security.Cryptography;
using System.Text;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;

namespace GovernedAgent.Governance;

public interface IAuditChain
{
    AuditRecord Append(AuditRecord record);

    IReadOnlyList<AuditRecord> ReadAll();

    bool VerifyIntegrity();
}

public sealed class InMemoryAuditChain : IAuditChain
{
    private readonly object _sync = new();
    private readonly List<AuditRecord> _records = [];

    public AuditRecord Append(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_sync)
        {
            var previousHash = _records.Count == 0 ? null : _records[^1].RecordHash;
            var unsigned = record with
            {
                PreviousRecordHash = previousHash,
                RecordHash = string.Empty
            };
            var hash = Hash(unsigned);
            var linked = unsigned with { RecordHash = hash };
            _records.Add(linked);
            return linked;
        }
    }

    public IReadOnlyList<AuditRecord> ReadAll()
    {
        lock (_sync)
        {
            return _records.ToArray();
        }
    }

    public bool VerifyIntegrity()
    {
        lock (_sync)
        {
            string? previousHash = null;
            foreach (var record in _records)
            {
                if (!string.Equals(
                        record.PreviousRecordHash,
                        previousHash,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                var unsigned = record with { RecordHash = string.Empty };
                if (!string.Equals(record.RecordHash, Hash(unsigned), StringComparison.Ordinal))
                {
                    return false;
                }

                previousHash = record.RecordHash;
            }

            return true;
        }
    }

    private static string Hash(AuditRecord record)
    {
        var json = CanonicalJson.Serialize(record);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
