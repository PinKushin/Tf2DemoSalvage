using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Sampling a moment and handing it to the scene.</summary>
/// <remarks>
/// **This was `MainForm.ShowMoment`, and it had no tests because it could not have any.** Sampling
/// went straight through a `DemoTimeline`, whose constructor is private and whose `Build` takes the
/// bytes of a real file — so reaching this code meant shipping a demo into the test project and
/// launching a window. `IMomentSource` is what makes the stub below possible.
/// </remarks>
public sealed class MomentPresenterTests
{
    [Test]
    public void Show_WithNoDemoOpen_DoesNothing()
    {
        // The render loop calls this without asking whether a demo is open — a resize or a repaint
        // arrives before anything is loaded — so the quiet path is the common one.
        StubSource source = new();
        MomentPresenter presenter = Presenter(out FrameLedger _);

        presenter.Show(tick: 100, View());

        source.PlayerCalls.ShouldBe(0);
    }

    [Test]
    public void Show_WithASource_SamplesAtTheTickItWasGiven()
    {
        StubSource source = new();
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        presenter.Show(tick: 123.5, View());

        source.PlayerCalls.ShouldBe(1);
        source.PropCalls.ShouldBe(1);
        source.LastTick.ShouldBe(123.5);
    }

    [Test]
    public void Show_CalledTwice_HandsTheSourceTheSameBuffersAgain()
    {
        // **The buffers are fields rather than locals, and this is the claim that encodes.** A
        // moment is rebuilt every frame while playing, so fresh lists would be two allocations a
        // frame for the collector to find again.
        //
        // **Clearing is deliberately NOT asserted here**, because it is the source's job:
        // `DemoTimeline.PlayersAt` calls `into.Clear()` first and `IMomentSource` says so. An
        // earlier version of this test asserted the buffer count after two samples — which the stub
        // decides by clearing, so it held identically against every possible presenter. A test whose
        // subject is the stub is not a test.
        StubSource source = new() { Players = 3 };
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        presenter.Show(tick: 10, View());
        presenter.Show(tick: 11, View());

        source.SawOneBufferThroughout.ShouldBeTrue("fresh lists per frame is the allocation this avoids");
    }

    [Test]
    public void Show_WithASource_TakesTheIntervalFromItRatherThanTheCaller()
    {
        // **The tick rate is the recording's, not the window's.** It is a server setting — 33 where
        // a box was left at its default, 66 where it was configured — so a caller cannot supply it
        // and interpolating at the wrong one reads as a slow server rather than a defect.
        StubSource source = new() { Interval = 0.03f };
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        presenter.Show(tick: 10, View());

        presenter.LastInterval.ShouldBe(0.03f);
    }

    [Test]
    public void Show_WithASource_ChargesTheSamplingToTheLedger()
    {
        // The sampling column exists because three untimed steps once hid 129 ms of a 133 ms pose
        // (B191). It moved out of the window with the sampling it measures.
        StubSource source = new();
        MomentPresenter presenter = Presenter(out FrameLedger ledger);
        presenter.Source = source;

        presenter.Show(tick: 10, View());

        ledger.SampledTicks.ShouldBeGreaterThan(0);
    }

    private static MomentPresenter Presenter(out FrameLedger ledger)
    {
        ledger = new FrameLedger();

        return new MomentPresenter(
            new MomentScene(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance),
            ledger,
            NullLogger.Instance);
    }

    private static MomentView View() =>
        new(CurrentTick: 0, FirstPerson: false, Followed: null, Eye: null, ViewmodelFieldOfView: 54f);

    /// <summary>A moment source that counts what was asked of it.</summary>
    private sealed class StubSource : IMomentSource
    {
        public int Players { get; init; }

        public float Interval { get; init; } = 0.015f;

        public int PlayerCalls { get; private set; }

        public int PropCalls { get; private set; }

        public double LastTick { get; private set; }

        /// <summary>Whether every sample arrived in the one buffer, rather than a fresh list.</summary>
        /// <remarks>
        /// **True until contradicted**, so a single call cannot make it pass by accident — it starts
        /// with nothing seen, and the first sample records the instance without asserting anything.
        /// </remarks>
        public bool SawOneBufferThroughout { get; private set; } = true;

        public float IntervalPerTick => Interval;

        public void PlayersAt(double tick, ICollection<ScenePlayer> into)
        {
            PlayerCalls++;
            LastTick = tick;

            if (_firstBuffer is null)
            {
                _firstBuffer = into;
            }
            else if (!ReferenceEquals(_firstBuffer, into))
            {
                SawOneBufferThroughout = false;
            }

            // Clears first, exactly as `DemoTimeline` does, so the presenter is exercised against
            // the contract its production source honours rather than a laxer one.
            into.Clear();

            for (int player = 0; player < Players; player++)
            {
                into.Add(default);
            }
        }

        private ICollection<ScenePlayer>? _firstBuffer;

        public void PropsAt(double tick, ICollection<SceneProp> into)
        {
            PropCalls++;
            into.Clear();
        }
    }
}
