using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The bone fields read off models TF2 actually ships.
/// </summary>
/// <remarks>
/// **The half that catches a self-consistently wrong reader.** <c>BoneFlagReaderTests</c> builds its
/// own bytes, so it can only prove the reader agrees with the fixture — and both were written by the
/// same hand on the same day. These read Valve's files, where the expectation came from measurement
/// (<c>BoneFlagContentProbe</c>) rather than from what the reader happens to return.
///
/// **The strongest assertion here is the negative one**: no bone in any shipped model sets a bit
/// outside the families <c>studio.h</c> declares. A field read four bytes early or late still yields
/// a number, and this project's standing failure mode is a plausible number rather than an
/// exception — so "every bit is one the engine names" is what distinguishes a correct offset from
/// a wrong one that happens to look sane.
///
/// Skips rather than fails without the game, in the house pattern.
/// </remarks>
public sealed class BoneFlagContentTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    /// <summary>Every bit any <c>BONE_*</c> family declares, so an unknown one is detectable.</summary>
    private const int KnownFlags =
        StudioBoneFlags.UsedByAnything |
        StudioBoneFlags.AlwaysProcedural |
        StudioBoneFlags.PhysicallySimulated |
        StudioBoneFlags.PhysicsProcedural |
        0x00000008 |   // BONE_SCREEN_ALIGN_SPHERE
        0x00000010 |   // BONE_SCREEN_ALIGN_CYLINDER
        0x00F00000 |   // BONE_TYPE_MASK, which covers FIXED_ALIGNMENT and the SAVEFRAME pair
        0x00600000;    // BONE_HAS_SAVEFRAME_POS, BONE_HAS_SAVEFRAME_ROT

    [TestCase("models/player/scout.mdl")]
    [TestCase("models/player/heavy.mdl")]
    [TestCase("models/player/soldier.mdl")]
    [TestCase("models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.mdl")]
    [TestCase("models/player/items/scout/scout_cap.mdl")]
    public void Read_AShippedModelsBoneFlags_SetNoBitTheEngineDoesNotDeclare(string path)
    {
        IReadOnlyList<StudioBone> bones = Bones(path);

        int union = bones.Aggregate(0, (all, bone) => all | bone.Flags);

        (union & ~KnownFlags).ShouldBe(
            0,
            $"{path} sets flag bits studio.h does not declare, which means the field is being read " +
            $"from the wrong offset rather than that Valve invented a bit");

        // The control for the assertion above: a reader returning zero for every bone would satisfy
        // it perfectly. Every shipped model has at least one bone used by SOMETHING.
        union.ShouldNotBe(0);
    }

    [Test]
    public void Read_APlayerModel_MarksTheBonesCosmeticsMergeOnto()
    {
        IReadOnlyList<StudioBone> bones = Bones("models/player/scout.mdl");

        // Measured: 42 of the scout's 78. Asserted as "most of the skeleton" rather than as 42,
        // because the exact count is a fact about one model version and the claim is about whether
        // Valve marks them at all — which decides whether a wearer builds its whole skeleton for
        // every worn item (bone_merge_cache.cpp:95).
        bones.Count(bone => bone.IsMergeTarget).ShouldBeGreaterThan(20);

        bones.First(bone => string.Equals(bone.Name, "bip_head", StringComparison.Ordinal))
            .IsMergeTarget
            .ShouldBeTrue("a hat merges onto bip_head, so it must be marked");
    }

    [Test]
    public void Read_AWornItem_DoesNotMarkMergeBonesBecauseTheWearerDoes()
    {
        // **A worn item carries no merge marks and that is correct, not a gap.** The mask is read
        // off the entity being FOLLOWED — CBoneMergeCache checks m_pFollowHdr's bone flags — so the
        // hat needs none of its own. Asserted because the opposite reading would look like a bug in
        // the reader when it is a fact about the format.
        IReadOnlyList<StudioBone> bones = Bones("models/player/items/scout/scout_cap.mdl");

        bones.ShouldNotBeEmpty();
        bones.ShouldAllBe(bone => !bone.IsMergeTarget);
    }

    [Test]
    public void Read_TheScoutsHelperForearms_AreQuaternionInterpolatedProceduralBones()
    {
        IReadOnlyList<StudioBone> bones = Bones("models/player/scout.mdl");

        StudioBone helper = bones.First(
            bone => string.Equals(bone.Name, "hlp_forearm_L", StringComparison.Ordinal));

        helper.ProcedureType.ShouldBe(StudioProcedureType.QuaternionInterpolate);
        helper.ProcedureIndex.ShouldNotBe(0);
        helper.IsProcedural.ShouldBeTrue();
    }

    [Test]
    public void Read_TheSoldier_HasNoProceduralBonesAtAll()
    {
        // **The control for the test above, and it is a shipped model rather than a fixture.**
        // "The scout has procedural bones" and "the reader reports procedural bones for everything"
        // predict the same observation on the scout alone. The soldier is the bystander that must
        // come back empty — measured, and it does.
        IReadOnlyList<StudioBone> bones = Bones("models/player/soldier.mdl");

        bones.ShouldNotBeEmpty();
        bones.ShouldAllBe(bone => bone.ProcedureType == 0);
    }

    [Test]
    public void Read_EveryControllerSlotOnAPlayerModel_IsUnusedOrAValidIndex()
    {
        // **Measured: TF2's player models drive no bone from a controller at all.** That is worth
        // an assertion rather than a shrug, because it bounds how much CalcBoneAdj can matter here
        // — and if a later model does use one, this is where that shows up as a change rather than
        // as a silent new code path.
        IReadOnlyList<StudioBone> bones = Bones("models/player/heavy.mdl");

        foreach (StudioBone bone in bones)
        {
            bone.Controllers.Length.ShouldBe(6);

            foreach (int slot in bone.Controllers.ToArray())
            {
                slot.ShouldBe(-1, $"{bone.Name} drives a bone controller, which no TF2 player model did when this was measured");
            }
        }
    }

    private static IReadOnlyList<StudioBone> Bones(string path)
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return [];
        }

        byte[]? file = GameArchives.Open(Game).Read(path);

        if (file is null)
        {
            Assert.Ignore($"{path} is not in this install");
            return [];
        }

        return StudioBones.Read(file);
    }
}
