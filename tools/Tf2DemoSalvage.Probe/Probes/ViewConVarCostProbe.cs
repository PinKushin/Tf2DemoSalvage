using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What one read of the <c>demo_*</c> ConVars costs, as <c>DemoPlayer.InterpolateViewpoint</c> now asks per call (D238).</summary>
/// <remarks>
/// **The production reader, <see cref="DemoSystems.ViewConVarsFrom"/>, over a stand-in for the watcher's values.** The
/// viewer's own lookup is the settings' ConVar table, then the console's (<c>SettingsPresenter.ConVar</c>, then
/// <c>ConfigConsole.ConVar</c>). The stand-in is two case-insensitive tables of every declared client ConVar, so each name
/// is looked up in both. It is an approximation of the lookups, and says so; the parse and the five names are the real ones.
/// A micro count, not a frame: nanoseconds a call over a long loop, five times, after a warm-up.
/// </remarks>
public sealed class ViewConVarCostProbe : IProbe
{
    private const int Calls = 1_000_000;

    /// <inheritdoc/>
    public string Name => "viewconvarcost";

    /// <inheritdoc/>
    public string Summary => "ns per read of the demo_* ConVars, the read InterpolateViewpoint makes per call: viewconvarcost";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);

        // Two tables of every client ConVar the project declares, at their defaults, as the settings and console hold them;
        // demo_* absent from the first so the read falls through to the second, the slower path.
        Dictionary<string, string> settings = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> console = new(StringComparer.OrdinalIgnoreCase);

        foreach (EngineConVar declared in EngineConVars.All.Where(declared => !declared.Replicated))
        {
            console[declared.Name] = declared.Default;

            if (!declared.Name.StartsWith("demo_", StringComparison.Ordinal))
            {
                settings[declared.Name] = declared.Default;
            }
        }

        string? Client(string name) => settings.TryGetValue(name, out string? value) ? value : console.GetValueOrDefault(name);

        DemoViewConVars sink = default;

        for (int warm = 0; warm < Calls / 10; warm++)
        {
            sink = DemoSystems.ViewConVarsFrom(Client);
        }

        List<double> nanoseconds = [];

        for (int run = 0; run < 5; run++)
        {
            long from = Stopwatch.GetTimestamp();

            for (int call = 0; call < Calls; call++)
            {
                sink = DemoSystems.ViewConVarsFrom(Client);
            }

            nanoseconds.Add(Stopwatch.GetElapsedTime(from).TotalNanoseconds / Calls);
        }

        nanoseconds.Sort();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"demo_* ConVar read: {nanoseconds[0]:0.0} ns min, {nanoseconds[2]:0.0} ns median of 5 x {Calls} calls ({settings.Count} + {console.Count} values); " +
            $"retime {sink.RetimeView}"));
    }
}
