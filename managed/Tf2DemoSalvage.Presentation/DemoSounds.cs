using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

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
    /// <summary>Decode every sound a demo will ask for, before it asks.</summary>
    /// <param name="cache">Where decoded sounds live.</param>
    /// <param name="timeline">The demo, or null when none is open.</param>
    /// <param name="game">The installed game's content, or null when it is not available.</param>
    /// <param name="soundscape">Supplies the ambient tracks a map wants.</param>
    /// <param name="audio">The audio log.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    /// <remarks>
    /// **This was `MainForm.PrecacheSounds`** (B188, D90). Nothing about deciding which sounds a
    /// demo needs, or about what to do when one will not decode, is window work.
    ///
    /// **The guards are here rather than at the call site, deliberately.** "There is no demo" and
    /// "the game content is not open" are both statements about whether precaching is possible, and
    /// leaving them in the view means every future caller has to know the same two preconditions —
    /// which is how one of them eventually gets forgotten.
    ///
    /// **The exceptions are caught rather than returned**, so the logger sees the exception object
    /// and keeps its stack trace. A sound that will not decode is a defect in that file or in our
    /// reading of it, and it must not take the whole demo down with it — the same reason the caught
    /// set is narrow and named rather than `Exception`.
    /// </remarks>
    public static void Precache(
        SoundCache cache,
        DemoTimeline? timeline,
        GameContent? game,
        SoundscapeSystem soundscape,
        ILogger audio)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(soundscape);
        ArgumentNullException.ThrowIfNull(audio);

        if (timeline is null || game is null)
        {
            return;
        }

        try
        {
            // **The map's ambience is listed alongside the demo's own sounds, because no demo
            // message names it.** A soundscape's loops come from the map's `env_soundscape`
            // entities via `scripts/soundscapes.txt`, so the timeline cannot list them and the
            // first pass here missed them entirely: measured 2026-08-25, `ambient/indoors.wav`
            // still cost 103 ms in one frame after the timeline's 395 sounds were already
            // precached.
            //
            // Every soundscape in the catalog rather than the ones this recording enters — which
            // soundscape is active changes as a player walks and a seek can land anywhere, so being
            // selective would only move the hitch to the next doorway.
            PrecacheResult result = cache.Precache(
                ToPrecache(timeline.SoundsToPrecache(), soundscape));

            audio.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"precached {result.Decoded} of {result.Named} sounds " +
                    $"in {result.Seconds * 1000d:0} ms"));
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            audio.LogWarning(failure, "precaching sounds");
        }
    }

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
