using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which bone-merged entities name a parent attachment, by class and model (B69).</summary>
/// <remarks>
/// **The engine never reads `m_iParentAttachment` for an `EF_BONEMERGE` entity** — `CalcAbsolutePosition` returns through
/// `MoveToAimEnt` first (`c_baseentity.cpp:4387-4391`) — so every row this prints is an item whose placement changes when the
/// port stops honouring it. Walks each track's whole identity history (`FactsHistory`), not one tick.
/// **The control** is the same count for parented entities that are NOT merged, the path that does read the attachment: a
/// zero there on a real match says the instrument reads nothing.
/// <code>
///   merged-attachments &lt;demo&gt; [&lt;demo&gt; ...]
/// </code>
/// </remarks>
public sealed class MergedAttachmentProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "merged-attachments";

    /// <inheritdoc/>
    public string Summary => "bone-merged entities naming m_iParentAttachment, by class and model: merged-attachments <demo>...";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("merged-attachments <demo> [<demo> ...]");
            return;
        }

        foreach (string argument in arguments)
        {
            if (DemoCorpus.Find(argument, output) is not { } path)
            {
                output.WriteLine($"no demo matches '{argument}'");
                continue;
            }

            Report(output, path);
        }
    }

    private static void Report(TextWriter output, string path)
    {
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        int merged = 0;
        int parented = 0;
        int parentedNamed = 0;
        Dictionary<(string Class, string Model, int Point), int> named = [];

        foreach (ScenePropTrack track in timeline.Props)
        {
            bool countedMerged = false;
            bool countedParented = false;
            HashSet<(string, string, int)> seen = [];

            foreach ((int _, ScenePropFacts facts) in track.FactsHistory)
            {
                if (facts.AttachedTo is null)
                {
                    continue;
                }

                if (!facts.BoneMerged)
                {
                    parented += countedParented ? 0 : 1;
                    parentedNamed += !countedParented && facts.AttachmentPoint is not null ? 1 : 0;
                    countedParented = true;
                    continue;
                }

                merged += countedMerged ? 0 : 1;
                countedMerged = true;

                if (facts.AttachmentPoint is { } point && seen.Add((facts.ClassName, facts.ModelPath, point)))
                {
                    named[(facts.ClassName, facts.ModelPath, point)] = named.GetValueOrDefault((facts.ClassName, facts.ModelPath, point)) + 1;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {timeline.Props.Count} tracks; bone-merged {merged}, naming an attachment " +
            $"{named.Values.Sum()}; control: parented not merged {parented}, naming one {parentedNamed}"));

        foreach (((string type, string model, int point), int count) in named.OrderByDescending(pair => pair.Value))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count,4} x {type} '{model}' attachment {point}"));
        }
    }
}
