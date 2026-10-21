using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
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
    /// <param name="emitted">
    /// What the client emits itself — every explosion's and bullet landing's sound, from
    /// <see cref="DemoSystems.AddEffectSounds(GameContent?, IReadOnlyList{BulletLanding})"/> — which
    /// the timeline cannot list because no demo message names it. Empty until the schedule has them.
    /// </param>
    /// <param name="scripts">The loaded scripts, whose every wave a script sound may deal is precached; null for none.</param>
    /// <param name="modelEvents">The packed models' animation events, whose sounds are precached with the model (<see cref="ModelWaves"/>).</param>
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
    /// set is narrow and named rather than `Exception`. A failure costs the precache and nothing
    /// else: anything missed is decoded on first play exactly as before, slower rather than broken.
    ///
    /// **Up front is the engine's own timing, and for sound it is a REFUSAL rather than a
    /// preference** (D86, D87). `CBaseEntity::PrecacheSound` opens with
    /// `if ( !CBaseEntity::IsPrecacheAllowed() )` and then
    /// `Assert( !"CBaseEntity::PrecacheSound:  too late" )` — `SoundEmitterSystem.cpp:1497`. Loading
    /// a sound during play is something Source treats as a programming error, and it passes
    /// `bPreload: true` to `enginesound->PrecacheSound` at `:1507`.
    ///
    /// **What this decides is the LIST**, now that `SoundCache.Precache` does the decoding — which
    /// sounds are worth having ready is the only part that needs a demo and a map.
    /// </remarks>
    public static void Precache(
        SoundCache cache,
        DemoTimeline? timeline,
        GameContent? game,
        SoundscapeSystem soundscape,
        ILogger audio,
        IReadOnlyList<SceneSound> emitted,
        IReadOnlyDictionary<string, SoundScriptEntry>? scripts,
        IEnumerable<StudioEvent> modelEvents)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(modelEvents);
        ArgumentNullException.ThrowIfNull(soundscape);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(emitted);

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
                ToPrecache(
                    // **The surfaces' sounds FIRST, as the engine precaches them at level init before the demo's.** The
                    // order matters only at the cache's cap, which the engine does not have: `SoundCache` lets the least
                    // recently used go, so the first precached are the first evicted — a step wave before a timeline
                    // sound. f12 holds 277 MB of 512 (finding 84).
                    // **Then the packed models' event sounds**, as the server's `PrecacheModelComponents` does with each model
                    // (finding 87; the client's route is the soundprecache table, B568): a viewmodel's draw and reload are the
                    // client's own sounds, so no sound message names them.
                    SurfaceWaves(game.Surfaces, scripts).Concat(ModelWaves(modelEvents, scripts)).Concat(timeline.SoundsToPrecache()).Concat(
                        emitted.Select(sound => sound.Name)
                            .Concat(ScriptWaves(emitted, scripts))
                            .Distinct(StringComparer.OrdinalIgnoreCase)),
                    soundscape));

            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            audio.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"precached {result.Decoded} of {result.Named} sounds " +
                    $"in {result.Seconds * 1000d:0} ms"));

            // Stryker restore all
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            audio.LogWarning(failure, "precaching sounds");
        }
    }

    /// <summary>Every wave of every script the client sounds name — what `PrecacheScriptSound` precaches (B503).</summary>
    /// <param name="sounds">The client's sounds.</param>
    /// <param name="scripts">The loaded scripts, or null.</param>
    /// <returns>Each wave once, in script then wave order.</returns>
    /// <remarks>
    /// `InternalPrecacheWaves` (`SoundEmitterSystem.cpp:351-374`) precaches EVERY wave of a script, not the one a draw will
    /// pick: the pick is dealt at play time from the script's flags, so any of them may be the next.
    /// </remarks>
    public static IEnumerable<string> ScriptWaves(IEnumerable<SceneSound> sounds, IReadOnlyDictionary<string, SoundScriptEntry>? scripts)
    {
        ArgumentNullException.ThrowIfNull(sounds);

        if (scripts is null)
        {
            return [];
        }

        return sounds
            .Select(static sound => sound.WaveDraw?.Script)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(script => scripts.TryGetValue(script, out SoundScriptEntry entry) ? entry.Waves : [])
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every wave of every script a model's client sound events name — the model's sound list, precached with it.</summary>
    /// <param name="events">The packed models' animation events.</param>
    /// <param name="scripts">The loaded scripts, or null.</param>
    /// <returns>Each wave once, in event then wave order.</returns>
    /// <remarks>
    /// `CModelSoundsCache::BuildAnimationEventSoundList` (`ModelSoundsCache.cpp:131-218`) adds the options of every
    /// `CL_EVENT_SOUND` as a script, and `PrecacheSoundList` precaches each one's waves (B503). Only the event this viewer
    /// plays (<see cref="SoundPresenter.PlaySoundEvent"/>), and only where the client fires it
    /// (<see cref="StudioEvent.FiresOnTheClient()"/>); the list's other cases are HL2's footsteps, NPC script events and the
    /// new event system's server sounds, which nothing here fires. `FindOrAddScriptSound` (`:112-124`) keeps only a name
    /// `GetSoundIndex` finds, so a raw wave adds nothing here either.
    ///
    /// **A divergence, deliberately interim** (finding 87, B568): this is the SERVER's route. The client receives the same
    /// waves, and every other server precache, through the demo's `soundprecache` table — on f12, 6,713 names including all
    /// twelve first-play misses. Decoding that whole table through <see cref="SoundCache"/> took 45.8 s and overflowed its cap,
    /// so this list stands in until the table can be honoured without decoding every entry up front.
    /// </remarks>
    public static IEnumerable<string> ModelWaves(IEnumerable<StudioEvent> events, IReadOnlyDictionary<string, SoundScriptEntry>? scripts)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (scripts is null)
        {
            return [];
        }

        return events
            .Where(static one => one.Id == SoundPresenter.PlaySoundEvent && one.FiresOnTheClient() && one.Options.Length > 0)
            .Select(static one => one.Options)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(script => scripts.TryGetValue(script, out SoundScriptEntry entry) ? entry.Waves : [])
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every wave of every script a surface names — what `PrecachePhysicsSounds` precaches at level init.</summary>
    /// <param name="surfaces">The game's surface properties, or null.</param>
    /// <param name="scripts">The loaded scripts, or null.</param>
    /// <returns>Each wave once, in surface, then key, then wave order.</returns>
    /// <remarks>
    /// `physics_shared.cpp:1034-1053` walks every surface and passes each of its sound names to `PrecacheScriptSound`, which
    /// precaches every wave (B503). A step, a bullet impact or a corpse's knock on a surface is the client's own sound, so no demo
    /// message names it: left out, each decoded inside the frame that first played it (finding 84). The keys are the ones this
    /// viewer reads and plays; `rolling`, `breaksound` and `strainsound` are not read yet.
    /// </remarks>
    public static IEnumerable<string> SurfaceWaves(VphysicsSurfaceProps? surfaces, IReadOnlyDictionary<string, SoundScriptEntry>? scripts)
    {
        if (surfaces is null || scripts is null)
        {
            return [];
        }

        return surfaces.Surfaces
            .SelectMany(static surface => new[]
            {
                surface.Sounds.StepLeft, surface.Sounds.StepRight, surface.Sounds.BulletImpact, surface.Sounds.ImpactHard,
                surface.Sounds.ImpactSoft, surface.Sounds.ScrapeRough, surface.Sounds.ScrapeSmooth,
            })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(script => scripts.TryGetValue(script, out SoundScriptEntry entry) ? entry.Waves : [])
            .Distinct(StringComparer.OrdinalIgnoreCase);
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
