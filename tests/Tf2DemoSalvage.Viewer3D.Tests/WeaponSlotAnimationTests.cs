using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

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
    public void HoldingASecondaryPlaysADifferentSequenceFromAPrimary()
    {
        if (Environment.GetEnvironmentVariable("TF2_FOLDER") is not { Length: > 0 } folder ||
            !Directory.Exists(folder))
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);

        if (archives.Read("models/player/medic.mdl") is not { } baseFile)
        {
            Assert.Ignore("medic.mdl not found in the install.");
            return;
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

        PropModels.SkinnedModel model = new(
            StudioBones.Read(baseFile),
            models,
            StudioSequenceTable.Merge(groups),
            groups,
            shared,
            masterPose);

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
    public void TheMedigunResolvesToTheSecondarySlot()
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

        WeaponRoles roles = WeaponRoles.Read(archives.Read, ["CWeaponMedigun", "CTFScatterGun"]);

        roles.Suffix("CWeaponMedigun").ShouldBe("SECONDARY");
        roles.Suffix("CTFScatterGun").ShouldBe("PRIMARY");
    }
}
