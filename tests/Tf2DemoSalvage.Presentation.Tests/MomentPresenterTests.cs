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
    public void Show_WithASource_AsksItForTheRoundAtTheTickBeingShown()
    {
        // **The wiring assertion, and it is the only kind that can fail when a value is decoded,
        // retained, unit-tested and never read.** `m_flPlaybackRate` was all four of those for
        // weeks and every animation played at rate 1 with a green suite.
        //
        // The round decides whether a spawn's team wall is drawn at all
        // (`C_FuncRespawnRoomVisualizer::DrawModel`, `c_func_respawnroom.cpp:47`), and only the
        // recording knows it — so a presenter that never asked would leave the scene deciding on
        // null for ever, which is the state that draws.
        StubSource source = new() { RoundState = RespawnRoomVisibility.TeamWin };
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        presenter.Show(tick: 123.5, View());

        source.RoundStateCalls.ShouldBe(1, "the scene cannot derive the round; it must be asked for");
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

    [Test]
    public void Show_WithAnAppearanceSource_AsksItBeforeBuildingTheScene()
    {
        // **This was `MainForm.EnsureWeaponRoles`, called from `ShowMoment` every frame** (B188,
        // D90). It has to run before the scene is built — the scene poses players with it — and
        // OUTSIDE both timers, because the first call reads the weapon scripts out of the archives
        // and each one costs an ICE decryption. Counting that as sampling reported one enormous
        // spike for work that is not sampling.
        StubSource source = new();
        StubAppearances appearances = new();
        MomentPresenter presenter = Presenter(out FrameLedger _);

        presenter.Source = source;
        presenter.Appearances = appearances;

        presenter.Show(tick: 10, View());

        appearances.Asked.ShouldBe(1);
    }

    [Test]
    public void Show_WithNoAppearanceSource_StillDrawsTheMoment()
    {
        // **A viewer with no demo open legitimately has no appearance source**, and the frame loop
        // calls `Show` before anything is loaded. Refusing to draw would be the wrong failure.
        StubSource source = new();
        MomentPresenter presenter = Presenter(out FrameLedger _);

        presenter.Source = source;

        presenter.Show(tick: 10, View());

        source.PlayerCalls.ShouldBe(1);
    }

    [Test]
    public void Show_WithAnAppearanceSource_KeepsWhatItAnswered()
    {
        // **The return value IS the wiring** — the old version wrote into `MomentScene.Appearance`
        // as a side effect, and a side effect is exactly what goes missing when code moves. That is
        // B193: every weapon suffix answered null and every player animated in the generic primary
        // pose, with the suite green.
        StubSource source = new();
        StubAppearances appearances = new();
        MomentPresenter presenter = Presenter(out FrameLedger _, out MomentScene scene);

        presenter.Source = source;
        presenter.Appearances = appearances;

        presenter.Show(tick: 10, View());

        scene.Appearance.ShouldBeSameAs(appearances.Answer);
    }

    private static MomentPresenter Presenter(out FrameLedger ledger) =>
        Presenter(out ledger, out MomentScene _);

    private static MomentPresenter Presenter(out FrameLedger ledger, out MomentScene scene)
    {
        ledger = new FrameLedger();
        scene = new MomentScene(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance);

        return new MomentPresenter(scene, ledger, NullLogger.Instance);
    }

    /// <summary>An appearance source that counts, and answers something identifiable.</summary>
    private sealed class StubAppearances : IAppearanceSource
    {
        /// <summary>An answer distinguishable from the sentinel, so an assignment is observable.</summary>
        public IPlayerAppearance Answer { get; } = new GameAppearance(Classes: null, Roles: null);

        public int Asked { get; private set; }

        public IPlayerAppearance Ensure(IPlayerAppearance current)
        {
            Asked++;

            return Answer;
        }
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

        public void PropsAt(
            double tick, ICollection<SceneProp> into, IReadOnlySet<int>? interpolate = null)
        {
            PropCalls++;
            into.Clear();
        }

        /// <summary>What round this stub claims, and whether it was asked.</summary>
        public int? RoundState { get; init; }

        public int RoundStateCalls { get; private set; }

        public int? RoundStateAt(double tick)
        {
            RoundStateCalls++;
            return RoundState;
        }
    }
}
