using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Every field of a player survives the walk from its track to the drawn scene.
/// </summary>
/// <remarks>
/// **The record that lost <c>Yaw</c>, guarded the way the pose is.** <c>ScenePlayer</c> is built
/// POSITIONALLY — twelve arguments, seven of them with defaults — and the argument list stopped at
/// <c>LifeState</c>, so every player in a frame faced due east while the number sat correctly in a
/// track nobody asked.
///
/// **Positional construction is the specific hazard here**, and it is worse than the pose's. A named
/// initialiser that forgets a field leaves it at its default; a positional call that stops early
/// does the same thing while LOOKING complete, because the remaining parameters have defaults and
/// the compiler is content. Nothing marks the difference between "chose the default" and "did not
/// get that far".
///
/// So this asserts by reflection, like <see cref="PoseCompletenessTests"/>: every property is given
/// a value that is not its default, and none may come back as one.
/// </remarks>
public sealed class PlayerCompletenessTests
{
    [Test]
    public void EveryFieldOfAPlayer_SurvivesBeingRebuilt()
    {
        // A `with` expression is the operation this checks, because it is what PlayersAt does to
        // attach position and movement to a player taken from a frame. Anything the rebuild drops
        // shows up here as a default.
        ScenePlayer filled = Distinctive();

        ScenePlayer rebuilt = filled with { X = 99f };

        List<string> lost = [];

        foreach (PropertyInfo property in Readable<ScenePlayer>())
        {
            if (property.Name is nameof(ScenePlayer.X))
            {
                continue;
            }

            object? expected = property.GetValue(filled);
            object? actual = property.GetValue(rebuilt);

            if (!Equals(expected, actual))
            {
                lost.Add($"{property.Name}: expected {expected}, got {actual}");
            }
        }

        lost.ShouldBeEmpty("a rebuilt player lost these: " + string.Join("; ", lost));
    }

    [Test]
    public void EveryFieldOfAPlayer_HasADistinctiveValueInThisTest()
    {
        // **The control that keeps the test above honest**, and the one that fails when someone adds
        // a field. Without it a new property sits at its default on both sides of every comparison
        // and passes no matter what the code does with it.
        ScenePlayer filled = Distinctive();
        ScenePlayer empty = new(EntityIndex: 0, X: 0f, Y: 0f, Z: 0f, Team: null, Health: null, PlayerClass: null);

        List<string> untouched =
        [
            .. Readable<ScenePlayer>()
                .Where(property => Equals(property.GetValue(filled), property.GetValue(empty)))
                .Select(property => property.Name),
        ];

        untouched.ShouldBeEmpty(
            "Distinctive() leaves these at their default, so the test above cannot measure them: " +
            string.Join(", ", untouched));
    }

    /// <summary>A player whose every field differs from the default for its type.</summary>
    private static ScenePlayer Distinctive() => new(
        EntityIndex: 3,
        X: 12f,
        Y: 34f,
        Z: 56f,
        Team: SceneTeams.Blu,
        Health: 125,
        PlayerClass: 4,
        Yaw: -139f,
        Speed: 320f,
        LifeState: 2,
        MoveX: 0.5f,
        MoveY: -0.5f,

        // Crouched and off the ground at once, so IsCrouched and IsAirborne are BOTH distinctive —
        // this test reads derived properties too, and a value that leaves either at its default is
        // one the test above cannot measure.
        Flags: PlayerActivityState.Ducking,

        // False, because the default is true. A player the engine would not draw is the unusual
        // case and therefore the measurable one.
        Drawn: false,

        // **`OBS_MODE_ROAMING`, which is both non-default AND makes `InFirstPersonView` false** —
        // this test reads derived properties too, so a mode that left that predicate at its default
        // would be a value this test could not measure. Roaming is also the one the owner described:
        // where TF2 puts a player who goes to spectator.
        ObserverMode: ObserverModes.Roaming,

        // Somebody other than this player, so losing it reads as "nobody" (B417).
        ObserverTarget: 9,

        // A weapon in hand, and its class — the pair that decides which suffix every body activity
        // takes, so losing either draws a medic running like a scout.
        ActiveWeapon: 17,
        WeaponClass: "CTFRevolver",
        WeaponItem: 61,

        // HandleJumping's answer, not null and not the push-off, so losing it reads as falling through to the run.
        JumpActivity: PlayerActivity.LegacyJump,

        // **A disguise that is BOTH up and enemy-facing**, because both halves gate every branch of
        // `C_TFPlayer::ValidateModelIndex` and `GetSkin`. A fixture with the condition and no
        // `IsEnemy` measures a disguise nobody is fooled by, which is the default behaviour again.
        //
        // Bit 3 is `TF_COND_DISGUISED` (`tf_shareddefs.h:693`), and the other four variables carry
        // distinct values so a reader that took only the first is measurable here too.
        Conditions: new PlayerConditions(
            1 << PlayerConditions.Disguised, 1 << 1, 1 << 2, 1 << 3, 1 << 4),

        // Half a second into a burn (B336) — past the 0.3-second peak, so it is on the falling side
        // and distinguishable from both ends of the ramp.
        BurningFor: 0.5f,

        // A demoman on the other team, and a medic's mask — the mask is read in exactly one branch
        // (an enemy spy disguised AS a spy), so a value that matched the disguise class would leave
        // that branch unmeasurable.
        DisguiseClass: 4,
        DisguiseTeam: SceneTeams.Red,
        DisguiseMaskClass: 5,
        IsEnemy: true,

        // **When this player last teleported or respawned** (B346), non-zero so losing it reads as
        // "never jumped" rather than as a default — and distinct from every other clock here, since
        // a discontinuity is its own event.
        DiscontinuitySeconds: 6.5d,

        // Non-zero, so losing it reads as level rather than as a default.
        EyePitch: 21f,

        // Different from Yaw above, which is the point: the feet and the eyes part company when a
        // player turns on the spot, and a rebuild that collapsed the two would pass if they matched.
        EyeYaw: -95f,
        AimYaw: 44f,

        // Waist deep, so losing it reads as dry land rather than as a default.
        WaterLevel: 2,

        // True, because false is the default and is exactly the value that was reaching the
        // renderer for every player before B280 — a dropped flag and a correct one were the same
        // observation, and every player slid through the map in one pose.
        ClientSideAnimated: true,

        // A reload in the slot it belongs to, because null is the default here and a player who
        // holds no gesture is indistinguishable from one whose gestures were dropped in a rebuild.
        Gestures: [new SceneGesture(
            GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND", null, AutoKill: true, 900)],

        // **Three DIFFERENT values, because the default of 1 is what they would hold if the wiring
        // dropped them** (B312) — and different from each other, so a carry into the wrong field is
        // visible too. This test is what caught all three when they were added: it enumerates the
        // record's properties and demands each be given a distinctive value, so a field cannot be
        // introduced without being covered.
        HeadScale: 1.5f,
        TorsoScale: 0.5f,
        HandScale: 2f,

        // A scout's 400, where a missing value is null and the step thresholds are zero (B172).
        MaxSpeed: 400f)
    {
        // Above Health, so a heal to it is measurable; null is the default (B415).
        MaxHealth = 175,
        MaxHealthForBuffing = 176,
        EntityHealth = 88,
        HideHud = 8,

        // A part-empty clip, and the HUD's ammo inputs — each null when not carried (STV).
        WeaponClip1 = 5,
        WeaponPrimaryAmmoType = 2,
        Ammo = [0, 32, 16],
        Items = [new SceneItem(40, "CTFWearable", 30, new EconAttributeWire([], [], HasValidItemId: true), IsWeapon: false)],
        OwnAttributes = [new EconAttributeValue(54, 0x3F000000)],

        // The target ID's inputs: a disguise worn as player 7 at 90 health, a kill streak, and a half-charged unique medigun.
        DisguiseTarget = 7,
        DisguiseHealth = 90,
        PlayerState = 3,
        CarryingObject = true,
        StunFlags = 2,
        StunIndex = 0,
        IsMiniBoss = true,
        ActiveWeaponClip = 6,
        KillStreak = 4,
        InvisChangeCompleteTime = 5.5f,
        CloakMeter = 42f,
        DisguiseWeapon = 31,
        Decapitations = 3,

        // The item effect meters' inputs: each non-default, the draining flag set.
        RageMeter = 64f,
        RageDraining = true,
        ItemChargeMeter = [0f, 50f],
        HypeMeter = 33f,
        RevengeCrits = 2,
        RuneCharge = 12f,
        KartNextAvailableBoost = 8.5f,
        KartHealth = 40,
        SpawnCounter = 1,
        PlayerSkinOverride = 1,
        DisguiseSkinOverride = 1,
        DisguiseWeaponItem = new SceneItem(31, "CTFRevolver", 24, new EconAttributeWire([], [], HasValidItemId: true), IsWeapon: true) { Quality = 6 },
        Velocity = (1f, 2f, 3f),

        // The recorder's restored movement state (B450), present where anyone else's is null.
        Movement = new SceneLocalMovement { Ducked = true, DuckTime = 812.5f, TickBase = 4000 },
        WeaponAccountId = 1234u,
        DeathTime = 77.25f,

        // Half gravity, since 0 is both the default and "unset" (D205's prediction reads it as 1).
        Gravity = 0.5f,
        WeaponQuality = 6,
        HasTheFlag = true,
        Medigun = (0.5f, 6, 29),
        ActiveMedigun = (3, 0.5f),

        // `GetFOV`'s inputs: a sniper zooming from 90 to 20 over 0.1 s, begun at 12.5 s. Zero is each one's "unset".
        Fov = 20,
        FovStart = 90,
        FovTime = 12.5f,
        FovRate = 0.1f,
        DefaultFov = 85,

        // The movement modes' inputs (B450).
        ViewOffsetZ = 68f,
        MovementStunTime = 2.5f,
        MovementStunAmount = 153,
        MovementStunParity = 3,
        AllowMoveDuringTaunt = true,
        CurrentTauntMoveSpeed = 212.5f,
        VehicleReverseTime = 104.25f,
        GrapplingHookTarget = 2,
        TauntItemDefIndex = 1157,
        ActiveTauntSlot = -1,
    };

    /// <summary>Every property of a type that a test can read.</summary>
    private static IEnumerable<PropertyInfo> Readable<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
}
