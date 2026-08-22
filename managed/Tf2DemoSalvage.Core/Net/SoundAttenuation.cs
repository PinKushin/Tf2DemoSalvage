using System;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Turns a sound's level into an attenuation and an audible radius, as Valve publishes them.
/// </summary>
/// <remarks>
/// **Every number here is transcribed from published source, and the boundary of what is published
/// matters as much as the formulas.** The mixer that computes per-sample GAIN lives in
/// <c>engine.dll</c> and is closed. What is published is the scale, the conversion, and — because a
/// SERVER has to decide who can hear a sound before sending it — the audible radius.
///
/// <code>
/// // public/soundflags.h
/// SNDLVL_NORM = 75
/// #define SNDLVL_TO_ATTN( a ) ((a > 50) ? (20.0f / (float)(a - 50)) : 4.0)
/// #define MAX_ATTENUATION 3.98f   // attenuation * 64 in 8 bits
///
/// // game/server/recipientfilter.cpp:409
/// maxAudible = ( 2 * SOUND_NORMAL_CLIP_DIST ) / attenuation;   // const.h: 1000.0f
/// </code>
///
/// **So the radius is 2000 / attenuation**, and at <c>SNDLVL_NORM</c> that is 2500 units.
///
/// **This is a CUTOFF, not a falloff.** It says where a sound stops being audible; it says nothing
/// about the gain curve inside that radius, which is what `snd_refdist` (36) and `snd_refdb` (60)
/// parameterise and which is still unrecovered. Do not treat the radius as the curve — see
/// `docs/findings/31-game-audio.md`, where several plausible dB falloff formulas fit those two
/// constants and disagree by several dB at ordinary range.
///
/// **Found by grepping for CALLERS of a closed component rather than for the component.** The mixer
/// will never be in the SDK, but a server-side recipient filter had to reason about audible range
/// and wrote the arithmetic down. `docs/memory/nothing-is-closed.md`.
/// </remarks>
public static class SoundAttenuation
{
    /// <summary>The ordinary sound level, <c>SNDLVL_NORM</c>.</summary>
    public const int Normal = 75;

    /// <summary>Largest attenuation the wire can carry: it sends <c>attenuation * 64</c> in 8 bits.</summary>
    public const float Maximum = 3.98f;

    /// <summary>Half the audible radius at attenuation 1, <c>SOUND_NORMAL_CLIP_DIST</c>.</summary>
    /// <remarks><c>public/const.h:428</c>. The radius doubles it, which is Valve's own factor of 2.</remarks>
    public const float NormalClipDistance = 1000f;

    /// <summary>Converts a sound level to an attenuation.</summary>
    /// <param name="soundLevel">A <c>soundlevel_t</c>, 0 to 255.</param>
    /// <returns>The attenuation, as <c>SNDLVL_TO_ATTN</c> computes it.</returns>
    /// <remarks>
    /// **The 50 is a floor, not a scale factor, and the branch is why.** At or below 50 the
    /// expression would divide by zero or go negative, so Valve clamps to 4.0 — the loudest
    /// attenuation, and therefore the SHORTEST audible radius at 500 units. A level of 0 is not
    /// "heard everywhere"; that is what an attenuation of zero means, which is a different thing
    /// entirely and is handled by <see cref="AudibleRadius"/>.
    /// </remarks>
    public static float FromSoundLevel(int soundLevel) =>
        soundLevel > 50 ? 20f / (soundLevel - 50) : 4f;

    /// <summary>Converts an attenuation back to a sound level.</summary>
    /// <param name="attenuation">An attenuation.</param>
    /// <returns>The level, as <c>ATTN_TO_SNDLVL</c> computes it.</returns>
    /// <remarks>
    /// <c>(a) ? (50 + 20 / a) : 0</c>, truncated to an integer by Valve's own cast. **Not an exact
    /// inverse of <see cref="FromSoundLevel"/>**: the truncation loses the fraction, and the clamp
    /// below 50 is not reversible at all. Round-tripping a level through both is lossy by
    /// construction rather than by defect.
    /// </remarks>
    public static int ToSoundLevel(float attenuation) =>
        attenuation != 0f ? (int)(50f + (20f / attenuation)) : 0;

    /// <summary>How far away a sound at this attenuation can still be heard.</summary>
    /// <param name="attenuation">The attenuation.</param>
    /// <returns>The radius in world units, or <see cref="float.PositiveInfinity"/> for no falloff.</returns>
    /// <remarks>
    /// <c>( 2 * SOUND_NORMAL_CLIP_DIST ) / attenuation</c>, from the server's recipient filter.
    ///
    /// **Zero or below means no cropping at all**, which is the early return in Valve's `Filter`
    /// rather than a radius of zero — an attenuation of 0 is <c>ATTN_NONE</c>, a sound audible
    /// anywhere the PVS reaches. Returning infinity keeps that distinction; returning 0 would
    /// silence exactly the sounds that are meant to carry.
    /// </remarks>
    public static float AudibleRadius(float attenuation) =>
        attenuation <= 0f
            ? float.PositiveInfinity
            : 2f * NormalClipDistance / attenuation;
}
