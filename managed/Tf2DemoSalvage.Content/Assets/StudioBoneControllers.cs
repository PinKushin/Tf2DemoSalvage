using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One bone controller: what a networked fraction means for one bone's axis.</summary>
/// <param name="Bone">Which bone it drives.</param>
/// <param name="Type">Which axis, plus the wrapping bit.</param>
/// <param name="Start">The value an encoded 0 maps to.</param>
/// <param name="End">The value an encoded 1 maps to.</param>
/// <remarks>
/// **Neither half is usable alone, which is the whole reason this has to be read.** The demo carries
/// <c>m_flEncodedController</c> as eleven bits over 0..1 (<c>baseanimating.cpp:248</c>) — a
/// fraction with no units. The model carries what that fraction spans. <c>CalcBoneAdj</c> is the
/// multiplication between them, and without this table the wire value is a number about nothing.
/// </remarks>
public readonly record struct StudioBoneController(
    int Bone,
    int Type,
    float Start,
    float End);

/// <summary>
/// The bone controllers a model declares.
/// </summary>
/// <remarks>
/// **Worth reading even though TF2's player models declare none.** Measured 2026-08-24: every one of
/// the 474 controller slots across the heavy's 79 bones is −1, and the scout and soldier are the
/// same. So <c>CalcBoneAdj</c> is close to dead weight for the models this viewer draws today.
///
/// It is read anyway for two reasons. The table is what makes that claim CHECKABLE rather than an
/// assumption — <c>BoneFlagContentTests</c> asserts the emptiness, so a model that does use one
/// shows up as a failing test rather than as a silent wrong pose. And the parser is upstream of
/// every stage: leaving one field unread is what turned five separate pipeline stages into "not
/// merely unwired, the data is not loaded" (B182).
/// </remarks>
public static class StudioBoneControllers
{
    /// <summary>Reads a model's bone controllers.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The controllers in file order, so a bone's slot addresses this list directly.</returns>
    /// <exception cref="InvalidDataException">The header names more controllers than it holds.</exception>
    public static IReadOnlyList<StudioBoneController> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderBoneControllerIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderBoneControllerCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderBoneControllerIndexOffset..]);

        if (count <= 0)
        {
            return [];
        }

        if (count > StudioReaderLimits.BoneControllers)
        {
            throw new InvalidDataException($"A model declares {count} bone controllers.");
        }

        if (at < 0 || (long)at + ((long)count * BoneControllerStride) > bytes.Length)
        {
            throw new InvalidDataException(
                $"A model's {count} bone controllers at {at} run past its own length of {bytes.Length}.");
        }

        List<StudioBoneController> controllers = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> controller =
                bytes.Slice(at + (index * BoneControllerStride), BoneControllerStride);

            controllers.Add(new StudioBoneController(
                BinaryPrimitives.ReadInt32LittleEndian(controller[BoneControllerBoneOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(controller[BoneControllerTypeOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(controller[BoneControllerStartOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(controller[BoneControllerEndOffset..])));
        }

        return controllers;
    }
}
