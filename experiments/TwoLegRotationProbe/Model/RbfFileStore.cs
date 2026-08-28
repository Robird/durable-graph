namespace Atelia.TwoLegRotationProbe.Model;

internal sealed class RbfFileStore {
    private readonly List<RbfFile> _files = [];

    public int FileCount => _files.Count;

    public RbfFile CreateFile() {
        uint fileNumber = checked((uint)_files.Count + 1);
        RbfFile file = new(fileNumber);
        _files.Add(file);
        return file;
    }

    public RbfFile GetFile(uint fileNumber) {
        if (fileNumber == 0 || fileNumber > _files.Count) {
            throw new KeyNotFoundException($"RBF file {fileNumber} does not exist.");
        }

        return _files[checked((int)fileNumber - 1)];
    }
}
