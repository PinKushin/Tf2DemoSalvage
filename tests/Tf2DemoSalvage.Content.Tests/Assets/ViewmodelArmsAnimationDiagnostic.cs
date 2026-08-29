using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What every animation in <c>c_demo_arms</c> claims for the viewmodel weapon rack.
/// </summary>
/// <remarks>
/// **This separates a decode bug from a wrong-sequence bug, and nothing else can** (B222). In the
/// viewer, `c_demo_arms` bone 17 (`vm_weapon_bone_1`) is displaced up to 139 units from its rest
/// position by animation while bone 16 (`vm_weapon_bone`) never moves more than 20 — two bones with
/// the same parent whose bind positions are 1.51 units apart. The sticky launcher merges onto both,
/// so the model tears.
///
/// Two very different faults produce that, and they need opposite fixes:
///
/// <list type="number">
/// <item>the file really does contain a large displacement, and we are playing the wrong animation
/// — or it is legitimate and something downstream is at fault;</item>
/// <item>the file contains nothing of the sort and <see cref="StudioAnimation"/> is manufacturing
/// it, in which case the decode is wrong.</item>
/// </list>
///
/// **The file is the arbiter and it answers without a viewer, a demo, or anybody watching.** Every
/// animation, every frame, both bones — so a maximum here is the model's own claim rather than a
/// sample of whatever our sequence selection happened to pick.
///
/// **`StudioAnimation`'s own summary says the compressed paths are unverified**: only the
/// Quaternion64 path is covered by a test, and the rest is "unproven code on first contact". The
/// viewer's numbers carry the fingerprint of a saturated read — a raw `int16` near 32767 times a
/// small `posscale` lands on the 65.5 and 32.8 that dominate the log, in a power-of-two
/// relationship.
///
/// Explicit: it reports rather than asserts.
/// </remarks>
[Explicit("Diagnostic: what c_demo_arms' animations claim for the weapon rack.")]
public sealed class ViewmodelArmsAnimationDiagnostic
{
    [TestCase("models/weapons/c_models/c_demo_arms.mdl")]
    [TestCase("models/weapons/c_models/c_demo_animations.mdl")]

    // **A `v_` model, which is the whole other scheme and was never measured.** The section fix was
    // validated entirely against `c_` models; the UI suite's 2013 demo uses `v_models`, where the
    // networked viewmodel IS the weapon and there is no separate arms model. It stopped drawing.
    [TestCase("models/weapons/v_models/v_scattergun_scout.mdl")]
    [TestCase("models/weapons/v_models/v_rocketlauncher_soldier.mdl")]
    public void ReportWeaponRackTravel(string Model)
    {
        if (Read(Model) is not { } bytes)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        // **The arms' own file carries ONE animation, and that is not what the viewer plays.** A
        // viewmodel's animations live in the models it `$includemodel`s, so sweeping only the base
        // file answers a narrower question than the one being asked — the first version of this
        // diagnostic did exactly that and read "bone 17 never moves" off the wrong file.
        foreach (string included in StudioModelGroups.Read(bytes))
        {
            TestContext.Out.WriteLine($"  includes: {included}");
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(bytes);
        int animations = StudioAnimation.Count(bytes);

        TestContext.Out.WriteLine($"{Model}: {bones.Count} bones, {animations} local animations");

        // **Every bone, not just 16 and 17.** A per-bone worst case over the whole file says whether
        // a large displacement is peculiar to the rack or is everywhere — and "everywhere" would
        // mean the decode, not the animation.
        float[] worst = new float[bones.Count];
        int[] worstAnimation = new int[bones.Count];
        int[] worstFrame = new int[bones.Count];

        Array.Fill(worstAnimation, -1);

        // **The worst frame-to-frame JUMP, which is a different question from travel from rest.**
        // Travel says how far a bone gets; a jump says whether it got there continuously. Setting a
        // bound on the continuity test needs the distribution of jumps across the whole file, not a
        // number picked to make two cases pass.
        float[] jump = new float[bones.Count];
        int[] jumpAnimation = new int[bones.Count];
        int[] jumpFrame = new int[bones.Count];

        Array.Fill(jumpAnimation, -1);

        Dictionary<int, (float X, float Y, float Z)> previous = [];

        for (int animation = 0; animation < animations; animation++)
        {
            int frames = StudioAnimation.Frames(bytes, animation);

            // **How each animation is laid out, and whether Pose can read it at all.** A section's
            // own `animblock` is what decides that, and the section fix made this reader consult it
            // for the first time — so an animation whose sections say "the data is elsewhere" now
            // returns nothing where it used to return section zero's data.
            (int f, int sf, int si, int blk, int dat) = StudioAnimation.Sectioning(bytes, animation);

            int decoded = StudioAnimation.Pose(bytes, bones, animation, 0).Count;

            if (sf != 0 || blk != 0 || decoded == 0)
            {
                TestContext.Out.WriteLine(
                    $"  LAYOUT animation {animation}: frames {f}, sectionFrames {sf}, " +
                    $"sectionIndex {si}, animblock {blk}, animindex {dat}, " +
                    $"decoded {decoded} bones at frame 0");
            }

            previous.Clear();

            for (int frame = 0; frame < frames; frame++)
            {
                foreach (StudioBonePose posed in StudioAnimation.Pose(bytes, bones, animation, frame))
                {
                    if (posed.Bone < 0 || posed.Bone >= bones.Count)
                    {
                        continue;
                    }

                    StudioBone rest = bones[posed.Bone];

                    float dx = posed.Position.X - rest.Position.X;
                    float dy = posed.Position.Y - rest.Position.Y;
                    float dz = posed.Position.Z - rest.Position.Z;

                    float travelled = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                    if (travelled > worst[posed.Bone])
                    {
                        worst[posed.Bone] = travelled;
                        worstAnimation[posed.Bone] = animation;
                        worstFrame[posed.Bone] = frame;
                    }

                    if (previous.TryGetValue(posed.Bone, out (float X, float Y, float Z) was))
                    {
                        float jx = posed.Position.X - was.X;
                        float jy = posed.Position.Y - was.Y;
                        float jz = posed.Position.Z - was.Z;

                        float moved = MathF.Sqrt((jx * jx) + (jy * jy) + (jz * jz));

                        if (moved > jump[posed.Bone])
                        {
                            jump[posed.Bone] = moved;
                            jumpAnimation[posed.Bone] = animation;
                            jumpFrame[posed.Bone] = frame;
                        }
                    }

                    previous[posed.Bone] = posed.Position;
                }
            }
        }

        int reported = 0;

        for (int bone = 0; bone < bones.Count; bone++)
        {
            if (worstAnimation[bone] < 0 || worst[bone] < 1f)
            {
                continue;
            }

            reported++;

            TestContext.Out.WriteLine(
                $"  [{bone,2}] {bones[bone].Name,-24} worst {worst[bone],9:0.##} units " +
                $"in animation {worstAnimation[bone]} frame {worstFrame[bone]} " +
                // **`rotscale` is the CONTROL for `posscale`.** Every bone of two different models
                // reported a `posscale` of exactly 1/256, which is either a genuine studiomdl
                // constant or a reader taking the same bytes twice. The two fields are adjacent in
                // `mstudiobone_t` (72 and 84), so if the offsets were wrong they would read the same
                // thing; a rotscale that differs proves they do not.
                $"(posscale {bones[bone].PositionScale.X:0.######}, " +
                $"{bones[bone].PositionScale.Y:0.######}, {bones[bone].PositionScale.Z:0.######}" +
                $" | rotscale {bones[bone].RotationScale.X:0.######}, " +
                $"{bones[bone].RotationScale.Y:0.######}, {bones[bone].RotationScale.Z:0.######})");
        }

        TestContext.Out.WriteLine($"  {reported} bones move more than a unit anywhere in the file");

        TestContext.Out.WriteLine("  --- worst frame-to-frame jump, every bone, descending ---");

        foreach (int bone in Enumerable.Range(0, bones.Count)
            .Where(each => jumpAnimation[each] >= 0)
            .OrderByDescending(each => jump[each]))
        {
            TestContext.Out.WriteLine(
                $"  JUMP [{bone,2}] {bones[bone].Name,-24} {jump[bone],8:0.##} units " +
                $"entering animation {jumpAnimation[bone]} frame {jumpFrame[bone]}");
        }
    }

    /// <summary>Whether a decoded animation is continuous from one frame to the next.</summary>
    /// <remarks>
    /// **This decides whether the decode is wrong WITHOUT a reference implementation, and nothing
    /// else available here does** (B222). Every other question about these numbers — is 128 units
    /// plausible, is `posscale` really 1/256, does `pPosV` land in the right place — needs something
    /// to compare against. Continuity does not: an animation is a person moving, so consecutive
    /// frames differ by a little. A misread stream produces `int16`s drawn from arbitrary bytes,
    /// which jump the full ±32767 range between neighbouring frames.
    ///
    /// So a large frame-to-frame jump is a decode fault by construction, and a smooth curve says the
    /// decode is right and the fault is downstream — the two possibilities that need opposite fixes.
    ///
    /// The saturation arithmetic that prompted it: `posscale` reads exactly 1/256 on every bone and
    /// axis, and 32767 × 1/256 = 128.0, which is `bip_wrist_R`'s worst travel to the decimal.
    /// </remarks>
    [TestCase("models/weapons/c_models/c_demo_animations.mdl", 41, 15)]
    [TestCase("models/weapons/c_models/c_demo_animations.mdl", 73, 19)]
    public void ReportFrameContinuity(string model, int animation, int bone)
    {
        if (Read(model) is not { } bytes)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(bytes);
        int frames = StudioAnimation.Frames(bytes, animation);

        // **Two mechanisms sit between the tracks and the engine's pose, and neither is implemented**
        // — `CalcZeroframeData` and `CalcLocalHierarchyAnimation`. A reparented bone would show up
        // as a localised discontinuity in exactly one bone, which is what the residual looks like,
        // so the first question is whether these animations use either at all.
        (int hierarchy, int zeroFrames) = StudioAnimation.Unimplemented(bytes, animation);

        TestContext.Out.WriteLine(
            $"{model} animation {animation}, bone {bone} ({bones[bone].Name}), {frames} frames, " +
            $"localHierarchy {hierarchy}, zeroFrames {zeroFrames}");

        // **The chain, as bone indices in the order `Pose` walked them.** Valve does not walk this
        // chain: `CalcAnimation` (`bone_setup.cpp:1095`) iterates BONES and consumes an entry only
        // where `panim->bone == i`, so a chain that is not strictly ascending leaves the later bones
        // on their bind values instead of applying an entry to whatever index it names. Printing the
        // order says whether these files rely on that.
        foreach (int at in new[] { 0, frames / 2 })
        {
            IReadOnlyList<StudioBonePose> walked = StudioAnimation.Pose(bytes, bones, animation, at);

            TestContext.Out.WriteLine(
                $"  chain at frame {at}: {walked.Count} entries — " +
                string.Join(", ", walked.Select(each => each.Bone)));
        }

        // **Which of the six encodings this bone's track actually uses.** RAWPOS is a Vector48 of
        // three float16s; ANIMPOS is run-length. DELTA changes what the value MEANS — the position
        // is an offset rather than a pose, so measuring it against the rest position is nonsense.
        // Five of the six paths are unverified by any test, so knowing which one is in play decides
        // where to look.
        foreach ((int at, int flags, int payload) in StudioAnimation.Tracks(bytes, bones, animation, 0)
            .Where(each => each.Bone == bone))
        {
            TestContext.Out.WriteLine(
                $"  bone {at} flags 0x{flags:X2}:" +
                $"{((flags & 0x01) != 0 ? " RAWPOS" : string.Empty)}" +
                $"{((flags & 0x02) != 0 ? " RAWROT" : string.Empty)}" +
                $"{((flags & 0x04) != 0 ? " ANIMPOS" : string.Empty)}" +
                $"{((flags & 0x08) != 0 ? " ANIMROT" : string.Empty)}" +
                $"{((flags & 0x10) != 0 ? " DELTA" : string.Empty)}" +
                $"{((flags & 0x20) != 0 ? " RAWROT2" : string.Empty)}");

            // **The raw bytes, because every structural explanation has been eliminated.** For
            // ANIMPOS|ANIMROT the rotation valueptr sits at the payload and the position valueptr
            // six bytes later, each three `short` offsets relative to ITSELF. A run-length block
            // then starts with `valid` and `total` bytes. If those read implausibly — a zero total,
            // or valid greater than total — the offset is landing somewhere that is not a block,
            // and that is the fault rather than anything about how it is walked afterwards.
            int posV = payload + 6;

            for (int channel = 0; channel < 3; channel++)
            {
                int offset = BinaryPrimitives.ReadInt16LittleEndian(
                    bytes.AsSpan()[(posV + (channel * 2))..]);

                string block = offset > 0 && posV + offset + 1 < bytes.Length
                    ? $"valid {bytes[posV + offset]}, total {bytes[posV + offset + 1]}"
                    : "(no data)";

                TestContext.Out.WriteLine(
                    $"    pos channel {channel}: offset {offset,6} -> {block}");

                // **The raw shorts, which settle whether this is our decode or the file.** A block
                // of `valid` values follows its two-byte header; scaled by `posscale` they are the
                // positions. If the file itself holds a value that produces a 100-unit excursion
                // then the excursion is the animation, and no amount of fixing the reader changes
                // it. If the raw values are smooth and the output is not, the reader is at fault.
                if (offset > 0 && posV + offset + 1 < bytes.Length)
                {
                    int valid = bytes[posV + offset];
                    List<string> raw = [];

                    for (int cell = 1; cell <= valid && posV + offset + (cell * 2) + 1 < bytes.Length; cell++)
                    {
                        short value = BinaryPrimitives.ReadInt16LittleEndian(
                            bytes.AsSpan()[(posV + offset + (cell * 2))..]);

                        raw.Add($"{value * bones[at].PositionScale.X:0.#}");
                    }

                    TestContext.Out.WriteLine($"      scaled: {string.Join(" ", raw)}");
                }
            }
        }

        (int f, int sf, int si, int blk, int dat) = StudioAnimation.Sectioning(bytes, animation);

        TestContext.Out.WriteLine(
            $"  layout: frames {f}, sectionFrames {sf}, sectionIndex {si}, " +
            $"animblock {blk}, animindex {dat}");

        (float X, float Y, float Z)? previous = null;

        float biggest = 0f;
        int biggestAt = -1;

        for (int frame = 0; frame < frames; frame++)
        {
            foreach (StudioBonePose posed in StudioAnimation.Pose(bytes, bones, animation, frame))
            {
                if (posed.Bone != bone)
                {
                    continue;
                }

                if (previous is { } was)
                {
                    float dx = posed.Position.X - was.X;
                    float dy = posed.Position.Y - was.Y;
                    float dz = posed.Position.Z - was.Z;

                    float jump = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                    if (jump > biggest)
                    {
                        biggest = jump;
                        biggestAt = frame;
                    }
                }

                // Every frame, no sampling: a jump is a single-frame event and a sample of one in
                // ten is exactly how an instrument misses it.
                TestContext.Out.WriteLine(
                    $"  frame {frame,3}: ({posed.Position.X,9:0.##}, {posed.Position.Y,9:0.##}, " +
                    $"{posed.Position.Z,9:0.##})");

                previous = posed.Position;
            }
        }

        TestContext.Out.WriteLine(
            $"  biggest frame-to-frame jump: {biggest:0.##} units, entering frame {biggestAt}");
    }

    private static byte[]? Read(string path)
    {
        if (!GameInstall.Available)
        {
            return null;
        }

        byte[]? found = null;

        foreach (string archive in new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" })
        {
            string full = Path.Combine(GameInstall.Require(), archive);

            if (File.Exists(full))
            {
                found ??= VpkArchive.Open(full).ReadFile(path);
            }
        }

        return found;
    }
}
