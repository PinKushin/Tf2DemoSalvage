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

        PlayerProps.Add([Soldier()], drawn, new Appearance());

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

        PlayerProps.Add([Soldier() with { Team = null }], drawn, new Appearance());

        drawn.ShouldBeEmpty();
    }

    [Test]
    public void Add_APlayerNotBeingDrawn_AddsNothing()
    {
        // A dead player keeps a team and a position — the position of whoever they are spectating —
        // so `Drawn` is what separates a corpse from a body, and without it several dead players
        // stack inside the living one they are watching.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { Drawn = false }], drawn, new Appearance());

        drawn.ShouldBeEmpty();
    }

    [Test]
    public void Add_APlayerWhoseClassIsUnknown_AddsNothing()
    {
        // Their model is chosen by class and nothing else can answer it. A prop with no model draws
        // as a missing asset, which reads as a loading fault rather than as a player we cannot name.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { PlayerClass = null }], drawn, new Appearance());

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
            new Appearance());

        drawn.Select(prop => prop.Pose.Skin).ShouldBe([0, 1]);
    }

    [Test]
    public void Add_APlayerLookingUp_StandsUpright()
    {
        // **Yaw only.** The server feeds pitch to the animation state to aim the torso, not to tip
        // the body (`tf_player.cpp:2689`). Rolling a player by their view lays them on their side
        // every time they look up, and the eye pitch still has to arrive for `body_pitch`.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { Yaw = 90f, EyePitch = -45f }], drawn, new Appearance());

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
            new Appearance());

        drawn[0].Pose.Airwalking.ShouldBeFalse();
    }

    [Test]
    public void Add_AnAirwalkingPlayerOfAClassThatDoes_IsAirwalking()
    {
        // The control for the pair. Without it, "never airwalks" passes the test above.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { Airwalking = true }], drawn, new Appearance());

        drawn[0].Pose.Airwalking.ShouldBeTrue();
    }

    [Test]
    public void Add_APlayerHoldingAWeapon_CarriesItsSuffix()
    {
        // Resolved where the player is known and used a pass later where the model is, so it has to
        // survive the conversion rather than being looked up again.
        List<SceneProp> drawn = [];

        PlayerProps.Add([Soldier() with { WeaponClass = "tf_weapon_rocketlauncher" }], drawn, new Appearance());

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

            return playerClass == MedicClass ? "models/player/medic.mdl" : null;
        }

        public string? WeaponSuffix(string? weaponClass, int? playerClass) =>
            weaponClass is null ? null : "PRIMARY";

        public bool Airwalks(int playerClass) => playerClass != MedicClass;

        public string? Hands(int playerClass) =>
            playerClass == SoldierClass ? "models/weapons/c_models/c_soldier_arms.mdl" : null;
    }
}
