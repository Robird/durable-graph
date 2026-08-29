using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Encoding;

/// <summary>
/// Sizes a candidate revision using a provisional record grammar. It intentionally does not
/// write or parse bytes and therefore makes no byte-compatibility promise.
/// </summary>
internal static class ProvisionalRevisionV0Estimator {
    public static ProvisionalRevisionV0Estimate Estimate(
        Frame frame,
        long frameStartOffsetBytes) {
        return Estimate(Project(frame), frameStartOffsetBytes);
    }

    /// <summary>
    /// Projects a runtime Revision Frame into the size-only provisional grammar.
    /// Runtime state remains authoritative; this projection intentionally drops semantics
    /// that do not affect the current byte estimate.
    /// </summary>
    public static ProvisionalRevisionV0Input Project(Frame frame) {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectVersionDictionary dictionary = frame.ObjectVersionDictionary
            ?? throw new InvalidDataException(
                "A provisional Revision Frame requires an explicit object-version dictionary.");

        ProvisionalDomainRecordInput[] domainRecords = frame.ObjectVersions
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new ProvisionalDomainRecordInput(
                pair.Key,
                pair.Value.Kind switch {
                    ObjectVersionKind.Base => ProvisionalDomainRecordRole.Base,
                    ObjectVersionKind.Delta => ProvisionalDomainRecordRole.Delta,
                    _ => throw new InvalidDataException(
                        $"Object {pair.Key} has unsupported version kind {pair.Value.Kind}."),
                },
                pair.Value.PayloadBytes,
                pair.Value.DeltaParentFrameTicket))
            .ToArray();
        ProvisionalObjectVersionDictionaryEntry[] ovdEntries = dictionary.Entries
            .OrderBy(static pair => pair.Key)
            .Select(static pair => pair.Value.Kind switch {
                ObjectVersionDictionaryBindingKind.Self =>
                    ProvisionalObjectVersionDictionaryEntry.BindSelf(pair.Key),
                ObjectVersionDictionaryBindingKind.External
                    when pair.Value.ExternalFrameTicket is RelativeFrameTicket external =>
                    ProvisionalObjectVersionDictionaryEntry.BindExternal(pair.Key, external),
                ObjectVersionDictionaryBindingKind.Remove =>
                    ProvisionalObjectVersionDictionaryEntry.Remove(pair.Key),
                _ => throw new InvalidDataException(
                    $"Object {pair.Key} has an invalid runtime OVD binding."),
            })
            .ToArray();

        return new ProvisionalRevisionV0Input(
            domainRecords,
            new ProvisionalObjectVersionDictionaryInput(
                dictionary.Kind switch {
                    ObjectVersionDictionaryKind.Base =>
                        ProvisionalObjectVersionDictionaryKind.Base,
                    ObjectVersionDictionaryKind.Delta =>
                        ProvisionalObjectVersionDictionaryKind.Delta,
                    _ => throw new InvalidDataException(
                        $"Unsupported runtime OVD kind {dictionary.Kind}."),
                },
                dictionary.ParentRevisionFrameTicket,
                ovdEntries));
    }

    /// <summary>The sole sizing algorithm for the provisional revision grammar.</summary>
    public static ProvisionalRevisionV0Estimate Estimate(
        ProvisionalRevisionV0Input input,
        long frameStartOffsetBytes) {
        ArgumentNullException.ThrowIfNull(input);
        if (frameStartOffsetBytes > RbfV040Layout.MaxNativeFrameStartOffsetBytes) {
            ThrowCapacity(
                RevisionCandidateCapacityLimit.TargetFrameStartNative,
                frameStartOffsetBytes,
                RbfV040Layout.MaxNativeFrameStartOffsetBytes);
        }

        ProvisionalDomainRecordInput[] canonicalDomainInputs = input.DomainRecords
            .OrderBy(static record => record.ObjectId)
            .ToArray();
        ValidateDomainRecords(canonicalDomainInputs);

        List<ProvisionalDomainRecordEstimate> domainRecords =
            new(canonicalDomainInputs.Length);
        long syntheticObjectPayloadBytes = 0;
        long domainRecordHeaderBytes = 0;
        long payloadOffsetBytes = 0;
        long addressTokenBytes = 0;

        foreach (ProvisionalDomainRecordInput record in canonicalDomainInputs) {
            int parentTokenWidth = record.Role == ProvisionalDomainRecordRole.Delta
                ? CanonicalUnsignedBase128.GetEncodedWidth(
                    EncodeReferencedFrameTicket(
                        record.DeltaParentFrameTicket!.Value))
                : 0;
            long bodyLengthBytes = checked(
                1L + parentTokenWidth + record.SyntheticPayloadBytes);
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
                record.ObjectId,
                recordOffsetBytes,
                record.SyntheticPayloadBytes,
                headerBytes,
                fullRecordBytes,
                parentTokenWidth));
            syntheticObjectPayloadBytes = checked(
                syntheticObjectPayloadBytes + record.SyntheticPayloadBytes);
            domainRecordHeaderBytes = checked(domainRecordHeaderBytes + headerBytes);
            payloadOffsetBytes = checked(payloadOffsetBytes + fullRecordBytes);
            addressTokenBytes = checked(addressTokenBytes + parentTokenWidth);
        }

        int ovdPayloadOffsetBytes = CheckedInt(
            payloadOffsetBytes,
            "object-version dictionary payload offset");
        ProvisionalObjectVersionDictionaryInput ovd = input.ObjectVersionDictionary;
        ValidateOvdKind(ovd);
        ProvisionalObjectVersionDictionaryEntry[] canonicalOvdEntries = ovd.Entries
            .OrderBy(static entry => entry.ObjectId)
            .ToArray();
        ValidateOvdEntries(canonicalOvdEntries, ovd.Kind, canonicalDomainInputs);

        ulong ovdParentToken = ovd.ParentFrameTicket is RelativeFrameTicket ovdParent
            ? EncodeReferencedFrameTicket(ovdParent)
            : ProvisionalRelativeFrameTicketCodec.NoneToken;
        int ovdParentTokenWidth = CanonicalUnsignedBase128.GetEncodedWidth(ovdParentToken);
        long ovdBodyLengthBytes = checked(
            1L +
            ovdParentTokenWidth +
            CanonicalUnsignedBase128.GetEncodedWidth(checked((ulong)canonicalOvdEntries.Length)));
        addressTokenBytes = checked(addressTokenBytes + ovdParentTokenWidth);

        foreach (ProvisionalObjectVersionDictionaryEntry entry in canonicalOvdEntries) {
            int objectIdWidth = CanonicalUnsignedBase128.GetEncodedWidth(entry.ObjectId);
            if (ovd.Kind == ProvisionalObjectVersionDictionaryKind.Base) {
                ulong bindingToken = EncodeRequiredOvdBinding(entry);
                int bindingTokenWidth = CanonicalUnsignedBase128.GetEncodedWidth(bindingToken);
                ovdBodyLengthBytes = checked(
                    ovdBodyLengthBytes + objectIdWidth + bindingTokenWidth);
                addressTokenBytes = checked(addressTokenBytes + bindingTokenWidth);
                continue;
            }

            // Delta entries always carry a one-byte operation. Self is encoded entirely
            // by that operation; External additionally carries a relative-ticket token.
            ovdBodyLengthBytes = checked(ovdBodyLengthBytes + objectIdWidth + 1);
            if (entry.BindingKind == ProvisionalObjectVersionDictionaryBindingKind.Self) {
                addressTokenBytes = checked(addressTokenBytes + 1);
            } else if (entry.BindingKind ==
                ProvisionalObjectVersionDictionaryBindingKind.External) {
                ulong bindingToken = EncodeRequiredOvdBinding(entry);
                int bindingTokenWidth = CanonicalUnsignedBase128.GetEncodedWidth(bindingToken);
                ovdBodyLengthBytes = checked(ovdBodyLengthBytes + bindingTokenWidth);
                addressTokenBytes = checked(addressTokenBytes + 1 + bindingTokenWidth);
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

        ValidateFrameEnvelope(payloadLengthBytes, tailMetaDirectoryBytes);
        int payloadLength = CheckedInt(payloadLengthBytes, "revision payload");
        int tailMetaLength = CheckedInt(tailMetaDirectoryBytes, "TailMeta directory");
        RbfFrameLayoutEstimate layout = RbfV040Layout.Estimate(
            frameStartOffsetBytes,
            payloadLength,
            tailMetaLength);
        if (!RbfV040Layout.IsDurableGraphRelativeStartRepresentable(layout.Ticket)) {
            if (frameStartOffsetBytes >
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes) {
                ThrowCapacity(
                    RevisionCandidateCapacityLimit.TargetFrameStartRelative,
                    frameStartOffsetBytes,
                    RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes);
            }

            throw new InvalidDataException(
                $"Candidate frame at {frameStartOffsetBytes} is not a valid DurableGraph relative ticket.");
        }

        foreach (ProvisionalObjectVersionDictionaryEntry entry in canonicalOvdEntries) {
            if (entry.BindingKind == ProvisionalObjectVersionDictionaryBindingKind.External &&
                entry.ExternalFrameTicket is RelativeFrameTicket external &&
                !external.IsPreviousFile &&
                external.FrameTicket == layout.Ticket) {
                throw new InvalidDataException(
                    $"Object {entry.ObjectId} explicitly aliases its containing frame; use Self instead.");
            }
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

    private static void ValidateDomainRecords(
        IReadOnlyList<ProvisionalDomainRecordInput> records) {
        for (int index = 0; index < records.Count; index++) {
            ProvisionalDomainRecordInput record = records[index];
            if (!Enum.IsDefined(record.Role)) {
                throw new InvalidDataException(
                    $"Object {record.ObjectId} has invalid domain-record role {record.Role}.");
            }

            if (record.SyntheticPayloadBytes < 0) {
                throw new InvalidDataException(
                    $"Object {record.ObjectId} has negative synthetic payload bytes.");
            }

            if (index > 0 && records[index - 1].ObjectId == record.ObjectId) {
                throw new InvalidDataException(
                    $"Domain record ObjectId {record.ObjectId} occurs more than once.");
            }

            if (record.Role == ProvisionalDomainRecordRole.Delta) {
                if (record.DeltaParentFrameTicket is null) {
                    throw new InvalidDataException(
                        $"Delta domain record {record.ObjectId} requires a parent frame ticket.");
                }

                if (record.SyntheticPayloadBytes == 0) {
                    throw new InvalidDataException(
                        $"Delta domain record {record.ObjectId} requires a positive synthetic payload.");
                }

                continue;
            }

            if (record.DeltaParentFrameTicket is not null) {
                throw new InvalidDataException(
                    $"Base domain record {record.ObjectId} cannot carry a direct Delta parent.");
            }
        }
    }

    private static void ValidateOvdKind(ProvisionalObjectVersionDictionaryInput ovd) {
        if (!Enum.IsDefined(ovd.Kind)) {
            throw new InvalidDataException(
                $"The object-version dictionary kind {ovd.Kind} is invalid.");
        }

        if (ovd.Kind == ProvisionalObjectVersionDictionaryKind.Delta &&
            ovd.ParentFrameTicket is null) {
            throw new InvalidDataException(
                "A Delta object-version dictionary requires a parent frame ticket.");
        }
    }

    private static void ValidateOvdEntries(
        IReadOnlyList<ProvisionalObjectVersionDictionaryEntry> entries,
        ProvisionalObjectVersionDictionaryKind ovdKind,
        IReadOnlyList<ProvisionalDomainRecordInput> domainRecords) {
        HashSet<uint> domainObjectIds = domainRecords
            .Select(static record => record.ObjectId)
            .ToHashSet();
        for (int index = 0; index < entries.Count; index++) {
            ProvisionalObjectVersionDictionaryEntry entry = entries[index];
            if (!Enum.IsDefined(entry.BindingKind)) {
                throw new InvalidDataException(
                    $"Object {entry.ObjectId} has invalid OVD binding kind {entry.BindingKind}.");
            }

            if (index > 0 && entries[index - 1].ObjectId == entry.ObjectId) {
                throw new InvalidDataException(
                    $"OVD entry ObjectId {entry.ObjectId} occurs more than once.");
            }

            switch (entry.BindingKind) {
                case ProvisionalObjectVersionDictionaryBindingKind.Self:
                    if (entry.ExternalFrameTicket is not null) {
                        throw new InvalidDataException(
                            $"Self-bound object {entry.ObjectId} cannot carry an external frame ticket.");
                    }

                    if (!domainObjectIds.Contains(entry.ObjectId)) {
                        throw new InvalidDataException(
                            $"Self-bound object {entry.ObjectId} has no same-ObjectId domain record.");
                    }

                    break;
                case ProvisionalObjectVersionDictionaryBindingKind.External:
                    if (entry.ExternalFrameTicket is null) {
                        throw new InvalidDataException(
                            $"Externally bound object {entry.ObjectId} requires a frame ticket.");
                    }

                    break;
                case ProvisionalObjectVersionDictionaryBindingKind.Remove:
                    if (entry.ExternalFrameTicket is not null) {
                        throw new InvalidDataException(
                            $"Removed object {entry.ObjectId} cannot carry an external frame ticket.");
                    }

                    if (ovdKind == ProvisionalObjectVersionDictionaryKind.Base) {
                        throw new InvalidDataException(
                            $"OVD Base cannot contain a Remove entry for object {entry.ObjectId}.");
                    }

                    break;
                default:
                    throw new InvalidDataException(
                        $"Object {entry.ObjectId} has unsupported OVD binding kind {entry.BindingKind}.");
            }
        }
    }

    private static ulong EncodeRequiredOvdBinding(
        ProvisionalObjectVersionDictionaryEntry entry) => entry.BindingKind switch {
            ProvisionalObjectVersionDictionaryBindingKind.Self =>
                ProvisionalObjectVersionDictionaryBinding.EncodeSelf(),
            ProvisionalObjectVersionDictionaryBindingKind.External
                when entry.ExternalFrameTicket is RelativeFrameTicket external =>
                EncodeReferencedFrameTicket(external),
            _ => throw new InvalidDataException(
                $"Object {entry.ObjectId} does not carry an encodable OVD binding."),
        };

    private static ulong EncodeReferencedFrameTicket(
        RelativeFrameTicket ticket) {
        try {
            return ProvisionalRelativeFrameTicketCodec.EncodeRequired(ticket);
        } catch (ArgumentOutOfRangeException exception) when (
            ticket.FrameTicket.OffsetBytes >
                RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes) {
            throw new RevisionCandidateCapacityException(
                new RevisionCandidateCapacityRejection(
                    RevisionCandidateCapacityLimit.ReferencedFrameTicketRelative,
                    ticket.FrameTicket.OffsetBytes,
                    RbfV040Layout.MaxDurableGraphRelativeFrameStartOffsetBytes),
                exception);
        }
    }

    private static void ValidateFrameEnvelope(
        long payloadLengthBytes,
        long tailMetaLengthBytes) {
        if (tailMetaLengthBytes > RbfV040Layout.MaxTailMetaLengthBytes) {
            ThrowCapacity(
                RevisionCandidateCapacityLimit.TailMetaLength,
                tailMetaLengthBytes,
                RbfV040Layout.MaxTailMetaLengthBytes);
        }

        long payloadAndTailMetaLengthBytes = checked(
            payloadLengthBytes + tailMetaLengthBytes);
        if (payloadAndTailMetaLengthBytes >
            RbfV040Layout.MaxPayloadAndTailMetaLengthBytes) {
            ThrowCapacity(
                RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
                payloadAndTailMetaLengthBytes,
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        }
    }

    private static int CheckedInt(long value, string componentName) {
        if (value < 0) {
            throw new InvalidDataException(
                $"The provisional {componentName} size {value} is negative.");
        }

        if (value > int.MaxValue) {
            ThrowCapacity(
                RevisionCandidateCapacityLimit.PayloadAndTailMetaLength,
                value,
                RbfV040Layout.MaxPayloadAndTailMetaLengthBytes);
        }

        return (int)value;
    }

    private static void ThrowCapacity(
        RevisionCandidateCapacityLimit limit,
        long attemptedValue,
        long maximumValue) => throw new RevisionCandidateCapacityException(
            new RevisionCandidateCapacityRejection(
                limit,
                attemptedValue,
                maximumValue));
}
