using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What each areaportal window in a demo says about its own fade, and what that blend comes to.
/// </summary>
/// <remarks>
/// **The instrument for "why is that window a black rectangle" (B358, B365).** A
/// `func_areaportalwindow` is drawn at `RemapValClamped( distance, FadeStartDist, FadeDist,
/// TranslucencyLimit, 1 )` (`c_func_areaportalwindow.cpp:138`), and the three knots come off the
/// wire — `DT_FuncAreaPortalWindow` is the only table that sends them. An entity whose table never
/// arrived has no knots at all, and the difference between "no knots" and "knots that happen to be
/// zero" is the whole question: the first draws at the brush's own renderamt, and the second hits
/// Valve's equal-knots branch and returns 1. **Both give a solid black panel**, and only this says
/// which.
///
/// **Reports the value the code USED.** The blend printed here is
/// <see cref="AreaPortalWindow.Blend"/> over <see cref="AreaPortalWindow.Distance"/>, the same two
/// calls `EntityModels` makes — not a second derivation that is free to be wrong (B243).
///
/// <code>
///   portal-windows z1800 300
///   portal-windows z1800 300 -416 -2300 130
/// </code>
///
/// The three optional numbers are an eye position; without them the probe reports the knots and
/// says so, because a blend with no viewer is not a number this can answer.
/// </remarks>
public sealed class PortalWindowProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "portal-windows";

    /// <inheritdoc/>
    public string Summary =>
        "each areaportal window's fade knots and blend: portal-windows <demo> [tick] [x y z]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("portal-windows <demo> [tick] [x y z]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        int tick = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        (float X, float Y, float Z)? eye = arguments.Count > 4
            ? (float.Parse(arguments[2], CultureInfo.InvariantCulture),
               float.Parse(arguments[3], CultureInfo.InvariantCulture),
               float.Parse(arguments[4], CultureInfo.InvariantCulture))
            : null;

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        List<SceneProp> windows =
            [.. props.Where(prop => prop.ClassName == "CFuncAreaPortalWindow")];

        output.WriteLine(
            $"{Path.GetFileName(path)} tick {tick.ToString(CultureInfo.InvariantCulture)}: " +
            $"{windows.Count.ToString(CultureInfo.InvariantCulture)} areaportal windows of " +
            $"{props.Count.ToString(CultureInfo.InvariantCulture)} props" +
            $"{(eye is { } from ? $", eye ({from.X:0} {from.Y:0} {from.Z:0})" : ", no eye given")}");

        int knotted = 0;

        foreach (SceneProp window in windows.OrderBy(prop => prop.EntityIndex))
        {
            string where = string.Create(
                CultureInfo.InvariantCulture,
                $"  entity {window.EntityIndex} model '{window.ModelPath}' " +
                $"at ({window.Pose.X:0}, {window.Pose.Y:0}, {window.Pose.Z:0}) " +
                $"rendermode {window.Pose.RenderMode} renderamt {window.Pose.RenderAlpha}");

            if (window.Pose.PortalWindow is not { } knots)
            {
                // **The finding, when it is this one.** No knots means `DT_FuncAreaPortalWindow`
                // never sent for this entity, so nothing overrides the brush's own renderamt — and
                // harvest's window brushes are `TOOLS/TOOLSBLACK` at 255.
                output.WriteLine($"{where}  NO FADE KNOWN — the table never sent for this entity");
                continue;
            }

            knotted++;

            output.WriteLine(
                $"{where}  fade {knots.FadeStart.ToString("0.#", CultureInfo.InvariantCulture)}" +
                $"..{knots.FadeEnd.ToString("0.#", CultureInfo.InvariantCulture)} " +
                $"limit {knots.TranslucencyLimit.ToString("0.##", CultureInfo.InvariantCulture)}" +
                Blend(window, knots, eye));
        }

        output.WriteLine(
            $"{knotted.ToString(CultureInfo.InvariantCulture)} of " +
            $"{windows.Count.ToString(CultureInfo.InvariantCulture)} carry their fade distances.");
    }

    private static string Blend(
        SceneProp window,
        (float FadeStart, float FadeEnd, float TranslucencyLimit) knots,
        (float X, float Y, float Z)? eye)
    {
        if (eye is not { } from)
        {
            return string.Empty;
        }

        // The window's own origin as the box, because a probe cannot reach the brush model's
        // bounds — stated rather than quietly substituted, since the engine measures to the box.
        float distance = AreaPortalWindow.Distance(
            from,
            (window.Pose.X, window.Pose.Y, window.Pose.Z),
            (window.Pose.X, window.Pose.Y, window.Pose.Z));

        float blend = AreaPortalWindow.Blend(
            distance, knots.FadeStart, knots.FadeEnd, knots.TranslucencyLimit);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"  distance-to-ORIGIN {distance:0} blend {blend:0.###} " +
            $"({(int)MathF.Round(blend * 255f)} of 255)");
    }
}
