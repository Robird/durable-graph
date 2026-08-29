using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Rotation;

/// <summary>
/// Pure witness for one preparatory Base-migration Revision appended within Current file B.
/// </summary>
internal sealed class PreparatoryBaseMigrationPlan {
    private readonly ReadOnlyCollection<uint> _relocatedObjectIds;

    internal PreparatoryBaseMigrationPlan(
        uint currentFileNumber,
        AbsoluteFrameAddress publishedRevisionAddress,
        PlannedRevisionV0 migrationRevision) {
        ArgumentNullException.ThrowIfNull(migrationRevision);

        CurrentFileNumber = currentFileNumber;
        PublishedRevisionAddress = publishedRevisionAddress;
        MigrationRevision = migrationRevision;
        _relocatedObjectIds = Array.AsReadOnly(
            migrationRevision.Frame.ObjectVersions.Keys.Order().ToArray());
    }

    public uint CurrentFileNumber { get; }

    public AbsoluteFrameAddress PublishedRevisionAddress { get; }

    public IReadOnlyList<uint> RelocatedObjectIds => _relocatedObjectIds;

    public PlannedRevisionV0 MigrationRevision { get; }
}
