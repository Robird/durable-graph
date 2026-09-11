namespace Atelia.DurableGraph;

/// <summary>
/// Marks a user-defined reference object eligible for explicit durable model binding.
/// </summary>
/// <remarks>
/// Implementing this interface does not provide serialization operations. Generated declarations
/// still require their DurableType and field metadata, and execution requires registered bindings.
/// Only classes can be object models; implementing the marker on a struct does not permit boxing it into a graph.
/// </remarks>
public interface IDurableObject {
}
