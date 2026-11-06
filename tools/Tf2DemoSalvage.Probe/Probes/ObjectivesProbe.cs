using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What the timeline holds of the objective resource at a tick (B709), through <see cref="DemoTimeline.ObjectivesAt"/>.</summary>
public sealed class ObjectivesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "objectives";

    /// <inheritdoc/>
    public string Summary => "control points at a tick (B709): objectives <demo> <tick>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("usage: objectives <demo> <tick>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(arguments[0]));
        int tick = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        SceneObjectives? objectives = timeline.ObjectivesAt(tick);

        output.WriteLine($"escort: {timeline.EscortChanges.Count} watcher changes; blue {timeline.EscortAt(tick, 3)}; red {timeline.EscortAt(tick, 2)}");
        output.WriteLine($"flags: {string.Join("; ", timeline.FlagsAt(tick)?.Flags ?? [])}");
        output.WriteLine($"zones: {string.Join("; ", timeline.FlagsAt(tick)?.Zones ?? [])}");

        if (timeline.PasstimeAt(tick) is { } passtime)
        {
            output.WriteLine($"passtime: {passtime with { TrackPoints = [], Goals = [] }}");
            output.WriteLine($"  track: {string.Join(" ", passtime.TrackPoints)}");
            output.WriteLine($"  goals: {string.Join("; ", passtime.Goals.Select(goal => $"{goal} frac {passtime.ProgressFrac(goal.Origin):F3}"))}");
            output.WriteLine($"  ball frac {passtime.ProgressFrac(passtime.BallOrigin):F3}");
        }
        if (timeline.RobotDestructionAt(tick) is { } rd)
        {
            output.WriteLine($"robot destruction: {rd with { Groups = [] }} leaders {rd.TeamLeaders}");

            foreach (SceneRobotGroup group in rd.Groups)
            {
                output.WriteLine($"  {group}");
            }

            foreach (SceneFlag flag in timeline.FlagsAt(tick)?.Flags ?? [])
            {
                output.WriteLine($"  flag {flag.EntityIndex} points {flag.PointValue} reset {flag.ResetTime} neutral {flag.NeutralTime} max {flag.MaxResetTime} prev {flag.PrevOwner}");
            }

            foreach (SceneCaptureZone zone in timeline.FlagsAt(tick)?.Zones ?? [])
            {
                output.WriteLine($"  zone {zone.EntityIndex} radius {zone.BoundingRadius}");
            }
        }

        output.WriteLine("resource teams: " + string.Join(' ', timeline.ScoreboardPlayersAt(tick).Select(player => $"{player.EntityIndex}:{player.Team}")));

        foreach ((int at, int player, int area) in timeline.TouchChanges.Where(change => Math.Abs(change.Tick - tick) < 200))
        {
            output.WriteLine($"  touch change at {at}: player {player}, area {area}");
        }

        output.WriteLine($"recorder {timeline.RecorderEntityIndex} touches point {timeline.TouchedPointAt(tick)}; {timeline.TouchChanges.Count} touch changes, the recorder's first at {timeline.TouchChanges.FirstOrDefault(change => change.Player == timeline.RecorderEntityIndex && change.Area >= 0)}");

        if (objectives is null)
        {
            output.WriteLine("no objective resource");
            return;
        }

        output.WriteLine($"{objectives.Points.Count} points, layout '{objectives.CapLayout}', names {string.Join(", ", objectives.IconNames.Select(name => name.Key + "=" + name.Value))}");

        foreach (SceneControlPoint point in objectives.Points)
        {
            output.WriteLine(point.ToString());
        }
    }
}
