using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One joint in an IK chain.</summary>
/// <param name="Bone">Which bone this link is.</param>
/// <param name="KneeDirection">Which way the joint prefers to bend.</param>
/// <remarks>
/// **The knee direction is zero on most links and meaningful on the middle one.** A two-bone solve
/// has two solutions mirrored about the line from hip to foot, and this is what picks the one where
/// the knee does not bend backwards. A reader that dropped it would produce a leg that solves
/// correctly by every distance measure and looks broken.
/// </remarks>
public readonly record struct StudioIkLink(
    int Bone,
    (float X, float Y, float Z) KneeDirection);

/// <summary>One IK chain: a named run of joints the engine can solve as a unit.</summary>
/// <param name="Name">Its name, such as <c>rfoot</c> — how a sequence's IK rules address it.</param>
/// <param name="LinkType">What kind of solve it wants.</param>
/// <param name="Links">The joints, root first.</param>
/// <remarks>
/// **A chain is addressed by NAME from an animation's IK rules**, the same way bones are matched
/// between models — so the name is the load-bearing field rather than the index.
/// </remarks>
public readonly record struct StudioIkChain(
    string Name,
    int LinkType,
    IReadOnlyList<StudioIkLink> Links);

/// <summary>
/// The IK chains a model declares.
/// </summary>
/// <remarks>
/// **Read because <c>CalculateIKLocks</c> cannot exist without them** (B182, D88). The engine's
/// whole IK stage — <c>m_pIk-&gt;Init</c>, <c>UpdateTargets</c>, <c>CalculateIKLocks</c>,
/// <c>SolveDependencies</c> — is gated on <c>hdr-&gt;numikchains()</c>, and this project's reader
/// has never read that field, so the stage had nothing to run on rather than merely being unwired.
///
/// **This reads the TABLE, not the solve.** Having the chains is what makes an implementation
/// possible; it does not make feet plant. That is deliberate sequencing — the parser is upstream of
/// every stage, and doing it first means each later stage is a self-contained change.
/// </remarks>
public static class StudioIkChains
{
    /// <summary>Reads a model's IK chains.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The chains in file order, so a rule's chain index addresses this list directly.</returns>
    /// <exception cref="InvalidDataException">The header names more chains than it holds.</exception>
    public static IReadOnlyList<StudioIkChain> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderIkChainIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderIkChainCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderIkChainIndexOffset..]);

        if (count <= 0)
        {
            return [];
        }

        if (count > StudioReaderLimits.IkChains)
        {
            throw new InvalidDataException($"A model declares {count} IK chains.");
        }

        if (at < 0 || (long)at + ((long)count * IkChainStride) > bytes.Length)
        {
            throw new InvalidDataException(
                $"A model's {count} IK chains at {at} run past its own length of {bytes.Length}.");
        }

        List<StudioIkChain> chains = new(count);

        for (int index = 0; index < count; index++)
        {
            int chainAt = at + (index * IkChainStride);
            ReadOnlySpan<byte> chain = bytes.Slice(chainAt, IkChainStride);

            int links = BinaryPrimitives.ReadInt32LittleEndian(chain[IkChainLinkCountOffset..]);

            // **Relative to the CHAIN, like every index in this format.** Resolving it against the
            // file would land inside the header and decode counts as bone numbers.
            int linksAt = chainAt + BinaryPrimitives.ReadInt32LittleEndian(chain[IkChainLinkIndexOffset..]);

            chains.Add(new StudioIkChain(
                StudioStrings.At(
                    bytes,
                    chainAt + BinaryPrimitives.ReadInt32LittleEndian(chain[IkChainNameOffset..])),
                BinaryPrimitives.ReadInt32LittleEndian(chain[IkChainLinkTypeOffset..]),
                ReadLinks(bytes, linksAt, links)));
        }

        return chains;
    }

    /// <summary>Reads one chain's links.</summary>
    /// <remarks>
    /// **Returns empty rather than throwing when the run does not fit.** A chain whose links are out
    /// of bounds is a defect in one chain, and refusing the whole model over it would lose the other
    /// chains that are fine — the same reasoning as clamping a frame rather than refusing a
    /// sequence. The count guard above still refuses a header claiming thousands.
    /// </remarks>
    private static List<StudioIkLink> ReadLinks(
        ReadOnlySpan<byte> bytes, int at, int count)
    {
        if (count <= 0 || count > StudioReaderLimits.IkLinks ||
            at < 0 || (long)at + ((long)count * IkLinkStride) > bytes.Length)
        {
            return [];
        }

        List<StudioIkLink> links = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> link = bytes.Slice(at + (index * IkLinkStride), IkLinkStride);

            links.Add(new StudioIkLink(
                BinaryPrimitives.ReadInt32LittleEndian(link[IkLinkBoneOffset..]),
                (
                    BinaryPrimitives.ReadSingleLittleEndian(link[IkLinkKneeDirectionOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(link[(IkLinkKneeDirectionOffset + 4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(link[(IkLinkKneeDirectionOffset + 8)..]))));
        }

        return links;
    }
}
