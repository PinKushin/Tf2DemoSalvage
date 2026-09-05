using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which player conditions a demo actually contains, and how long each episode lasts (B336).
/// </summary>
/// <remarks>
/// **The denominator for every condition-driven effect.** `YellowLevel` runs on 7,570 shipped
/// materials and `BurnLevel` on 6,718, and both rest at a no-op — so the number that says whether
/// implementing them changes any frame is not how many materials run them but how often anybody is
/// actually alight or jarate'd on camera. That is a question about the DEMO, not about TF2.
///
/// **It also validates the burn clock, which is reconstructed rather than read.**
/// `m_flBurnEffectStartTime` is set client-side when the condition is added and networked nowhere,
/// so `ScenePlayer.BurningFor` watches the transition instead. A clock built that way has two ways
/// to be wrong that no unit test sees: it can fail to reset between episodes, which shows as a
/// duration climbing past the flame's ten-second life; and it can reset every tick, which shows as
/// every sample reading zero. Both are visible in the spread this prints.
///
/// <code>
///   conditions &lt;demo&gt;          — every condition seen, with episode counts and durations
/// </code>
/// </remarks>
public sealed class ConditionProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "conditions";

    /// <inheritdoc/>
    public string Summary =>
        "which player conditions a demo contains, and for how long: conditions <demo>";

    /// <summary>The conditions worth naming, by index.</summary>
    /// <remarks>
    /// Named rather than numbered because a bare index is unreadable, and only these have a
    /// consumer — the rest are counted as a total so an unexpectedly busy demo still shows up.
    /// </remarks>
    private static readonly (int Index, string Name)[] Interesting =
    [
        (PlayerConditions.Zoomed, "TF_COND_ZOOMED"),
        (PlayerConditions.Disguising, "TF_COND_DISGUISING"),
        (PlayerConditions.Disguised, "TF_COND_DISGUISED"),
        (PlayerConditions.Burning, "TF_COND_BURNING"),
        (PlayerConditions.Urine, "TF_COND_URINE"),
    ];

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("Give a demo: conditions <demo>");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo matching '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        output.WriteLine($"{Path.GetFileName(path)}: {timeline.Frames.Count} frames");
        output.WriteLine();

        // Per condition: how many player-frames carry it, and how many distinct episodes — a run of
        // consecutive frames for one entity. Episodes are what a proxy's clock is measured against.
        Dictionary<int, int> frames = [];
        Dictionary<int, int> episodes = [];
        Dictionary<(int Condition, int Entity), bool> holding = [];

        // Every burn duration seen, so the clock's SPREAD is visible rather than its maximum alone.
        List<float> burnClocks = [];

        // **The frames worth LOOKING at**, which is the output this probe exists to produce. A
        // count says the feature has data; a tick number is what lets somebody point a camera at
        // it — and an effect nobody has seen is not verified, whatever the numbers say.
        List<(int Tick, int Burning, float Peak)> alight = [];

        int samples = 0;

        foreach (TimelineFrame frame in timeline.Frames)
        {
            int burningHere = 0;
            float peakHere = 0f;

            foreach (ScenePlayer player in frame.Players)
            {
                samples++;

                foreach ((int index, _) in Interesting)
                {
                    bool now = player.Conditions.Has(index);
                    bool before = holding.GetValueOrDefault((index, player.EntityIndex));

                    if (now)
                    {
                        frames[index] = frames.GetValueOrDefault(index) + 1;

                        if (!before)
                        {
                            episodes[index] = episodes.GetValueOrDefault(index) + 1;
                        }
                    }

                    holding[(index, player.EntityIndex)] = now;
                }

                if (player.BurningFor is { } burning)
                {
                    burnClocks.Add(burning);
                    burningHere++;

                    // **The proxy's own value, not the raw clock.** A player eleven seconds into a
                    // burn has a clock of 11 and a burn LEVEL of zero — the flame has gone out —
                    // so ranking frames by the clock would send somebody to look at a player who
                    // is not visibly alight.
                    peakHere = Math.Max(peakHere, MaterialProxies.BurnLevel(burning));
                }
            }

            if (burningHere > 0)
            {
                alight.Add((frame.Tick, burningHere, peakHere));
            }
        }

        output.WriteLine($"{samples} player-frames");
        output.WriteLine();

        foreach ((int index, string name) in Interesting)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {frames.GetValueOrDefault(index),7} frames  " +
                $"{episodes.GetValueOrDefault(index),5} episodes  {name}"));
        }

        output.WriteLine();

        if (burnClocks.Count == 0)
        {
            output.WriteLine("No burn clock samples — nobody caught fire in this recording.");
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"burn clock: {burnClocks.Count} samples, " +
            $"{burnClocks.Min():0.00} to {burnClocks.Max():0.00} seconds, " +
            $"mean {burnClocks.Average():0.00}"));

        // **The control on the clock, and the reason this prints rather than asserts.** A flame
        // lives ten seconds (`TF_BURNING_FLAME_LIFE`), so a maximum far past that means the clock
        // is not being reset between episodes — and a maximum at zero means it is being reset every
        // tick. Both read as a plausible number on their own.
        output.WriteLine(
            burnClocks.Max() > 30f
                ? "  ^ past a flame's life by a wide margin: the clock is not resetting"
                : "  ^ within a flame's life, so the transition reset is working");

        output.WriteLine();
        output.WriteLine("ticks worth looking at — most players at full burn:");

        foreach ((int tick, int burning, float peak) in alight
            .OrderByDescending(entry => entry.Peak)
            .ThenByDescending(entry => entry.Burning)
            .Take(6))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  --tick {tick,-8} {burning} burning, strongest at {peak:0.00}"));
        }
    }
}
