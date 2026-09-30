using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The body's activity goes through the held weapon's TABLE, as the engine's does (B105).
/// </summary>
/// <remarks>
/// **`CTFPlayerAnimState::TranslateActivity` hands `CalcMainActivity`'s answer to the weapon**
/// (`tf_playeranimstate.cpp:124-133`): `pWeapon->ActivityOverride( translateActivity, NULL )` walks the table
/// `ActivityList` picked for the weapon's role (`tf_weaponbase.cpp:4208`). The main sequence and every gesture take
/// the same step — `ComputeMainSequence` asks `SelectWeightedSequence( TranslateActivity( idealActivity ) )`
/// (`multiplayer_animstate.cpp:1168`).
///
/// **Pasting the role onto the activity's name is a table for ten of the twelve roles and wrong for two**, which is
/// why these exist: `s_acttablePrimary2` runs with the PRIMARY movement names (`tf_weaponbase.cpp:3780-3795`) and the
/// all-class melee table is keyed `MELEEALLCLASS` while its activities are spelled `_MELEE_ALLCLASS` (:4143-4153).
/// Each model here carries the pasted name as a decoy beside the table's answer, so a paste is caught picking it.
/// </remarks>
public sealed class PlayerAnimationWeaponTableTests
{
    /// <summary>Faster than the standing threshold, so the state machine chooses running.</summary>
    private const float Running = 200f;

    [Test]
    public void For_TheAllClassMeleeTable_RunsWithItsOwnActivity()
    {
        // The Frying Pan's, the Saxxy's and seventeen others' table (`anim_slot "MELEE_ALLCLASS"`).
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "ACT_MP_RUN_PRIMARY", "ACT_MP_RUN_MELEE", "ACT_MP_RUN_MELEE_ALLCLASS");

        PlayerAnimation.For(model, Running, flags: null, alive: true, slot: "MELEEALLCLASS").ShouldBe(2);
    }

    [Test]
    public void For_ThePrimary2Table_RunsWithThePrimaryActivity()
    {
        // The Cow Mangler's (`anim_slot "primary2"`): `{ ACT_MP_RUN, ACT_MP_RUN_PRIMARY }` at tf_weaponbase.cpp:3785.
        // Its reloads and attacks are the `_ALT` ones; its legs are the rocket launcher's.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With("ACT_MP_RUN_PRIMARY2", "ACT_MP_RUN_PRIMARY");

        PlayerAnimation.For(model, Running, flags: null, alive: true, slot: "PRIMARY2").ShouldBe(1);
    }

    /// <remarks>
    /// **What a table maps each of `CalcMainActivity`'s answers to**, one row per activity the port chooses. The primary
    /// rows are the control: they are what every player was drawn with before the table, and must not move.
    /// </remarks>
    [TestCase(PlayerActivity.StandIdle, "PRIMARY", "ACT_MP_STAND_PRIMARY")]
    [TestCase(PlayerActivity.Run, "PRIMARY", "ACT_MP_RUN_PRIMARY")]
    [TestCase(PlayerActivity.CrouchIdle, "PRIMARY", "ACT_MP_CROUCH_PRIMARY")]
    [TestCase(PlayerActivity.CrouchWalk, "PRIMARY", "ACT_MP_CROUCHWALK_PRIMARY")]
    [TestCase(PlayerActivity.Airwalk, "PRIMARY", "ACT_MP_AIRWALK_PRIMARY")]
    [TestCase(PlayerActivity.JumpStart, "PRIMARY", "ACT_MP_JUMP_START_PRIMARY")]
    [TestCase(PlayerActivity.Jump, "PRIMARY", "ACT_MP_JUMP_FLOAT_PRIMARY")]
    [TestCase(PlayerActivity.Swim, "PRIMARY", "ACT_MP_SWIM_PRIMARY")]
    [TestCase(PlayerActivity.SwimIdle, "MELEE", "ACT_MP_SWIM_MELEE")]
    [TestCase(PlayerActivity.JumpStart, "ITEM1", "ACT_MP_JUMP_START_ITEM1")]
    [TestCase(PlayerActivity.CrouchWalk, "MELEEALLCLASS", "ACT_MP_CROUCHWALK_MELEE_ALLCLASS")]
    [TestCase(PlayerActivity.StandIdle, "PRIMARY2", "ACT_MP_STAND_PRIMARY")]
    [TestCase(PlayerActivity.Run, "ITEM4", "ACT_MP_RUN_ITEM4")]
    [TestCase(PlayerActivity.Run, "SECONDARY2", "ACT_MP_RUN_SECONDARY2")]
    [TestCase(PlayerActivity.Die, "MELEE", "ACT_DIESIMPLE")]
    public void Translate_AnActivityInARolesHands_IsTheRowItsTableHolds(
        PlayerActivity activity, string role, string expected)
    {
        PlayerAnimation.Translate(activity, role).ShouldBe(expected);
    }
}
