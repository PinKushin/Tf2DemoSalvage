using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Whether anything ever pushes a rope: the <c>SetForce</c> input on the maps, and the impulse or shake in demos (B478).</summary>
/// <remarks>
/// **Two sources reach <c>m_flImpulse</c>**: the rope's own entity message, which only <c>CRopeKeyframe::InputSetForce</c>
/// sends (`rope.cpp:520-546`), and the <c>ShakeRopes</c> client effect. A map wires the input in an output's text, so the
/// map half reads every installed map's decompressed entity lump for <c>SetForce</c>; the control is the count of
/// <c>move_rope</c> and <c>keyframe_rope</c>, which must be nonzero. The demo half counts what the timeline took — rope
/// impulses and dispatches named <c>ShakeRopes</c> — beside the dispatch total as its control.
/// </remarks>
public sealed class RopeEventsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "rope-events";

    /// <inheritdoc/>
    public string Summary =>
        "whether anything pushes a rope: SetForce on the installed maps, impulses and ShakeRopes in demos: rope-events [demo|directory]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            Maps(output);
            return;
        }

        IEnumerable<string> demos = Directory.Exists(arguments[0])
            ? Directory.EnumerateFiles(arguments[0], "*.dem", SearchOption.AllDirectories)
            : [arguments[0]];

        foreach (string demo in demos)
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
            int shakes = timeline.Dispatches.All.Count(dispatch =>
                string.Equals(timeline.Dispatches.Names.Name(dispatch.Name), "ShakeRopes", StringComparison.Ordinal));
            int ropes = timeline.Props.Count(track => track.ClassName == RopeImpulseFeed.RopeClassName);

            output.WriteLine(
                $"{Path.GetFileName(demo)}: {ropes} rope tracks, {timeline.RopeImpulses.All.Count} rope impulses, " +
                $"{shakes} ShakeRopes of {timeline.Dispatches.All.Count} dispatches");
        }
    }

    /// <summary><c>LUMP_ENTITIES</c>, the first lump (`bspfile.h`).</summary>
    private const int EntityLump = 0;

    private static void Maps(TextWriter output)
    {
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find("koth_harvest_final") is not { } anyMap)
        {
            output.WriteLine("No installed maps found.");
            return;
        }

        int maps = 0;
        int ropeEntities = 0;
        int setForce = 0;

        foreach (string map in Directory.EnumerateFiles(Path.GetDirectoryName(anyMap) ?? ".", "*.bsp").Order(StringComparer.Ordinal))
        {
            byte[] file = File.ReadAllBytes(map);
            BspHeader header = BspHeader.Parse(file);
            string text = Encoding.Latin1.GetString(BspLumpData.Read(file, header.Lump(EntityLump)).Span);

            maps++;
            ropeEntities += BspEntities.Parse(Encoding.Latin1.GetBytes(text))
                .Count(entity => entity.ClassName is "move_rope" or "keyframe_rope");

            int here = Occurrences(text, "SetForce");

            if (here > 0)
            {
                output.WriteLine($"{Path.GetFileNameWithoutExtension(map)}: {here} SetForce");
            }

            setForce += here;
        }

        output.WriteLine($"{maps} maps, {ropeEntities} rope entities (the control), {setForce} SetForce in their outputs");
    }

    private static int Occurrences(string text, string word)
    {
        int count = 0;

        for (int at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }
}
