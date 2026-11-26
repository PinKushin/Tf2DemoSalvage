using System.Collections.Generic;
using System.Text;

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

    /// <remarks>
    /// **`engine-&gt;IsPaused()` clears `s_bInterpolate` for every entity at once**
    /// (`c_baseentity.cpp:3226`), and `BaseInterpolatePart1` then calls `MoveToLastReceivedPosition`
    /// (`:2845`). Only the window knows whether someone pressed pause, so the sampler can only obey
    /// it if the presenter carries it — and a `--shot` capture is a paused frame, which is how B397
    /// came to compare an airborne soldier against a landed one.
    /// </remarks>
    [Test]
    public void Show_WhenPaused_TellsTheSourceNotToInterpolate()
    {
        StubSource source = new();
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        presenter.Show(tick: 123.5, View() with { Playing = false });

        source.LastPropsInterpolating.ShouldBe(false);
        source.LastPlayersInterpolating.ShouldBe(false);
    }

    /// <summary>
    /// **`FRAME_START` comes before the frame reads its packets** (B860): `CEngine::Frame` notifies the client, then the
    /// host frame's `CL_ReadPackets` reads. Playing forward, each frame starts having read the tick the last one showed, and
    /// a second build of the same frame keeps its start. The first frame and a rewind are jumps, which the engine reads as a
    /// skip (B860 review 2): negative infinity hands that to the source. A paused frame has read to its own tick.
    /// </summary>
    [Test]
    public void Show_FrameAfterFrame_StartsEachHavingReadTheLastOnesTick()
    {
        StubSource source = new();
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        foreach (double tick in (double[])[10d, 10.5d, 10.5d, 11.25d, 9d])
        {
            presenter.Show(tick, View());
        }

        presenter.Show(12d, View() with { Playing = false });
        presenter.Show(12d, View());

        // Unpausing on the paused tick reads nothing new (B860 review 3, Fable): the paused frame had read to 12.
        source.FrameStarts.ShouldBe(
            [double.NegativeInfinity, 10d, 10d, 10.5d, double.NegativeInfinity, 12d, 12d]);
        source.FrameTicks.ShouldBe([10d, 10.5d, 10.5d, 11.25d, 9d, 12d, 12d]);
    }

    [Test]
    public void Show_WhilePlaying_LeavesInterpolationOn()
    {
        // The control: without it, a presenter that hardcoded `false` would pass the paused test
        // and stop interpolating the whole demo.
        StubSource source = new();
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        presenter.Show(tick: 123.5, View() with { Playing = true });

        source.LastPropsInterpolating.ShouldBe(true);
        source.LastPlayersInterpolating.ShouldBe(true);
    }

    /// <remarks>
    /// **An effect that follows a player follows where the frame DREW him** (B523). `CParticleProperty::UpdateControlPoint`
    /// reads `GetAbsOrigin()` (`particle_property.cpp:637`), the interpolated origin while playing; the medigun beam's
    /// far end read the newest recorded frame instead and led a running patient by the interpolation delay. The sample
    /// the frame was built from is kept so nothing asks the recording a second, different question.
    /// </remarks>
    [Test]
    public void Show_WithASource_KeepsThePlayersTheFrameWasBuiltFrom()
    {
        StubSource source = new() { Players = 3 };
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        presenter.Players.ShouldBeEmpty("the control: nothing before the first frame");

        presenter.Show(tick: 123.5, View());

        presenter.Players.Count.ShouldBe(3);
        source.PlayerCalls.ShouldBe(1, "kept, not asked for again");
    }

    /// <remarks>
    /// **A trace against a player is against where he was DRAWN** (B526): `GetCollisionOrigin()` is the interpolated abs
    /// origin and his hitboxes were posed there. The frame's sample answers for the tick it showed and for no other —
    /// a replay asking about an earlier tick gets nothing here and falls back to the recording.
    /// </remarks>
    [Test]
    public void PlayersShownAt_TheTickTheFrameShowed_IsItsSampleAndAnyOtherTickIsNot()
    {
        StubSource source = new() { Players = 2 };
        MomentPresenter presenter = Presenter(out FrameLedger _);
        presenter.Source = source;

        presenter.PlayersShownAt(123).ShouldBeNull("the control: nothing has been shown");

        presenter.Show(tick: 123.5, View());

        presenter.PlayersShownAt(123).ShouldNotBeNull().Count.ShouldBe(2);
        presenter.PlayersShownAt(124).ShouldBeNull();
        presenter.PlayersShownAt(122).ShouldBeNull();
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

    /// <remarks>
    /// **The value has to ARRIVE, not merely be asked for** (B354). A presenter that read
    /// <c>Recorder</c> and dropped it would pass a call-counting assertion while every Pyroland item
    /// in every point-of-view demo stayed hidden — the recorder's own Balloonicorn included.
    ///
    /// So this asserts through the SCENE: the same Pyroland item is drawn or not depending only on
    /// who the source says recorded the demo, which is observable at the far end of the wiring and
    /// nowhere in between.
    /// </remarks>
    [Test]
    public void Show_WhenTheSourceNamesARecorderWithPyrovision_DrawsThePyrolandItem()
    {
        MomentPresenter presenter = Presenter(out FrameLedger _, out MomentScene scene);
        scene.Weapons = new WeaponModels(VisionSchema, NullLogger.Instance);
        presenter.Source = new StubSource { Recorder = 3, Props = [Balloonicorn(), Rainblower()] };

        presenter.Show(tick: 123.5, View());

        scene.Drawn.Count.ShouldBe(2, "the recorder carries a Rainblower, so they see Pyroland");
    }

    /// <remarks>
    /// The control, and the half that fails when the recorder never travels: the same props with
    /// nobody recording — a SourceTV demo — hide the Balloonicorn, because a spectator sees no
    /// Pyroland (`tf_spectate_pyrovision` defaults to 0, `c_tf_player.cpp:218`).
    /// </remarks>
    [Test]
    public void Show_WhenTheSourceNamesNoRecorder_HidesThePyrolandItem()
    {
        MomentPresenter presenter = Presenter(out FrameLedger _, out MomentScene scene);
        scene.Weapons = new WeaponModels(VisionSchema, NullLogger.Instance);
        presenter.Source = new StubSource { Recorder = null, Props = [Balloonicorn(), Rainblower()] };

        presenter.Show(tick: 123.5, View());

        scene.Drawn.Count.ShouldBe(1, "the Rainblower has no filter of its own and stays");
        scene.Drawn[0].ItemDefinitionIndex.ShouldBe(741);
    }

    private static SceneProp Balloonicorn() =>
        new(
            EntityIndex: 20,
            ModelPath: "models/player/items/pyro/balloonicorn.mdl",
            Kind: SceneModelKind.Studio,
            Pose: new ScenePose(),
            AttachedTo: 3,
            BoneMerged: true,
            ItemDefinitionIndex: 738);

    private static SceneProp Rainblower() =>
        new(
            EntityIndex: 21,
            ModelPath: "models/weapons/c_models/c_rainblower/c_rainblower.mdl",
            Kind: SceneModelKind.Studio,
            Pose: new ScenePose(),
            AttachedTo: 3,
            BoneMerged: true,
            ItemDefinitionIndex: 741);

    /// <summary>The two shipped items this needs, with their real numbers and keys.</summary>
    private static byte[]? VisionSchema(string path) =>
        path == "scripts/items/items_game.txt"
            ? Encoding.UTF8.GetBytes(
                """
                "items_game"
                {
                    "attributes"
                    {
                        "406"
                        {
                            "name" "vision opt in flags"
                            "attribute_class" "vision_opt_in_flags"
                            "description_format" "value_is_or"
                            "stored_as_integer" "0"
                        }
                    }
                    "items"
                    {
                        "738"
                        {
                            "name" "Pet Balloonicorn"
                            "vision_filter_flags" "1"
                        }
                        "741"
                        {
                            "name" "The Rainblower"
                            "attributes"
                            {
                                "vision opt in flags"
                                {
                                    "attribute_class" "vision_opt_in_flags"
                                    "value" "1"
                                }
                            }
                        }
                    }
                }
                """)
            : null;

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

    /// <summary>The view's two cvars reach the average a pose uses (B676).</summary>
    [Test]
    public void PoseNow_AViewWithTheLightAverageOff_HandsItToTheModelSet()
    {
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance) { Source = new StubSource() };

        presenter.Show(0d, View() with { LightAverage = false, LightInterp = 2f });
        presenter.PoseNow();

        models.LightAverage.Enabled.ShouldBeFalse();
        models.LightAverage.Rate.ShouldBe(2f);
    }

    /// <summary>
    /// B744: every render frame poses, paused or not. `OnRenderStart` invalidates every bone cache each frame
    /// (cdll_client_int.cpp:2163), and the jiggle bones step on `gpGlobals->realtime` (c_baseanimating.cpp:1586).
    /// </summary>
    [Test]
    public void NextFrame_AHeldMoment_IsPosedAgainAtTheNewRealTime()
    {
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance) { Source = new StubSource() };

        presenter.Show(0d, View() with { RealTime = 5d });
        presenter.PoseNow();
        models.RealTime.ShouldBe(5d);

        presenter.NextFrame(6d);
        presenter.PoseNow();

        models.RealTime.ShouldBe(6d);
    }

    [Test]
    public void NextFrame_WithNoRealTime_KeepsTheLastOne()
    {
        // A clock that cannot be read must not drop the jiggle onto demo seconds.
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance) { Source = new StubSource() };

        presenter.Show(0d, View() with { RealTime = 5d });
        presenter.PoseNow();
        presenter.NextFrame(null);
        presenter.PoseNow();

        models.RealTime.ShouldBe(5d);
    }

    [Test]
    public void NextFrame_ThenANewBuild_PosesTheBuildAtItsOwnRealTime()
    {
        // A playing frame: `Show` replaces the held build after `NextFrame`, and the new build is what is posed.
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance) { Source = new StubSource() };

        presenter.Show(0d, View() with { RealTime = 5d });
        presenter.PoseNow();
        presenter.NextFrame(6d);
        presenter.Show(1d, View() with { RealTime = 7d });
        presenter.PoseNow();

        models.RealTime.ShouldBe(7d);
    }

    [Test]
    public void NextFrame_BeforeAnyBuild_PosesNothing()
    {
        EntityModelSet models = new() { RealTime = 3d };
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance) { Source = new StubSource() };

        presenter.NextFrame(6d);
        presenter.PoseNow();

        models.RealTime.ShouldBe(3d);
    }

    [Test]
    public void PoseNow_TwiceInOneFrame_PosesOnce()
    {
        // The control for the one above: the frame's two callers (the camera stage and the projection) pose it once.
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance) { Source = new StubSource() };

        presenter.Show(0d, View() with { RealTime = 5d });
        presenter.PoseNow();
        models.RealTime = 9d;

        presenter.PoseNow();

        models.RealTime.ShouldBe(9d, "not posed again within the frame");
    }

    /// <summary>The source's dormant players reach the model set the eyes read (B725).</summary>
    [Test]
    public void Show_ASourceWithDormantPlayers_HandsThemToTheModelSet()
    {
        SceneDormantPlayer[] dormant = [new(7, 1f, 2f, 3f, true)];
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance) { Source = new StubSource { Dormant = dormant } };

        presenter.Show(0d, View());

        models.DormantPlayers.ShouldBeSameAs(dormant);
    }

    /// <summary>
    /// Any entity's own dormancy reaches the moment from the source (B69 part 5): an econ item's effects stop when THE ITEM
    /// leaves the snapshot (<c>CEconEntity::SetDormant</c>, econ_entity.cpp:994-1001), asked at the tick shown.
    /// </summary>
    [Test]
    public void Show_ASourceWithADormantEntity_HandsItsDormancyToTheMoment()
    {
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance);
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance) { Source = new StubSource { AsleepEntity = 40 } };

        presenter.Show(12d, View());

        System.Func<int, bool> dormant = presenter.BuiltFor.ShouldNotBeNull().EntityDormant.ShouldNotBeNull();
        (dormant(40), dormant(41)).ShouldBe((true, false));

        // Where each spell began, from the same source at the same tick (B69 review 6: a seek's dormant owner).
        System.Func<int, int?> since = presenter.BuiltFor.ShouldNotBeNull().DormantSince.ShouldNotBeNull();
        (since(40), since(41)).ShouldBe((5, null));
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

        /// <summary>What the presenter passed as <c>render->GetViewEntity()</c> (B385).</summary>
        public int? LastViewEntity { get; private set; }

        public double LastTick { get; private set; }

        /// <summary>Whether every sample arrived in the one buffer, rather than a fresh list.</summary>
        /// <remarks>
        /// **True until contradicted**, so a single call cannot make it pass by accident — it starts
        /// with nothing seen, and the first sample records the instance without asserting anything.
        /// </remarks>
        public bool SawOneBufferThroughout { get; private set; } = true;

        public float IntervalPerTick => Interval;

        /// <summary>The dormant players the source answers with, for any tick (B725).</summary>
        public IReadOnlyList<SceneDormantPlayer> Dormant { get; init; } = [];

        public IReadOnlyList<SceneDormantPlayer> DormantPlayersAt(double tick) => Dormant;

        /// <summary>The one entity the source calls dormant, at tick 12 only (B69).</summary>
        public int? AsleepEntity { get; init; }

        public bool IsDormantAt(int entity, double tick) => entity == AsleepEntity && System.Math.Abs(tick - 12d) < 1e-9;

        /// <summary>The asleep entity's spell began at 5, asked at tick 12 only (B69 review 6).</summary>
        public int? DormantSinceAt(int entity, double tick) => IsDormantAt(entity, tick) ? 5 : null;

        /// <summary>What the presenter passed as <c>IsInterpolationEnabled()</c> (B399).</summary>
        /// <remarks>
        /// **Nullable so "never asked" and "asked with false" are different observations.** A plain
        /// bool starting false would let a presenter that dropped the argument entirely pass the
        /// paused assertion.
        /// </remarks>
        public bool? LastPlayersInterpolating { get; private set; }

        /// <summary>The same, for the prop sampling.</summary>
        public bool? LastPropsInterpolating { get; private set; }

        /// <summary>Every <c>FRAME_START</c> the presenter announced, in order (B860).</summary>
        public List<double> FrameStarts { get; } = [];

        /// <summary>The tick each announced frame draws, which scopes the start to that frame (B860 review 2, Fable).</summary>
        public List<double> FrameTicks { get; } = [];

        public void FrameStart(double lastRead, double frameTick)
        {
            FrameStarts.Add(lastRead);
            FrameTicks.Add(frameTick);
        }

        public void PlayersAt(double tick, ICollection<ScenePlayer> into, bool interpolating = true)
        {
            PlayerCalls++;
            LastTick = tick;
            LastPlayersInterpolating = interpolating;

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

        /// <summary>Props this stub hands over at every tick, so a scene has something to filter.</summary>
        public IReadOnlyList<SceneProp> Props { get; init; } = [];

        public void PropsAt(
            double tick,
            ICollection<SceneProp> into,
            int? viewEntity = null,
            bool interpolating = true)
        {
            PropCalls++;
            LastPropsInterpolating = interpolating;
            LastViewEntity = viewEntity;
            into.Clear();

            foreach (SceneProp prop in Props)
            {
                into.Add(prop);
            }
        }

        /// <summary>What round this stub claims, and whether it was asked.</summary>
        public int? RoundState { get; init; }

        public int RoundStateCalls { get; private set; }

        public int? RoundStateAt(double tick)
        {
            RoundStateCalls++;
            return RoundState;
        }

        /// <summary>Who recorded it, so a test can assert the fact reaches the moment (B354).</summary>
        public int? Recorder { get; init; }

        /// <summary>Which entities were told what, so a test can assert the fact travelled.</summary>
        public List<(int Entity, IReadOnlyList<bool> Looping, bool StaticProp)> NewModels { get; } = [];

        public void OnNewModel(
            int entityIndex, IReadOnlyList<bool> looping, bool staticProp, System.Func<int, bool>? sequenceLoops = null) =>
            NewModels.Add((entityIndex, looping, staticProp));
    }
}
