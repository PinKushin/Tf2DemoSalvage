using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>One player's recorded origin, eye angles, health and life state tick by tick, and the director's shot in force (B535).</summary>
/// <remarks>
/// **The recorded frame, `DemoTimeline.PlayersAt( int )`** — the newest state on that tick, which is what a HUD prints
/// and what a paused frame draws; a playing frame draws an interpolation delay behind it. One timeline, every tick asked
/// for, where `moment-dump` builds one a tick. The director's column is `DirectorAt`: `-` on a demo with no director.
/// </remarks>
public sealed class PlayerTrackProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "player-track";

    /// <inheritdoc/>
    public string Summary =>
        "one player's recorded origin, eye angles and health per tick, beside the director's shot: player-track <demo> <entity> <fromTick> <toTick> [step]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 4 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Usage: player-track <demo> <entity> <fromTick> <toTick> [step]");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        int entity = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        int from = int.Parse(arguments[2], CultureInfo.InvariantCulture);
        int to = int.Parse(arguments[3], CultureInfo.InvariantCulture);
        int step = arguments.Count > 4 ? Math.Max(1, int.Parse(arguments[4], CultureInfo.InvariantCulture)) : 1;

        output.WriteLine("tick  x y z  eyeYaw eyePitch  yaw  health life ducking  | director: inEye target target2 distance offset theta phi");

        for (int tick = from; tick <= to; tick += step)
        {
            string director = timeline.DirectorAt(tick) is { } shot
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{shot.InEye} {shot.Target} {shot.SecondTarget} {shot.Distance:0.#} {shot.Offset:0.#} {shot.Theta:0.#} {shot.Phi:0.#}")
                : "-";

            bool found = false;

            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (player.EntityIndex != entity)
                {
                    continue;
                }

                found = true;
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{tick}  {player.X:0.#} {player.Y:0.#} {player.Z:0.#}  {player.EyeYaw:0.##} {player.EyePitch:0.##}  {player.Yaw:0.##}  {player.Health} {player.LifeState} {player.Movement?.Ducked}  | {director}"));
            }

            if (!found)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{tick}  (no such player on this tick)  | {director}"));
            }
        }
    }
}
