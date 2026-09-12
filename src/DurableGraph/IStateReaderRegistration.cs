using Atelia.DurableGraph.Runtime;

namespace Atelia.DurableGraph;

/// <summary>Receives generated readers for explicitly selected model families and their history.</summary>
public interface IStateReaderRegistration : IStateDefinitionRegistration {
    void Register(StateReaderBinding reader);
}
