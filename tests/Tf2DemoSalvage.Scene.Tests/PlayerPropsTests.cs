using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Turning the timeline's players into props the draw loop can pose.
/// </summary>
/// <remarks>
/// **This adapter exists because OUR timeline splits players from props, not because the engine
/// does.** In Valve a player is already a `C_BaseAnimating` in the renderables list
/// (`clientleafsystem.h:48`) and needs no conversion; `DemoTimeline.PlayerTracks` is separate from
/// `Props` because a player's model is never networked — it is resolved from the installed game by
/// `CTFPlayerClassShared::GetModelName`.
///
/// So the conversion is ours, which is exactly why it needs tests of its own: nothing in the SDK
/// constrains it, and every mistake here draws a player somewhere plausible.
///
/// Lived in `MainForm.ShowMoment` until 2026-08-25, where it could not be tested without a window
/// (B188, B184).
/// </remarks>
public sealed class PlayerPropsTests
{
    [Test]
    public void Add_APlayingDrawnPlayer_BecomesAPropWithTheirClassModel()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier()], drawn, new Appearance(), NoParts);

        drawn.Count.ShouldBe(1);
        drawn[0].ModelPath.ShouldBe("models/player/soldier.mdl");
        drawn[0].EntityIndex.ShouldBe(3);
        drawn[0].Kind.ShouldBe(SceneModelKind.Studio);
    }

    [Test]
    public void Add_APlayerOnNoTeam_AddsNothing()
    {
        // **The control that keeps the spectators out.** A SourceTV camera and every spectator is a
        // CTFPlayer entity with a real position that follows the action, so drawing everything puts
        // convincing players where nobody is standing.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { Team = null }], drawn, new Appearance(), NoParts);

        drawn.ShouldBeEmpty();
    }

    [Test]
    public void Add_APlayerNotBeingDrawn_AddsNothing()
    {
        // A dead player keeps a team and a position — the position of whoever they are spectating —
        // so `Drawn` is what separates a corpse from a body, and without it several dead players
        // stack inside the living one they are watching.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { Drawn = false }], drawn, new Appearance(), NoParts);

        drawn.ShouldBeEmpty();
    }

    [Test]
    public void Add_APlayerWhoseClassIsUnknown_AddsNothing()
    {
        // Their model is chosen by class and nothing else can answer it. A prop with no model draws
        // as a missing asset, which reads as a loading fault rather than as a player we cannot name.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { PlayerClass = null }], drawn, new Appearance(), NoParts);

        drawn.ShouldBeEmpty();
    }

    [Test]
    public void Add_ARedAndABluPlayer_TakeSkinsZeroAndOne()
    {
        // **The game's own convention**, `m_nSkin = ( team == TF_TEAM_RED ) ? 0 : 1`
        // (`c_tf_player.cpp:712-719`). Both in one test because a single team cannot distinguish
        // "computed from team" from "always zero".
        List<SceneProp> drawn = [];

        PlayerProps.Add(
            [Soldier(), Soldier() with { EntityIndex = 4, Team = SceneTeams.Blu }],
            drawn,
            new Appearance(), NoParts);

        drawn.Select(prop => prop.Pose.Skin).ShouldBe([0, 1]);
    }

    [Test]
    public void Add_APlayerLookingUp_StandsUpright()
    {
        // **Yaw only.** The server feeds pitch to the animation state to aim the torso, not to tip
        // the body (`tf_player.cpp:2689`). Rolling a player by their view lays them on their side
        // every time they look up, and the eye pitch still has to arrive for `body_pitch`.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { Yaw = 90f, EyePitch = -45f }], drawn, new Appearance(), NoParts);

        drawn[0].Pose.Pitch.ShouldBe(0f);
        drawn[0].Pose.Roll.ShouldBe(0f);
        drawn[0].Pose.Yaw.ShouldBe(90f);
        drawn[0].Pose.EyePitch.ShouldBe(-45f);
    }

    [Test]
    public void Add_AnAirwalkingPlayerOfAClassThatDoesNot_IsNotAirwalking()
    {
        // **Both halves meet here and neither layer can answer alone.** The timeline says the
        // player rose fast enough to start an air-walk; the class script says whether their class
        // does it at all, and only the medic opts out.
        List<SceneProp> drawn = [];

        PlayerProps.Add(
            [Soldier() with { Airwalking = true, PlayerClass = MedicClass }],
            drawn,
            new Appearance(), NoParts);

        drawn[0].Pose.Airwalking.ShouldBeFalse();
    }

    [Test]
    public void Add_AnAirwalkingPlayerOfAClassThatDoes_IsAirwalking()
    {
        // The control for the pair. Without it, "never airwalks" passes the test above.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { Airwalking = true }], drawn, new Appearance(), NoParts);

        drawn[0].Pose.Airwalking.ShouldBeTrue();
    }

    [Test]
    public void Add_APlayerHoldingAWeapon_CarriesItsSuffix()
    {
        // Resolved where the player is known and used a pass later where the model is, so it has to
        // survive the conversion rather than being looked up again.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { WeaponClass = "tf_weapon_rocketlauncher" }], drawn, new Appearance(), NoParts);

        drawn[0].Pose.Slot.ShouldBe("PRIMARY");
    }

    private const int SoldierClass = 3;
    private const int MedicClass = 5;

    private static ScenePlayer Soldier() =>
        new(
            EntityIndex: 3,
            X: 10f,
            Y: 20f,
            Z: 30f,
            Team: SceneTeams.Red,
            Health: 200,
            PlayerClass: SoldierClass);

    /// <summary>A stand-in for the installed game, so this needs no TF2 and no window.</summary>
    private sealed class Appearance : IPlayerAppearance
    {
        public string? ModelOf(int playerClass)
        {
            if (playerClass == SoldierClass)
            {
                return "models/player/soldier.mdl";
            }

            if (playerClass == SpyClass)
            {
                return "models/player/spy.mdl";
            }

            return playerClass == MedicClass ? "models/player/medic.mdl" : null;
        }

        public string? WeaponSuffix(string? weaponClass, int? playerClass) =>
            weaponClass is null ? null : "PRIMARY";

        public bool Airwalks(int playerClass) => playerClass != MedicClass;

        // The medic is the class TF2 measures as setting BOTH DontDoAirwalk and DontDoNewJump, so
        // the stub says the same thing rather than a convenient different one.
        public bool Lands(int playerClass) => playerClass != MedicClass;

        public string? Hands(int playerClass) =>
            playerClass == SoldierClass ? "models/weapons/c_models/c_soldier_arms.mdl" : null;

        // Nothing, so these tests keep measuring what they were written to measure — the wardrobe
        // half is `PlayerBodygroupWiringTests`, with a stub of its own.
        public ItemBodygroups BodygroupsOf(int itemDefinitionIndex) => ItemBodygroups.None;
    }

    [Test]
    public void Add_ADisguisedFriendlySpy_AsksForTheMaskBodygroupAndCarriesIt()
    {
        // **The wiring assertion for the mask, and it is the only kind that could have caught the
        // bug.** `Disguise.WearsMask` had its own tests and `GetSkin`'s mask offset had its own
        // tests, and between them the mask still never drew — because nothing set `m_nBody` and the
        // mask mesh is alternative 1 of the `spyMask` part. A rule nobody applies is a rule that
        // does nothing.
        List<SceneProp> drawn = [];
        Recorder asked = new();

        PlayerProps.Add([DisguisedSpy()], drawn, new Appearance(), asked);

        asked.Found.ShouldHaveSingleItem();
        asked.Found[0].Group.ShouldBe("spyMask", "the part is addressed by NAME; its index differs per model");
        asked.WasSet.ShouldHaveSingleItem();
        asked.WasSet[0].Value.ShouldBe(1, "alternative 1 is the mask mesh on models/player/spy.mdl");
        drawn.ShouldHaveSingleItem();
        drawn[0].Pose.Body.ShouldBe(1, "the resolved body number has to reach the prop");
    }

    [Test]
    public void Add_AnUndisguisedSpy_AsksForNoBodygroupAndDrawsAtBodyZero()
    {
        // **The control, and it is the branch that takes the mask OFF.** Without it a rule that
        // always asked would pass the test above while leaving every spy in a mask for the rest of
        // the round — and `Body` would be right for the wrong reason.
        List<SceneProp> drawn = [];
        Recorder asked = new();

        PlayerProps.Add(
            [DisguisedSpy() with { Conditions = default }], drawn, new Appearance(), asked);

        asked.Found.ShouldBeEmpty();
        drawn.ShouldHaveSingleItem();
        drawn[0].Pose.Body.ShouldBe(0);
    }

    /// <summary><c>TF_CLASS_SPY</c>.</summary>
    private const int SpyClass = 8;

    /// <summary><c>TF_CLASS_DEMOMAN</c>.</summary>
    private const int DemomanClass = 4;

    /// <summary>A BLU spy disguised as a RED demoman, seen by a teammate.</summary>
    private static ScenePlayer DisguisedSpy() =>
        Soldier() with
        {
            PlayerClass = SpyClass,
            Team = SceneTeams.Blu,
            Conditions = new PlayerConditions(1 << PlayerConditions.Disguised, 0, 0, 0, 0),
            DisguiseClass = DemomanClass,
            DisguiseTeam = SceneTeams.Red,
            IsEnemy = false,
        };

    /// <summary>A model with no body parts at all, which is what most of these tests want.</summary>
    /// <remarks>
    /// **The body unchanged is the honest answer for a model that has not been loaded**, not a
    /// stand-in: a part's index cannot be resolved without the .mdl, and the production resolver
    /// says the same thing on the first frame a model is seen. Tests that care about the mask
    /// supply their own.
    /// </remarks>
    private static readonly IModelBodygroups NoParts = NoBodygroups.Instance;

    /// <summary>A model whose every part resolves, recording what was asked of it.</summary>
    /// <remarks>
    /// **It records the two calls separately because the engine makes them separately.**
    /// `FindBodygroupByName` answering -1 is what stops a part being set at all, so a recorder that
    /// only saw the set could not tell "asked for a part this model lacks" from "never asked".
    /// </remarks>
    private sealed class Recorder : IModelBodygroups
    {
        public List<(string Model, string Group)> Found { get; } = [];

        public List<(string Model, int Group, int Value)> WasSet { get; } = [];

        public int FindBodygroup(string modelPath, string group)
        {
            Found.Add((modelPath, group));
            return 0;
        }

        public int SetBodygroup(string modelPath, int group, int value, int body)
        {
            WasSet.Add((modelPath, group, value));
            return 1;
        }
    }
}
