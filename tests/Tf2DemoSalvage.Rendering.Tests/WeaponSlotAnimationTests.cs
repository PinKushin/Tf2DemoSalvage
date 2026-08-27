using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The weapon a player holds changes which sequence their body plays.
/// </summary>
/// <remarks>
/// **The assertion that catches the wiring, which is the half unit tests keep missing here.** Three
/// no-ops have shipped in this project with a green suite — a dumper annotation that matched the
/// wrong type, a kill feed that looked up numbers in strings, and a playback rate nothing read. Each
/// was a component that worked when called and a caller that never called it that way.
///
/// So this asks the real lookup, on a real model, for two different weapons, and requires the
/// answers to DIFFER. A suffix that is computed and then discarded — which is what happened between
/// reading the weapon scripts and wiring them in — produces the same sequence for both and fails
/// here, while every component test still passes.
///
/// <c>ACT_MP_RUN_SECONDARY</c> and <c>ACT_MP_RUN_PRIMARY</c> are separate sequences in a class's
/// animation model, with the arms carried differently, so a medic running with a medigun genuinely
/// should not resolve to the scattergun's number.
/// </remarks>
public sealed class WeaponSlotAnimationTests
{
    [Test]
    public void WeaponSlotAnimation_ASecondary_PlaysADifferentSequenceFromAPrimary()
    {
        if (Model() is not { } model)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        // Running, on the ground, alive — the ordinary case, varied only by what is in the hands.
        const float running = 300f;

        int primary = PlayerAnimation.For(
            model, running, PlayerActivityState.OnGround, alive: true, slot: "PRIMARY");

        int secondary = PlayerAnimation.For(
            model, running, PlayerActivityState.OnGround, alive: true, slot: "SECONDARY");

        primary.ShouldBeGreaterThanOrEqualTo(0, "a medic has a primary run");
        secondary.ShouldBeGreaterThanOrEqualTo(0, "and a secondary one, which is the medigun's");

        secondary.ShouldNotBe(
            primary,
            "the suffix must reach the lookup; equal numbers mean it was computed and discarded");
    }

    [Test]
    public void WeaponSlotAnimation_TheTwoJumpPhases_ResolveToDifferentSequences()
    {
        // **The same wiring question as the slot, asked of the jump clock.** A push-off and a float
        // are separate sequences in every class model, so a player in their first half second must
        // not resolve to the one they play a second later. If the airborne time were computed and
        // discarded these would be equal, and every jump would look like the float it looked like
        // before — which is exactly the shape of the four no-ops this project has recorded.
        if (Model() is not { } model)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        // Airborne: the ground flag clear and nothing else set.
        const int airborne = 0;

        int start = PlayerAnimation.For(
            model, speed: 200f, airborne, alive: true, slot: "PRIMARY", airborneSeconds: 0.1f);

        int floating = PlayerAnimation.For(
            model, speed: 200f, airborne, alive: true, slot: "PRIMARY", airborneSeconds: 1.0f);

        start.ShouldBeGreaterThanOrEqualTo(0, "a medic has a jump push-off");
        floating.ShouldBeGreaterThanOrEqualTo(0, "and a float");

        start.ShouldNotBe(
            floating,
            "the jump clock must reach the lookup; equal numbers mean it was computed and discarded");
    }

    [Test]
    public void WeaponSlotAnimation_TheMedigun_ResolvesToTheSecondarySlot()
    {
        // **The other half of the wiring, asked of the game's data rather than of a literal.** The
        // test above proves the slot changes the sequence; this proves the slot a medigun produces
        // is the one that does it. Split because they fail for different reasons — a broken script
        // read and a broken lookup are not the same defect.
        if (Environment.GetEnvironmentVariable("TF2_FOLDER") is not { Length: > 0 } folder ||
            !Directory.Exists(folder))
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);

        const int medic = 5;

        WeaponRoles roles = WeaponRoles.Read(
            archives.Read, [("CWeaponMedigun", medic), ("CTFScatterGun", (int?)null)]);

        roles.Suffix("CWeaponMedigun", medic).ShouldBe("SECONDARY");
        roles.Suffix("CTFScatterGun").ShouldBe("PRIMARY");
    }

    /// <summary>The medic, loaded the way the viewer loads a player model.</summary>
    /// <returns>The model, or null when the game is not installed.</returns>
    /// <remarks>
    /// **Through the real merge**, including the pose parameters, because a model built any other
    /// way would not resolve the activities these tests ask for — the movement parameters live in
    /// the included animation model rather than in the base one (B101).
    /// </remarks>
    private static PropModels.SkinnedModel? Model()
    {
        if (Environment.GetEnvironmentVariable("TF2_FOLDER") is not { Length: > 0 } folder ||
            !Directory.Exists(folder))
        {
            return null;
        }

        GameArchives archives = GameArchives.Open(folder);

        if (archives.Read("models/player/medic.mdl") is not { } baseFile)
        {
            return null;
        }

        List<byte[]> models = [baseFile];
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [(0, StudioSequences.Read(baseFile))];

        foreach (string include in StudioModelGroups.Read(baseFile))
        {
            if (archives.Read(include) is { } included)
            {
                groups.Add((models.Count, StudioSequences.Read(included)));
                models.Add(included);
            }
        }

        (IReadOnlyList<StudioPoseParameter> shared, IReadOnlyList<IReadOnlyList<int>> masterPose) =
            StudioPoseParameterMerge.Merge(
                [.. models.Select(file => StudioSequences.PoseParameters(file))]);

        return new PropModels.SkinnedModel(
            StudioBones.Read(baseFile),
            models,
            StudioSequenceTable.Merge(groups),
            groups,
            shared,
            masterPose);
    }
}
