using Atelia.TwoLegRotationProbe.Encoding;
using Atelia.TwoLegRotationProbe.Model;

namespace Atelia.TwoLegRotationProbe.Tests;

public sealed class ProvisionalRevisionV0GrammarTests {
    private static readonly RelativeFrameTicket PreviousFirstFrame = new(
        IsPreviousFile: true,
        new FrameTicket(4, 24));

    [Fact]
    public void Ovd_Base_can_have_parent_and_mixed_Self_External_bindings() {
        ProvisionalRevisionV0Input input = new(
            [
                new ProvisionalDomainRecordInput(
                    99,
                    ProvisionalDomainRecordRole.Delta,
                    SyntheticPayloadBytes: 1,
                    PreviousFirstFrame),
                new ProvisionalDomainRecordInput(
                    7,
                    ProvisionalDomainRecordRole.Base,
                    SyntheticPayloadBytes: 5,
                    DeltaParentFrameTicket: null),
            ],
            new ProvisionalObjectVersionDictionaryInput(
                ProvisionalObjectVersionDictionaryKind.Base,
                new RelativeFrameTicket(
                    IsPreviousFile: true,
                    new FrameTicket(32, 24)),
                [
                    ProvisionalObjectVersionDictionaryEntry.BindSelf(7),
                    ProvisionalObjectVersionDictionaryEntry.BindExternal(
                        3,
                        PreviousFirstFrame),
                ]));

        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(input, frameStartOffsetBytes: 4);

        Assert.Equal([7U, 99U], estimate.DomainRecords.Select(static record => record.ObjectId));
        Assert.Equal(2, estimate.DomainRecordCount);
        Assert.Equal(6, estimate.SyntheticObjectPayloadBytes);
        Assert.Equal(6, estimate.DomainRecordHeaderBytes);
        Assert.Equal(12, estimate.ObjectVersionDictionaryPayloadOffsetBytes);
        Assert.Equal(10, estimate.ObjectVersionDictionaryRecordBytes);
        Assert.Equal(6, estimate.TailMetaDirectoryBytes);
        Assert.Equal(7, estimate.AddressTokenBytes);
        Assert.Equal(22, estimate.PayloadLengthBytes);
        Assert.Equal(52, estimate.RbfLayout.FrameLengthBytes);
    }

    [Fact]
    public void Base_domain_record_has_no_Delta_parent_token_or_NoneToken_placeholder() {
        ProvisionalRevisionV0Input input = CreateInput(
            [new ProvisionalDomainRecordInput(
                7,
                ProvisionalDomainRecordRole.Base,
                SyntheticPayloadBytes: 5,
                DeltaParentFrameTicket: null)],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            [ProvisionalObjectVersionDictionaryEntry.BindSelf(7)]);

        ProvisionalDomainRecordEstimate record = Assert.Single(
            ProvisionalRevisionV0Estimator.Estimate(input, frameStartOffsetBytes: 4)
                .DomainRecords);

        Assert.Equal(0, record.DeltaParentTokenBytes);
        Assert.Equal(2, record.HeaderBytes);
        Assert.Equal(7, record.FullRecordBytes);
    }

    [Fact]
    public void Delta_domain_record_requires_and_encodes_its_exact_parent_token() {
        ProvisionalDomainRecordInput delta = new(
            7,
            ProvisionalDomainRecordRole.Delta,
            SyntheticPayloadBytes: 1,
            PreviousFirstFrame);
        ProvisionalRevisionV0Input valid = CreateInput(
            [delta],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            [ProvisionalObjectVersionDictionaryEntry.BindSelf(7)]);
        ProvisionalRevisionV0Input missingParent = CreateInput(
            [delta with { DeltaParentFrameTicket = null }],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            []);

        ProvisionalDomainRecordEstimate record = Assert.Single(
            ProvisionalRevisionV0Estimator.Estimate(valid, frameStartOffsetBytes: 4)
                .DomainRecords);

        Assert.Equal(2, record.DeltaParentTokenBytes);
        Assert.Equal(4, record.HeaderBytes);
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                missingParent,
                frameStartOffsetBytes: 4));
    }

    [Fact]
    public void Base_domain_record_rejects_a_direct_Delta_parent() {
        ProvisionalRevisionV0Input input = CreateInput(
            [new ProvisionalDomainRecordInput(
                7,
                ProvisionalDomainRecordRole.Base,
                SyntheticPayloadBytes: 5,
                DeltaParentFrameTicket: PreviousFirstFrame)],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            []);

        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(input, frameStartOffsetBytes: 4));
    }

    [Fact]
    public void Empty_Ovd_Delta_without_domain_records_is_valid() {
        ProvisionalRevisionV0Input input = new(
            [],
            new ProvisionalObjectVersionDictionaryInput(
                ProvisionalObjectVersionDictionaryKind.Delta,
                PreviousFirstFrame,
                []));

        ProvisionalRevisionV0Estimate estimate =
            ProvisionalRevisionV0Estimator.Estimate(input, frameStartOffsetBytes: 4);

        Assert.Empty(estimate.DomainRecords);
        Assert.Equal(0, estimate.ObjectVersionDictionaryPayloadOffsetBytes);
        Assert.Equal(5, estimate.ObjectVersionDictionaryRecordBytes);
        Assert.Equal(2, estimate.TailMetaDirectoryBytes);
        Assert.Equal(2, estimate.AddressTokenBytes);
    }

    [Fact]
    public void Delta_External_reserves_an_operation_byte_and_relative_ticket_token() {
        ProvisionalDomainRecordInput domainRecord = new(
            7,
            ProvisionalDomainRecordRole.Delta,
            SyntheticPayloadBytes: 1,
            PreviousFirstFrame);
        ProvisionalRevisionV0Input self = CreateInput(
            [domainRecord],
            ProvisionalObjectVersionDictionaryKind.Delta,
            PreviousFirstFrame,
            [ProvisionalObjectVersionDictionaryEntry.BindSelf(7)]);
        ProvisionalRevisionV0Input external = CreateInput(
            [domainRecord],
            ProvisionalObjectVersionDictionaryKind.Delta,
            PreviousFirstFrame,
            [ProvisionalObjectVersionDictionaryEntry.BindExternal(7, PreviousFirstFrame)]);

        ProvisionalRevisionV0Estimate selfEstimate =
            ProvisionalRevisionV0Estimator.Estimate(self, frameStartOffsetBytes: 4);
        ProvisionalRevisionV0Estimate externalEstimate =
            ProvisionalRevisionV0Estimator.Estimate(external, frameStartOffsetBytes: 4);

        Assert.Equal(
            selfEstimate.ObjectVersionDictionaryRecordBytes + 2,
            externalEstimate.ObjectVersionDictionaryRecordBytes);
        Assert.Equal(selfEstimate.AddressTokenBytes + 2, externalEstimate.AddressTokenBytes);
    }

    [Fact]
    public void Grammar_canonicalizes_both_record_and_binding_order() {
        ProvisionalRevisionV0Input descending = new(
            [
                new ProvisionalDomainRecordInput(
                    200,
                    ProvisionalDomainRecordRole.Base,
                    4,
                    null),
                new ProvisionalDomainRecordInput(
                    1,
                    ProvisionalDomainRecordRole.Base,
                    3,
                    null),
            ],
            new ProvisionalObjectVersionDictionaryInput(
                ProvisionalObjectVersionDictionaryKind.Base,
                null,
                [
                    ProvisionalObjectVersionDictionaryEntry.BindSelf(200),
                    ProvisionalObjectVersionDictionaryEntry.BindSelf(1),
                ]));
        ProvisionalRevisionV0Input ascending = new(
            descending.DomainRecords.Reverse(),
            new ProvisionalObjectVersionDictionaryInput(
                ProvisionalObjectVersionDictionaryKind.Base,
                null,
                descending.ObjectVersionDictionary.Entries.Reverse()));

        ProvisionalRevisionV0Estimate first =
            ProvisionalRevisionV0Estimator.Estimate(descending, frameStartOffsetBytes: 4);
        ProvisionalRevisionV0Estimate second =
            ProvisionalRevisionV0Estimator.Estimate(ascending, frameStartOffsetBytes: 4);

        Assert.Equal(first.DomainRecords, second.DomainRecords);
        Assert.Equal(first.PayloadLengthBytes, second.PayloadLengthBytes);
        Assert.Equal(first.TailMetaDirectoryBytes, second.TailMetaDirectoryBytes);
        Assert.Equal(first.RbfLayout, second.RbfLayout);
    }

    [Fact]
    public void Self_requires_a_same_ObjectId_domain_record() {
        ProvisionalRevisionV0Input input = CreateInput(
            domainRecords: [],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            [ProvisionalObjectVersionDictionaryEntry.BindSelf(7)]);

        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(input, frameStartOffsetBytes: 4));
    }

    [Fact]
    public void Duplicate_domain_or_Ovd_ObjectIds_fail_closed() {
        ProvisionalDomainRecordInput record = new(
            7,
            ProvisionalDomainRecordRole.Base,
            0,
            null);
        ProvisionalRevisionV0Input duplicateDomain = CreateInput(
            [record, record],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            []);
        ProvisionalDomainRecordInput delta = new(
            8,
            ProvisionalDomainRecordRole.Delta,
            1,
            PreviousFirstFrame);
        ProvisionalRevisionV0Input duplicateDeltaDomain = CreateInput(
            [delta, delta],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            []);
        ProvisionalRevisionV0Input duplicateBinding = CreateInput(
            [record],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            [
                ProvisionalObjectVersionDictionaryEntry.BindSelf(7),
                ProvisionalObjectVersionDictionaryEntry.BindSelf(7),
            ]);

        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                duplicateDomain,
                frameStartOffsetBytes: 4));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                duplicateDeltaDomain,
                frameStartOffsetBytes: 4));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                duplicateBinding,
                frameStartOffsetBytes: 4));
    }

    [Fact]
    public void Invalid_kinds_and_binding_shapes_fail_closed() {
        ProvisionalDomainRecordInput invalidRole = new(
            1,
            (ProvisionalDomainRecordRole)255,
            0,
            null);
        ProvisionalRevisionV0Input invalidOvdKind = CreateInput(
            domainRecords: [],
            (ProvisionalObjectVersionDictionaryKind)255,
            parentFrameTicket: null,
            []);
        ProvisionalRevisionV0Input parentlessDelta = CreateInput(
            [new ProvisionalDomainRecordInput(
                1,
                ProvisionalDomainRecordRole.Delta,
                1,
                null)],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            []);
        ProvisionalRevisionV0Input emptyDelta = CreateInput(
            [new ProvisionalDomainRecordInput(
                1,
                ProvisionalDomainRecordRole.Delta,
                0,
                PreviousFirstFrame)],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            []);
        ProvisionalRevisionV0Input externalWithoutTicket = CreateInput(
            domainRecords: [],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            [new ProvisionalObjectVersionDictionaryEntry(
                1,
                ProvisionalObjectVersionDictionaryBindingKind.External,
                null)]);
        ProvisionalRevisionV0Input invalidBindingKind = CreateInput(
            domainRecords: [],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            [new ProvisionalObjectVersionDictionaryEntry(
                1,
                (ProvisionalObjectVersionDictionaryBindingKind)255,
                null)]);
        ProvisionalRevisionV0Input baseRemove = CreateInput(
            domainRecords: [],
            ProvisionalObjectVersionDictionaryKind.Base,
            parentFrameTicket: null,
            [ProvisionalObjectVersionDictionaryEntry.Remove(1)]);

        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                CreateInput(
                    [invalidRole],
                    ProvisionalObjectVersionDictionaryKind.Base,
                    parentFrameTicket: null,
                    []),
                frameStartOffsetBytes: 4));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                invalidOvdKind,
                frameStartOffsetBytes: 4));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                parentlessDelta,
                frameStartOffsetBytes: 4));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                emptyDelta,
                frameStartOffsetBytes: 4));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                externalWithoutTicket,
                frameStartOffsetBytes: 4));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                invalidBindingKind,
                frameStartOffsetBytes: 4));
        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                baseRemove,
                frameStartOffsetBytes: 4));
    }

    [Fact]
    public void Frame_adapter_requires_an_explicit_runtime_Ovd() {
        FrameBuilder builder = new();
        ObjectVersionBuilder objectVersion = builder.Add(1);
        objectVersion.Kind = ObjectVersionKind.Base;
        objectVersion.PayloadBytes = 0;
        objectVersion.ReconstructionObjectPayloadBytes = 0;
        objectVersion.ResultBasePayloadBytes = 0;
        objectVersion.LogicalVersionOrdinal = 1;

        Assert.Throws<InvalidDataException>(
            () => ProvisionalRevisionV0Estimator.Estimate(
                builder.Build(),
                frameStartOffsetBytes: 4));
    }

    private static ProvisionalRevisionV0Input CreateInput(
        IEnumerable<ProvisionalDomainRecordInput> domainRecords,
        ProvisionalObjectVersionDictionaryKind ovdKind,
        RelativeFrameTicket? parentFrameTicket,
        IEnumerable<ProvisionalObjectVersionDictionaryEntry> entries) =>
        new(
            domainRecords,
            new ProvisionalObjectVersionDictionaryInput(
                ovdKind,
                parentFrameTicket,
                entries));
}
