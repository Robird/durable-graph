using Atelia.DurableGraph.StateStore.Storage;

namespace Atelia.DurableGraph.StateStore;

/// <summary>One private, single-use save candidate retained until publication is resolved.</summary>
internal sealed class PreparedWorldSave<TWorld> : IDisposable where TWorld : DurableBase {
    private WorldWorkspace<TWorld>? _owner;
    private readonly CaptureContext _context;
    private readonly CapturedGraph _candidate;
    private readonly NormalizedRevision _next;
    private NormalizedRevision? _installation;

    internal PreparedWorldSave(WorldWorkspace<TWorld> owner, CaptureContext context,
        CapturedGraph candidate, ObjectId worldId, StateRevision revision, NormalizedRevision next) {
        _owner = owner;
        _context = context;
        _candidate = candidate;
        WorldId = worldId;
        Revision = revision;
        _next = next;
    }

    internal ObjectId WorldId { get; }
    internal StateRevision Revision { get; }

    /// <summary>Completes the last address-dependent allocation before publishing the head.</summary>
    internal void PrepareInstall(FrameAddress address) {
        RequireOwner();
        if (_installation is not null) {
            throw new InvalidOperationException("This save already has its installation prepared.");
        }
        if (address.FileNumber == 0 || address.FrameTicket.Length == 0) {
            throw new ArgumentException("Installation requires the appended Revision address.", nameof(address));
        }
        _installation = _next.WithAddress(address);
    }

    /// <summary>Called only after confirmed publication; no field capture, callbacks or allocation.</summary>
    internal void Install() {
        WorldWorkspace<TWorld> owner = RequireOwner();
        NormalizedRevision installation = _installation
            ?? throw new InvalidOperationException("Prepare the installation before publishing and installing it.");
        owner.Install(this, _candidate, installation);
        _owner = null;
    }

    public void Dispose() {
        if (_owner is { } owner) {
            owner.Discard(this, _context);
            _owner = null;
        }
    }

    private WorldWorkspace<TWorld> RequireOwner() => _owner
        ?? throw new InvalidOperationException("The save candidate has already been resolved.");
}
