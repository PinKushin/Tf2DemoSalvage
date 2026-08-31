using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Every model a demo will ever show, worked out before anything is drawn.</summary>
/// <remarks>
/// **This was <c>MainForm.DemoModelPaths</c> and <c>WornModelPaths</c>** (B188, D90) — a question
/// about a demo and an install, asked from a window that had neither to do with it.
///
/// **The whole set is resolved up front because the loader is a dictionary, not an on-demand read.**
/// <see cref="MapAssets"/> is handed this set at map load and packs exactly what is in it, so a
/// model missing from it packs to nothing for ever. Loading during playback would grow the material
/// table and force a re-upload mid-match, which is the cost the up-front pass exists to avoid.
/// </remarks>
public static class DemoModels
{
    /// <summary>Pack every model a demo will show, before anything is drawn.</summary>
    /// <param name="models">Where packed models live.</param>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides, or null when it is not available.</param>
    /// <param name="render">The render log.</param>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> or <paramref name="render"/> is null.</exception>
    /// <remarks>
    /// **This was `MainForm.PrecacheModels`** (B208), and it is the exact twin of
    /// `Presentation.DemoSounds.Precache` — same guards, same narrow catch, same timing line. Its
    /// twin moved out of the window on 2026-08-26 and this one was left behind, which is the worse
    /// kind of miss: an asymmetry that afterwards looks deliberate.
    ///
    /// **It lives in `Scene` while its twin lives in `Presentation`, and that is not an
    /// inconsistency.** Each sits with its collaborators — this needs `EntityModelSet` and
    /// `GameContent`, both here; the sound one needs `SoundCache` from `Audio`, which only
    /// `Presentation` can see. Composing downward is D92.
    ///
    /// **The guards are here rather than at the call site**, so a future caller does not have to
    /// rediscover that precaching needs both a demo and an open install.
    ///
    /// **The exceptions are caught rather than thrown**, and narrowly: a model that will not read is
    /// a defect in that file or in our reading of it, and must not take the whole demo down with it.
    /// A failure costs the precache and nothing else — anything missed is packed on sight exactly as
    /// before, which is slower rather than broken.
    ///
    /// **Up front is the engine's own timing** (D86). `CBaseEntity::PrecacheModel` is guarded by
    /// `IsPrecacheAllowed()` and warns on an out-of-order precache, because Source loads models at
    /// level load and not on sight. Packing when a prop first became visible is what cost 385 ms in
    /// a single frame here, and an asynchronous load would only move the hitch rather than remove
    /// it — the first appearance would still wait.
    ///
    /// **The timeline is a better list than `modelprecache`, which is what the engine uses.** The
    /// table names what the SERVER precached, including models this recording never shows; the
    /// tracks name what actually appears. Both are known before the first frame, which is the part
    /// that matters, so the narrower list wins at no cost.
    /// </remarks>
    public static void Precache(
        EntityModelSet models, DemoTimeline? timeline, GameContent? game, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(render);

        if (timeline is null || game is null)
        {
            return;
        }

        try
        {
            long packedAt = Stopwatch.GetTimestamp();

            models.Precache(ToPack(timeline, game));

            double packedSeconds =
                (Stopwatch.GetTimestamp() - packedAt) / (double)Stopwatch.Frequency;

            render.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"precached models in {packedSeconds * 1000d:0} ms " +
                    $"({models.Count} packed, {models.Vertices.Count} vertices)"));
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            render.LogWarning(failure, "precaching models");
        }
    }

    /// <summary>Every studio model the demo shows, at any tick, from any source.</summary>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides.</param>
    /// <returns>Distinct model paths, compared without regard to case.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    /// <remarks>
    /// **Four sources, and three of them are easy to forget** — each was a real bug:
    ///
    /// <list type="bullet">
    /// <item><b>Every class, not only the ones standing at tick zero.</b> A player can switch class
    /// at any moment, so a set built from who is playing now is missing whatever they change to, and
    /// that player simply vanishes mid-round. Nine models is the whole roster and it loads once.</item>
    /// <item><b>The viewmodels</b>, which are in neither of the other sets: a viewmodel has no origin
    /// so the timeline deliberately keeps it out of <c>Props</c>. It cost a whole feature — the
    /// viewer resolved <c>c_demo_arms.mdl</c>, packed it, reported "0 instances" and drew nothing,
    /// with the model in the archive the entire time.</item>
    /// <item><b>The held weapons</b>, which are not entities at all and are resolved through the item
    /// schema.</item>
    /// </list>
    /// </remarks>
    public static HashSet<string> Needed(DemoTimeline? timeline, GameContent game)
    {
        ArgumentNullException.ThrowIfNull(game);

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string model in game.ModelPaths())
        {
            paths.Add(model);
        }

        if (timeline is not { } demo)
        {
            return paths;
        }

        foreach (ScenePropTrack track in demo.Props)
        {
            // **A studio track can have no path yet, and that is not a model to load.** A weapon
            // whose model the wire never carried reaches Core with an empty path and an item
            // number — `CEconEntity::UpdateModelToClass` resolves it from `items_game.txt`, which
            // Core cannot read — and its kind is Studio because every `model_player` is a `.mdl`.
            //
            // Passing the empty string on threw out of `PakFile.ReadFile` and killed the viewer at
            // load. The models themselves are not lost: `game.Weapons.AllIn` below walks what every
            // player holds and resolves each through the same item schema, so `c_medigun.mdl` is
            // loaded by the route that knows its name.
            if (track.Kind == SceneModelKind.Studio && track.ModelPath.Length > 0)
            {
                paths.Add(track.ModelPath);
            }
        }

        foreach (string arms in demo.ViewmodelModels)
        {
            paths.Add(arms);
        }

        foreach (string weapon in game.Weapons.AllIn(demo))
        {
            paths.Add(weapon);
        }

        return paths;
    }

    /// <summary>Every model to pack before playback, so nothing is read mid-match.</summary>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides.</param>
    /// <returns>Distinct model paths, compared without regard to case.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    /// <remarks>
    /// **This is NOT <see cref="Needed"/>, and the two disagreeing is B195.** This set is what gets
    /// PACKED into the vertex buffer; `Needed` is what gets DECODED by the asset loader. They are
    /// built from different sources — this one from the timeline's own accessor plus the class
    /// roster, that one from the prop tracks, the viewmodels and the item schema — so a path in one
    /// and not the other either packs to nothing or hitches on first sight. Neither fails loudly.
    ///
    /// Kept as it was rather than unified here, because merging them changes which models are packed
    /// and that wants its own measurement rather than riding along with a move.
    ///
    /// **The class models are added because a player's model is chosen at runtime.** Nothing on the
    /// wire carries it — `CTFPlayerClassShared::GetModelName` resolves it from the class script — so
    /// a player track's own path may not name every model that player will wear.
    /// </remarks>
    public static HashSet<string> ToPack(DemoTimeline? timeline, GameContent game)
    {
        ArgumentNullException.ThrowIfNull(game);

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        if (timeline is { } demo)
        {
            // Props, players and — the one a caller would miss — the viewmodels, which change on
            // every weapon switch and are private to the timeline.
            foreach (string path in demo.ModelPaths())
            {
                paths.Add(path);
            }
        }

        foreach (string model in game.ModelPaths())
        {
            paths.Add(model);
        }

        return paths;
    }

    /// <summary>The models the demo ever hangs off another entity's skeleton.</summary>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides.</param>
    /// <returns>The worn set, which is empty when no demo is open.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    /// <remarks>
    /// The rule itself is <see cref="WornModels.From"/>, which is where its reasoning and its tests
    /// live. This supplies the two sources: the demo's own prop tracks, and the first-person
    /// weapons, which are built by the viewer and appear in no timeline.
    /// </remarks>
    public static HashSet<string> Worn(DemoTimeline? timeline, GameContent game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return timeline is { } demo
            ? WornModels.From(demo.Props, game.Weapons.AllIn(demo))
            : [];
    }
}
