namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class FrameBuilder {
    public Dictionary<uint, ObjectVersionBuilder> ObjectVersions { get; } = [];

    public ObjectVersionBuilder Add(uint objectId) {
        ObjectVersionBuilder objectVersion = new();
        ObjectVersions.Add(objectId, objectVersion);
        return objectVersion;
    }

    public Frame Build() {
        Dictionary<uint, ObjectVersion> objectVersions = new(ObjectVersions.Count);
        foreach ((uint objectId, ObjectVersionBuilder builder) in ObjectVersions) {
            objectVersions.Add(objectId, builder.Build());
        }

        return new Frame(objectVersions);
    }
}
