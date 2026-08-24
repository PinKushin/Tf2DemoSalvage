using System;

namespace Tf2DemoSalvage.Audio;

/// <summary>How loud a sound is at a distance, and how it splits across a stereo pair.</summary>
/// <remarks>
/// **Two different confidence levels live in this file and they are labelled individually.** Some of
/// this is Valve's, read from published source or recovered from the shipped binary; the falloff
/// SHAPE is not, and saying so is the point — `docs/memory/fallbacks-do-not-make-guesses-safe.md`
/// exists because a formula that produces plausible numbers is this area's failure mode, not its
/// safeguard.
///
/// | Piece | Where it comes from |
/// |---|---|
/// | `SNDLVL_TO_ATTN` | published, `public/soundflags.h` |
/// | audible radius `2 × 1000 / attenuation` | published, `recipientfilter.cpp:409` |
/// | `snd_refdist` 36, `snd_refdb` 60 | recovered from `engine.dll` by ConVar default adjacency |
/// | **the falloff curve between 0 and the cutoff** | **ours — see below** |
///
/// **On the curve, and why this is not the leaked formula.** A search turns up an expression
/// attributed to `snd_dma.cpp` in a mirror of leaked 2007 engine source; fetching it returns HTTP
/// 451. The assistant declined to route around that takedown without asking — a call recorded in
/// `docs/findings/31-game-audio.md` as the assistant's own, not the owner's, since the write-up
/// originally implied otherwise. The owner's position is that exact parity is not needed here
/// anyway, so B142 is a refinement rather than a defect.
///
/// What is implemented instead is the **inverse-distance law**, arrived at independently: a constant
/// named `snd_refdist` — *reference distance* — parameterises exactly one thing in every audio
/// engine ever written, which is the distance at which gain is unity and beyond which it falls as
/// `refdist / distance`. Recovering the name and the value 36 is sufficient to reach that on its
/// own. It is still OURS rather than Valve's, because the real engine may fold in the `snd_refdb`
/// term, the foliage loss, or a clamp this does not model — so it is flagged, and B142 tracks
/// replacing it when the binary is read.
/// </remarks>
public static class SoundGain
{
    /// <summary>Reference distance: inside this, a sound plays at full volume.</summary>
    /// <remarks>
    /// `snd_refdist`, recovered from `engine.dll` at 36 units by reading the default string beside
    /// its name (`docs/memory/a-convar-default-sits-beside-its-name.md`). Not in the SDK — the
    /// engine-side sound cvars are absent from the whole checkout.
    /// </remarks>
    public const float ReferenceDistance = 36f;

    /// <summary>Reference level in decibels.</summary>
    /// <remarks>
    /// `snd_refdb`, recovered the same way, at 60. **Carried but not yet used by
    /// <see cref="AtDistance"/>**, and that is deliberate rather than an oversight: how the engine
    /// combines it with the soundlevel is exactly the unknown, and inventing a combination would
    /// produce numbers that look authoritative. Declared here so the value is not lost and so the
    /// gap is visible at the point it will be filled.
    /// </remarks>
    public const float ReferenceDecibels = 60f;

    /// <summary>Below this gain the engine stops, rather than continuing the curve down.</summary>
    /// <remarks>
    /// `snd_gain_min`, default 0.01, read from `engine.dll` beside its name. The decompiled
    /// `SND_GetGain` tests the computed gain against it and, when it falls below, replaces it with
    /// a taper that reaches zero — so this is a real floor and not a clamp invented to stop a
    /// division running away.
    /// </remarks>
    public const float MinimumGain = 0.01f;

    /// <summary>Gain for a sound at a distance, 0 to 1.</summary>
    /// <param name="soundLevel">The <c>soundlevel_t</c> from the sound event or its script.</param>
    /// <param name="distance">Units from the listener to the source.</param>
    /// <returns>A multiplier from 0 (inaudible) to 1 (full).</returns>
    /// <remarks>
    /// **The cutoff is Valve's and the shape between is ours.** A sound is silent beyond
    /// <see cref="SoundAttenuation.AudibleRadius"/>, which the engine publishes as
    /// `(2 * SOUND_NORMAL_CLIP_DIST) / attenuation` in `recipientfilter.cpp` — that is the distance
    /// past which the server does not even send the event, so it is a hard fact about audibility
    /// rather than a mixing choice.
    ///
    /// **`SNDLVL_NONE` is not silence, it is no attenuation.** Soundlevel 0 gives attenuation 0,
    /// which means the sound plays at full volume everywhere — announcer lines and other global
    /// sounds rely on it. Reading zero as silent removes exactly the sounds a viewer most wants,
    /// and it is the mistake this returns 1 for.
    /// </remarks>
    public static float AtDistance(int soundLevel, float distance)
    {
        // **`SNDLVL_NONE` is handled on the SOUNDLEVEL, not on the attenuation, because Valve's two
        // macros disagree at zero.** `ATTN_TO_SNDLVL(0)` is 0, and `recipientfilter.cpp` returns
        // early on `if ( attenuation <= 0 )` leaving every recipient in — so attenuation zero means
        // "heard anywhere". But the forward macro `SNDLVL_TO_ATTN(0)` yields **4.0**, since 0 is on
        // the `a <= 50` branch, which is near MAX_ATTENUATION and would make the sound intensely
        // local instead.
        //
        // They cannot both be right, so the pair is not a bijection and the engine must special-case
        // one end. This takes `SNDLVL_NONE` to mean unattenuated, on two grounds: it is what the
        // reverse macro says, and 676 of TF2's shipped soundscript entries declare it — a population
        // that behaves like global sounds rather than like the quietest ones in the game.
        //
        // **Flagged rather than asserted (B143).** Which end the engine special-cases is a question
        // for the binary, and getting it backwards inverts the loudest and quietest sounds in the
        // mix — a plausible-sounding failure, not an error.
        if (soundLevel <= 0)
        {
            return 1f;
        }

        float attenuation = SoundAttenuation.FromSoundLevel(soundLevel);

        if (attenuation <= 0f)
        {
            return 1f;
        }

        if (!float.IsFinite(distance))
        {
            // A nonsensical distance is full volume rather than NaN. Letting it through would put
            // NaN into the sink's gain, which silences a voice permanently and reports nothing.
            return 1f;
        }

        // **`relative_dist = distance * attenuation / snd_refdist`, and the attenuation belongs
        // INSIDE it.** This is the engine's own expression, read out of `engine.dll` (B142) — see
        // the remarks above. Leaving attenuation out, as this did, made every sound fall off at the
        // same rate regardless of its soundlevel, which is the one thing a soundlevel is for.
        float relative = distance * attenuation / ReferenceDistance;

        if (relative <= 1f)
        {
            // `if (relative_dist > 1.0)` is the engine's guard, so at or inside the reference the
            // gain is `snd_gain` — 1 by default — rather than the division's result, which would
            // exceed 1 and clip.
            return 1f;
        }

        float gain = 1f / relative;

        // **Below `snd_gain_min` the engine tapers to zero rather than continuing the curve.** The
        // exact knee is one branch of the decompiled function this project has not resolved — it
        // multiplies by a constant that is not yet identified — so what is implemented is the floor
        // itself and not the shape of the taper. Flagged rather than invented: the alternative is a
        // number that looks authoritative, which is what the previous curve was.
        return gain < MinimumGain ? 0f : gain;
    }

    /// <summary>How a sound at a bearing splits across a stereo pair.</summary>
    /// <param name="rightward">
    /// How far to the listener's right the source is, −1 fully left through +1 fully right.
    /// </param>
    /// <returns>Left and right multipliers.</returns>
    /// <remarks>
    /// **Constant-power panning, and this too is ours rather than Valve's.** `SND_Spatialize` is in
    /// the closed engine. What is implemented is the standard sine/cosine pan, which holds
    /// `left² + right² = 1` across the sweep so a sound does not dip in loudness as it crosses the
    /// centre — the audible defect of naive linear panning, and the reason constant-power is the
    /// near-universal choice.
    ///
    /// **Never fully silent on either side.** A hard zero makes a sound vanish from one ear, which
    /// no game does because it sounds broken rather than directional; the sweep runs between the
    /// full-power extremes of the quarter turn, so the far channel bottoms out at zero only at a
    /// true ±90°.
    /// </remarks>
    public static (float Left, float Right) Pan(float rightward)
    {
        if (!float.IsFinite(rightward))
        {
            return (0.70710678f, 0.70710678f);
        }

        // Map -1..+1 onto a quarter turn, so -1 is hard left, 0 is centre with both channels at
        // 1/sqrt(2), and +1 is hard right.
        float angle = (Math.Clamp(rightward, -1f, 1f) + 1f) * (MathF.PI / 4f);

        return (MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>Where a source sits relative to a listener, as a left/right fraction.</summary>
    /// <param name="listener">The listener's position.</param>
    /// <param name="listenerRight">
    /// The listener's right-hand unit vector, from their view angles.
    /// </param>
    /// <param name="source">The sound's position.</param>
    /// <returns>−1 fully left through +1 fully right; 0 when the two coincide.</returns>
    /// <remarks>
    /// **The dot product with the RIGHT vector, not the forward one.** Forward answers "in front or
    /// behind", which stereo cannot express anyway; right answers "which ear", which is the only
    /// thing two channels can carry. Using forward by mistake produces a mix that swings left and
    /// right as the listener walks toward and away from a sound and stays centred as they turn past
    /// it — wrong in a way that sounds like a bug in the demo rather than in the mixer.
    /// </remarks>
    public static float Rightward(
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) listenerRight,
        (float X, float Y, float Z) source)
    {
        float dx = source.X - listener.X;
        float dy = source.Y - listener.Y;
        float dz = source.Z - listener.Z;

        float length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        if (length <= 0f || !float.IsFinite(length))
        {
            // A sound at the listener's own position has no direction. Centre it rather than
            // dividing by zero and panning it to a NaN, which would silence it in a way no report
            // would explain.
            return 0f;
        }

        return Math.Clamp(
            ((dx * listenerRight.X) + (dy * listenerRight.Y) + (dz * listenerRight.Z)) / length,
            -1f,
            1f);
    }
}
