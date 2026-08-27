using Atelia.DurableGraph;

namespace PackageConsumerProbe;

[DurableType("probe.package-character", 2)]
internal sealed partial class Character : DurableBase {
    [DurableField(1)]
    private string _displayName = string.Empty;

    [DurableField(2)]
    private bool _isActive;

    private static int UpgradeCalls { get; set; }

    public static string ExerciseGeneratedSnapshots() {
        InMemoryStateStore store = new();
        LegacySerializer legacySerializer = new();
        store.Save(
            "root",
            new LegacyCarrier { DisplayName = "V1" },
            legacySerializer);

        Character first = store.Load("root", Serializer);
        Character second = store.Load("root", Serializer);

        if (first._displayName != "V1" || !first._isActive) {
            throw new InvalidOperationException("Historical Load returned the wrong current object.");
        }

        try {
            store.SchemaStore.GetRequired("probe.package-character", 2);
            throw new InvalidOperationException("Load unexpectedly registered V2.");
        } catch (SchemaNotFoundException) {
        }

        store.Save("root", second, Serializer);
        Character current = store.Load("root", Serializer);
        return $"{current._displayName}:{current._isActive}:{UpgradeCalls}";
    }

    private static partial void UpgradeV1ToV2(
        in __DurableSnapshotV1 oldValue,
        out __DurableSnapshotV2 newValue) {
        UpgradeCalls++;
        newValue = new __DurableSnapshotV2 {
            Field1 = oldValue.Field1,
            Field2 = true,
        };
    }

    private sealed class LegacyCarrier : DurableBase {
        public string DisplayName = string.Empty;
    }

    private sealed class LegacySerializer : IDurableSerializer<LegacyCarrier> {
        public DurableSchema Schema { get; } = new(
            "probe.package-character",
            1,
            new DurableFieldInfo(1, TypeTag.String));

        public IReadOnlyDictionary<int, object?> Serialize(LegacyCarrier value) {
            return new Dictionary<int, object?> {
                [1] = value.DisplayName,
            };
        }

        public LegacyCarrier Deserialize(
            DurableSchema storedSchema,
            IReadOnlyDictionary<int, object?> fields) {
            if (!storedSchema.Equals(Schema)) {
                throw new SchemaConflictException(storedSchema, Schema);
            }

            return new LegacyCarrier {
                DisplayName = (string)fields[1]!,
            };
        }
    }
}
