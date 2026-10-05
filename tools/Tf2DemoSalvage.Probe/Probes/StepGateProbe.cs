using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What <see cref="StepGateProbe.Measure"/> counted.</summary>
/// <param name="Samples">Live player samples carrying `m_fFlags`.</param>
/// <param name="Moving">Of those, the ones with a speed.</param>
/// <param name="Steps">Of those, the ones <see cref="Footsteps.Step"/> sounded.</param>
/// <param name="ClientBit">Of those, the ones with `1&lt;&lt;7` set.</param>
public readonly record struct StepGateCounts(int Samples, int Moving, int Steps, int ClientBit);

/// <summary>
/// Whether <see cref="Footsteps.Step"/> lets a step through for the players a demo sends `m_fFlags` for, asked as if an
/// animation event 7001 fired on each at sampled ticks, over a plain concrete surface whose two sounds always resolve.
/// </summary>
/// <remarks>
/// **Production's own gate and production's own players**: the step is <see cref="Footsteps.Step"/> with the demo's
/// <see cref="DemoTimeline.FlagLayout"/>, and the players come from <see cref="TimelineMoments"/>, which is where the
/// viewer's stepper gets its speed (`DemoTimeline.PlayersAt` carries none, and gave 0 steps on every demo until this was
/// found). The surface and scripts are fabricated so nothing but the player's own state can refuse a step. The control is
/// a demo in the current layout, where moving grounded players must step.
/// </remarks>
public sealed class StepGateProbe : IProbe
{
    private const string Left = "Probe.StepLeft";
    private const string Right = "Probe.StepRight";

    /// <inheritdoc/>
    public string Name => "step-gate";

    /// <inheritdoc/>
    public string Summary => "how often Footsteps.Step sounds for players carrying m_fFlags: step-gate <demo> [every]";

    /// <summary>Walks the demo every <paramref name="every"/> ticks and counts.</summary>
    /// <param name="timeline">The demo.</param>
    /// <param name="every">The tick stride.</param>
    /// <returns>The counts.</returns>
    public static StepGateCounts Measure(DemoTimeline timeline, int every)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        SoundScriptEntry entry = new(Left, 0, new SoundRange(1f, 1f), new SoundRange(100f, 100f), new SoundRange(75f, 75f),["player/footsteps/concrete1.wav"]);
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase)
        {
            [Left] = entry,
            [Right] = entry with { Name = Right },
        };
        StepSurface concrete = new('C', Left, Right);
        Footsteps footsteps = new();
        TimelineMoments moments = new(timeline);
        List<ScenePlayer> players = [];
        int samples = 0;
        int moving = 0;
        int steps = 0;
        int clientBit = 0;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += every)
        {
            players.Clear();
            moments.PlayersAt(tick, players, interpolating: true);

            foreach (ScenePlayer player in players)
            {
                if (player.Flags is not { } flags || !player.IsAlive)
                {
                    continue;
                }

                samples++;
                moving += player.Speed > 0f ? 1 : 0;
                clientBit += (flags >> 7) & 1;

                if (footsteps.Step(tick, player, concrete, _ => concrete, scripts, timeline.FlagLayout) is not null)
                {
                    steps++;
                }
            }
        }

        return new StepGateCounts(samples, moving, steps, clientBit);
    }

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Usage: step-gate <demo> [every]");
            return;
        }

        int every = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 3;
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        StepGateCounts counts = Measure(timeline, every);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: layout {timeline.FlagLayout}; {counts.Samples} live player samples with flags, {counts.Moving} moving, {counts.Steps} stepped; 1<<7 set in {counts.ClientBit}"));
    }
}
