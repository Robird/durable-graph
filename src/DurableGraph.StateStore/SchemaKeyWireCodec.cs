using Atelia.DurableGraph.StateStore.Serialization;

namespace Atelia.DurableGraph.StateStore;

internal static class SchemaKeyWireCodec {
    internal static void Write(ref BinaryPayloadWriter writer, SchemaKey key) {
        key.Validate();
        writer.WriteString(key.SchemaId);
        writer.WriteUInt32((uint)key.Version);
    }

    internal static SchemaKey Read(ref BinaryPayloadReader reader) {
        string id = reader.ReadString();
        uint version = reader.ReadUInt32();
        if (string.IsNullOrWhiteSpace(id) || version is 0 or > int.MaxValue) {
            throw new InvalidDataException("Schema keys require a nonblank identity and a positive Int32 version.");
        }
        return new SchemaKey(id, (int)version);
    }
}
