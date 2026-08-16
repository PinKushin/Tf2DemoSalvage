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
}
