using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// How a cosmetic's skeleton hangs together, and onto which of a player's bones.
/// </summary>
/// <remarks>
/// A probe, not a test. The owner reports a scout's hat lying on the ground while the merge log
/// says the item matched <c>bip_head</c> — so the bone carrying the geometry is one of the seven
/// that did NOT match, and where it lands depends entirely on what its parent is.
///
/// If <c>prp_hat</c> is a child of <c>bip_head</c> it rides the merged head. If it is a root, it is
/// placed by the wearer's transform alone, which on a player is their FEET — which is what a hat on
/// the floor looks like.
///
/// Read from the model rather than from the running viewer deliberately: it needs no desktop, gives
/// the same answer every time, and the viewer had to reach a frame with players drawn before it
/// would say anything at all.
/// </remarks>
public sealed class HatSkeletonProbe
{
    [Test]
    public void WhatDoesAHatHangOff()
    {
        if (Environment.GetEnvironmentVariable("TF2_FOLDER") is not { Length: > 0 } folder ||
            !Directory.Exists(folder))
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);

        string[] wanted =
        [
            "models/player/items/all_class/ghostly_gibus_scout.mdl",
            "models/player/items/all_class/hwn_spellbook_complete.mdl",
            "models/player/scout.mdl",
            "models/player/soldier.mdl",
        ];

        foreach (string path in wanted)
        {
            if (archives.Read(path) is not { } bytes)
            {
                TestContext.Out.WriteLine($"HAT {path}: not found");
                continue;
            }

            IReadOnlyList<StudioBone> bones = StudioBones.Read(bytes);

            List<string> described = [];

            for (int index = 0; index < bones.Count && described.Count < 14; index++)
            {
                int parent = bones[index].Parent;

                described.Add(
                    parent >= 0 && parent < bones.Count
                        ? $"[{index}]{bones[index].Name}<-[{parent}]{bones[parent].Name}"
                        : $"[{index}]{bones[index].Name}<-ROOT");
            }

            TestContext.Out.WriteLine(
                $"HAT {Path.GetFileName(path)}: {bones.Count} bones; {string.Join("  ", described)}");

            // **Whether any parent comes AFTER its child**, which is the other way the placement
            // could fail: the walk builds a bone from its parent's matrix and can only do that if
            // the parent was already built, so a model listing them out of order would silently
            // fall back to model space for exactly those bones.
            int outOfOrder = 0;

            for (int index = 0; index < bones.Count; index++)
            {
                if (bones[index].Parent >= index)
                {
                    outOfOrder++;
                }
            }

            TestContext.Out.WriteLine(
                $"HAT   bones whose parent is listed after them: {outOfOrder}");

            // **Where the rest skeleton actually puts each bone**, which is the number the merge
            // hands over. A scout stands about 83 units tall with their origin at their feet, so
            // bip_head belongs near z 64. Anything close to zero means what is being passed is not
            // a bone-to-world matrix at all.
            StudioSkeleton rest = StudioBones.RestPose(bones);

            List<string> placed = [];

            for (int index = 0; index < bones.Count && placed.Count < 4; index++)
            {
                if (!bones[index].Name.Contains("head", StringComparison.OrdinalIgnoreCase) &&
                    !bones[index].Name.Contains("pelvis", StringComparison.OrdinalIgnoreCase) &&
                    !bones[index].Name.Contains("prp_hat", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                float[] world = rest.BoneToWorld[index];
                float[] skin = rest.Matrices[index];

                placed.Add(
                    $"{bones[index].Name} boneToWorld=({world[3]:0.#},{world[7]:0.#},{world[11]:0.#})" +
                    $" skinning=({skin[3]:0.#},{skin[7]:0.#},{skin[11]:0.#})");
            }

            TestContext.Out.WriteLine($"HAT   rest: {string.Join("  ", placed)}");
        }

        Assert.Pass();
    }
}
