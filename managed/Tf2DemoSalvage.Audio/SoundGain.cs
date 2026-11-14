using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>How loud a sound is at a distance; how it splits across the ears is <see cref="SpeakerPan"/>.</summary>
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
    /// **`snd_refdist`, 36 units — a DECLARATION now rather than a constant** (D106). Not in the
    /// SDK: the engine-side sound cvars are absent from the whole checkout, so the value comes from
    /// `engine.dll`'s own ConVar registration. `SoundConVarConformanceTests` holds the provenance.
    /// </remarks>
    public static float ReferenceDistance => EngineConVars.ByName("snd_refdist").Number;

    /// <summary>Reference level in decibels.</summary>
    /// <remarks>
    /// `snd_refdb`, 60. **Carried but not yet used by <see cref="AtDistance"/>**, and that is
    /// deliberate rather than an oversight: how the engine combines it with the soundlevel is
    /// exactly the unknown, and inventing a combination would produce numbers that look
    /// authoritative. Declared so the value is not lost and the gap is visible where it will be
    /// filled.
    /// </remarks>
    public static float ReferenceDecibels => EngineConVars.ByName("snd_refdb").Number;

    /// <summary>Below this gain the engine stops, rather than continuing the curve down.</summary>
    /// <remarks>
    /// `snd_gain_min`, 0.01. The decompiled `SND_GetGain` tests the computed gain against it and,
    /// when it falls below, replaces it with a taper that reaches zero — so this is a real floor and
    /// not a clamp invented to stop a division running away.
    ///
    /// **The provenance was wrong and the number was right.** This said the value came from reading
    /// the default string beside the name in `engine.dll`. That trick works for `snd_refdist` and
    /// `snd_refdb`, whose defaults happen to be pooled immediately before their names, and it cannot
    /// work here: the four sound names are packed consecutively with no literal between them and
    /// this one's default lives in a different section. Re-read on 2026-08-27 from the ConVar
    /// registration itself, which is authoritative — and which also settles a disagreement with
    /// `tf/cvarlist.log`, where the dump prints `0`. See `SoundConVarConformanceTests`.
    /// </remarks>
    public static float MinimumGain => EngineConVars.ByName("snd_gain_min").Number;

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

    /// <summary>Whether the engine mixes a sound as music, the channels <c>snd_musicvolume</c> scales (B545).</summary>
    /// <param name="name">The wave as played, sound characters included.</param>
    /// <param name="soundLevel">Its <c>SNDLVL</c> as sent.</param>
    /// <returns>True for a <c>'#'</c> among the leading sound characters at soundlevel exactly 0.</returns>
    /// <remarks>
    /// engine.dll: FUN_18002d5c0 walks the name's leading sound characters with no length cap and sets bit 4 at
    /// +0x13c on reaching <c>'#'</c>; any other character ends the walk. FUN_1800292f0 (1800293ba) scales by
    /// <c>snd_musicvolume</c> when that bit is set and the distance multiplier is 0, which both start paths
    /// (18002e0b5, 18002eef2) write only for soundlevel 0; 256 and up takes the other gain branch. Citation:
    /// <c>SoundMusicVolumeConformanceTests</c>. Allocation-free because soundscape loops ask every frame.
    ///
    /// Those addresses are an earlier build's. In build 11191711 the walk is the first loop of FUN_18002dc10 and the
    /// bit is mask 4, bit 2, of <c>+0x13c</c>: the same walk, read again for <see cref="IsDry"/>.
    /// </remarks>
    public static bool IsMusic(string name, int soundLevel) => soundLevel == 0 && HasDryCharacter(name);

    /// <summary>Whether <c>SND_Spatialize</c> takes a channel as dry: <c>'#'</c> among its sound characters and no distance multiplier (B724).</summary>
    /// <param name="name">The wave as played, sound characters included.</param>
    /// <param name="soundLevel">Its <c>SNDLVL</c> as sent.</param>
    /// <returns>True for a <c>'#'</c> name at soundlevel 0 or 256.</returns>
    /// <remarks>
    /// engine.dll build 11191711. FUN_18002dc10's first loop walks the name from its first byte: <c>! * ?</c> (the mask
    /// <c>0x8000040200000000</c>), <c>) &lt; &gt; @ ^</c> (the mask <c>0x20000000a80001</c> from <c>')'</c>) and
    /// <c>}</c> are stepped over, <c>'#'</c> sets mask 4 of <c>+0x13c</c> and ends it, and any other byte ends it with
    /// the bit clear. Those nine and <c>'#'</c> are <see cref="SoundName.IsSoundChar"/>'s ten, with no length cap.
    /// FUN_180029440 then tests the bit with <c>dist_mult &lt;= 0</c> (<c>0x18002950a</c>).
    ///
    /// **Wider than <see cref="IsMusic"/> by one soundlevel.** Both starts set the compatibility bit and subtract 256
    /// from a soundlevel of 256 or more before they test it for 0 (FUN_18002e6e0, <c>0x18002e9f9</c>-), so 256 has no
    /// distance multiplier either: dry, and summed, but on the gain branch that never reads <c>snd_musicvolume</c>.
    /// </remarks>
    public static bool IsDry(string name, int soundLevel) => HasNoDistance(soundLevel) && HasDryCharacter(name);

    /// <summary>Whether a soundlevel leaves the channel's distance multiplier 0: 0, or 256, compatibility mode's 0.</summary>
    /// <param name="soundLevel">The <c>SNDLVL</c> as sent.</param>
    /// <returns>True at 0 and at 256.</returns>
    public static bool HasNoDistance(int soundLevel) => soundLevel is 0 or CompatibilityBase;

    /// <summary>
    /// <c>SNDLEVEL_TO_COMPATIBILITY_MODE</c>'s 256 (IEngineSound.h:40): a soundlevel at or above it asks for goldsrc
    /// attenuation, which both starts mark in bit 3 of <c>+0x13d</c>.
    /// </summary>
    private const int CompatibilityBase = 256;

    /// <summary>The walk both predicates share: a <c>'#'</c> before the first byte that is not a sound character.</summary>
    private static bool HasDryCharacter(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (char character in name)
        {
            if (character == '#')
            {
                return true;
            }

            if (!SoundName.IsSoundChar(character))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The 2 dB a local channel's weapon sound is raised by: 1.2589254 on <c>CHAN_WEAPON</c>, else 1 (B724).</summary>
    /// <param name="local">Whether the channel is the listener's own (<see cref="ChannelTraits.Local"/>).</param>
    /// <param name="channel">Its <c>CHAN_*</c>.</param>
    /// <param name="soundLevel">Its <c>SNDLVL</c> as sent.</param>
    /// <returns>The multiplier on its gain.</returns>
    /// <remarks>
    /// <c>SND_Spatialize</c> (engine.dll build 11191711, FUN_180029440): after the distance gain, a local channel whose
    /// entchannel is 1 (the test at <c>0x18002a095</c>) is multiplied by the float at <c>0x18036137c</c>. That is inside
    /// the branch for a channel without bit 3 of <c>+0x13d</c>, which a soundlevel of 256 or more sets, so a
    /// compatibility-mode sound is not raised.
    /// </remarks>
    public static float LocalWeapon(bool local, int channel, int soundLevel) =>
        local && channel == SoundScript.WeaponChannel && soundLevel < CompatibilityBase ? 1.2589254f : 1f;

    /// <summary>One channel's gain before its ears' shares: what a start plays at and every pass re-gains to.</summary>
    /// <param name="volume">The sound's own volume.</param>
    /// <param name="soundLevel">Its <c>SNDLVL</c> as sent.</param>
    /// <param name="distance">The distance its gain is taken at (<see cref="SpeakerPan.Distance"/>).</param>
    /// <param name="local">Whether the channel is the listener's own.</param>
    /// <param name="channel">Its <c>CHAN_*</c>.</param>
    /// <param name="isMusic">What <see cref="IsMusic"/> answered at its start.</param>
    /// <param name="musicVolume">`snd_musicvolume`.</param>
    /// <returns>Volume × distance gain × the local weapon's 2 dB × the music volume.</returns>
    public static float OfChannel(
        float volume, int soundLevel, float distance, bool local, int channel, bool isMusic, float musicVolume) =>
        volume * AtDistance(soundLevel, distance) * LocalWeapon(local, channel, soundLevel) * Music(isMusic, musicVolume);

    /// <summary>`snd_musicvolume`'s share of a sound's gain: the setting for music, 1 for everything else.</summary>
    /// <param name="isMusic">What <see cref="IsMusic"/> answered.</param>
    /// <param name="musicVolume">`snd_musicvolume`; NaN is silence, as a NaN `volume` is.</param>
    /// <returns>The multiplier.</returns>
    public static float Music(bool isMusic, float musicVolume)
    {
        if (!isMusic)
        {
            return 1f;
        }

        return float.IsNaN(musicVolume) ? 0f : musicVolume;
    }
}
