using System;
using System.IO;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The sentences a load shows, and the outcome each carries.</summary>
/// <remarks>
/// **Built inline in `MainForm` until 2026-08-26** (B188, D90). `MapProvider.Fetching` already
/// showed the pattern — a line the viewer says, kept beside the thing it describes — and six others
/// had not followed it.
///
/// **Wording is presentation, not view.** A second frontend shows the same sentences, and a window
/// concatenating them is a window deciding what the program says.
/// </remarks>
public sealed class DemoLoadResultTests
{
    private const string Path = @"D:\demos\season31\esea_match_13977649.dem";

    [Test]
    public void Opening_NamesTheFileWithoutItsFolder()
    {
        // **The file name, not the path.** A status bar is one line and an ESEA path is long enough
        // to push the interesting half off the end.
        DemoLoadResult.Opening(Path).ShouldBe("Opening esea_match_13977649.dem...");
    }

    [Test]
    public void Superseded_SaysWhyItWasDiscarded()
    {
        // Double-clicking two demos starts two decodes and the slower must stand aside. Saying so
        // matters: silence there reads as a load that failed.
        DemoLoadResult result = DemoLoadResult.Superseded(Path);

        result.Outcome.ShouldBe(DemoLoadOutcome.Superseded);
        result.Message.ShouldBe("discarding esea_match_13977649.dem: a newer demo was asked for");
    }

    [Test]
    public void Superseded_IsNotAFailure()
    {
        // **The distinction the outcome exists for.** A superseded load is the transport working as
        // designed, and reporting it as a failure would put an error on screen for a demo the person
        // deliberately moved on from.
        DemoLoadResult.Superseded(Path).Loaded.ShouldBeFalse();
        DemoLoadResult.Superseded(Path).Outcome.ShouldNotBe(DemoLoadOutcome.Failed);
    }

    [Test]
    public void CouldNotOpen_CarriesTheReasonAndNotJustTheFact()
    {
        // **The exception's message is the useful half.** "Could not open X" is true of a missing
        // file, a truncated file and a permissions error, and the person's next step differs for
        // each — the same reason `LeafVis.WhyNothing` names which silence it is.
        DemoLoadResult result =
            DemoLoadResult.CouldNotOpen(Path, new IOException("the file is in use"));

        result.Outcome.ShouldBe(DemoLoadOutcome.Failed);
        result.Message.ShouldBe("Could not open esea_match_13977649.dem: the file is in use");
    }

    [Test]
    public void CouldNotOpen_WithoutAFailure_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => DemoLoadResult.CouldNotOpen(Path, failure: null!));
    }
}
