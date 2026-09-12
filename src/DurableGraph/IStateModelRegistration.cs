using Atelia.DurableGraph.Runtime;

namespace Atelia.DurableGraph;

/// <summary>Receives explicitly selected generated model families.</summary>
public interface IStateModelRegistration : IStateDefinitionRegistration {
    void Register(StateModelBinding model);
}
