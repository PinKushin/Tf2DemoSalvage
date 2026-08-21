using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading a model's attachment points.
/// </summary>
/// <remarks>
/// **The half of item placement this project has never read.** A hat shares bone names with the
/// player and is bone-merged; a halo, a canteen and a spellbook do not, and the engine hangs those
/// off a named attachment instead. `hwn_spellbook_complete.mdl` has one bone called `mvm`, which no
/// player skeleton has — so nothing matches and the item falls back to the wearer's origin, at their
/// feet (RISKS B82).
///
/// <c>mstudioattachment_t</c>, <c>studio.h:511</c>:
///
/// <code>
/// int sznameindex; unsigned int flags; int localbone; matrix3x4_t local; int unused[8];
/// </code>
///
/// Four plus four plus four, a twelve-float matrix, then thirty-two bytes of padding — ninety-two
/// in all. **The padding is on disk and the stride is `sizeof()`, not the sum of the fields anyone
/// cares about**, which is the trap this repo has already met once
/// (<c>docs/memory/struct-padding-is-on-disk.md</c>).
/// </remarks>
public sealed class StudioAttachmentTests
{
    [Test]
    public void Read_AModelWithNoAttachments_IsEmpty()
    {
        StudioAttachment.Read(Model([])).ShouldBeEmpty();
    }

    [Test]
    public void Read_AnAttachment_CarriesItsNameBoneAndLocalMatrix()
    {
        // The three things the engine uses: which bone it hangs from, where on that bone, and the
        // name a schema or a script refers to it by.
        float[] local =
        [
            1f, 0f, 0f, 11f,
            0f, 1f, 0f, 22f,
            0f, 0f, 1f, 33f,
        ];

        IReadOnlyList<StudioAttachment> read = StudioAttachment.Read(
            Model([("head", 0u, 7, local)]));

        read.Count.ShouldBe(1);
        read[0].Name.ShouldBe("head");
        read[0].Bone.ShouldBe(7);
        read[0].Local[3].ShouldBe(11f);
        read[0].Local[7].ShouldBe(22f);
        read[0].Local[11].ShouldBe(33f);
    }

    [Test]
    public void Read_SeveralAttachments_AreStridedByNinetyTwoBytes()
    {
        // **The control for the stride.** A wrong stride still reads a first attachment correctly
        // and produces nonsense for every one after it, so a single-entry test cannot catch it.
        IReadOnlyList<StudioAttachment> read = StudioAttachment.Read(Model(
        [
            ("first", 0, 1, Identity(1f)),
            ("second", 0, 2, Identity(2f)),
            ("third", 0, 3, Identity(3f)),
        ]));

        read.Count.ShouldBe(3);
        read[1].Name.ShouldBe("second");
        read[1].Bone.ShouldBe(2);
        read[1].Local[3].ShouldBe(2f);
        read[2].Name.ShouldBe("third");
        read[2].Local[3].ShouldBe(3f);
    }

    [Test]
    public void Read_AWorldAlignedAttachment_SaysSo()
    {
        // ATTACHMENT_FLAG_WORLD_ALIGN, studio.h:508. The engine keeps such an attachment's POSITION
        // and throws its rotation away — SetupBones_AttachmentHelper builds an identity matrix and
        // sets only the column. An implementation that ignored the flag would rotate a halo with
        // the head it hangs above.
        IReadOnlyList<StudioAttachment> read = StudioAttachment.Read(
            Model([("halo", 0x10000u, 3, Identity(5f))]));

        read[0].IsWorldAligned.ShouldBeTrue();

        StudioAttachment.Read(Model([("hand", 0u, 3, Identity(5f))]))[0]
            .IsWorldAligned.ShouldBeFalse();
    }

    [Test]
    public void Read_ACountThatOverrunsTheFile_IsRefusedRatherThanRead()
    {
        // A malformed or truncated model must not be read past its end. The reader answers with
        // what it can rather than throwing, because one bad model should not stop a map loading.
        byte[] model = Model([("head", 0, 1, Identity(1f))]);

        BinaryPrimitives.WriteInt32LittleEndian(
            model.AsSpan(StudioLayout.HeaderAttachmentCountOffset), 9999);

        StudioAttachment.Read(model).Count.ShouldBeLessThan(9999);
    }

    private static float[] Identity(float x) =>
        [1f, 0f, 0f, x, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    /// <summary>A studio header carrying only an attachment table.</summary>
    private static byte[] Model(
        IReadOnlyList<(string Name, uint Flags, int Bone, float[] Local)> attachments)
    {
        const int HeaderBytes = 408;
        const int Stride = 92;

        int names = HeaderBytes + (attachments.Count * Stride);
        byte[] model = new byte[names + attachments.Sum(entry => entry.Name.Length + 1) + 8];

        BinaryPrimitives.WriteInt32LittleEndian(
            model.AsSpan(StudioLayout.HeaderAttachmentCountOffset), attachments.Count);

        BinaryPrimitives.WriteInt32LittleEndian(
            model.AsSpan(StudioLayout.HeaderAttachmentIndexOffset), HeaderBytes);

        int nameAt = names;

        for (int index = 0; index < attachments.Count; index++)
        {
            (string name, uint flags, int bone, float[] local) = attachments[index];
            int at = HeaderBytes + (index * Stride);

            // sznameindex is relative to the attachment itself, as every studio index is.
            BinaryPrimitives.WriteInt32LittleEndian(model.AsSpan(at), nameAt - at);
            BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(at + 4), flags);
            BinaryPrimitives.WriteInt32LittleEndian(model.AsSpan(at + 8), bone);

            for (int cell = 0; cell < 12; cell++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    model.AsSpan(at + 12 + (cell * 4)), local[cell]);
            }

            Encoding.ASCII.GetBytes(name).CopyTo(model.AsSpan(nameAt));
            nameAt += name.Length + 1;
        }

        return model;
    }
}
