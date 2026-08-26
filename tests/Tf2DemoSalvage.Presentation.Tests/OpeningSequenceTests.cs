namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The countdown from a window opening to a capture being taken.</summary>
/// <remarks>
/// **Three fields and a countdown in `MainForm`** (B188, D90) — `_shotDelay`, `_shotPath` and
/// `_openingDone` — reachable only by launching the viewer with `--shot` and watching whether a file
/// appeared. It is a small state machine, and the acting it drives (apply the opening state, capture
/// the viewport, close the window) stays with the window.
///
/// **The split is `FramePacer`'s**: the decision is here, the act is the caller's. That is what makes
/// a countdown testable without waiting forty-five frames for anything.
/// </remarks>
public sealed class OpeningSequenceTests
{
    private const int Opening = 45;
    private const int Settle = 5;

    [Test]
    public void Advance_OnTheSettleFrame_AsksForTheOpeningState()
    {
        // **Not on the first frame, which is the whole reason for a countdown.** Applying the
        // opening state at once seeks into a scene that is not ready — the world has not settled,
        // the textures upload on a later frame — and then latches itself done. The countdown exists
        // to let all of that happen first.
        OpeningSequence sequence = new(shotPath: null, Opening, Settle);

        for (int frame = 0; frame < Settle - 1; frame++)
        {
            sequence.Advance().ShouldBe(OpeningStep.Nothing, $"frame {frame} is too early");
        }

        sequence.Advance().ShouldBe(OpeningStep.ApplyOpeningState);
    }

    [Test]
    public void Advance_WithNoShotAsked_StopsOnceTheOpeningIsApplied()
    {
        // A viewer nobody asked for a capture from must stop counting, or every frame for the rest
        // of the session runs a countdown that can never do anything.
        OpeningSequence sequence = new(shotPath: null, Opening, Settle);

        Run(sequence, Settle);
        sequence.MarkApplied();

        sequence.Advance().ShouldBe(OpeningStep.Nothing);
        sequence.Finished.ShouldBeTrue();
    }

    [Test]
    public void Advance_WithAShotAsked_CapturesWhenTheCountdownRunsOut()
    {
        OpeningSequence sequence = new(@"D:\shot.png", Opening, Settle);

        Run(sequence, Opening);

        sequence.Advance().ShouldBe(OpeningStep.Capture);
    }

    [Test]
    public void Advance_AfterCapturing_DoesNotCaptureTwice()
    {
        // **The capture closes the window, so a second one is a race rather than a duplicate file.**
        // The path is taken rather than read, which is what makes it once.
        OpeningSequence sequence = new(@"D:\shot.png", Opening, Settle);

        Run(sequence, Opening);

        sequence.Advance().ShouldBe(OpeningStep.Capture);
        sequence.TakeShotPath().ShouldBe(@"D:\shot.png");

        sequence.Advance().ShouldBe(OpeningStep.Nothing);
        sequence.TakeShotPath().ShouldBeNull();
    }

    [Test]
    public void Restart_AfterADemoOpens_CountsFromTheBeginningAgain()
    {
        // **A demo opened from the playlist arrives long after the frame the countdown fired on**,
        // so the opening state was being lost. Restarting measures the wait from the DEMO rather
        // than from the window, which is what the original reasoning wanted all along.
        OpeningSequence sequence = new(shotPath: null, Opening, Settle);

        Run(sequence, Settle);

        sequence.Restart();

        for (int frame = 0; frame < Settle - 1; frame++)
        {
            sequence.Advance().ShouldBe(OpeningStep.Nothing);
        }

        sequence.Advance().ShouldBe(OpeningStep.ApplyOpeningState);
    }

    [Test]
    public void Advance_WhenTheOpeningWasNeverApplied_AsksAgainAfterARestart()
    {
        // **`MarkApplied` is the WINDOW's to call**, because applying can fail: with no demo open
        // there is nothing to seek. A sequence that marked itself applied would count a refusal as
        // a success and never offer again.
        OpeningSequence sequence = new(shotPath: null, Opening, Settle);

        Run(sequence, Settle);

        sequence.Finished.ShouldBeFalse("nothing has confirmed the opening state was applied");
    }

    /// <summary>Advances a number of frames, ignoring what it asks for.</summary>
    private static void Run(OpeningSequence sequence, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            sequence.Advance();
        }
    }
}
