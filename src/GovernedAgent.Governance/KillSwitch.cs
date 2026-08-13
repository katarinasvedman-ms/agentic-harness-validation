namespace GovernedAgent.Governance;

public interface IKillSwitch
{
    bool IsActive { get; }

    void Activate();

    void Deactivate();
}

public sealed class InMemoryKillSwitch : IKillSwitch
{
    private int _active;

    public bool IsActive => Volatile.Read(ref _active) == 1;

    public void Activate() => Interlocked.Exchange(ref _active, 1);

    public void Deactivate() => Interlocked.Exchange(ref _active, 0);
}
