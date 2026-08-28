namespace Atelia.TwoLegRotationProbe.Workloads.Generation;

/// <summary>
/// Identifies an independent deterministic random lane.
/// The explicit numeric tags are part of the probe's reproducibility contract.
/// </summary>
internal enum RandomDomain : ulong {
    // ASCII "LIFECYCL", "OBJCREAT", and "OBJUPDAT" when read as big-endian bytes.
    Lifecycle = 0x4C4946454359434C,
    ObjectCreate = 0x4F424A4352454154,
    ObjectUpdate = 0x4F424A5550444154,
}
