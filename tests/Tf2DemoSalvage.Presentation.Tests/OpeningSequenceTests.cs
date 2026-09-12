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

    /// <summary>Frames a capture run waits for a demo before giving up (B401).</summary>
    /// <remarks>
    /// **Far above `Opening`, as the viewer's own is**, so a test about the ordering of the two
    /// halves never trips the backstop by accident and a test about the backstop has to reach past
    /// every other boundary in the class to get there.
    /// </remarks>
    private const int Patience = 2_000;

    [Test]
    public void Advance_OnTheSettleFrame_AsksForTheOpeningState()
    {
        // **Not on the first frame, which is the whole reason for a countdown.** Applying the
        // opening state at once seeks into a scene that is not ready — the world has not settled,
        // the textures upload on a later frame — and then latches itself done. The countdown exists
        // to let all of that happen first.
        OpeningSequence sequence = new(shotPath: null, Opening, Settle, Patience);

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
        OpeningSequence sequence = new(shotPath: null, Opening, Settle, Patience);

        Run(sequence, Settle);
        sequence.MarkApplied();

        sequence.Advance().ShouldBe(OpeningStep.Nothing);
        sequence.Finished.ShouldBeTrue();
    }

    [Test]
    public void Advance_WithAShotAsked_CapturesWhenTheCountdownRunsOut()
    {
        // **`MarkApplied` first, and this test USED to omit it** — it ran the countdown out with
        // the opening state never applied and asserted a capture, which is B401 written down as an
        // expectation. A shot before the seek is a photograph of an empty viewport.
        OpeningSequence sequence = new(@"D:\shot.png", Opening, Settle, Patience);

        Run(sequence, Settle);
        sequence.MarkApplied();
        Run(sequence, Opening);

        sequence.Advance().ShouldBe(OpeningStep.Capture);
    }

    [Test]
    public void Advance_WithAShotAskedAndNoDemoEverLoaded_NeverCaptures()
    {
        // **The defect B401 names, at the level that decides it.** The window refuses to apply the
        // opening state while there is no demo, and says so by not calling `MarkApplied` — so this
        // sequence must go on asking rather than reaching the shutter. Ten times the whole opening
        // budget, because the real failure took 45 frames at ~300 fps, a seventh of a second, while
        // a map takes twenty seconds to load.
        OpeningSequence sequence = new(@"D:\shot.png", Opening, Settle, Patience);

        for (int frame = 0; frame < Opening * 10; frame++)
        {
            sequence.Advance().ShouldNotBe(OpeningStep.Capture, $"nothing is loaded at frame {frame}");
        }

        sequence.TakeShotPath().ShouldBe(@"D:\shot.png", "the capture is still owed");
    }

    [Test]
    public void Advance_WhenNoDemoArrivesWithinThePatience_GivesUpWithoutCapturing()
    {
        // **A wait on a condition has to be able to end.** Ordering the capture after the opening
        // state is what B401 needed, and the first version of it turned a lost demo argument into a
        // viewer drawing an empty viewport at 299 fps until an outer timeout killed it. Giving up
        // writes nothing, which is the whole point: an empty PNG reads as "the viewer drew nothing"
        // rather than "the viewer had nothing".
        OpeningSequence sequence = new(@"D:\shot.png", Opening, Settle, Patience);

        for (int frame = 0; frame < Patience; frame++)
        {
            sequence.Advance().ShouldNotBe(OpeningStep.GiveUp, $"frame {frame} is still patient");
        }

        sequence.Advance().ShouldBe(OpeningStep.GiveUp);
    }

    [Test]
    public void Advance_WithNoShotAskedAndNoDemo_WaitsForEverRatherThanGivingUp()
    {
        // **The backstop is for automation, and a person is not automation.** Someone who opened
        // the viewer to look at it has asked for no capture and is waiting for nothing; a viewer
        // that closed itself out from under them because no demo had been picked yet would be a
        // worse defect than the one the backstop exists for.
        OpeningSequence sequence = new(shotPath: null, Opening, Settle, Patience);

        for (int frame = 0; frame < Patience * 3; frame++)
        {
            sequence.Advance().ShouldNotBe(OpeningStep.GiveUp, $"nobody is waiting at frame {frame}");
        }
    }

    [Test]
    public void Advance_AfterADemoArrivesLate_StillCaptures()
    {
        // **The other half of the same rule: waiting for the demo must not mean waiting for ever.**
        // A demo that loads on frame 500 gets its settle wait measured from THERE, and the shutter
        // fires afterwards — which is what a `--shot` run on a map that has to be fetched needs.
        OpeningSequence sequence = new(@"D:\shot.png", Opening, Settle, Patience);

        Run(sequence, 500);
        sequence.MarkApplied();

        Run(sequence, Opening);

        sequence.Advance().ShouldBe(OpeningStep.Capture);
    }

    [Test]
    public void Advance_AfterCapturing_DoesNotCaptureTwice()
    {
        // **The capture closes the window, so a second one is a race rather than a duplicate file.**
        // The path is taken rather than read, which is what makes it once.
        OpeningSequence sequence = new(@"D:\shot.png", Opening, Settle, Patience);

        Run(sequence, Settle);
        sequence.MarkApplied();
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
        OpeningSequence sequence = new(shotPath: null, Opening, Settle, Patience);

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
        OpeningSequence sequence = new(shotPath: null, Opening, Settle, Patience);

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
