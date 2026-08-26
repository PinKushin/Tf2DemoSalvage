using System;
using System.IO;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// The playback controls, driven the way a person drives them.
/// </summary>
/// <remarks>
/// **Laid out like a video player** — jump to start, shuttle down, play, shuttle up, jump to end,
/// then the scrub bar and the readouts. That is what someone reaching for it expects, and the
/// shuttle ladder runs through reverse because this viewer can do something the engine cannot:
/// TF2 streams a demo forward and each snapshot is a delta on the last, so it has nothing to step
/// back into.
/// </remarks>
[TestFixture]
public sealed class TransportUiTests
{
    /// <summary>The one viewer this assembly runs, with its demo already open.</summary>
    private static ViewerApplication _viewer => ViewerSession.App;

    [Test]
    public void Transport_ShuttleIntoReverse_UpdatesTheSpeedReadout()
    {
        // **The readout is the point.** A speed that changes with nothing to show it leaves the
        // user guessing whether the button did anything, and reverse especially needs saying: a
        // demo running backwards at a quarter speed looks a lot like one that has stalled.
        // **Derived from the bar's own ladder, not typed out.** Both numbers below were hardcoded
        // against a wording the constructor used and no update ever produced, so the test failed on
        // the first press of faster and the failure read as a broken transport bar. Asking
        // TransportBar.Speeds means a change to the ladder or to the wording moves the test with it
        // instead of breaking it.
        AutomationElement speed = _viewer.Find(TransportBar.SpeedLabelId);

        // **Put the speed somewhere known first, because this fixture is shared and arrival state
        // is not a given** — the same lesson `Transport_JumpToEnd_MovesTheScrubBar` records forty
        // lines below, learned again the moment a second test touched the speed.
        //
        // This asserted the readout was 1x on arrival, which held only while nothing else in the
        // assembly changed the speed. `SpeedSlider_AtEachEnd_...` now leaves it at 8x, and this went
        // red against a shuttle that works. The claim here is "the buttons step the ladder", so it
        // is measured from a speed this test set itself.
        StepTo(1d);

        speed.Name.ShouldBe(
            TransportBar.SpeedDescription(1), "the shuttle was stepped to real time");

        _viewer.Find(TransportBar.FasterButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name == TransportBar.SpeedDescription(2),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "The speed readout did not follow the faster button.");

        // Down past one, past the quarter speeds, and into reverse. The ladder is
        // -4 -2 -1 -0.5 -0.25 0.25 0.5 1 2 4 8, so from 2x that is four steps to -0.25x and five to
        // -0.5x. It said five to -0.25x, which nothing caught because the assertion below only asks
        // whether the wording says reversed.
        for (int step = 0; step < 5; step++)
        {
            _viewer.Find(TransportBar.SlowerButtonId).AsButton().Invoke();
        }

        // **The exact speed, not merely "something reversed".** Contains("reversed") is true for
        // every one of the five reverse rungs, so it cannot tell a shuttle that stepped correctly
        // from one that ran to the end of the ladder — and it is why the off-by-one above sat in
        // the comment unnoticed.
        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name ==
                TransportBar.SpeedDescription(-0.5),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "The shuttle never reached −0.5x, five steps down from 2x.");

        TestContext.Out.WriteLine(
            $"TRANSPORT speed reads '{_viewer.Find(TransportBar.SpeedLabelId).Name}'");
    }

    [Test]
    public void Transport_JumpToEnd_MovesTheScrubBar()
    {
        // A jump is a seek and must be heard as one, unlike playback reporting where it has got
        // to - the two go through different paths in the bar for exactly that reason.
        // **Read through the tick readout, not the scrub bar's RangeValue pattern.** That pattern
        // is not supported on this control — "The requested pattern 'RangeValue' is not supported",
        // with a null native pattern underneath — so the previous version of this test could never
        // have passed against any behaviour. It threw in 43 ms, before the button was ever pressed,
        // which is worth recognising: a test that fails faster than the thing it measures could
        // possibly happen is failing at the instrument, not at the application.
        //
        // The readout is also the better measurement. It is what the person watching actually sees,
        // and it says which tick rather than only that a slider moved.
        string StartTick() => _viewer.Find(TransportBar.TickLabelId).Name;

        // **Put playback somewhere known first, because this fixture is shared and arrival state is
        // not a given.** As written this read the current tick, pressed End, and waited for the
        // reading to CHANGE — which is a test of "the button moved playback from wherever it
        // happened to be". Another fixture leaves the demo at its last tick, so End had nothing to
        // do, the reading never changed, and the failure said "Jumping to the end did not move
        // playback; the tick still reads tick 8065 / 8065" against a button that works.
        //
        // The claim is "End goes to the end", so it is measured as that: seek to the start, prove
        // we are not at the end, then press End and require the two halves of the readout to meet.
        // That is independent of where playback started and it is a stronger statement than
        // "something changed".
        _viewer.Find(TransportBar.StartButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => !AtEnd(StartTick()),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage:
                $"Jumping to the start left playback at the end; the tick reads {StartTick()}.");

        string before = StartTick();

        _viewer.Find(TransportBar.EndButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => AtEnd(StartTick()),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: $"Jumping to the end did not reach it; the tick reads {StartTick()}.");

        string atEnd = StartTick();

        _viewer.Find(TransportBar.StartButtonId).AsButton().Invoke();

        Retry.WhileFalse(
            () => StartTick() != atEnd,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: $"Jumping to the start did not move playback; the tick still reads {atEnd}.");

        // **Stated rather than left to the retry.** Waiting for a condition and asserting one are
        // different things: the retry says when to stop looking, and this says what the answer had
        // to be. An analyser reads only the second, and so does anyone deciding what this test
        // claims - the timeout message is not a prediction, it is an excuse prepared in advance.
        // Back to exactly where the demo opened, which is the first tick — asserted against the
        // reading taken before anything was pressed rather than against a formatted string, so it
        // stays true whatever the readout's wording is and whatever tick a demo starts on. Demo
        // ticks do not begin at zero, so a literal 0 here would be wrong for most files.
        StartTick().ShouldBe(before, "the start button seeks back to the demo's first tick");
    }

    [Test]
    public void SpeedSlider_AtEachEnd_ReachesSpeedsTheButtonsCannot()
    {
        // **The point of D97, and the only test that can show it.** The model went continuous from
        // 0.01 to 8 while the buttons still stepped eleven fixed stops, so the fine band existed and
        // no person could reach it. This drives the slider to each end and asks the readout.
        //
        // **Driven by the keyboard, because this assembly already knows a `TrackBar` cannot be set
        // through automation.** `Transport_JumpToEnd_MovesTheScrubBar` records it thirty lines above
        // — *"The requested pattern 'RangeValue' is not supported"* — and `ViewerSession` passes
        // `--tick` on the command line for the same reason rather than dragging the scrub bar. The
        // first draft of this test asked for `RangeValue` anyway and got "Native pattern is null",
        // which is that note being rediscovered the expensive way.
        //
        // `Home` and `End` are what a person pressing the control would use, so this is real input
        // rather than a poke at the model, and no value has to be typed out.
        //
        // **`Home` reaches −8x, which is NOT one of the stops** (they run
        // −4 −2 −1 −0.5 −0.25 0.25 0.5 1 2 4 8). So it proves two things at once: the slider spans
        // into reverse, and it reaches past the ladder. A speed the buttons could also produce would
        // pass whether or not the slider did anything.
        string Readout() => _viewer.Find(TransportBar.SpeedLabelId).Name;

        _viewer.Find(TransportBar.SpeedBarId).Focus();
        _viewer.PressKey(VirtualKeyShort.HOME);

        // **The message reports what it SAW, not only what it wanted.** The first version said only
        // "did not reach the fastest reverse speed", so two runs were spent guessing at a cause the
        // failure could have named — and the cause was that `Home` never reached the slider at all,
        // which a readout still showing 1x would have said immediately.
        Retry.WhileFalse(
            () => Readout() == TimeScale.From(-TimeScale.Fastest).Description(),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage:
                $"Home on the focused speed slider did not reach {TimeScale.From(-TimeScale.Fastest).Label()}; "
                + $"the readout says '{Readout()}'.");

        _viewer.Find(TransportBar.SpeedLabelId).Name.ShouldBe(
            TimeScale.From(-TimeScale.Fastest).Description(),
            "the left end runs backwards at a speed no button offers");

        // **The control, and it is the half that matters.** Without it a readout stuck on "reversed"
        // — or a slider whose halves both mean forward — would pass everything above.
        _viewer.PressKey(VirtualKeyShort.END);

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name
                == TimeScale.From(TimeScale.Fastest).Description(),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "The slider's right end did not reach the fastest forward speed.");

        _viewer.Find(TransportBar.SpeedLabelId).Name.ShouldBe(
            TimeScale.From(TimeScale.Fastest).Description(),
            "and the right end runs forwards");

        TestContext.Out.WriteLine(
            $"TRANSPORT speed slider spans {TimeScale.From(-TimeScale.Fastest).Label()} to "
            + $"{TimeScale.From(TimeScale.Fastest).Label()}");
    }

    /// <summary>Drives the shuttle to a known speed, wherever it started.</summary>
    /// <param name="speed">One of <see cref="TimeScale.ShuttleStops"/>.</param>
    /// <remarks>
    /// **Bottoms out first, then counts up**, so it needs no knowledge of where the speed was. The
    /// ladder is eleven stops, so eleven presses of slower reaches the bottom from anywhere, and the
    /// index of the wanted speed is how many presses of faster then reach it.
    ///
    /// The alternative — reading the readout and stepping toward it — would depend on parsing a
    /// label this file deliberately does not parse.
    /// </remarks>
    private static void StepTo(double speed)
    {
        for (int press = 0; press < TimeScale.ShuttleStops.Length; press++)
        {
            _viewer.Find(TransportBar.SlowerButtonId).AsButton().Invoke();
        }

        for (int press = 0; press < Array.IndexOf(TimeScale.ShuttleStops, speed); press++)
        {
            _viewer.Find(TransportBar.FasterButtonId).AsButton().Invoke();
        }

        Retry.WhileFalse(
            () => _viewer.Find(TransportBar.SpeedLabelId).Name
                == TimeScale.From(speed).Description(),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage:
                $"Could not step the shuttle to {TimeScale.From(speed).Label()}; the readout says "
                + $"'{_viewer.Find(TransportBar.SpeedLabelId).Name}'.");
    }

    /// <summary>Whether a tick readout says playback has reached the last tick.</summary>
    /// <param name="readout">The label's text, formatted by <see cref="DemoPosition.Label"/>.</param>
    /// <remarks>
    /// **Asks `DemoPosition` rather than parsing** (D90). This used to split on `/` and compare the
    /// halves, under a comment naming the arrangement: *"the format is the bar's own"*. That is two
    /// places knowing one format, so a rewording would have reddened this with nothing wrong. The
    /// parser now lives beside the writer and a round-trip test pins them together.
    ///
    /// A readout of an unexpected shape reads as `null` and is not a claim either way — that is a
    /// different failure and the caller's retry message shows it verbatim.
    /// </remarks>
    private static bool AtEnd(string readout) => DemoPosition.Read(readout)?.AtEnd ?? false;
}
