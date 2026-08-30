namespace Atelia.TwoLegRotationProbe.Planning;

internal abstract record CanonicalTerminalSettlementAttempt;

internal sealed record CanonicalTerminalSettlementProven(
    CanonicalTerminalSettlementCertificate Certificate) :
    CanonicalTerminalSettlementAttempt;

internal sealed record CanonicalTerminalSettlementRejectedUnproven(
    CanPrepareAndRotateRejection Rejection) :
    CanonicalTerminalSettlementAttempt;
