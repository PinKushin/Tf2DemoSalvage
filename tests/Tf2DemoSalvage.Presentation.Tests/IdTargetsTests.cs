using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// What the local player can look at, as it left the view (D214): `C_TFPlayer::GetIDTarget()` drawn only for a
/// point-of-view demo's living recorder outside any observer mode (`CMainTargetID`), over the players where the frame
/// drew them, and `GetDroppedWeaponInRange`'s trace, `TF_WEAPON_PICKUP_RANGE` long (tf_player_shared.cpp:14761).
/// </summary>
/// <remarks>
/// The demo is `SyntheticPlayer`'s with recorded views (D38, D213), which is all that makes it a point-of-view demo; the
/// players are handed over as the frame's own sample. **The traces are Scene's and have their own suites**
/// (`IdTargetTraceConformanceTests`, `IdTargetTracesTests`); asserted here is who is in them, from which eye, and when
/// nothing is traced at all.
///
/// **Not here:** a building's box and a dropped weapon's collide, which need a timeline that carries them.
/// </remarks>
public sealed class IdTargetsTests
{
    private const int Tick = 100;
    private const int Local = 1;
    private const int Other = 2;
    private const int Behind = 3;
    private const int Red = 2;
    private const int Blue = 3;

    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    private static readonly DemoTimeline Pov = DemoTimeline.Build(SyntheticPlayer.DemoWithRecordedViews(
        (0, (0f, 0f, 64f)), (200, (0f, 0f, 64f))));

    private static readonly DemoTimeline SourceTv = SyntheticEffects.TimelineWrittenWith(PlayerFlagLayout.Current);

    private static readonly FreeCamera Eye = new() { Origin = (0f, 0f, 64f), Angles = (0f, 0f, 0f) };

    private static readonly HudState Living = new(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: 125, Alive: true)
    {
        LocalIndex = Local,
        Team = Red,
    };

    private static ScenePlayer Player(int entity, float x, int team = Blue, int? lifeState = 0) =>
        new(entity, x, 0f, 0f, Team: team, Health: 125, PlayerClass: 1, LifeState: lifeState);

    /// <summary>Target ID over players nobody posed, whose hulls answer for them, and how often the eye was asked for.</summary>
    private sealed class Looking
    {
        private readonly IdTargets _targets = new(new BulletStrikes(new FakePosedEntities(), new RecordingLogger()), new RecordingLogger());

        public int EyeAsked { get; private set; }

        public HudState With(HudState state, DemoTimeline? timeline, IReadOnlyList<ScenePlayer>? shown, MapLevel? level, FreeCamera? eye)
        {
            IdTargetFrame frame = new(
                timeline,
                null,
                null,
                shown,
                () =>
                {
                    EyeAsked++;

                    return eye;
                });

            return _targets.WithOver(state, Tick, frame, level);
        }
    }

    [Test]
    public void WithOver_ThePovRecorderLookingAtAPlayer_NamesHimAndSaysWhereTheEyeIs()
    {
        Pov.HasRecordedView.ShouldBeTrue("the control: recorded views are what make this a point-of-view demo");

        HudState state = new Looking().With(Living, Pov, [Player(Local, 0f, Red), Player(Other, 300f)], Empty, Eye);

        state.IdTarget.ShouldBe(Other);
        state.EyePosition.ShouldBe((0f, 0f, 64f));
        state.WeaponPickupTraceHit.ShouldNotBe(Other, "three hundred units is past TF_WEAPON_PICKUP_RANGE");
    }

    [Test]
    public void WithOver_APlayerWithinAHundredAndFiftyUnits_IsWhatThePickupTraceMeets()
    {
        HudState state = new Looking().With(Living, Pov, [Player(Local, 0f, Red), Player(Other, 100f)], Empty, Eye);

        state.WeaponPickupTraceHit.ShouldBe(Other, "EyePosition() + vecForward * TF_WEAPON_PICKUP_RANGE");
    }

    [Test]
    public void WithOver_ADeadPlayerInTheWay_IsLookedThrough()
    {
        HudState state = new Looking().With(
            Living, Pov, [Player(Local, 0f, Red), Player(Other, 200f, lifeState: 2), Player(Behind, 400f)], Empty, Eye);

        state.IdTarget.ShouldBe(Behind);
    }

    [Test]
    public void WithOver_LookingTheOtherWay_HasNoTarget()
    {
        FreeCamera away = new() { Origin = (0f, 0f, 64f), Angles = (0f, 180f, 0f) };

        HudState state = new Looking().With(Living, Pov, [Player(Local, 0f, Red), Player(Other, 300f)], Empty, away);

        (state.IdTarget ?? 0).ShouldBe(0);
        state.EyePosition.ShouldBe((0f, 0f, 64f));
    }

    [Test]
    public void WithOver_WithNoSampleOfThisTickFromTheFrame_TracesTheRecordingsOwnPlayers()
    {
        DemoTimeline recorded = DemoTimeline.Build(SyntheticEffects.Demo(
            [
                new(Local, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, Red)),
                new(Other, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(300f, 0f, 0f, Blue)),
            ],
            [],
            [],
            [],
            new SyntheticEffects.Effect(200, "CTETFBlood", new Dictionary<string, Core.Schema.PropertyValue>(System.StringComparer.Ordinal))));

        recorded.PlayersAt(Tick).Count.ShouldBe(2, "the control: both stand at this tick");

        // Not a point-of-view demo, so nothing is traced; the recorded players are reached only past that guard.
        new Looking().With(Living, recorded, null, Empty, Eye).IdTarget.ShouldBeNull();
        recorded.HasRecordedView.ShouldBeFalse();
    }

    [TestCase("dead", TestName = "WithOver_ADeadLocalPlayer_TracesNothingAndNeverAsksForTheEye")]
    [TestCase("none", TestName = "WithOver_WithNoLocalPlayer_TracesNothingAndNeverAsksForTheEye")]
    [TestCase("observer", TestName = "WithOver_AnObserver_TracesNothingAndNeverAsksForTheEye")]
    [TestCase("sourcetv", TestName = "WithOver_ADemoWithNoRecordedView_TracesNothingAndNeverAsksForTheEye")]
    [TestCase("nomap", TestName = "WithOver_WithNoMap_TracesNothingAndNeverAsksForTheEye")]
    public void WithOver_WhereTheTargetIdCannotDraw_ClearsTheTargetWithoutTracing(string refused)
    {
        Looking looking = new();
        HudState had = Living with { IdTarget = 9 };

        HudState state = refused switch
        {
            "dead" => had with { Alive = false },
            "none" => had with { HasLocalPlayer = false },
            "observer" => had with { ObserverMode = ObserverModes.InEye },
            _ => had,
        };

        HudState traced = looking.With(
            state,
            refused == "sourcetv" ? SourceTv : Pov,
            [Player(Local, 0f, Red), Player(Other, 300f)],
            refused == "nomap" ? null : Empty,
            Eye);

        traced.IdTarget.ShouldBeNull();
        traced.EyePosition.ShouldBeNull();
        looking.EyeAsked.ShouldBe(0, "the camera is asked for last, once everything cheaper allows a trace");
    }

    [Test]
    public void WithOver_WithNoFirstPersonCamera_ClearsTheTarget()
    {
        Looking looking = new();

        HudState state = looking.With(Living with { IdTarget = 9 }, Pov, [Player(Local, 0f, Red), Player(Other, 300f)], Empty, null);

        state.IdTarget.ShouldBeNull();
        state.EyePosition.ShouldBeNull();
        looking.EyeAsked.ShouldBe(1);
    }
}
