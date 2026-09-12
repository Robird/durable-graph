using Atelia.DurableGraph.Schema;
namespace Atelia.DurableGraph.Persistence;

/// <summary>An owned raw Base body with its fully resolved historical layout.</summary>
internal sealed class DecodedBaseObjectBody {
    private readonly byte[] _body;

    internal DecodedBaseObjectBody(ObjectLayout layout, RepresentationId representationId, ReadOnlySpan<byte> body) {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;
        RepresentationId = representationId;
        _body = body.ToArray();
    }

    internal ObjectLayout Layout { get; }
    internal ObjectStateKind Kind => Layout.Kind;
    internal RepresentationId RepresentationId { get; }
    internal ReadOnlySpan<byte> Body => _body;
}
