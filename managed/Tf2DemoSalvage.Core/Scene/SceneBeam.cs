using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What a <c>CBeam</c> says about itself — <c>DT_Beam</c> as <c>C_Beam</c> holds it after receive.</summary>
/// <param name="Type">
/// <c>m_nBeamType</c>: <c>BEAM_POINTS</c> 0, <c>BEAM_ENTPOINT</c> 1, <c>BEAM_ENTS</c> 2, <c>BEAM_HOSE</c> 3,
/// <c>BEAM_SPLINE</c> 4, <c>BEAM_LASER</c> 5 (`beam_shared.h:464`).
/// </param>
/// <param name="Flags"><c>m_nBeamFlags</c>, the <c>FBEAM_*</c> bits of `beam_flags.h`.</param>
/// <param name="EntityCount"><c>m_nNumBeamEnts</c> — how many of <paramref name="Ends"/> are in use.</param>
/// <param name="Ends">
/// <c>m_hAttachEntity[]</c> and <c>m_nAttachIndex[]</c>, all <c>MAX_BEAM_ENTS</c> of them, as raw handles: the
/// client dereferences an EHANDLE when it USES it, so the serial is checked at draw time, not here.
/// </param>
/// <param name="HaloIndex"><c>m_nHaloIndex</c>, a <c>modelprecache</c> index; 0 is none.</param>
/// <param name="HaloScale"><c>m_fHaloScale</c>.</param>
/// <param name="Width"><c>m_fWidth</c>.</param>
/// <param name="EndWidth"><c>m_fEndWidth</c>.</param>
/// <param name="FadeLength"><c>m_fFadeLength</c>; zero means the whole beam (`view_beams.cpp:1606`).</param>
/// <param name="Amplitude"><c>m_fAmplitude</c>, the noise scale.</param>
/// <param name="StartFrame"><c>m_fStartFrame</c>.</param>
/// <param name="Speed">
/// <c>m_fSpeed</c> AFTER <c>RecvProxy_Beam_ScrollSpeed</c>, which multiplies the wire value by 0.1
/// (`beam_shared.cpp:83-96`) — the texture scroll rate the client draws with.
/// </param>
/// <param name="FrameRate"><c>m_flFrameRate</c>.</param>
/// <param name="HdrColourScale"><c>m_flHDRColorScale</c>; the constructor's default is 1 (`beam_shared.cpp:332`).</param>
/// <param name="Frame"><c>m_flFrame</c>.</param>
/// <param name="EndPosition"><c>m_vecEndPos</c> — absolute, unless the beam has a move parent.</param>
/// <param name="MinDxLevel"><c>m_nMinDXLevel</c>; <c>C_Beam::ShouldDraw</c> refuses above the hardware's (`:1046`).</param>
/// <remarks>
/// **A NOBASE table, so nothing on it is <c>DT_BaseEntity</c>'s** (`beam_shared.cpp:147`). It declares its own
/// <c>m_nModelIndex</c>, <c>m_vecOrigin</c>, <c>moveparent</c>, <c>m_clrRender</c>, <c>m_nRenderFX</c> and
/// <c>m_nRenderMode</c>; those reach the entity's ordinary members through <see cref="EntityState"/>, so only
/// the beam's own fields live here.
///
/// **Measured on 49 lcor and 10 gcor demos, every one of 18,942 beams is the same thing**: a
/// <c>point_spotlight</c>'s shaft — <c>BEAM_POINTS</c>, <c>FBEAM_SHADEOUT | FBEAM_NOTILE</c>, model
/// <c>sprites/glow_test02.vmt</c>, halo <c>sprites/light_glow03.vmt</c>, no noise, no scroll, no move parent
/// (`entity-census`, docs/findings/73). The other types are ported anyway; the census is what says which
/// branch the corpus exercises.
/// </remarks>
public sealed record SceneBeam(
    int Type,
    int Flags,
    int EntityCount,
    IReadOnlyList<(int Handle, int Attachment)> Ends,
    int HaloIndex,
    float HaloScale,
    float Width,
    float EndWidth,
    float FadeLength,
    float Amplitude,
    float StartFrame,
    float Speed,
    float FrameRate,
    float HdrColourScale,
    float Frame,
    (float X, float Y, float Z) EndPosition,
    int MinDxLevel)
{
    /// <summary><c>MAX_BEAM_ENTS</c> — the length of both attach arrays (`shareddefs.h`).</summary>
    public const int MaximumEnds = 10;

    /// <summary>The halo sprite's path, resolved through <c>modelprecache</c>; null when there is none.</summary>
    public string? HaloPath { get; init; }

    /// <inheritdoc/>
    /// <remarks>
    /// **By value, list included**, because a track collapses a repeated pose by comparing it with the last one and a
    /// list compared by reference would make every update a new keyframe.
    /// </remarks>
    public bool Equals(SceneBeam? other) =>
        other is not null &&
        Type == other.Type && Flags == other.Flags && EntityCount == other.EntityCount &&
        Ends.SequenceEqual(other.Ends) &&
        HaloIndex == other.HaloIndex && string.Equals(HaloPath, other.HaloPath, StringComparison.Ordinal) &&
        Same(HaloScale, other.HaloScale) && Same(Width, other.Width) && Same(EndWidth, other.EndWidth) &&
        Same(FadeLength, other.FadeLength) && Same(Amplitude, other.Amplitude) &&
        Same(StartFrame, other.StartFrame) && Same(Speed, other.Speed) && Same(FrameRate, other.FrameRate) &&
        Same(HdrColourScale, other.HdrColourScale) && Same(Frame, other.Frame) &&
        Same(EndPosition.X, other.EndPosition.X) && Same(EndPosition.Y, other.EndPosition.Y) &&
        Same(EndPosition.Z, other.EndPosition.Z) && MinDxLevel == other.MinDxLevel;

    /// <summary>Two received floats are the same value bit for bit — a repeat of a networked value, not a nearness.</summary>
    private static bool Same(float first, float second) =>
        BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Type, Flags, EntityCount, HaloIndex, Width, EndPosition);
}
