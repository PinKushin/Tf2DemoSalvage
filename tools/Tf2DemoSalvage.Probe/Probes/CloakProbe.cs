using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Where a demo's spies cloak, and every screen overlay its recorder's condition hooks leave.</summary>
/// <remarks>
/// **Through the production path**: the cloak is <c>PlayerInvisibility.Percent</c> on the server clock the viewer uses,
/// and the overlay is the <c>ScenePlayer.ScreenOverlayMaterial</c> the timeline stepped. **The control** is the spy
/// count: a demo whose spies number zero cannot answer "nobody cloaked".
/// <code>
///   cloak &lt;demo&gt;
/// </code>
/// </remarks>
public sealed class CloakProbe : IProbe
{
    private const int Spy = 8;

    /// <inheritdoc/>
    public string Name => "cloak";

    /// <inheritdoc/>
    public string Summary => "where spies cloak (partial and full), and the recorder's screen overlays: cloak <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Give a demo: cloak <demo>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        float interval = timeline.IntervalPerTick;
        HashSet<int> spies = [];
        Dictionary<int, string> watchesOf = [];
        List<string> dry = [];
        List<string> partial = [];
        List<string> full = [];
        string? overlay = null;
        int overlayChanges = 0;

        output.WriteLine($"{Path.GetFileName(path)}: {timeline.Frames.Count} frames, recorder {timeline.RecorderEntityIndex?.ToString(CultureInfo.InvariantCulture) ?? "none (SourceTV)"}");

        foreach (TimelineFrame frame in timeline.Frames)
        {
            float serverTime = (timeline.ServerTickAt(frame.Tick) ?? frame.Tick) * interval;

            foreach (ScenePlayer player in frame.Players)
            {
                if (player.PlayerClass == Spy)
                {
                    spies.Add(player.EntityIndex);

                    foreach (SceneItem item in player.Items ?? [])
                    {
                        if (item.ClassName == "CTFWeaponInvis" && item.DefinitionIndex is { } watch)
                        {
                            string seen = watchesOf.GetValueOrDefault(player.EntityIndex, "");
                            string entry = string.Create(CultureInfo.InvariantCulture, $"{watch}");

                            if (!seen.Split(' ').Contains(entry))
                            {
                                watchesOf[player.EntityIndex] = (seen + " " + entry).Trim();
                            }
                        }
                    }
                }

                float percent = PlayerInvisibility.Percent(player, serverTime, motionCloak: false);
                string at = string.Create(
                    CultureInfo.InvariantCulture,
                    $"--tick {frame.Tick} entity {player.EntityIndex} team {player.Team} percent {percent:0.000} enemy {player.IsEnemy} at {player.X:0} {player.Y:0} {player.Z:0} yaw {player.Yaw:0}");

                // Spies only: a decloak ramp also runs, as the engine runs it, on anyone whose change time is ahead.
                // The motion-cloak fade's own inputs (tf_player_shared.cpp:8017-8024): stealthed, the change done, an empty
                // meter, moving. Printed whatever the watch, which the 'watches' list below names.
                if (player.PlayerClass == Spy && player.Conditions.IsStealthed && (player.InvisChangeCompleteTime ?? 0f) <= serverTime &&
                    player.CloakMeter is 0f && player.Velocity is { } v && (v.X * v.X) + (v.Y * v.Y) > 100f && dry.Count < 10)
                {
                    dry.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"--tick {frame.Tick} entity {player.EntityIndex} speed {MathF.Sqrt((v.X * v.X) + (v.Y * v.Y)):0} max {player.MaxSpeed:0}"));
                }

                if (player.PlayerClass == Spy && percent > 0f && percent < 1f && partial.Count < 40)
                {
                    partial.Add(at);
                }
                else if (player.PlayerClass == Spy && percent >= 1f && full.Count < 6)
                {
                    full.Add(at);
                }

                if (player.EntityIndex == timeline.RecorderEntityIndex && frame.Tick % 20 == 0 && arguments.Count > 1 &&
                    Math.Abs(frame.Tick - int.Parse(arguments[1], CultureInfo.InvariantCulture)) < 100)
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  recorder --tick {frame.Tick} at {player.X:0} {player.Y:0} {player.Z:0} eye yaw {player.EyeYaw:0} pitch {player.EyePitch:0}"));
                }

                if (player.EntityIndex == timeline.RecorderEntityIndex && player.ScreenOverlayMaterial != overlay)
                {
                    overlay = player.ScreenOverlayMaterial;
                    overlayChanges++;

                    if (overlayChanges <= 30)
                    {
                        output.WriteLine($"  overlay --tick {frame.Tick}: {overlay ?? "(none)"}");
                    }
                }
            }
        }

        output.WriteLine($"spies seen: {spies.Count} ({string.Join(", ", spies.Order())}); overlay changes: {overlayChanges}");
        output.WriteLine("stealthed on an empty meter, moving (a motion cloak's fade):");
        dry.ForEach(line => output.WriteLine("  " + line));
        output.WriteLine("watches (CTFWeaponInvis definition per spy; 60 is the Cloak and Dagger):");

        foreach ((int spy, string watches) in watchesOf.OrderBy(pair => pair.Key))
        {
            output.WriteLine($"  entity {spy}: {watches}");
        }

        // The control for "no feign death": every corpse is counted, so zero cloaked of zero corpses says nothing.
        List<SceneRagdoll> cloaked = [.. timeline.Corpses.Where(corpse => corpse.Cloaked || corpse.FeignDeath)];

        output.WriteLine(
            $"corpses: {timeline.Corpses.Count}, m_bCloaked: {cloaked.Count(corpse => corpse.Cloaked)}, m_bFeignDeath: {cloaked.Count(corpse => corpse.FeignDeath)}");

        foreach (SceneRagdoll corpse in cloaked.Take(10))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  --tick {corpse.FirstTick} corpse {corpse.EntityIndex} cloaked {corpse.Cloaked} feign {corpse.FeignDeath} of {corpse.PlayerIndex} team {corpse.Team} at {corpse.X:0} {corpse.Y:0} {corpse.Z:0} until {corpse.LastTick}"));
        }
        output.WriteLine("partly cloaked:");
        partial.ForEach(line => output.WriteLine("  " + line));
        output.WriteLine("fully cloaked:");
        full.ForEach(line => output.WriteLine("  " + line));
    }
}
