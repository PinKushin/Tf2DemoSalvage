using System;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// That the kill annotation reaches the actual dump, not just its own unit tests.
/// </summary>
/// <remarks>
/// **This test exists because the feature shipped as a no-op and its unit tests were green.**
/// `KillDescription` was correct and fully covered; `DemoTextDumper` matched the field value against
/// `int`, and game event fields are typed by their definition — `customkill` arrives as a **byte**.
/// So the pattern matched nothing, no annotation was ever produced, and every assertion still
/// passed.
///
/// That is the shape recorded in memory as `measure-the-output-not-the-capability`: a component
/// tested in isolation proves the component, and says nothing about whether anything calls it with
/// the values that actually occur. The only thing that caught it was reading the real output.
///
/// So this asserts on the rendered text of a real demo, which is the artefact a person reads.
/// </remarks>
public sealed class CorpusKillAnnotationTests
{
    [Test]
    public void ARealDeathRendersItsCustomKillInWords()
    {
        string path = Corpus.Demo("z1800");
        byte[] bytes = File.ReadAllBytes(path);

        StringWriter rendered = new();

        DemoTextDumper.Write(
            rendered,
            Path.GetFileName(path),
            DemoHeader.Parse(bytes),
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))],
            new DemoDumpOptions { IncludeGameEvents = true });

        string text = rendered.ToString();

        // The demo's first sampled death is a headshot with the Bazaar Bargain. Asserted as the
        // annotated pair rather than the word alone: "headshot" could appear in a weapon name or a
        // player name, and the pairing is what proves the annotation ran.
        text.ShouldContain("customkill=1 (headshot)");
    }

    [Test]
    public void TheKillFeedListsEveryDeathRatherThanTheEventSample()
    {
        // **Completeness is the entire point of the section**, so it is what gets asserted. The
        // game event section is capped and shows one death; this must show all 407.
        //
        // Derived from the demo's own event count rather than compared against a typed 407: the
        // count is printed in the same output, so the two can be checked against each other and the
        // test cannot drift from the corpus.
        string path = Corpus.Demo("z1800");
        byte[] bytes = File.ReadAllBytes(path);

        StringWriter rendered = new();

        DemoTextDumper.Write(
            rendered,
            Path.GetFileName(path),
            DemoHeader.Parse(bytes),
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))],
            new DemoDumpOptions { IncludeGameEvents = true });

        string[] lines = rendered.ToString().Split('\n');

        int declared = 0;
        int listed = 0;
        bool inFeed = false;

        foreach (string line in lines)
        {
            if (line.StartsWith("Kills", StringComparison.Ordinal))
            {
                inFeed = true;
                continue;
            }

            if (inFeed && line.TrimStart().StartsWith("tick ", StringComparison.Ordinal))
            {
                listed++;
            }
            else if (inFeed && line.Trim().Length == 0 && listed > 0)
            {
                inFeed = false;
            }

            // The count line from the game event section: "  player_death   407".
            if (line.TrimStart().StartsWith("player_death", StringComparison.Ordinal) &&
                int.TryParse(line.Trim()["player_death".Length..].Trim(), out int count))
            {
                declared = count;
            }
        }

        declared.ShouldBeGreaterThan(1, "the demo should contain many deaths");
        listed.ShouldBe(declared);

        // And the sentinel that made most lines read "(assist -1)" until it was noticed in the
        // output. Asserted on the rendered text because that is where it was wrong.
        rendered.ToString().ShouldNotContain("assist -1");
    }
}
