using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One named point on a model that other entities can hang from.
/// </summary>
/// <param name="Name">What the attachment is called, such as <c>head</c> or <c>partyhat</c>.</param>
/// <param name="Flags">Its <c>ATTACHMENT_FLAG_</c> bits.</param>
/// <param name="Bone">The bone it rides, as an index into the model's own skeleton.</param>
/// <param name="Local">Twelve floats, row major: where on that bone the point sits.</param>
/// <remarks>
/// **This is the OTHER way an item rides a wearer, and the one this project has never read.** A hat
/// shares bone names with the player and is bone-merged; a halo, an MvM canteen and a spellbook do
/// not — <c>hwn_spellbook_complete.mdl</c> has a single bone named <c>mvm</c> that no player
/// skeleton has, so nothing matches and the item is placed by the wearer's transform alone, which on
/// a player is their feet (RISKS B82).
/// </remarks>
public readonly record struct StudioAttachment(
    string Name,
    uint Flags,
    int Bone,
    IReadOnlyList<float> Local)
{
    /// <summary>Whether the attachment keeps its position and discards the bone's rotation.</summary>
    /// <remarks>
    /// <c>ATTACHMENT_FLAG_WORLD_ALIGN</c>, <c>studio.h:508</c>.
    /// <c>SetupBones_AttachmentHelper</c> takes the local position through the bone and then builds
    /// an IDENTITY matrix around it, so a world-aligned attachment does not turn with what it hangs
    /// from. A halo above a head is the case: it stays level while the head looks around.
    /// </remarks>
    public bool IsWorldAligned => (Flags & WorldAlign) != 0;

    /// <summary><c>ATTACHMENT_FLAG_WORLD_ALIGN</c>.</summary>
    private const uint WorldAlign = 0x10000;

    /// <summary>Bytes per <c>mstudioattachment_t</c>.</summary>
    /// <remarks>
    /// **Ninety-two, of which the last thirty-two are padding nobody reads.** Four for the name
    /// index, four for the flags, four for the bone, forty-eight for the matrix, then
    /// <c>int unused[8]</c>. The stride is <c>sizeof()</c> rather than the sum of the interesting
    /// fields, which this repository has already paid for once — ten tests passed a wrong stride
    /// because the fixture was built from the same belief as the reader
    /// (<c>docs/memory/struct-padding-is-on-disk.md</c>).
    /// </remarks>
    private const int Stride = 92;

    /// <summary>Byte offset of <c>localbone</c> within the struct.</summary>
    private const int BoneOffset = 8;

    /// <summary>Byte offset of the twelve-float <c>local</c> matrix.</summary>
    private const int LocalOffset = 12;

    /// <summary>How many floats a <c>matrix3x4_t</c> holds.</summary>
    private const int MatrixCells = 12;

    /// <summary>Reads every attachment a model declares.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The attachments, in the model's own order.</returns>
    /// <remarks>
    /// **The caller indexes these ONE-BASED**, because the engine stores them that way:
    /// <c>SetupBones_AttachmentHelper</c> ends with <c>PutAttachment( i + 1, world )</c>, so
    /// <c>m_iParentAttachment</c> of 0 means "not attached" and 1 is the first entry here. An
    /// implementation that indexed from zero would hang every item off a real but wrong point,
    /// which looks like a placement bug rather than an off-by-one.
    ///
    /// A truncated or malformed table is read as far as it goes rather than throwing: one bad model
    /// should not stop a map loading, and an item with no attachment falls back to what it did
    /// before.
    /// </remarks>
    public static IReadOnlyList<StudioAttachment> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < StudioLayout.HeaderAttachmentIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderAttachmentCountOffset..]);

        int at = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderAttachmentIndexOffset..]);

        if (count <= 0 || at <= 0 || at >= bytes.Length)
        {
            return [];
        }

        List<StudioAttachment> attachments = [];

        for (int index = 0; index < count; index++)
        {
            int entry = at + (index * Stride);

            if (entry < 0 || entry + Stride > bytes.Length)
            {
                // Truncated: keep what was read rather than throwing away a whole model for the
                // sake of an attachment nothing may reference.
                break;
            }

            float[] local = new float[MatrixCells];

            for (int cell = 0; cell < MatrixCells; cell++)
            {
                local[cell] = BinaryPrimitives.ReadSingleLittleEndian(
                    bytes[(entry + LocalOffset + (cell * 4))..]);
            }

            attachments.Add(new StudioAttachment(
                // **The name index is relative to the attachment, not to the file.** Every studio
                // index is, which is why it is added to `entry` rather than used outright — read
                // absolutely it lands in whatever happens to sit at that offset.
                StudioStrings.At(bytes, entry + BinaryPrimitives.ReadInt32LittleEndian(bytes[entry..])),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entry + 4)..]),
                BinaryPrimitives.ReadInt32LittleEndian(bytes[(entry + BoneOffset)..]),
                local));
        }

        return attachments;
    }
}
