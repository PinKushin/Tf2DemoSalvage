using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// Where the recording client's camera was, and which way it faced, for one command.
/// </summary>
/// <param name="Flags">Engine flags for this view.</param>
/// <param name="OriginX">Camera position, X.</param>
/// <param name="OriginY">Camera position, Y.</param>
/// <param name="OriginZ">Camera position, Z.</param>
/// <param name="Pitch">View angle, pitch.</param>
/// <param name="Yaw">View angle, yaw.</param>
/// <param name="Roll">View angle, roll.</param>
/// <remarks>
/// **The only record in a demo of where the camera actually was.** Entity origins say where
/// players stood; this says what the person recording was looking at, and nothing else in the
/// file answers that. It is the 2D and 3D viewers' camera track.
///
/// Read from <c>democmdinfo_t</c>, the 76-byte block ahead of every <c>dem_signon</c> and
/// <c>dem_packet</c> payload: an <c>int32</c> of flags followed by six <c>Vector</c>s of three
/// <c>float32</c> each — view origin, view angles and local view angles, then the same three
/// again for a second split-screen slot. This record exposes the first three, which are the ones
/// TF2 fills; the rest are kept as raw bytes.
///
/// **These were being skipped.** The reader stepped over all 76 bytes to reach the payload, so
/// every demo the project has ever read discarded its camera track. Plain little-endian floats,
/// no bit packing, one per packet — the cheapest useful data in the format.
/// </remarks>
public readonly record struct ViewInfo(
    int Flags,
    float OriginX,
    float OriginY,
    float OriginZ,
    float Pitch,
    float Yaw,
    float Roll)
{
    /// <summary>Size of <c>democmdinfo_t</c> at demo protocol 3.</summary>
    public const int SizeBytes = 76;

    /// <summary>Reads the block, or returns <c>null</c> when it is not fully present.</summary>
    /// <param name="data">Bytes positioned at the start of <c>democmdinfo_t</c>.</param>
    /// <returns>The view, or <c>null</c> if fewer than <see cref="SizeBytes"/> bytes remain.</returns>
    /// <remarks>
    /// Null rather than a zeroed view when the block is short. A viewer cannot tell an invented
    /// origin from a real one at the map origin, so absence has to be representable.
    /// </remarks>
    public static ViewInfo? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < SizeBytes)
        {
            return null;
        }

        return new ViewInfo(
            BinaryPrimitives.ReadInt32LittleEndian(data),
            ReadFloat(data, 4),
            ReadFloat(data, 8),
            ReadFloat(data, 12),
            ReadFloat(data, 16),
            ReadFloat(data, 20),
            ReadFloat(data, 24));
    }

    private static float ReadFloat(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(data[offset..]));
}
