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

            // **The model's own hull, which says which axis is up without any of our code.**
            // studiohdr_t carries hull_min at 104 and hull_max at 116 — counted as id 0, version 4,
            // checksum 8, name[64] 12, length 76, eyeposition 80, illumposition 92, then the two
            // hulls, which puts numbones at 156 exactly where this project already reads it.
            //
            // A standing TF2 player's hull is about (-24,-24,0) to (24,24,82). If the hull is tall
            // in Z while the bones this project reads are tall in Y, then the bone reader is
            // transposing axes and every skeleton in the game is lying down — one fault, not the
            // dozen symptoms being chased.
            if (bytes.Length >= 128)
            {
                float Read(int at) =>
                    BitConverter.ToSingle(bytes, at);

                TestContext.Out.WriteLine(
                    $"HAT   hull: ({Read(104):0.#},{Read(108):0.#},{Read(112):0.#}) to " +
                    $"({Read(116):0.#},{Read(120):0.#},{Read(124):0.#})");
            }

            // **And the vertices, which is the tie-breaker.** The hull says these models stand 83
            // units tall in Z; the bones this project reads put the head 73 along Y. Whichever the
            // VERTICES agree with is the one telling the truth, because the vertices and the bones
            // have to be in the same space for skinning to mean anything.
            if (archives.Read(Path.ChangeExtension(path, ".vvd")) is { } vertexFile)
            {
                IReadOnlyList<StudioVertex> corners = StudioVertices.Read(vertexFile);

                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

                foreach (StudioVertex corner in corners)
                {
                    minX = MathF.Min(minX, corner.X);
                    maxX = MathF.Max(maxX, corner.X);
                    minY = MathF.Min(minY, corner.Y);
                    maxY = MathF.Max(maxY, corner.Y);
                    minZ = MathF.Min(minZ, corner.Z);
                    maxZ = MathF.Max(maxZ, corner.Z);
                }

                TestContext.Out.WriteLine(
                    $"HAT   vertices: {corners.Count} corners, x {minX:0.#}..{maxX:0.#} " +
                    $"y {minY:0.#}..{maxY:0.#} z {minZ:0.#}..{maxZ:0.#}");
            }
        }

        Assert.Pass();
    }
}
