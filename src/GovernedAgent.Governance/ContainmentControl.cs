using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public enum ContainmentMode
{
    Operational,
    Contained,
    ReadOnlyRecovery
}

public sealed record ReattestationArtifact(
    string ArtifactDigest,
    string KnownGoodVersion,
    string ActorId,
    DateTimeOffset AttestedAt);

public sealed record RecoverySignOff(
    string ActorId,
    string RootCause,
    DateTimeOffset SignedAt);

public interface IContainmentExecutionAdmission : IDisposable
{
}

public interface IContainmentControl
{
    ContainmentMode Mode { get; }

    ReattestationArtifact? Reattestation { get; }

    RecoverySignOff? SignOff { get; }

    void Contain();

    void BeginReadOnlyRecovery(ReattestationArtifact artifact);

    void RestoreOperational(RecoverySignOff signOff);

    bool TryBeginExecution(
        EffectKind effect,
        out IContainmentExecutionAdmission? admission,
        out ContainmentMode observedMode);
}

public sealed class InMemoryContainmentControl : IContainmentControl
{
    private readonly object _sync = new();
    private ContainmentMode _mode;
    private ReattestationArtifact? _reattestation;
    private RecoverySignOff? _signOff;
    private int _activeExecutions;

    public ContainmentMode Mode
    {
        get
        {
            lock (_sync)
            {
                return _mode;
            }
        }
    }

    public ReattestationArtifact? Reattestation
    {
        get
        {
            lock (_sync)
            {
                return _reattestation;
            }
        }
    }

    public RecoverySignOff? SignOff
    {
        get
        {
            lock (_sync)
            {
                return _signOff;
            }
        }
    }

    public void Contain()
    {
        lock (_sync)
        {
            _mode = ContainmentMode.Contained;
            _reattestation = null;
            _signOff = null;
        }
    }

    public void BeginReadOnlyRecovery(ReattestationArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ValidateRequired(artifact.ArtifactDigest, nameof(artifact.ArtifactDigest));
        ValidateRequired(artifact.KnownGoodVersion, nameof(artifact.KnownGoodVersion));
        ValidateRequired(artifact.ActorId, nameof(artifact.ActorId));

        lock (_sync)
        {
            if (_mode != ContainmentMode.Contained)
            {
                throw new InvalidOperationException(
                    "Read-only recovery can begin only from the contained state.");
            }

            _reattestation = artifact;
            _mode = ContainmentMode.ReadOnlyRecovery;
        }
    }

    public void RestoreOperational(RecoverySignOff signOff)
    {
        ArgumentNullException.ThrowIfNull(signOff);
        ValidateRequired(signOff.ActorId, nameof(signOff.ActorId));
        ValidateRequired(signOff.RootCause, nameof(signOff.RootCause));

        lock (_sync)
        {
            if (_mode != ContainmentMode.ReadOnlyRecovery || _reattestation is null)
            {
                throw new InvalidOperationException(
                    "Operational access can be restored only after re-attestation.");
            }

            _signOff = signOff;
            _mode = ContainmentMode.Operational;
        }
    }

    public bool TryBeginExecution(
        EffectKind effect,
        out IContainmentExecutionAdmission? admission,
        out ContainmentMode observedMode)
    {
        lock (_sync)
        {
            observedMode = _mode;
            if (_mode == ContainmentMode.Contained ||
                (_mode == ContainmentMode.ReadOnlyRecovery && effect != EffectKind.Read))
            {
                admission = null;
                return false;
            }

            _activeExecutions++;
            admission = new ExecutionAdmission(this);
            return true;
        }
    }

    private void EndExecution()
    {
        lock (_sync)
        {
            _activeExecutions--;
        }
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A non-empty value is required.",
                parameterName);
        }
    }

    private sealed class ExecutionAdmission(
        InMemoryContainmentControl owner) : IContainmentExecutionAdmission
    {
        private InMemoryContainmentControl? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndExecution();
        }
    }
}
