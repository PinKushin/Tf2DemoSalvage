using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>The rules the time panel reads and every `team_round_timer`, each time they change, with the time shown.</summary>
/// <remarks>
/// The control is `m_iTimerToShowInHUD`: a round-based map names a timer for the HUD, and that timer must be among those
/// read. A named timer missing from the list means the read is broken, not the demo.
/// </remarks>
public sealed class RoundTimerProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "round-timer";

    /// <inheritdoc/>
    public string Summary => "the time panel's rules and each team_round_timer as they change, with the time shown: round-timer <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("round-timer <demo>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        string last = string.Empty;
        int named = 0;
        int found = 0;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick++)
        {
            SceneGameRules rules = timeline.RulesAt(tick);
            IReadOnlyList<SceneRoundTimer> timers = timeline.RoundTimersAt(tick);
            float curTime = (timeline.ServerTickAt(tick) ?? tick) * timeline.IntervalPerTick;

            if (rules.TimerToShowInHud != 0)
            {
                named++;
                found += timers.Any(timer => timer.EntityIndex == rules.TimerToShowInHud) ? 1 : 0;
            }

            string state = string.Create(
                CultureInfo.InvariantCulture,
                $"rules waiting {rules.WaitingForPlayers} setup {rules.Setup} overtime {rules.Overtime} stopwatch {rules.StopWatch} type {rules.GameType} koth {rules.Koth} summary {rules.ShowMatchSummary} hud timer {rules.TimerToShowInHud}; ")
                + string.Join("; ", timers.Select(timer => string.Create(
                    CultureInfo.InvariantCulture,
                    $"timer {timer.EntityIndex} state {timer.State} paused {timer.Paused} end {timer.EndTime} left {timer.TimeRemaining} max {timer.TimerMaxLength} hud {timer.ShowInHud} disabled {timer.Disabled}")));

            if (state == last)
            {
                continue;
            }

            last = state;
            SceneRoundTimer? shown = timers.FirstOrDefault(timer => timer.EntityIndex == rules.TimerToShowInHud) is { EntityIndex: > 0 } hud ? hud : null;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"tick {tick,7} curtime {curTime:0.##} shows {(shown is { } s ? s.TimeRemainingAt(curTime).ToString("0.#", CultureInfo.InvariantCulture) : "-")}: {state}"));
        }

        output.WriteLine($"control: the named HUD timer was present on {found} of {named} ticks that named one");
    }
}
