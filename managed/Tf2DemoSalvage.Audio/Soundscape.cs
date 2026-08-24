using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Audio;

/// <summary>One sound a soundscape plays, and how.</summary>
/// <param name="Wave">The file, as the script names it, relative to <c>sound/</c>.</param>
/// <param name="Volume">0 to 1, as written. Defaults to full when the script omits it.</param>
/// <param name="Pitch">100 is unshifted, matching every other sound path here.</param>
/// <param name="Position">
/// Which of the <c>env_soundscape</c>'s numbered positions to play from, or <c>null</c> to play at
/// the listener. A soundscape places sounds in the world through the entity that triggered it
/// rather than carrying coordinates of its own.
/// </param>
/// <param name="Attenuation">
/// How fast it falls off, in Valve's attenuation units rather than a soundlevel. Null when the
/// script omits it, which means no falloff — the sound is heard at its stated volume anywhere in
/// the soundscape.
/// </param>
/// <remarks>
/// **A soundscape's sounds are a LIST and not a map**, which is the first thing a naive KeyValues
/// reader gets wrong. `tf2.respawn_room` declares three separate `playlooping` blocks, and reading
/// them into a dictionary keyed by block name collapses all three into one — leaving the room with
/// a third of its ambience and no error.
/// </remarks>
public readonly record struct SoundscapeSound(
    string Wave,
    float Volume = 1f,
    int Pitch = 100,
    int? Position = null,
    float? Attenuation = null);

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
