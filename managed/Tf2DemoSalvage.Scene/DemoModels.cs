using System;
using System.Collections.Generic;

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
            if (track.Kind == SceneModelKind.Studio)
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
