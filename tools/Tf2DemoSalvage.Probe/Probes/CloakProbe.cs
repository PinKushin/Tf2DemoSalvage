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
                }

                float percent = PlayerInvisibility.Percent(player, serverTime, motionCloak: false);
                string at = string.Create(
                    CultureInfo.InvariantCulture,
                    $"--tick {frame.Tick} entity {player.EntityIndex} team {player.Team} percent {percent:0.000} enemy {player.IsEnemy} at {player.X:0} {player.Y:0} {player.Z:0} yaw {player.Yaw:0}");

                // Spies only: a decloak ramp also runs, as the engine runs it, on anyone whose change time is ahead.
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
        output.WriteLine("partly cloaked:");
        partial.ForEach(line => output.WriteLine("  " + line));
        output.WriteLine("fully cloaked:");
        full.ForEach(line => output.WriteLine("  " + line));
    }
}
