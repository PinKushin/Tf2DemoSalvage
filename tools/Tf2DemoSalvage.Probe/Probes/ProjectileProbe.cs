using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every projectile a demo carries, over the whole demo rather than at one tick.
/// </summary>
/// <remarks>
/// **A projectile lives about a second, so a tick sample cannot answer whether they exist.** The
/// `props` probe reports one tick; two ticks chosen by hand on `z1800` showed no projectile and
/// that was an absence with no control behind it — the very failure
/// `docs/memory/instrument-bugs-outnumber-decoder-bugs.md` names. This walks every track in the
/// timeline instead, so "none" means none in the demo.
///
/// **The denominator is printed beside the answer** for the same reason: a census that finds zero
/// projectiles and cannot say how many tracks it looked at is indistinguishable from a broken walk.
///
/// <code>
///   projectiles z1800
///   projectiles z1800 rocket
/// </code>
/// </remarks>
public sealed class ProjectileProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "projectiles";

    /// <inheritdoc/>
    public string Summary =>
        "every projectile a demo carries, over the whole demo: projectiles <demo> [class substring]";

    /// <summary>
    /// The class-name fragments that make an entity a projectile.
    /// </summary>
    /// <remarks>
    /// **Taken from the schema rather than from a guess.** These are the families
    /// `DT_TFBaseProjectile`, `DT_TFBaseRocket` and `DT_BaseGrenade` head, as the demo's own
    /// `dem_datatables` declares them — `schema &lt;demo&gt; projectile` lists the whole set.
    /// Matching by NAME here is a probe's licence and not the renderer's: production must resolve a
    /// projectile through its send table, because a class list goes stale on the next update.
    /// </remarks>
    private static readonly string[] Families =
    [
        "Projectile",
        "Rocket",
        "Grenade",
        "Pipebomb",
        "Arrow",
        "Flare",
        "Jar",
        "Cleaver",
        "EnergyBall",
        "EnergyRing",
        "BallOfFire",
        "Ornament",
    ];

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("projectiles <demo> [class substring]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        string filter = arguments.Count > 1 ? arguments[1] : string.Empty;

        DemoTimeline timeline;

        try
        {
            timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        }
        catch (InvalidDataException truncated)
        {
            output.WriteLine($"{Path.GetFileName(path)} will not decode: {truncated.Message}");
            return;
        }

        // **Weapons are excluded by name, and that is the trap this probe would otherwise fall
        // into.** `CTFRocketLauncher` and `CTFGrenadeLauncher` contain "Rocket" and "Grenade" but
        // are the things a player HOLDS, not the things they fire; counting them would report a
        // demo full of projectiles that draws none.
        bool Launcher(string className) =>
            className.Contains("Launcher", StringComparison.Ordinal) ||
            className.Contains("Wearable", StringComparison.Ordinal) ||
            className.EndsWith("Gun", StringComparison.Ordinal);

        List<ScenePropTrack> projectiles = [];

        foreach (ScenePropTrack track in timeline.Props)
        {
            if (Launcher(track.ClassName))
            {
                continue;
            }

            if (!Families.Any(family =>
                track.ClassName.Contains(family, StringComparison.Ordinal)))
            {
                continue;
            }

            if (filter.Length > 0 &&
                !track.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            projectiles.Add(track);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {projectiles.Count} projectile tracks " +
            $"of {timeline.Props.Count} prop tracks"));

        // **By class, because the question is which KINDS are carried**, and a raw list of two
        // thousand rockets answers nothing a count does not.
        foreach ((string className, List<ScenePropTrack> of) in projectiles
            .GroupBy(track => track.ClassName, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => (group.Key, group.ToList())))
        {
            int withModel = of.Count(track => track.ModelPath.Length > 0);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {className}: {of.Count}, {withModel} with a model path"));

            // **One example in full, so the claim can be checked by looking.** A count says a
            // track exists; where it is and what model it names is what a camera needs.
            // **The LAST one rather than the first, and its position with it.** The first projectile
            // in a demo is usually fired during the pre-round while everyone is still in spawn, so
            // a camera aimed at it sees a spawn room — which cost two screenshots before this line
            // existed. A late one is mid-match, and printing where it is means the camera comes
            // from the data rather than from a guess
            // (`docs/memory/point-the-camera-from-the-data.md`).
            // **And one from the MIDDLE, because the last is not always usable either.** Comparing
            // against real TF2 means driving the game to the tick, and the last rocket of this demo
            // lives 43 ticks from its end — playback runs off the end before a screenshot can be
            // taken, and the capture comes back as the main menu. A middle example is surrounded by
            // demo on both sides (B161).
            foreach (ScenePropTrack example in (ScenePropTrack[])[of[0], of[of.Count / 2], of[^1]])
            {
                (int tick, ScenePose pose) = example.Keyframes.Count > 0
                    ? example.Keyframes[0]
                    : (example.FirstTick, default);

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    entity {example.EntityIndex} ticks {example.FirstTick}-" +
                    $"{(example.Keyframes.Count > 0 ? example.Keyframes[^1].Tick : example.FirstTick)}" +
                    $" at tick {tick} ({pose.X:0} {pose.Y:0} {pose.Z:0}) " +
                    $"model '{example.ModelPath}'"));
            }
        }

        // **`ModelPaths()` cannot answer "which projectile models does the demo declare", and this
        // is here so nobody asks it again.** It is built FROM the tracks, so on a demo whose
        // projectiles produce no track it reports no projectile models — which is the question
        // restated, not evidence about the precache. The real question is whether
        // `state.ModelIndex()` surfaces the class baseline's index and whether the precache
        // resolves it; both live below this probe's reach (B372).

        if (projectiles.Count == 0)
        {
            output.WriteLine(
                "  none — and the denominator above is the control: a walk that saw tracks and " +
                "found no projectile means the demo carries none, not that the walk is broken.");
        }
    }
}
