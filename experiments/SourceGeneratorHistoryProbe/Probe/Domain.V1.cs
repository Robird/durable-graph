namespace HistoryProbe;

[Durable("probe.character", version: 1)]
internal sealed class Character {
    [Field(1)]
    private string _displayName = string.Empty;

    public string DisplayName => _displayName;
}

internal static class Program {
    private static void Main() {
        Console.WriteLine("V1 compiled");
    }
}
