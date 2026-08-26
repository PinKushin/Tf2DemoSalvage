using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Every sound a demo will ask for, worked out before playback starts.</summary>
/// <remarks>
/// **This was the interesting half of <c>MainForm.PrecacheSounds</c>** (B188, D90) — a question
/// about a demo and an install, asked from a window that had neither to do with it. It is
/// <c>DemoModels.ToPack</c>'s sibling and shares its reasoning.
///
/// **Two sources, and the second is easy to forget.** The timeline names every sound the recording
/// plays; the soundscape catalog names the AMBIENCE, which no timeline mentions because a soundscape
/// is chosen by the server and reaches the client as an index in private per-player data that a
/// SourceTV recording carries for nobody (B173). A precache built from the timeline alone therefore
/// decodes every ambient loop mid-playback, which is exactly the stall precaching exists to remove.
///
/// **Valve has no equivalent, and that is worth stating rather than leaving as a silence.** Source
/// precaches from ENTITIES — each one calls `PrecacheScriptSound` during its own precache — because
/// a running game does not know the future. A demo does: the whole sound list is on disk before the
/// first frame, which is a better list than the engine can build and the reason this exists at all.
///
/// **A gap found while checking that, and NOT closed here:** `CSoundEmitterSystem::LevelInitPreEntity`
/// (`SoundEmitterSystem.cpp:258`) loads per-map sound OVERRIDES — `scripts/level_sounds.txt`, and
/// `scripts/mvm_level_sounds.txt` on an MvM map — through `AddSoundOverrides`. This project reads
/// neither, so a map that redefines a sound gets the global one. Filed rather than fixed: it changes
/// what is heard, which wants its own measurement.
/// </remarks>
public static class DemoSounds
{
    /// <summary>The names to decode before playback, from the demo and from the map's ambience.</summary>
    /// <param name="fromDemo">What the timeline says the recording plays.</param>
    /// <param name="soundscape">The ambience system, for its catalog.</param>
    /// <returns>The union, which is what a precache should decode.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Takes the demo's LIST rather than the demo**, because a `DemoTimeline` can only be built
    /// from a real file — its constructor is private and `Build` takes bytes. A function that
    /// demanded one could not be tested without shipping a demo into this project's fixtures, and
    /// the union is the whole of what this decides.
    /// </remarks>
    public static IEnumerable<string> ToPrecache(
        IEnumerable<string> fromDemo, SoundscapeSystem soundscape)
    {
        ArgumentNullException.ThrowIfNull(fromDemo);
        ArgumentNullException.ThrowIfNull(soundscape);

        return fromDemo.Concat(soundscape.Catalog?.WaveNames() ?? []);
    }
}
