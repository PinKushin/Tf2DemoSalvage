using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// The camera a demo was recorded through, from a packet command's <c>democmdinfo_t</c>.
/// </summary>
/// <param name="Origin">Where the view was, in world units.</param>
/// <param name="Angles">Where it looked: pitch, yaw and roll in degrees.</param>
/// <param name="IsCut">
/// Whether the engine was told not to interpolate from the previous view — a camera cut.
/// </param>
/// <remarks>
/// **This is the view the engine itself plays a demo through**, and it is in every packet of every
/// demo. The container reader has always consumed these 76 bytes and kept them as opaque bytes so
/// a file could be written back unchanged; that is still what happens, and this reads the same
/// bytes a second time rather than replacing them.
///
/// Keeping the raw prologue is not redundancy. A demo must round-trip byte for byte, and the
/// structure has two copies of everything with only one live — so rebuilding it from the decoded
/// view would discard whichever copy was not selected and produce a different file. The bytes are
/// the record; this is a reading of them.
///
/// **What it is FOR is the first-person camera.** A viewer with a free camera and a top-down camera
/// can show a match from outside; this is what lets it show the match as the recorder saw it, which
/// on a POV demo is the whole point of the recording.
/// </remarks>
public readonly record struct RecordedView(
    (float X, float Y, float Z) Origin,
    (float Pitch, float Yaw, float Roll) Angles,
    bool IsCut)
{
    /// <summary>Size of <c>democmdinfo_t</c> at demo protocol 3.</summary>
    /// <remarks>
    /// Four bytes of flags and six three-float structures — <c>viewOrigin</c>, <c>viewAngles</c>,
    /// <c>localViewAngles</c> and a resampled copy of each. 4 + (6 × 3 × 4) = 76, which is the
    /// same constant <see cref="DemoCommandReader"/> skips.
    /// </remarks>
    public const int SizeBytes = 76;

    /// <summary><c>FDEMO_USE_ORIGIN2</c>: the resampled origin is the live one.</summary>
    private const int UseOrigin2 = 1 << 0;

    /// <summary><c>FDEMO_USE_ANGLES2</c>: the resampled angles are the live ones.</summary>
    private const int UseAngles2 = 1 << 1;

    /// <summary><c>FDEMO_NOINTERP</c>: do not interpolate between this view and the last.</summary>
    private const int NoInterpolation = 1 << 2;

    /// <summary>Reads the view from a packet command's prologue.</summary>
    /// <param name="prologue">
    /// The prologue bytes. A packet's is <see cref="SizeBytes"/> plus two sequence numbers, and
    /// only the leading structure is read.
    /// </param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="prologue"/> is shorter than <see cref="SizeBytes"/>.
    /// </exception>
    /// <remarks>
    /// **Which copy is live is chosen per field, not per structure**, and the SDK's accessors are
    /// the specification rather than an implementation detail:
    ///
    /// <code>
    /// const Vector&amp; GetViewOrigin()
    /// {
    ///     if ( flags &amp; FDEMO_USE_ORIGIN2 ) { return viewOrigin2; }
    ///     return viewOrigin;
    /// }
    /// </code>
    ///
    /// <c>GetViewAngles</c> tests a different flag, so a reader that switched both together would
    /// agree with the engine on every demo that sets both or neither and disagree on the rest —
    /// producing a camera in the wrong place rather than an error.
    ///
    /// <c>localViewAngles</c> is deliberately not read. It is the client's own unclamped angles,
    /// which matter for input replay rather than for where the camera points, and nothing here
    /// replays input.
    /// </remarks>
    public static RecordedView Parse(ReadOnlySpan<byte> prologue)
    {
        if (prologue.Length < SizeBytes)
        {
            throw new ArgumentException(
                $"A democmdinfo_t is {SizeBytes} bytes and only {prologue.Length} were given, so " +
                $"this is not a packet command's prologue.",
                nameof(prologue));
        }

        int flags = BinaryPrimitives.ReadInt32LittleEndian(prologue);

        return new RecordedView(
            Vector(prologue, (flags & UseOrigin2) != 0 ? Origin2Offset : OriginOffset),
            Vector(prologue, (flags & UseAngles2) != 0 ? Angles2Offset : AnglesOffset),
            (flags & NoInterpolation) != 0);
    }

    /// <summary>Byte offset of <c>viewOrigin</c>, straight after the flags.</summary>
    private const int OriginOffset = 4;

    /// <summary>Byte offset of <c>viewAngles</c>.</summary>
    private const int AnglesOffset = 16;

    /// <summary>Byte offset of <c>viewOrigin2</c>, past the three original fields.</summary>
    private const int Origin2Offset = 40;

    /// <summary>Byte offset of <c>viewAngles2</c>.</summary>
    private const int Angles2Offset = 52;

    /// <summary>Reads three consecutive little-endian floats.</summary>
    private static (float, float, float) Vector(ReadOnlySpan<byte> bytes, int at) =>
        (BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 8)..]));
}
