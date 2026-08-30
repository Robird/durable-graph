using System.Collections.ObjectModel;
using Atelia.TwoLegRotationProbe.Planning;

namespace Atelia.TwoLegRotationProbe.Evaluation;

/// <summary>
/// Lightweight diagnostic for the actually replayed terminal settlement. It does not
/// retain candidate plans, normalized facts, or counterfactual alternatives.
/// </summary>
internal sealed class TerminalSettlementObservation {
    private readonly ReadOnlyCollection<uint> _migratedObjectIds;

    internal TerminalSettlementObservation(
        CanonicalTerminalSettlementCertificate certificate) {
        ArgumentNullException.ThrowIfNull(certificate);

        uint[] migratedObjectIds = certificate.MaintenanceStayBSteps
            .Select(step => step.Plan.Decision.UnchangedMigrationObjectIds.Single())
            .ToArray();
        if (!migratedObjectIds.SequenceEqual(
            migratedObjectIds.OrderBy(static objectId => objectId).Distinct())) {
            throw new InvalidDataException(
                "Terminal settlement migrations are not unique ascending ObjectIds.");
        }

        _migratedObjectIds = Array.AsReadOnly(migratedObjectIds);
    }

    public IReadOnlyList<uint> MigratedObjectIds => _migratedObjectIds;

    public int MaintenanceRevisionCount => _migratedObjectIds.Count;

    public int RealizedRevisionCount => checked(MaintenanceRevisionCount + 1);
}
