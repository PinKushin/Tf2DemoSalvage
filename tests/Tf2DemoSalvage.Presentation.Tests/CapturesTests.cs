using System;
using System.IO;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Where screenshots go, what they are called, and how many are kept.</summary>
/// <remarks>
/// **This was `MainForm.CaptureFolder`, `CaptureName` and `CapturesKept`** (B208).
/// </remarks>
public sealed class CapturesTests
{
    // Collision and ordering are covered by `CaptureNameTests`, which moved here from
    // `Viewer3D.Tests` with the code and carries the 2026-08-20 measurement that produced them.

    [Test]
    public void Name_ForAMoment_MatchesThePatternRetentionDeletesBy()
    {
        // **The two constants have to agree or nothing is ever pruned** — and deleting zero files
        // looks exactly like having nothing to delete, so the failure would be silent. Asserted
        // against the glob rather than restating the prefix, which would just be a third copy.
        string name = Captures.Name(new DateTime(2026, 8, 26, 14, 30, 15, 123, DateTimeKind.Utc));

        string prefix = Captures.Pattern[..Captures.Pattern.IndexOf('*', StringComparison.Ordinal)];
        string suffix = Captures.Pattern[(Captures.Pattern.IndexOf('*', StringComparison.Ordinal) + 1)..];

        name.ShouldStartWith(prefix);
        name.ShouldEndWith(suffix);
    }

    [Test]
    public void Folder_WithNothingAsked_IsTheFallback()
    {
        Captures.Folder(wanted: null, fallback: "C:/logs", new RecordingLogger())
            .ShouldBe("C:/logs");

        Captures.Folder(wanted: "   ", fallback: "C:/logs", new RecordingLogger())
            .ShouldBe("C:/logs");
    }

    [Test]
    public void Folder_WithAWritableChoice_UsesItAndCreatesIt()
    {
        // **The control**: without a case that succeeds, "returns the fallback" would be satisfied
        // by a method that ignored `wanted` entirely.
        string wanted = Path.Combine(
            Path.GetTempPath(), "tf2ds-shots-" + Guid.NewGuid().ToString("N")[..8]);

        Captures.Folder(wanted, fallback: "C:/logs", new RecordingLogger()).ShouldBe(wanted);
        Directory.Exists(wanted).ShouldBeTrue("deciding on a folder is what creates it");
    }

    [Test]
    public void Folder_WithAnUnusableChoice_FallsBackAndNamesThePathItRefused()
    {
        // **Falls back rather than failing** — a screenshot is a diagnostic and must not stop the
        // viewer — **but says so**, because a silent fallback leaves the user hunting for files in
        // the folder they configured.
        RecordingLogger log = new();

        Captures.Folder(wanted: "\0:/nope", fallback: "C:/logs", log).ShouldBe("C:/logs");

        log.Count("cannot write captures to").ShouldBe(1);
    }

    [Test]
    public void Folder_WithoutALogger_Refuses()
    {
        Should.Throw<ArgumentNullException>(
            () => Captures.Folder("x", "y", log: null!));
    }
}
