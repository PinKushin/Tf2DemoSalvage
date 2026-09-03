using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Whether a weapon actually pairs with the player holding it, on the models TF2 ships.
/// </summary>
/// <remarks>
/// **Written because the viewer put eight of nine weapons at the map origin and the log could not
/// say why.** The line that would have answered it — <c>bone merge X onto Y: N of M bones
/// matched</c> — lived inside the method D88 deleted, so the refactor removed the diagnostic for
/// the thing it broke. That is the failure this project's logging rules exist to prevent, and it
/// happened anyway.
///
/// **A weapon is not a hat, and that is the question here.** A cosmetic shares <c>bip_head</c> and
/// a dozen more with its wearer; a <c>c_</c> weapon carries its own rig. If the two skeletons share
/// nothing, the merge matches nothing, and under D88 the weapon's bones stay at ITS OWN placement —
/// which for an attached entity is (0,0,0), the map origin. Before D88 it would at least have taken
/// the wearer's transform and sat at their feet.
///
/// So the observable difference between "correct" and "broken" is a bone count, and that is what
/// this measures.
/// </remarks>
public sealed class WeaponMergeContentTests
{
    private static string Game => GameInstall.Require();

    private const int Vertices = 0x00000400;

    [TestCase("models/player/demo.mdl", "models/weapons/c_models/c_stickybomb_launcher/c_stickybomb_launcher.mdl")]
    [TestCase("models/player/scout.mdl", "models/weapons/c_models/c_scattergun.mdl")]
    [TestCase("models/player/medic.mdl", "models/weapons/c_models/c_medigun.mdl")]
    public void SetupBones_AWeaponHeldByItsClass_SharesAtLeastOneBoneWithThePlayer(
        string player, string weapon)
    {
        IReadOnlyList<StudioBone> wielder = Bones(player);
        IReadOnlyList<StudioBone> held = Bones(weapon);

        List<string> shared = held
            .Select(bone => bone.Name)
            .Where(name => wielder.Any(
                other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // **The whole question, and it is a count.** No shared bone means no merge, and no merge
        // under D88 means the weapon's own placement stands — which for an attached entity is the
        // map origin. The viewer reported "8 AT THE ORIGIN" and this is what decides whether that
        // is a pairing failure or an instrument reading the wrong quantity.
        TestContext.Out.WriteLine(
            $"{Path.GetFileName(weapon)} on {Path.GetFileName(player)}: " +
            $"{shared.Count} of {held.Count} bones shared" +
            (shared.Count == 0 ? string.Empty : $" — {string.Join(", ", shared.Take(6))}"));

        shared.ShouldNotBeEmpty(
            $"{Path.GetFileName(weapon)} shares no bone name with {Path.GetFileName(player)}, so a " +
            $"bone merge cannot place it at all");
    }

    [Test]
    public void SetupBones_AWeaponMergedOntoAPlayerStandingAwayFromTheOrigin_IsNotAtTheOrigin()
    {
        // **The output-level version of the test above**, and the one that would have caught what
        // the viewer showed. The player stands a thousand units out; if the weapon's bones come
        // back near zero it is at the map origin, which is exactly the symptom reported.
        BoneFrameCounter clock = new();

        IReadOnlyList<StudioBone> playerBones = Bones("models/player/demo.mdl");
        IReadOnlyList<StudioBone> weaponBones =
            Bones("models/weapons/c_models/c_stickybomb_launcher/c_stickybomb_launcher.mdl");

        SkeletonPose playerPose = new(playerBones, (_, _, _, _) => [])
        {
            EntityTransform = [1f, 0f, 0f, 1000f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f],
        };

        AnimatingEntity player = new(playerPose, clock);

        // The weapon's own placement is the origin, exactly as the wire sends it: FollowEntity
        // zeroes local origin and angles because the client takes the parent's bones outright.
        AnimatingEntity held = new(new SkeletonPose(weaponBones, (_, _, _, _) => []), clock)
        {
            Follows = player,
        };

        held.SetupBones(Vertices, 0d).ShouldBeTrue();

        float furthest = 0f;

        for (int bone = 0; bone < held.Bones.Count; bone++)
        {
            furthest = MathF.Max(furthest, MathF.Abs(held.Bones.Bone(bone)[3]));
        }

        TestContext.Out.WriteLine($"furthest weapon bone along X: {furthest:0.#}");

        furthest.ShouldBeGreaterThan(
            900f,
            "every weapon bone is near the origin, so the merge placed nothing and the weapon is " +
            "drawn in the middle of the map rather than in the player's hands");
    }

    private static IReadOnlyList<StudioBone> Bones(string path)
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
        }

        byte[]? file = GameArchives.Open(Game).Read(path);

        if (file is null)
        {
            // Assert.Ignore is [DoesNotReturn], so nothing below runs and no null-forgiving
            // operator is needed — adding one is what S8969 objects to.
            Assert.Ignore($"{path} is not in this install");
        }

        return StudioBones.Read(file);
    }
}
