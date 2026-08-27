using HistoryProbe.Generated;

namespace HistoryProbe;

[Durable("probe.character", version: 2)]
internal sealed class Character {
    [Field(1)]
    private string _displayName = string.Empty;

    [Field(2)]
    private bool _isActive = false;

    public string DisplayName => _displayName;

    public bool IsActive => _isActive;
}

internal static class CharacterUpgrades {
    public static CharacterSnapshotV2 UpgradeV1ToV2(
        CharacterSnapshotV1 oldValue) {
        return new CharacterSnapshotV2 {
            Field1 = oldValue.Field1,
            Field2 = true,
        };
    }
}

internal static class Program {
    private static int Main() {
        CharacterSnapshotV1 oldValue = new() {
            Field1 = "Ada",
        };
        CharacterSnapshotV2 upgraded = CharacterUpgrades.UpgradeV1ToV2(oldValue);

        if (upgraded.Field1 != "Ada" || !upgraded.Field2) {
            return 1;
        }

        Console.WriteLine("typed V1 -> V2 upgrade succeeded");
        return 0;
    }
}
