using Atelia.TwoLegRotationProbe.Model;
using Atelia.TwoLegRotationProbe.Workloads;

namespace Atelia.TwoLegRotationProbe.Encoding;

/// <summary>
/// Sizes a candidate revision using a provisional record grammar. It intentionally does not
/// write or parse bytes and therefore makes no byte-compatibility promise.
/// </summary>
internal static class ProvisionalRevisionV0Estimator {
    public static ProvisionalRevisionV0Estimate Estimate(
        Frame frame,
        SaveStep step,
        RelativeFrameTicket? parentObjectVersionDictionaryFrameTicket,
        long frameStartOffsetBytes) {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(step);

        List<ProvisionalDomainRecordEstimate> domainRecords =
            new(frame.ObjectVersions.Count);
        long syntheticObjectPayloadBytes = 0;
        long domainRecordHeaderBytes = 0;
        long payloadOffsetBytes = 0;
        long addressTokenBytes = 0;

        foreach ((uint objectId, ObjectVersion version) in
            frame.ObjectVersions.OrderBy(static pair => pair.Key)) {
            ulong parentToken = version.ParentFrameTicket is RelativeFrameTicket parent
                ? ProvisionalRelativeFrameTicketCodec.EncodeRequired(parent)
                : ProvisionalRelativeFrameTicketCodec.NoneToken;
            int parentTokenWidth = CanonicalUnsignedBase128.GetEncodedWidth(parentToken);
            long bodyLengthBytes = checked(
                1L + parentTokenWidth + version.PayloadBytes);
            int bodyLengthPrefixBytes = CanonicalUnsignedBase128.GetEncodedWidth(
                checked((ulong)bodyLengthBytes));
            int headerBytes = CheckedInt(
                checked((long)bodyLengthPrefixBytes + 1 + parentTokenWidth),
                "domain record header");
            int fullRecordBytes = CheckedInt(
                checked(bodyLengthPrefixBytes + bodyLengthBytes),
                "domain record");
            int recordOffsetBytes = CheckedInt(payloadOffsetBytes, "domain record offset");

            domainRecords.Add(new ProvisionalDomainRecordEstimate(
                objectId,
                recordOffsetBytes,
                version.PayloadBytes,
                headerBytes,
                fullRecordBytes,
                parentTokenWidth));
            syntheticObjectPayloadBytes = checked(
                syntheticObjectPayloadBytes + version.PayloadBytes);
            domainRecordHeaderBytes = checked(domainRecordHeaderBytes + headerBytes);
            payloadOffsetBytes = checked(payloadOffsetBytes + fullRecordBytes);
            addressTokenBytes = checked(addressTokenBytes + parentTokenWidth);
        }

        int ovdPayloadOffsetBytes = CheckedInt(
            payloadOffsetBytes,
            "object-version dictionary payload offset");
        bool isFirstRevision = parentObjectVersionDictionaryFrameTicket is null;
        ulong ovdParentToken = parentObjectVersionDictionaryFrameTicket is RelativeFrameTicket ovdParent
            ? ProvisionalRelativeFrameTicketCodec.EncodeRequired(ovdParent)
            : ProvisionalRelativeFrameTicketCodec.NoneToken;
        int ovdParentTokenWidth = CanonicalUnsignedBase128.GetEncodedWidth(ovdParentToken);
        long ovdBodyLengthBytes = checked(
            1L +
            ovdParentTokenWidth +
            CanonicalUnsignedBase128.GetEncodedWidth(checked((ulong)step.Changes.Count)));
        addressTokenBytes = checked(addressTokenBytes + ovdParentTokenWidth);

        if (isFirstRevision) {
            ValidateFirstRevision(frame, step);
            foreach (uint objectId in frame.ObjectVersions.Keys.Order()) {
                ovdBodyLengthBytes = checked(
                    ovdBodyLengthBytes +
                    CanonicalUnsignedBase128.GetEncodedWidth(objectId) +
                    CanonicalUnsignedBase128.GetEncodedWidth(
                        ProvisionalObjectVersionDictionaryBinding.SelfToken));
                addressTokenBytes = checked(
                    addressTokenBytes +
                    CanonicalUnsignedBase128.GetEncodedWidth(
                        ProvisionalObjectVersionDictionaryBinding.SelfToken));
            }
        } else {
            foreach (WorkloadChange change in step.Changes) {
                ovdBodyLengthBytes = checked(
                    ovdBodyLengthBytes +
                    CanonicalUnsignedBase128.GetEncodedWidth(change.ObjectId) +
                    1);
                if (change is CreateObject or UpdateObject) {
                    // In a Delta OVD entry, the one-byte upsert operation itself carries
                    // BindSelf; there is deliberately no following ticket field.
                    addressTokenBytes = checked(addressTokenBytes + 1);
                }
            }
        }

        int ovdBodyLengthPrefixBytes = CanonicalUnsignedBase128.GetEncodedWidth(
            checked((ulong)ovdBodyLengthBytes));
        int ovdRecordBytes = CheckedInt(
            checked(ovdBodyLengthPrefixBytes + ovdBodyLengthBytes),
            "object-version dictionary record");
        long payloadLengthBytes = checked(payloadOffsetBytes + ovdRecordBytes);

        long tailMetaDirectoryBytes = checked(
            CanonicalUnsignedBase128.GetEncodedWidth(checked((ulong)ovdPayloadOffsetBytes)) +
            CanonicalUnsignedBase128.GetEncodedWidth(checked((ulong)domainRecords.Count)));
        foreach (ProvisionalDomainRecordEstimate record in domainRecords) {
            tailMetaDirectoryBytes = checked(
                tailMetaDirectoryBytes +
                CanonicalUnsignedBase128.GetEncodedWidth(record.ObjectId) +
                CanonicalUnsignedBase128.GetEncodedWidth(
                    checked((ulong)record.RecordOffsetBytes)));
        }

        int payloadLength = CheckedInt(payloadLengthBytes, "revision payload");
        int tailMetaLength = CheckedInt(tailMetaDirectoryBytes, "TailMeta directory");
        RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
            frameStartOffsetBytes,
            payloadLength,
            tailMetaLength);
        if (!RbfV040Layout.IsDurableGraphRelativeStartRepresentable(layout.Ticket)) {
            throw new InvalidDataException(
                $"Candidate frame at {frameStartOffsetBytes} exceeds the DurableGraph relative-ticket range.");
        }

        return new ProvisionalRevisionV0Estimate(
            domainRecords,
            CheckedInt(syntheticObjectPayloadBytes, "synthetic object payload"),
            CheckedInt(domainRecordHeaderBytes, "domain record headers"),
            ovdRecordBytes,
            tailMetaLength,
            CheckedInt(addressTokenBytes, "address tokens"),
            ovdPayloadOffsetBytes,
            layout);
    }

    private static void ValidateFirstRevision(Frame frame, SaveStep step) {
        if (frame.ObjectVersions.Count != step.Changes.Count ||
            step.Changes.Any(static change => change is not CreateObject)) {
            throw new InvalidDataException(
                "The provisional first-revision OVD Base requires every change to be a self-bound create.");
        }
    }

    private static int CheckedInt(long value, string componentName) {
        if ((ulong)value > int.MaxValue) {
            throw new InvalidDataException(
                $"The provisional {componentName} size {value} exceeds Int32.MaxValue.");
        }

        return (int)value;
    }
}
