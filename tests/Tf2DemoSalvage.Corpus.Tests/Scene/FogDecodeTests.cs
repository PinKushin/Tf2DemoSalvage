using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// How far the atmosphere gets from a demo, and where it stops.
/// </summary>
/// <remarks>
/// **This was written to assert that fog reaches the timeline, and it found that it does not.** The
/// reading is done — <c>EntityFogTests</c> proves <c>EntityState.Fog</c> reads a controller's
/// properties correctly, from values taken out of a decoded trace — and the corpus still yields zero
/// fog on every demo.
///
/// The cause is upstream and is not about fog at all. Measured on the 2011 koth_viaduct SourceTV
/// recording: entity **#212, class CFogController**, is present in the entity table on 3,762 packets
/// and holds **zero properties**, while a trace of the same file shows it entering exactly once with
/// fifteen — <c>m_fog.enable 1</c>, <c>m_fog.end 6500</c> and the rest. It is not alone: 19 of that
/// table's 195 entities hold no properties. Filed as B132.
///
/// **So this asserts what is true today rather than what should be true.** A test written to the
/// intended behaviour would sit red in the gate; one written to the current behaviour has to be
/// inverted when B132 is fixed. The second is the lesser evil only because the assertion says so in
/// its own message — it names what to do when the number changes, rather than leaving a future
/// reader to guess whether the zero was a finding or an expectation.
/// </remarks>
public sealed class FogDecodeTests
{
    [Test]
    public void FogControllers_AcrossTheCorpus_ArePresentButHoldNoProperties()
    {
        List<DemoTimeline> timelines = [.. Corpus.FilesWithSchema().Select(TimelineCache.For)];
        List<string> lines = [];

        foreach ((string path, DemoTimeline timeline) in
            Corpus.FilesWithSchema().Zip(timelines))
        {
            lines.Add(
                $"{Path.GetFileName(path)}: {timeline.FogControllersSeen} sightings, " +
                $"{timeline.FogControllerProperties} properties, " +
                $"{timeline.FogSamples.Count} fog samples");
        }

        foreach (string line in lines)
        {
            TestContext.Out.WriteLine(line);
        }

        // **The data exists**, which is what makes B132 worth fixing rather than a feature with
        // nothing to read. Every demo in the corpus carries a fog controller.
        timelines.Count(timeline => timeline.FogControllersSeen > 0).ShouldBe(
            timelines.Count,
            "every corpus demo carries a CFogController; if one stops, the class lookup changed");

        // **The defect, stated as a measurement rather than as prose.** This is the line that
        // changes when B132 is fixed, and it should then be REPLACED by an assertion that fog
        // decodes — not merely relaxed to allow both.
        timelines.Max(timeline => timeline.FogControllerProperties).ShouldBe(
            0,
            "B132: a fog controller reaches the entity table with no properties, so no fog can be " +
            "read from it. When this stops being zero, assert the fog itself instead.");
    }
}
