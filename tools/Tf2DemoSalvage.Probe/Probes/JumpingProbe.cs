using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// How many airborne player-ticks are jumping (`m_bJumping`, from PLAYERANIMEVENT_JUMP) and how many are airborne without
/// a jump — rocket jumps, falls — which HandleJumping lets through to the crouch or the run.
/// </summary>
/// <remarks>
/// **The control for the jump clock**: a demo whose jump events never arrive would read every airborne tick as not
/// jumping, and every jump would draw as a run in the air.
/// </remarks>
public sealed class JumpingProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "jumping";

    /// <inheritdoc/>
    public string Summary => "airborne player-ticks with and without a jump event in force: jumping <demo> [fromTick] [toTick]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Usage: jumping <demo> [fromTick] [toTick]");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        int from = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : timeline.FirstTick;
        int to = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : timeline.LastTick;
        int jumping = 0;
        int airborne = 0;
        int grounded = 0;

        for (int tick = from; tick <= to; tick++)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (!player.IsAlive || !player.IsPlaying)
                {
                    continue;
                }

                if (player.AirborneSeconds is not null)
                {
                    jumping++;
                }
                else if (player.IsAirborne)
                {
                    airborne++;
                }
                else
                {
                    grounded++;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"ticks {from}-{to}: {jumping} player-ticks jumping, {airborne} airborne without a jump, {grounded} grounded"));
    }
}
