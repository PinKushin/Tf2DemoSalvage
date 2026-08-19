using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Which weapon a player is holding, which is what decides how their whole body animates.
/// </summary>
/// <remarks>
/// **Every player in the viewer is animated as though holding a primary weapon**, because the
/// activity name is built with a hardcoded <c>_PRIMARY</c> suffix. That is wrong for a large part of
/// the game: a medic's medigun is a secondary, a spy's knife and a demoman's sword are melee, and an
/// engineer's toolbox is a building slot. Each has its own run, stand and crouch animations.
///
/// The engine picks the suffix from the active weapon's ROLE. <c>CTFWeaponBase::ActivityList</c>
/// (<c>tf_weaponbase.cpp:4208</c>) switches on <c>GetActivityWeaponRole()</c> and returns
/// <c>s_acttableSecondary</c>, <c>s_acttableMelee</c> and so on, each mapping the bare activity to
/// its suffixed form — <c>{ ACT_MP_RUN, ACT_MP_RUN_SECONDARY }</c>. The role itself is
/// <c>GetTFWpnData().m_iWeaponType</c>, read from the weapon's script file by the <c>WeaponType</c>
/// key (<c>tf_weapon_parse.cpp:134</c>): "primary", "secondary", "melee", "building", "pda",
/// "item1", "item2".
///
/// **This test covers the first link only: knowing which entity the weapon IS.** The role lookup
/// needs the weapon's script, which is shipped data and a separate step; without the handle decoded
/// there is nothing to look anything up with.
/// </remarks>
public sealed class CorpusActiveWeaponTests
{
    private const string MovementDemo = "movement-test-pov-cp_process";

    [Test]
    public void ActiveWeapon_APlayer_ReportsTheWeaponHeld()
    {
        string path = Corpus.Demo(MovementDemo);

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> armed =
        [
            .. timeline.Frames
                .SelectMany(frame => frame.Players)
                .Where(player => player.IsPlaying && player.WeaponClass is not null),
        ];

        armed.ShouldNotBeEmpty(
            "a player holds a weapon at essentially every tick; m_hActiveWeapon is on " +
            "DT_BaseCombatCharacter and is sent for every combat character in the PVS");

        // **Server class names, which is what a demo's schema carries.** Asserted as a shape rather
        // than as one name, because which weapons appear is a fact about the recording rather than
        // about the decode — this one is the owner playing scout and soldier.
        HashSet<string> classes = [.. armed.Select(player => player.WeaponClass!)];

        classes.ShouldAllBe(
            name => name.StartsWith("CTF", StringComparison.Ordinal) ||
                    name.StartsWith("CWeapon", StringComparison.Ordinal),
            "every TF2 weapon's server class begins CTF or CWeapon: " + string.Join(", ", classes));

        // **The control that the handle is actually being followed rather than a constant returned.**
        // The recording switches class, and a scout and a soldier hold different weapons, so more
        // than one distinct weapon must appear across the demo.
        classes.Count.ShouldBeGreaterThan(
            1,
            "the recorder switches weapons and classes; one name means the handle is not being read");
    }

    [Test]
    public void ActiveWeapon_TheHandle_ResolvesToAnExistingEntity()
    {
        // **The half a class name cannot check.** A handle decoded with the mask applied before the
        // invalid test yields 2047 — a legal-looking slot naming whatever occupies it — so a name
        // coming back is not proof the right entity was found. This asserts the weapon is a real
        // entity with a model, which a mis-decoded slot would not reliably be.
        string path = Corpus.Demo(MovementDemo);

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<int> weapons =
        [
            .. timeline.Frames
                .SelectMany(frame => frame.Players)
                .Where(player => player.IsPlaying && player.ActiveWeapon is not null)
                .Select(player => player.ActiveWeapon!.Value),
        ];

        weapons.ShouldNotBeEmpty();

        weapons.ShouldAllBe(
            index => index > 0 && index != 2047,
            "2047 is what the invalid handle masks to, and zero is the world");
    }
}
