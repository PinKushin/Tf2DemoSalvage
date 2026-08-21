using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// How much of the corpus actually hangs items from an attachment.
/// </summary>
/// <remarks>
/// **Before implementing placement, ask whether anything uses it.** This project has spent whole
/// sessions on mechanisms the data never exercises — zero-frame animation data and local hierarchy
/// were both read out of the SDK and both turned out absent from every animation being posed.
///
/// RISKS B82 was reported from the screen, so something does use it; this says how much, and on
/// which eras.
/// </remarks>
public sealed class AttachmentUseTests
{
    [Test]
    public void AttachmentPoint_AcrossTheCorpus_IsUsedByRealItems()
    {
        int demosWithAny = 0;
        List<string> lines = [];

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = TimelineCache.For(path);

            List<ScenePropTrack> attached =
                [.. timeline.Props.Where(track => track.AttachmentPoint is not null)];

            if (attached.Count == 0)
            {
                continue;
            }

            demosWithAny++;

            lines.Add(
                $"{Path.GetFileName(path)}: {attached.Count} attached — " +
                string.Join(
                    ", ",
                    attached
                        .Take(6)
                        .Select(track =>
                            $"{Path.GetFileNameWithoutExtension(track.ModelPath)}" +
                            $"@{track.AttachmentPoint} on {track.AttachedTo} " +
                            $"from tick {track.FirstTick}")));
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, lines));
        TestContext.Out.WriteLine($"DEMOS WITH ATTACHED ITEMS: {demosWithAny}");

        // A positive control: an empty corpus would report zero and prove nothing about attachments.
        Corpus.FilesWithSchema().Count.ShouldBeGreaterThan(0);
    }
}
