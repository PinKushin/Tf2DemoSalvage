using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every weapon the recorder holds, as a list of CHANGES with the tick each began.
/// </summary>
/// <remarks>
/// **Written to answer "which tick shows the thing you saw".** The owner: *"the red sleeve was seen
/// when i switched to the solly shotgun, so if you look for those ticks you can see it"* — and
/// finding that by scrubbing is minutes of somebody's attention for a question the timeline can
/// answer outright.
///
/// Reports the OWNER as well as the model, because the viewmodel's owner is what decides its skin
/// family — `CEconItemView::GetSkin( iTeam, … )` takes `pOwner->GetTeamNumber()` — and a viewmodel
/// that names no owner cannot be team-coloured at all (B242).
///
/// <code>
///   viewmodels tf2-2026-pub-pov-clean
///   viewmodels tf2-2026-pub-pov-clean shotgun
/// </code>
/// </remarks>
public sealed class ViewmodelProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "viewmodels";

    /// <inheritdoc/>
    public string Summary => "what the recorder holds, tick by change: viewmodels <demo> [substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("viewmodels <demo> [model substring]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        string filter = arguments.Count > 1 ? arguments[1] : string.Empty;

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        if (timeline.RecorderEntityIndex is not { } recorder)
        {
            output.WriteLine("The demo names no recorder, so it has no first-person weapon.");
            return;
        }

        TimelineViewmodels viewmodels = new(timeline);

        output.WriteLine(
            $"{Path.GetFileName(path)} recorder {recorder.ToString(CultureInfo.InvariantCulture)}, "
            + $"ticks {timeline.FirstTick.ToString(CultureInfo.InvariantCulture)}"
            + $"-{timeline.LastTick.ToString(CultureInfo.InvariantCulture)}, filter '{filter}'");

        string was = string.Empty;
        int changes = 0;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick++)
        {
            if (viewmodels.MainHandAt(tick, recorder) is not { } weapon)
            {
                continue;
            }

            if (string.Equals(weapon.ModelPath, was, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            was = weapon.ModelPath;

            if (filter.Length > 0
                && !weapon.ModelPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The team of the owner is what the skin family comes from, so an absent owner is the
            // interesting case rather than a detail.
            output.WriteLine(
                $"  tick {tick,7}  owner "
                + $"{weapon.OwnerEntityIndex?.ToString(CultureInfo.InvariantCulture) ?? "none",5}  "
                + weapon.ModelPath);

            if (++changes > 60)
            {
                output.WriteLine("  ... more changes than are worth printing");
                return;
            }
        }

        if (changes == 0)
        {
            output.WriteLine("  the recorder never held anything matching that");
        }
    }
}
