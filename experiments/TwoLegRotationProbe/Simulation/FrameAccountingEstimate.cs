using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Simulation;

internal sealed record FrameAccountingEstimate {
    private FrameAccountingEstimate(
        AccountingScope scope,
        RbfFrameLayoutEstimate rbfLayout,
        ProvisionalRevisionV0Estimate? provisionalRevisionV0) {
        Scope = scope;
        RbfLayout = rbfLayout;
        ProvisionalRevisionV0 = provisionalRevisionV0;
    }

    public AccountingScope Scope { get; }

    public RbfFrameLayoutEstimate RbfLayout { get; }

    public ProvisionalRevisionV0Estimate? ProvisionalRevisionV0 { get; }

    public static FrameAccountingEstimate ObjectPayloadOnly(
        RbfFrameLayoutEstimate layout) =>
        new(AccountingScope.ObjectPayloadOnly, layout, provisionalRevisionV0: null);

    public static FrameAccountingEstimate Provisional(
        ProvisionalRevisionV0Estimate estimate) {
        ArgumentNullException.ThrowIfNull(estimate);
        return new(
            AccountingScope.ProvisionalRevisionV0,
            estimate.RbfLayout,
            estimate);
    }
}
