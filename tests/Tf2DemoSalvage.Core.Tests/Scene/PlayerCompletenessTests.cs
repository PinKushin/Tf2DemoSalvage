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

        // A weapon in hand, and its class — the pair that decides which suffix every body activity
        // takes, so losing either draws a medic running like a scout.
        ActiveWeapon: 17,
        WeaponClass: "CTFRevolver",
        WeaponItem: 61,

        // Inside the push-off window, so losing it reads as the float rather than as a default.
        AirborneSeconds: 0.25f,

        // **A disguise that is BOTH up and enemy-facing**, because both halves gate every branch of
        // `C_TFPlayer::ValidateModelIndex` and `GetSkin`. A fixture with the condition and no
        // `IsEnemy` measures a disguise nobody is fooled by, which is the default behaviour again.
        //
        // Bit 3 is `TF_COND_DISGUISED` (`tf_shareddefs.h:693`), and the other four variables carry
        // distinct values so a reader that took only the first is measurable here too.
        Conditions: new PlayerConditions(
            1 << PlayerConditions.Disguised, 1 << 1, 1 << 2, 1 << 3, 1 << 4),

        // A demoman on the other team, and a medic's mask — the mask is read in exactly one branch
        // (an enemy spy disguised AS a spy), so a value that matched the disguise class would leave
        // that branch unmeasurable.
        DisguiseClass: 4,
        DisguiseTeam: SceneTeams.Red,
        DisguiseMaskClass: 5,
        IsEnemy: true,

        // True, since false is the default and would hide the field being dropped.
        Airwalking: true,

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
            GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND", null, AutoKill: true, 900)]);

    /// <summary>Every property of a type that a test can read.</summary>
    private static IEnumerable<PropertyInfo> Readable<T>() =>
        typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
}
