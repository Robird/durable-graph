using System.Globalization;
using Atelia.MultiSegmentStateStoreProbe.Model;

namespace Atelia.MultiSegmentStateStoreProbe.Storage;

internal static class FileNameConvention {
    private const int DecimalWidth = 10;
    private const string Extension = ".rbf";

    public static string Format(FileNumber fileNumber) =>
        fileNumber.Value.ToString($"D{DecimalWidth}", CultureInfo.InvariantCulture) +
        Extension;

    public static FileNumber Parse(string fileName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.Length != DecimalWidth + Extension.Length ||
            !fileName.EndsWith(Extension, StringComparison.Ordinal)) {
            throw new InvalidDataException(
                $"'{fileName}' is not a canonical MultiSegment probe filename.");
        }

        ReadOnlySpan<char> digits = fileName.AsSpan(0, DecimalWidth);
        if (!uint.TryParse(
                digits,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out uint value) ||
            value == 0) {
            throw new InvalidDataException(
                $"'{fileName}' does not contain a valid 1-based UInt32 file number.");
        }

        FileNumber parsed = new(value);
        if (!string.Equals(Format(parsed), fileName, StringComparison.Ordinal)) {
            throw new InvalidDataException(
                $"'{fileName}' is not the canonical filename for file {parsed}.");
        }

        return parsed;
    }
}
