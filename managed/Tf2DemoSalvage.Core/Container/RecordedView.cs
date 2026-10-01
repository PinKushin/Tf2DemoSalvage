using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// The camera a demo was recorded through: a packet command's <c>democmdinfo_t</c>, field for field.
/// </summary>
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
/// **All six copies and the flags are kept, because the engine reads more than the live ones** (B56).
/// <c>CDemoPlayer::InterpolateViewpoint</c> tests whether the ORIGINALS are still their constructor's
/// (<see cref="IsDefault"/>) and searches with <see cref="Reset"/> copies, which read the originals whatever
/// the flags chose. <see cref="Origin"/>, <see cref="Angles"/> and <see cref="LocalAngles"/> are the
/// SDK's accessors (<c>demoformat.h:112-136</c>), and they are what everything else reads.
///
/// **What it is FOR is the first-person camera and the recorder's own animation.** The view origin
/// and angles are the camera; the local view angles are his <c>pl.v_angle</c>, which his animation is
/// driven from (<c>c_tf_player.cpp:4279-4284</c>).
/// </remarks>
public readonly record struct RecordedView
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

    /// <summary><c>FDEMO_USE_ANGLES2</c>: the resampled view and local angles are the live ones.</summary>
    private const int UseAngles2 = 1 << 1;

    /// <summary><c>FDEMO_NOINTERP</c>: "don't interpolate between this an last view" (<c>demoformat.h:76</c>).</summary>
    private const int NoInterpolation = 1 << 2;

    /// <summary><c>flags</c>.</summary>
    public int Flags { get; init; }

    /// <summary><c>viewOrigin</c>, the original.</summary>
    public (float X, float Y, float Z) ViewOrigin { get; init; }

    /// <summary><c>viewAngles</c>, the original.</summary>
    public (float Pitch, float Yaw, float Roll) ViewAngles { get; init; }

    /// <summary><c>localViewAngles</c>, the original.</summary>
    public (float Pitch, float Yaw, float Roll) LocalViewAngles { get; init; }

    /// <summary><c>viewOrigin2</c>, the resampled copy.</summary>
    public (float X, float Y, float Z) ViewOrigin2 { get; init; }

    /// <summary><c>viewAngles2</c>, the resampled copy.</summary>
    public (float Pitch, float Yaw, float Roll) ViewAngles2 { get; init; }

    /// <summary><c>localViewAngles2</c>, the resampled copy.</summary>
    public (float Pitch, float Yaw, float Roll) LocalViewAngles2 { get; init; }

    /// <summary><c>GetViewOrigin()</c>: where the view was, in world units — the recorder's feet.</summary>
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
    /// </remarks>
    public (float X, float Y, float Z) Origin => (Flags & UseOrigin2) != 0 ? ViewOrigin2 : ViewOrigin;

    /// <summary><c>GetViewAngles()</c>: where the view looked, pitch, yaw and roll in degrees.</summary>
    public (float Pitch, float Yaw, float Roll) Angles => (Flags & UseAngles2) != 0 ? ViewAngles2 : ViewAngles;

    /// <summary><c>GetLocalViewAngles()</c>: the recorder's own <c>pl.v_angle</c>.</summary>
    /// <remarks>
    /// **The angles' flag, not a third one** (<c>demoformat.h:129-136</c>). These were "deliberately not read"
    /// until B56: the engine hands them to <c>IPrediction::SetLocalViewAngles</c>, which is <c>pl.v_angle</c>, and
    /// the local player's animation reads that rather than the server's <c>m_angEyeAngles</c>.
    /// </remarks>
    public (float Pitch, float Yaw, float Roll) LocalAngles =>
        (Flags & UseAngles2) != 0 ? LocalViewAngles2 : LocalViewAngles;

    /// <summary>Whether the engine was told not to interpolate from the previous view — a camera cut.</summary>
    public bool IsCut => (Flags & NoInterpolation) != 0;

    /// <summary>
    /// Whether this is still the constructor's <c>democmdinfo_t</c>: <c>FDEMO_NORMAL</c> and the three originals
    /// at <c>vec3_origin</c> and <c>vec3_angle</c>.
    /// </summary>
    /// <remarks>
    /// **The test <c>CDemoPlayer::InterpolateViewpoint</c> makes before setting any view**
    /// (<c>engine.dll 0x1800721e7..0x1800722a3</c>): while it holds, the local player keeps whatever it had. It
    /// compares the originals and the flags and nothing else. Each comparison is <c>UCOMISS</c> then <c>JNZ</c>
    /// with no <c>JP</c>, so an unordered NaN reads as equal to zero.
    /// </remarks>
    public bool IsDefault =>
        Flags == 0 &&
        Zero(ViewOrigin.X) && Zero(ViewOrigin.Y) && Zero(ViewOrigin.Z) &&
        Zero(ViewAngles.Pitch) && Zero(ViewAngles.Yaw) && Zero(ViewAngles.Roll) &&
        Zero(LocalViewAngles.Pitch) && Zero(LocalViewAngles.Yaw) && Zero(LocalViewAngles.Roll);

    /// <summary><c>democmdinfo_t::Reset()</c>: flags cleared, each resampled copy overwritten by its original.</summary>
    /// <returns>The reset view.</returns>
    public RecordedView Reset() => this with
    {
        Flags = 0,
        ViewOrigin2 = ViewOrigin,
        ViewAngles2 = ViewAngles,
        LocalViewAngles2 = LocalViewAngles,
    };

    /// <summary>Reads the view from a packet command's prologue.</summary>
    /// <param name="prologue">
    /// The prologue bytes. A packet's is <see cref="SizeBytes"/> plus two sequence numbers, and
    /// only the leading structure is read.
    /// </param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="prologue"/> is shorter than <see cref="SizeBytes"/>.
    /// </exception>
    public static RecordedView Parse(ReadOnlySpan<byte> prologue)
    {
        if (prologue.Length < SizeBytes)
        {
            throw new ArgumentException(
                $"A democmdinfo_t is {SizeBytes} bytes and only {prologue.Length} were given, so " +
                $"this is not a packet command's prologue.",
                nameof(prologue));
        }

        return new RecordedView
        {
            Flags = BinaryPrimitives.ReadInt32LittleEndian(prologue),
            ViewOrigin = Vector(prologue, OriginOffset),
            ViewAngles = Vector(prologue, AnglesOffset),
            LocalViewAngles = Vector(prologue, LocalAnglesOffset),
            ViewOrigin2 = Vector(prologue, Origin2Offset),
            ViewAngles2 = Vector(prologue, Angles2Offset),
            LocalViewAngles2 = Vector(prologue, LocalAngles2Offset),
        };
    }

    /// <summary>Byte offset of <c>viewOrigin</c>, straight after the flags.</summary>
    private const int OriginOffset = 4;

    /// <summary>Byte offset of <c>viewAngles</c>.</summary>
    private const int AnglesOffset = 16;

    /// <summary>Byte offset of <c>localViewAngles</c>.</summary>
    private const int LocalAnglesOffset = 28;

    /// <summary>Byte offset of <c>viewOrigin2</c>, past the three original fields.</summary>
    private const int Origin2Offset = 40;

    /// <summary>Byte offset of <c>viewAngles2</c>.</summary>
    private const int Angles2Offset = 52;

    /// <summary>Byte offset of <c>localViewAngles2</c>.</summary>
    private const int LocalAngles2Offset = 64;

    /// <summary>Reads three consecutive little-endian floats.</summary>
    private static (float, float, float) Vector(ReadOnlySpan<byte> bytes, int at) =>
        (BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 8)..]));

    /// <summary>`UCOMISS` against zero then `JNZ`: equal, or unordered.</summary>
    private static bool Zero(float value) => value.Equals(0f) || float.IsNaN(value);
}
