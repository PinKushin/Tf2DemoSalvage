using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Audio;

/// <summary>One <c>playlooping</c> rule, as <c>ProcessPlayLooping</c> reads it (<c>c_soundscape.cpp:722-807</c>).</summary>
/// <param name="Wave">The file, as the script names it, relative to <c>sound/</c>.</param>
/// <param name="Volume">
/// The <c>volume</c> key's interval, drawn each time the soundscape starts. **Zero when the script omits it** —
/// <c>float volume = 0</c> (<c>:724</c>) — and a loop that draws zero is never started (<c>:790</c>).
/// </param>
/// <param name="Pitch">The <c>pitch</c> key's interval; <c>PITCH_NORM</c> (100) when omitted (<c>:727</c>).</param>
/// <param name="Position">
/// The <c>position</c> key through <c>atoi</c>, or <c>null</c> when omitted. A negative one plays at the listener,
/// as no position does (<c>:769-772</c>); any other names a slot of the placement's audio params.
/// </param>
/// <param name="Level">How its soundlevel is drawn — <see cref="SoundscapeLevel"/>.</param>
/// <remarks>
/// **Kept as written, not as drawn**, because the engine draws at <c>StartNewSoundscape</c> — every time a
/// soundscape starts, not once when the script loads. So these are intervals, and the mixer draws them (B462).
///
/// **A soundscape's sounds are a LIST and not a map**, which is the first thing a naive KeyValues
/// reader gets wrong. `tf2.respawn_room` declares three separate `playlooping` blocks, and reading
/// them into a dictionary keyed by block name collapses all three into one — leaving the room with
/// a third of its ambience and no error.
/// </remarks>
public readonly record struct SoundscapeSound(
    string Wave,
    Interval Volume,
    Interval Pitch,
    int? Position,
    SoundscapeLevel Level);

/// <summary>How a <c>playlooping</c> rule's soundlevel is drawn: from an attenuation, or as a level.</summary>
/// <param name="Value">The interval, or a fixed level for a <c>SNDLVL_</c> name.</param>
/// <param name="IsAttenuation">Whether <paramref name="Value"/> is an attenuation to pass through <c>ATTN_TO_SNDLVL</c>.</param>
/// <remarks>
/// **One variable, written by two keys in the order the script gives them** (<c>c_soundscape.cpp:749-763</c>):
/// <c>attenuation</c> is <c>ATTN_TO_SNDLVL( RandomInterval( ... ) )</c>; <c>soundlevel</c> is
/// <c>TextToSoundLevel</c> for a <c>SNDLVL_</c> name and <c>(int)RandomInterval( ... )</c> otherwise. Whichever came
/// last is what plays, so the reader keeps only the last.
/// </remarks>
public readonly record struct SoundscapeLevel(Interval Value, bool IsAttenuation)
{
    /// <summary>The default, <c>ATTN_TO_SNDLVL(ATTN_NORM)</c> (<c>:725</c>) — 75 by arithmetic.</summary>
    public static SoundscapeLevel Normal => new(new Interval(SoundAttenuation.NormalAttenuation, 0f), IsAttenuation: true);

    /// <summary>Draws the soundlevel, as the engine does when the soundscape starts.</summary>
    /// <param name="random">The stream a ranged value draws from.</param>
    /// <returns>The <c>soundlevel_t</c>.</returns>
    public int Draw(UniformRandomStream random)
    {
        float drawn = Value.Random(random);

        return IsAttenuation ? SoundAttenuation.ToSoundLevel(drawn) : (int)drawn;
    }
}

/// <summary>What a soundscape plays, as its script defines it.</summary>
/// <param name="Name">The section name, such as <c>tf2.respawn_room</c>.</param>
/// <param name="Dsp">
/// The room effect the engine applies while this soundscape is active, from the table at the top of
/// <c>scripts/soundscapes.txt</c> — 1 is "Generic", 19 is "Concrete Large", and so on. Recorded
/// rather than applied: reproducing Valve's DSP is a separate problem from playing the loops, and
/// keeping the number means a later implementation has it.
/// </param>
/// <param name="Looping">The sounds that play continuously while this soundscape is active.</param>
/// <param name="OtherRules">
/// The names of rules present in the script that this reader does not implement — <c>playrandom</c>
/// and <c>playsoundscape</c>. Recorded rather than dropped so a soundscape that is only partly
/// reproduced can say so, instead of sounding thin for no visible reason.
/// </param>
public sealed record Soundscape(
    string Name,
    int Dsp,
    IReadOnlyList<SoundscapeSound> Looping,
    IReadOnlyList<string> OtherRules);
