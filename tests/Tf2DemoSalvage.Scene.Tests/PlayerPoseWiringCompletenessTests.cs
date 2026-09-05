using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Every field a player and a pose SHARE survives the hop between them (B346).
/// </summary>
/// <remarks>
/// **This hop has now lost a field four times, and each time a new per-field test was written for
/// the one that was lost.** B259 lost `ClientSideAnimated` — no parameter at all, so every player
/// animated on the wrong clock. B312 lost the three per-bone scales, which is what
/// `PlayerBoneScaleWiringTests` guards. B346 lost `DiscontinuitySeconds`, and the timeline stamped
/// zero across 570 prop tracks while every unit test passed.
///
/// **A test per lost field cannot catch the next one**, which is the whole reason this exists. It
/// asserts the CLASS of defect rather than an instance: give a player a distinctive value for every
/// property it shares a name with on the pose, run the real `PlayerProps.Add`, and require the pose
/// to come back holding something other than its default.
///
/// **`PlayerProps.Add` builds the pose field by field**, so a value with no assignment there is one
/// the renderer never sees however well the timeline decoded it — and a missing assignment is a
/// DEFAULT rather than an error, which is why nothing else notices.
///
/// **Exemptions are named individually with the reason**, never as a blanket. A field the pose
/// deliberately computes rather than copies is a real answer; a field nobody thought about is the
/// defect. Requiring the distinction to be written down is what stops this suite decaying into a
/// list of whatever currently passes.
/// </remarks>
public sealed class PlayerPoseWiringCompletenessTests
{
    /// <summary>
    /// Shared names the pose does NOT simply copy, each with why.
    /// </summary>
    /// <remarks>
    /// **Every entry is a behaviour, not a convenience.** If a name lands here without one of these
    /// reasons still being true, the exemption is the bug.
    /// </remarks>
    private static readonly Dictionary<string, string> Computed = new(StringComparer.Ordinal)
    {
        ["Yaw"] =
            "the pose's yaw is the player's, but a player facing due east is the DEFAULT, so a " +
            "non-default fixture cannot distinguish carried from dropped here — covered by " +
            "PlayerCompletenessTests, which guards the record itself",

        ["Airwalking"] =
            "gated by the class script: `player.Airwalking && appearance.Airwalks(class)`, and " +
            "only the medic opts out — so a stub appearance decides this, not the player",

        ["Skin"] =
            "computed rather than carried: `m_nSkin = (team == TF_TEAM_RED) ? 0 : 1`, which the " +
            "client works out for itself (`c_tf_player.cpp:712`)",

        ["Slot"] =
            "resolved through the appearance from the weapon class, not copied",
    };

    [Test]
    public void Add_ForAPlayerWithEveryFieldSet_CarriesAllOfThemToThePose()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add([Distinctive()], drawn, new StubAppearance(), (_, _, _, body) => body);

        drawn.Count.ShouldBe(1, "the player reached the draw list at all");

        ScenePose pose = drawn[0].Pose;
        List<string> lost = [];

        foreach (PropertyInfo shared in Shared())
        {
            if (Computed.ContainsKey(shared.Name))
            {
                continue;
            }

            object? value = shared.GetValue(pose);

            if (IsDefault(value, shared.PropertyType))
            {
                lost.Add(shared.Name);
            }
        }

        lost.ShouldBeEmpty(
            $"PlayerProps.Add builds the pose field by field, and these came back at their " +
            $"defaults: {string.Join(", ", lost)}. Either add the assignment, or add the name to " +
            $"Computed with the reason the pose works it out for itself.");
    }

    /// <remarks>
    /// **The control, and without it the test above passes against a stub that fills everything.**
    /// A player stating nothing must produce a pose at its defaults — so the assertion above is
    /// about the CARRYING rather than about `PlayerProps.Add` writing values of its own.
    /// </remarks>
    [Test]
    public void Add_ForAPlayerStatingNothing_LeavesTheSharedFieldsAtTheirDefaults()
    {
        List<SceneProp> drawn = [];

        PlayerProps.Add([Plain()], drawn, new StubAppearance(), (_, _, _, body) => body);

        drawn.Count.ShouldBe(1);

        ScenePose pose = drawn[0].Pose;
        ScenePose empty = new();
        List<string> invented = [];

        foreach (PropertyInfo shared in Shared())
        {
            if (Computed.ContainsKey(shared.Name) ||
                shared.Name is nameof(ScenePose.Scale)
                    or nameof(ScenePose.HeadScale)
                    or nameof(ScenePose.TorsoScale)
                    or nameof(ScenePose.HandScale)
                    or nameof(ScenePose.Speed))
            {
                // **The four scales default to 1 at BOTH ends**, which is the engine's own answer
                // (`c_tf_player.cpp:577`) rather than a fallback — so they are not evidence either
                // way and are excluded from this direction only.
                //
                // **`Speed` widens.** A player's is a `float` and the pose's a `float?`, so a
                // stationary player carries a real zero where an unset pose holds null — and that
                // difference is the carrying working, not a value invented on the way. It is what
                // `UpdateClientSideAnimations` tests for membership of the animation list, so the
                // widening is load-bearing rather than incidental.
                continue;
            }

            if (!Equals(shared.GetValue(pose), shared.GetValue(empty)))
            {
                invented.Add(shared.Name);
            }
        }

        invented.ShouldBeEmpty(
            $"a player who states nothing must not acquire values on the way: {
                string.Join(", ", invented)}");
    }

    /// <summary>Properties that exist by name on both the player and the pose.</summary>
    private static IEnumerable<PropertyInfo> Shared()
    {
        HashSet<string> onPlayer = typeof(ScenePlayer)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        return typeof(ScenePose)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => onPlayer.Contains(property.Name) && property.CanRead);
    }

    /// <summary>Whether a value is the default for its type.</summary>
    private static bool IsDefault(object? value, Type type) =>
        value is null ||
        (type.IsValueType && value.Equals(Activator.CreateInstance(
            Nullable.GetUnderlyingType(type) ?? type)));

    /// <summary>A scout stating a distinctive value for every field it shares with a pose.</summary>
    /// <remarks>
    /// **Distinct values throughout, not one repeated.** A shared value cannot tell "carried" from
    /// "carried into the neighbouring field", which is the mistake `PlayerBoneScaleWiringTests`
    /// records for the three scales.
    /// </remarks>
    private static ScenePlayer Distinctive() =>
        Plain() with
        {
            X = 64f,
            Y = -32f,
            Z = 16f,
            Gestures =
            [
                new SceneGesture(
                    GestureSlot.AttackAndReload, "ACT_MP_ATTACK_STAND_PRIMARY", 1, false, 1d),
            ],
            HeadScale = 1.5f,
            TorsoScale = 0.5f,
            HandScale = 2f,
            Speed = 133f,
            MoveX = 0.25f,
            MoveY = -0.75f,
            Flags = 1,
            AirborneSeconds = 0.375f,
            EyePitch = 11f,
            EyeYaw = 22f,
            AimYaw = 33f,
            WaterLevel = 2,
            DiscontinuitySeconds = 4.5d,
        };

    /// <summary>An ordinary scout, stating nothing beyond what it must.</summary>
    private static ScenePlayer Plain() => new(2, 0f, 0f, 0f, SceneTeams.Red, 125, 1);
}
