using System;
using System.IO;
using Tf2DemoSalvage.Cli;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Cli.Tests;

/// <summary>
/// The progress bar.
/// </summary>
/// <remarks>
/// Testable at all because the redraw rule is "the rendered text changed" rather than "enough
/// time has passed". A clock-based throttle would make these assertions depend on machine
/// speed, which is the difference between a test that measures a rule and one that measures
/// how busy the runner was.
/// </remarks>
public sealed class ProgressBarTests
{
    private static DumpProgress At(int completed) => new("Scanning", completed, 100);

    [Fact]
    public void Report_DrawsTheBarWithItsStage()
    {
        StringWriter output = new();
        using ProgressBar bar = new(output, enabled: true);

        bar.Report(At(50));

        output.ToString().ShouldContain("Scanning");
        output.ToString().ShouldContain("50%");
    }

    [Fact]
    public void RepeatedIdenticalProgress_IsDrawnOnce()
    {
        // The throttle. A 120,000-command demo reports hundreds of times per visible percentage
        // point, so this is the difference between one line of output and a hundred thousand.
        StringWriter output = new();
        using ProgressBar bar = new(output, enabled: true);

        bar.Report(At(50));
        int afterFirst = output.ToString().Length;
        bar.Report(At(50));
        bar.Report(At(50));

        output.ToString().Length.ShouldBe(afterFirst);
    }

    [Fact]
    public void ProgressThatChangesTheBar_IsDrawnAgain()
    {
        // The control for the test above. Without it, "drawn once" and "never drawn again"
        // are indistinguishable, and a bar that froze after its first report would pass.
        StringWriter output = new();
        using ProgressBar bar = new(output, enabled: true);

        bar.Report(At(10));
        int afterFirst = output.ToString().Length;
        bar.Report(At(90));

        output.ToString().Length.ShouldBeGreaterThan(afterFirst);
        output.ToString().ShouldContain("90%");
    }

    [Fact]
    public void EachDraw_StartsWithACarriageReturn()
    {
        // What makes it overwrite itself rather than scroll. A bar drawn with newlines would
        // still show progress and would still pass every other test here.
        StringWriter output = new();
        using ProgressBar bar = new(output, enabled: true);

        bar.Report(At(10));
        bar.Report(At(90));

        output.ToString().Split('\r').Length.ShouldBe(3);   // "", first draw, second draw
    }

    [Fact]
    public void Disabled_DrawsNothing()
    {
        StringWriter output = new();
        using ProgressBar bar = new(output, enabled: false);

        bar.Report(At(50));
        bar.Finish();

        output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Finish_EndsTheLineOnlyIfSomethingWasDrawn()
    {
        // A run that drew nothing must not leave a stray blank line behind - standard error is
        // shared with real diagnostics.
        StringWriter drew = new();
        ProgressBar drawing = new(drew, enabled: true);
        drawing.Report(At(50));
        drawing.Finish();
        drew.ToString().ShouldEndWith(Environment.NewLine);

        StringWriter silent = new();
        ProgressBar quiet = new(silent, enabled: true);
        quiet.Finish();
        silent.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Finish_IsIdempotent()
    {
        // Dispose calls Finish, and Run calls it explicitly before printing its summary line,
        // so the two paths overlap by design.
        StringWriter output = new();
        using ProgressBar bar = new(output, enabled: true);

        bar.Report(At(50));
        bar.Finish();
        string afterFirst = output.ToString();
        bar.Finish();

        output.ToString().ShouldBe(afterFirst);
    }

    [Fact]
    public void NullWriter_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new ProgressBar(null!, enabled: true));
    }
}
