using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Tf2DemoSalvage.Probe;

/// <summary>
/// Runs one probe against the corpus and prints what it found.
/// </summary>
/// <remarks>
/// <code>
///   dotnet run --project tools/Tf2DemoSalvage.Probe                 # list them
///   dotnet run --project tools/Tf2DemoSalvage.Probe -- spy-draw     # run one
/// </code>
///
/// **Exit code 1 for an unknown name.** A probe host that shrugged at a typo would print a listing
/// and exit zero, which in a script is indistinguishable from a probe that found nothing — the same
/// shape as `dotnet test --filter` matching nothing and exiting green.
/// </remarks>
internal static class Program
{
    private static int Main(string[] arguments)
    {
        List<IProbe> probes =
        [
            .. Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(type => typeof(IProbe).IsAssignableFrom(type)
                    && type is { IsAbstract: false, IsInterface: false })
                .Select(type => (IProbe)Activator.CreateInstance(type)!)
                .OrderBy(probe => probe.Name, StringComparer.Ordinal),
        ];

        if (arguments.Length == 0)
        {
            List(probes);
            return 0;
        }

        string wanted = arguments[0];
        IProbe? chosen = probes.FirstOrDefault(
            probe => string.Equals(probe.Name, wanted, StringComparison.OrdinalIgnoreCase));

        if (chosen is null)
        {
            Console.Error.WriteLine($"No probe named '{wanted}'.");
            List(probes);
            return 1;
        }

        // The corpus is located from the running binary, so a probe run from anywhere finds the
        // same files the suite does. Announced, because a run restricted to gcor sees a different
        // corpus and a report that did not say so would be read as covering everything.
        if (DemoCorpus.Directory() is null)
        {
            Console.Error.WriteLine(
                "No corpus found. Expected tools/corpus/demos above " + AppContext.BaseDirectory);
            return 1;
        }

        chosen.Run(Console.Out, arguments[1..]);
        return 0;
    }

    private static void List(List<IProbe> probes)
    {
        Console.Out.WriteLine(
            probes.Count.ToString(CultureInfo.InvariantCulture) + " probes:");

        int width = probes.Count == 0 ? 0 : probes.Max(probe => probe.Name.Length);

        foreach (IProbe probe in probes)
        {
            Console.Out.WriteLine($"  {probe.Name.PadRight(width)}  {probe.Summary}");
        }
    }
}
