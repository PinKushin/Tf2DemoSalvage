using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which per-bone flags TF2's own models actually set.
/// </summary>
/// <remarks>
/// **`mstudiobone_t::flags` drives more of the engine than any other field on a bone** — the bone
/// mask, the merge cache, the procedural rules, and which of two blends `SlerpBones` runs. This
/// project reads the field and tests three of the bits, and had no idea which ones real content
/// uses.
///
/// <code>
///   bone-flags z1800
///   bone-flags z1800 20000
/// </code>
///
/// **Written for B292, whose flag may be rare**, and kept general because the same walk answers the
/// open questions beside it: how many bones are marked for bone merge (an unmarked one makes a
/// wearer build its whole skeleton, `bone_merge_cache.cpp:95`) and how many are procedural (B182,
/// where TF2's cosmetics lean on jiggle bones this project does not simulate).
///
/// **Denominators, always.** Every row prints the count of bones examined beside the count carrying
/// the bit, because a zero with no denominator is a fact about the probe
/// (<c>docs/memory/an-empty-search-needs-a-control.md</c>).
/// </remarks>
public sealed class BoneFlagProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "bone-flags";

    /// <inheritdoc/>
    public string Summary => "which per-bone flags real models set: bone-flags <demo> [tick]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("bone-flags <demo> [tick]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);

        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(file).MapName;

        if (mapName.Length == 0 ||
            locator.Find(mapName) is not { } mapPath ||
            locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        int tick = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        // **Players are not props, and IK lives on nothing else.** `PropsAt` reports entities with
        // models — weapons, cosmetics, buildings — and a PLAYER becomes a prop only when
        // `PlayerProps.Add` puts it there. Without this the census walks map decorations and
        // reports zero IK chains on a demo full of them, which is exactly the wrong answer this
        // probe gave before it was corrected (B296).
        PlayerProps.Add(
            players, props, new GameAppearance(game.Classes, null), (_, _, _, body) => body);

        new WeaponPropModels().Resolve(props, players, game.Weapons.For);

        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        (string Name, int Bit)[] flags =
        [
            ("BONE_FIXED_ALIGNMENT", StudioBoneFlags.FixedAlignment),
            ("BONE_USED_BY_BONE_MERGE", StudioBoneFlags.UsedByBoneMerge),
            ("BONE_ALWAYS_PROCEDURAL", StudioBoneFlags.AlwaysProcedural),
            ("BONE_PHYSICALLY_SIMULATED", StudioBoneFlags.PhysicallySimulated),
            ("BONE_PHYSICS_PROCEDURAL", StudioBoneFlags.PhysicsProcedural),
            ("BONE_USED_BY_HITBOX", StudioBoneFlags.UsedByHitbox),
            ("BONE_USED_BY_ATTACHMENT", StudioBoneFlags.UsedByAttachment),
            ("BONE_USED_BY_VERTEX_LOD0", StudioBoneFlags.UsedByVertexLod0),
        ];

        int[] counts = new int[flags.Length];
        Dictionary<int, List<string>> examples = [];

        // **`proctype` decides WHICH rule computes a procedural bone, and the five are not
        // interchangeable.** `CalcProceduralBone` (`bone_setup.cpp:4932`) handles AXISINTERP,
        // QUATINTERP, AIMATBONE and AIMATATTACH and returns false for anything else; JIGGLE is
        // handled separately in `BuildTransformations`. So the flag count alone cannot say which
        // implementation a model is waiting on.
        Dictionary<int, int> byType = [];
        Dictionary<int, List<string>> typeExamples = [];

        int bones = 0;
        int models = 0;
        int springs = 0;
        int helpers = 0;
        int chained = 0;
        int links = 0;
        List<string> ikExamples = [];

        foreach (string model in props
            .Select(prop => prop.ModelPath)
            .Where(model => model.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.Ordinal))
        {
            if (assets.Geometry(model)?.Skinned is not { } skinned)
            {
                continue;
            }

            models++;

            // **IK, measured with the same discipline as the procedural rules.** The chains are
            // read already — `StudioIkChains.Read` has existed since the reader was written — and
            // nothing solves them. Whether that matters is a count, and the conformance table has
            // been claiming the data "is not even loaded", which is not true.
            if (skinned.Models.Count > 0 &&
                StudioIkChains.Read(skinned.Models[0]) is { Count: > 0 } chains)
            {
                chained++;
                links += chains.Count;

                if (ikExamples.Count < 4)
                {
                    ikExamples.Add(
                        $"{Path.GetFileName(model)}:{chains.Count.ToString(CultureInfo.InvariantCulture)}");
                }
            }

            for (int index = 0; index < skinned.Bones.Count; index++)
            {
                StudioBone bone = skinned.Bones[index];

                bones++;

                if (bone.ProcedureType != 0)
                {
                    byType[bone.ProcedureType] = byType.GetValueOrDefault(bone.ProcedureType) + 1;

                    if (!typeExamples.TryGetValue(bone.ProcedureType, out List<string>? shown))
                    {
                        typeExamples[bone.ProcedureType] = shown = [];
                    }

                    if (shown.Count < 4)
                    {
                        // **Whether the bone carries VERTICES, which decides whether implementing
                        // its rule changes anything a viewer can see.** A procedural bone nothing
                        // is skinned to computes a transform that reaches no mesh — the count alone
                        // would say "four bones unimplemented" either way, and only this separates
                        // a real gap from a bookkeeping one.
                        bool skinnedTo =
                            (bone.Flags & StudioBoneFlags.UsedByVertexLod0) != 0;

                        shown.Add(
                            $"{Path.GetFileName(model)}:{bone.Name}{(skinnedTo ? " SKINNED" : " no-verts")}");
                    }

                    // **The parameters themselves, for the first few, because a stride or a field
                    // order that is wrong reads as plausible numbers rather than as an error.** A
                    // length of 3 to 12 units with stiffnesses in the tens or hundreds is what an
                    // authored jiggle bone looks like; a length of 1e-38 or 4e9 is a misread.
                    if (springs < 6 &&
                        assets.Geometry(model)?.Skinned is { Models.Count: > 0 } withBytes &&
                        StudioJiggleBones.Read(withBytes.Models[0], index) is { } jiggle)
                    {
                        springs++;

                        output.WriteLine(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"  jiggle {Path.GetFileName(model)}:{bone.Name} " +
                                $"flags 0x{jiggle.Flags:X2} length {jiggle.Length:0.##} " +
                                $"tipMass {jiggle.TipMass:0.##} " +
                                $"yaw {jiggle.YawStiffness:0.#}/{jiggle.YawDamping:0.#} " +
                                $"pitch {jiggle.PitchStiffness:0.#}/{jiggle.PitchDamping:0.#} " +
                                $"angleLimit {jiggle.AngleLimit:0.###}"));
                    }

                    // **The control bone and whether it has a PARENT, which is B317's open
                    // question.** `DoQuatInterpBone` fills its `bonematrix` only inside
                    // `if (pProc && pbones[pProc->control].parent != -1)` and concatenates it into
                    // the skeleton OUTSIDE that guard — so a rule whose control is the root writes
                    // uninitialised stack memory. There is no behaviour to copy, so what matters is
                    // whether any shipped model can reach it.
                    if (helpers < 6 &&
                        assets.Geometry(model)?.Skinned is { Models.Count: > 0 } quatBytes &&
                        StudioQuatInterp.Read(quatBytes.Models[0], index) is { } quat)
                    {
                        helpers++;

                        int control = quat.Control;

                        string named = control >= 0 && control < skinned.Bones.Count
                            ? skinned.Bones[control].Name
                            : $"#{control}";

                        int above = control >= 0 && control < skinned.Bones.Count
                            ? skinned.Bones[control].Parent
                            : -1;

                        output.WriteLine(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"  quatinterp {Path.GetFileName(model)}:{bone.Name} " +
                                $"control {named} triggers {quat.Triggers.Count} " +
                                $"{(above < 0 ? "CONTROL IS ROOT — Valve reads uninitialised memory here" : "control has a parent")}"));
                    }
                }

                for (int flag = 0; flag < flags.Length; flag++)
                {
                    if ((bone.Flags & flags[flag].Bit) == 0)
                    {
                        continue;
                    }

                    counts[flag]++;

                    if (!examples.TryGetValue(flag, out List<string>? named))
                    {
                        examples[flag] = named = [];
                    }

                    if (named.Count < 4)
                    {
                        named.Add($"{Path.GetFileName(model)}:{bone.Name}");
                    }
                }
            }
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)} on {mapName} at tick {tick}: " +
                $"{bones} bones across {models} skinned models"));

        for (int flag = 0; flag < flags.Length; flag++)
        {
            string named = examples.TryGetValue(flag, out List<string>? some)
                ? string.Join(", ", some)
                : "none";

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{flags[flag].Name,-28} {counts[flag],6} of {bones}   {named}"));
        }

        string[] rules =
            ["none", "AXISINTERP", "QUATINTERP", "AIMATBONE", "AIMATATTACH", "JIGGLE"];

        foreach ((int type, int count) in byType.OrderBy(entry => entry.Key))
        {
            string rule = type >= 0 && type < rules.Length
                ? rules[type]
                : $"unknown {type.ToString(CultureInfo.InvariantCulture)}";

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"proctype {rule,-24} {count,6} of {bones}   " +
                    $"{string.Join(", ", typeExamples[type])}"));
        }

        if (byType.Count == 0)
        {
            output.WriteLine("proctype: no bone declares a procedural rule");
        }

        // **The wiring check, and it is the only line here that runs the PRODUCTION pose path**
        // (B293). Everything above reads the model; this poses the props the way the viewer does and
        // asks how many bones the spring simulation actually touched. A model census and a
        // simulation count that disagree is the difference between "TF2 has jiggle bones" and "this
        // viewer simulates them" — and every no-op this project has shipped lived in that gap.
        EntityModelSet posed = new() { Geometry = assets.Geometry };

        posed.Add(props, assets.Geometry);

        // The step that chooses a player's sequence; see the scan below for what its absence did.
        posed.UpdateClientSideAnimations(props);

        List<ModelInstance> instances = [];
        posed.Instances(props, instances, seconds: tick * timeline.IntervalPerTick);

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SIMULATED {posed.JigglingBones} jiggle bones across {instances.Count} instances"));

        // **The number that says QUATINTERP is wired, carried from where the work happened** (B317).
        // The declaration count above says how many bones ASK for the rule; this says how many got
        // it. The two disagreeing is the whole point of printing both.
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"DRIVEN {posed.QuatInterpBones} bones posed by a quaternion-interpolation rule, " +
                $"furthest move {posed.QuatInterpFurthestMove:0.##} units"));

        // **Which IK rule TYPES the content actually carries, over every animation of every model
        // the demo draws.** The whole `CIKTarget` half of `CIKContext` — `UpdateTargets`,
        // `CalculateIKLocks`, the latching, `AutoIKRelease` — is reachable ONLY through
        // `IK_ATTACHMENT` and `IK_GROUND`: they are the two cases of `UpdateTargets`' switch that
        // establish a target, and `// case IK_SELF:` sits commented out beside them
        // (`bone_setup.cpp:3743`). `IK_RELEASE` and `IK_UNLATCH` only reduce a weight on a target
        // something else established, and the function's closing loop is gated on
        // `est.flWeight > 0`.
        //
        // So a measured zero for those two types is what says that half can be left unbuilt. That
        // is a large claim to rest on two models, which is what B296 measured it on.
        Dictionary<int, int> byRule = [];
        int animationsWalked = 0;

        foreach (string model in props
            .Select(prop => prop.ModelPath)
            .Where(model => model.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (assets.Geometry(model)?.Skinned is not { } declaring)
            {
                continue;
            }

            foreach (ReadOnlyMemory<byte> group in declaring.Models)
            {
                for (int animation = 0; animation < StudioAnimation.Count(group); animation++)
                {
                    animationsWalked++;

                    foreach (int type in StudioIkRules.Read(group, animation)
                        .Select(rule => rule.Type))
                    {
                        byRule[type] = byRule.GetValueOrDefault(type) + 1;
                    }
                }
            }
        }

        string[] ruleNames =
            ["?", "SELF", "WORLD", "GROUND", "RELEASE", "ATTACHMENT", "UNLATCH"];

        string tally = string.Join(
            ", ",
            Enumerable.Range(1, 6).Select(type =>
                $"{ruleNames[type]} {byRule.GetValueOrDefault(type).ToString(CultureInfo.InvariantCulture)}"));

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"IK RULES over {animationsWalked} animations: {tally}"));

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"IK {chained} of {models} models declare chains, {links} chains total   " +
                $"{(ikExamples.Count == 0 ? "none" : string.Join(", ", ikExamples))}; " +
                $"{posed.SolvedIkChains} chains SOLVED"));

        (int chainedOn, int ruled, int weighed) = posed.IkWork;

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"IK work: {chainedOn} chains reached the pose, {ruled} SELF rules read, " +
                $"{weighed} weighed"));

        // **Whether the lock bracket runs on a real demo** (B311). The unit tests prove a lock pins
        // an effector when `IkLocks` is called; only production says whether anything calls it.
        // Zero here with a non-zero sequence-flags census means the wiring, not the content.
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"IK locks APPLIED on the pose path: {posed.AppliedIkLocks}"));

        // **The transition counters are NOT reported here, deliberately** (B346). This probe calls
        // `PropsAt` once, and a transition compares this frame's sequence with the LAST one — so a
        // single-tick instrument reports zero however the code behaves. It did, for one run, and
        // the zero was a fact about the probe rather than about the subject. `transitions` walks a
        // tick range with one `EntityModelSet` and carries its own control; ask that instead.

        (int lockMoved, float lockFurthest) = posed.IkLockEffect;

        // **Whether they HOLD anything, which the count above cannot say.** A lock whose remembered
        // position already equals where the sequence left the foot solves to the same place — the
        // bracket runs, the pose is unchanged, and on screen that is indistinguishable from the
        // lock never running. This is the foot-sliding question in numbers.
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"IK locks that MOVED an effector: {lockMoved}, furthest {lockFurthest:0.##} units"));

        // **How far a skeleton's bones sit from its own root.** A TF2 player stands about 83 units
        // tall and a hat is a few units across, so a pose that has come apart says so in one
        // number, without a screenshot and without the desktop.
        //
        // **Through the bind position, like the census below it** — `ModelInstance.Bones` holds
        // SKINNING matrices, `Concatenate(boneToWorld, poseToBone)`, whose translation column is
        // not where the bone is. The first version of this read that column and named two
        // cosmetics as bursting by sixteen hundred units, which was a fact about the arithmetic
        // (B298).
        List<string> burst = [];
        int nonFinite = 0;

        foreach (ModelInstance instance in instances)
        {
            if (assets.Geometry(instance.ModelPath)?.Skinned is not { } worn ||
                instance.Bones is not { Count: > 0 } skeleton)
            {
                continue;
            }

            Vector3 root = Apply(skeleton[0], BindPosition(worn.Bones[0]));
            float furthest = 0f;
            int strayed = -1;

            for (int bone = 0; bone < skeleton.Count && bone < worn.Bones.Count; bone++)
            {
                // **A bone outside every mask is one the engine never builds and no vertex skins
                // to, so it cannot reach the picture.** `BuildTransformations` opens with
                // `if ( !(hdr->boneFlags( i ) & boneMask) ) continue;` — and studiomdl sets those
                // bits from USE: a bone with none is vestigial, usually an artist's leftover.
                //
                // Counting them made this census report `sum19_bottle_cap.mdl` bursting by 1703
                // units at `bonkhat.001`, a flags-0x0 root left in the model, sitting at the map
                // origin exactly as it does in TF2. A denominator that includes what is never drawn
                // finds defects that are not there.
                if (worn.Bones[bone].Flags == 0)
                {
                    continue;
                }

                Vector3 at = Apply(skeleton[bone], BindPosition(worn.Bones[bone]));

                if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z))
                {
                    nonFinite++;
                    continue;
                }

                float reach = (at - root).Length();

                if (reach > furthest)
                {
                    furthest = reach;
                    strayed = bone;
                }
            }

            if (furthest > 200f)
            {
                // **Named, because "a model came apart" and "one bone of it did" are different
                // defects.** A bone-merged cosmetic takes the wearer's bones BY NAME, and one the
                // wearer does not have is built from the cosmetic's own placement instead — so a
                // single stray bone with a name nobody matched is the signature of a merge miss
                // rather than of a bad pose.
                burst.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Path.GetFileName(instance.ModelPath)} {furthest:F0} at bone {strayed} " +
                        $"'{(strayed >= 0 ? worn.Bones[strayed].Name : "?")}' " +
                        $"of {worn.Bones.Count} " +
                        $"proctype {(strayed >= 0 ? worn.Bones[strayed].ProcedureType : -1)} " +
                        $"flags 0x{(strayed >= 0 ? worn.Bones[strayed].Flags : 0):X} " +
                        $"matrix [{string.Join(" ", skeleton[strayed])}] " +
                        $"parent {(strayed >= 0 ? worn.Bones[strayed].Parent : -1)} " +
                        $"at ({Stray(skeleton, worn, strayed).X:F0}," +
                        $"{Stray(skeleton, worn, strayed).Y:F0}," +
                        $"{Stray(skeleton, worn, strayed).Z:F0}) " +
                        $"root at ({root.X:F0},{root.Y:F0},{root.Z:F0})"));
            }
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SPREAD {burst.Count} of {instances.Count} instances reach past 200 units from " +
                $"their root, {nonFinite} bones non-finite: " +
                $"{(burst.Count == 0 ? "none" : string.Join(", ", burst.Take(6)))}"));

        // **How far a player's head sits above its foot, which is what "upside down" means as a
        // number.** Spread cannot see a flip: an inverted skeleton is the same size as an upright
        // one.
        //
        // **`ModelInstance.Bones` is the SKINNING palette, not the bone-to-world matrices** — it is
        // `Concatenate(boneToWorld, poseToBone)`, so its translation column is a mixture of
        // placement and bind offset and is not the bone's position. The first version of this
        // measured that column and reported every skeleton on the map collapsed, INCLUDING with
        // every layer disabled, which is the tell: a defect that survives removing its cause is a
        // defect in the instrument (B222 recorded the same mistake on the viewmodel size check).
        //
        // A skinning matrix maps a point in the model's BIND space to the world, so applying it to
        // the bone's own bind position gives that bone where it is now. The bind position is the
        // translation of the inverse of `poseToBone`.
        //
        // **The control is the bind pose itself**, printed beside the posed figure: every TF2 class
        // stands with its head about seventy units above its foot, so a bind rise that is not that
        // means the bones were picked wrongly and the posed number means nothing.
        List<string> inverted = [];
        int skeletons = 0;
        float bindRise = 0f;

        foreach (ModelInstance instance in instances)
        {
            if (assets.Geometry(instance.ModelPath)?.Skinned is not { } body ||
                instance.Bones is not { Count: > 0 } skeleton)
            {
                continue;
            }

            int head = -1;
            int foot = -1;

            for (int index = 0; index < body.Bones.Count && index < skeleton.Count; index++)
            {
                string name = body.Bones[index].Name;

                if (head < 0 && name.EndsWith("head", StringComparison.OrdinalIgnoreCase))
                {
                    head = index;
                }

                if (foot < 0 && name.EndsWith("foot_L", StringComparison.OrdinalIgnoreCase))
                {
                    foot = index;
                }
            }

            if (head < 0 || foot < 0)
            {
                continue;
            }

            skeletons++;

            Vector3 headBind = BindPosition(body.Bones[head]);
            Vector3 footBind = BindPosition(body.Bones[foot]);

            // **The bind pose is Y-up and the world is Z-up, and the skinning matrix is where the
            // two meet.** Measured on `engineer.mdl`: the head's bind position is (-0, 69, -1) and
            // the foot's is (6, 6, -2), so the model's own height runs along Y — this project
            // converts to a Y-up space when it loads a model and crosses back over exactly once
            // (`docs/memory/two-matrix-conventions-on-purpose.md`). The same two bones come out at
            // (-108, -1986, 38) and (-121, -2044, 16) once posed, which is Z-up.
            //
            // **So the control and the measurement read DIFFERENT axes, and that is not a bug.**
            // The first version read Z for both and reported a bind rise of 4 on a model whose
            // bind rise is 63 — which correctly refused to let the posed number be believed.
            bindRise = headBind.Y - footBind.Y;

            float rise =
                Apply(skeleton[head], headBind).Z - Apply(skeleton[foot], footBind).Z;

            if (rise < 20f)
            {
                inverted.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Path.GetFileName(instance.ModelPath)} head {rise:F0} above foot"));
            }
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"UPRIGHT {inverted.Count} of {skeletons} skeletons carry the head less than 20 " +
                $"units above the foot (bind pose control: {bindRise:F0}): " +
                $"{(inverted.Count == 0 ? "none" : string.Join(", ", inverted.Take(8)))}"));

        // **Which animations a player's sequence actually blends** (B296). TF2 has no separate aim
        // layer — `CMultiPlayerAnimState::ComputeSequences` is main sequence plus gestures, and the
        // aim matrix is the main sequence's own BLEND GRID, driven by body_pitch and body_yaw. So
        // a standing player's sequence should blend `a_PRIMARY_aimmatrix_idle_*`, which is where
        // every solving IK rule lives. Naming what it blends instead is the whole diagnosis.
        int reported = 0;

        foreach (SceneProp prop in props)
        {
            if (reported >= 3 ||
                assets.Geometry(prop.ModelPath)?.Skinned is not { } skinned ||
                skinned.IkChains.Count == 0 ||
                posed.FrameOf(prop.EntityIndex) is not { } frame)
            {
                continue;
            }

            reported++;

            (int blendGroup, IReadOnlyList<(int Animation, float Weight)> blend) =
                skinned.BlendedAnimations(frame.Sequence, posed.PoseValuesOf(prop.EntityIndex));

            string named = blendGroup < skinned.Models.Count
                ? string.Join(
                    ", ",
                    blend.Take(3).Select(entry =>
                        StudioAnimation.Name(skinned.Models[blendGroup], entry.Animation)))
                : "no group";

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  entity {prop.EntityIndex} '{Path.GetFileName(prop.ModelPath)}' " +
                    $"sequence {frame.Sequence} '{skinned.LabelOf(frame.Sequence)}' " +
                    $"group {blendGroup} blends {blend.Count}: {named}"));

            // **What a STANDING player would blend**, asked directly rather than waited for. The
            // solving rules live on the aim-matrix idles, and if the stand sequence does not blend
            // those cells then no amount of scanning will ever find a solve — which is a different
            // finding from "nobody happened to be standing".
            int standing = posed.SequenceFor(prop.ModelPath, speed: 0f);

            (int standGroup, IReadOnlyList<(int Animation, float Weight)> standBlend) =
                standing >= 0
                    ? skinned.BlendedAnimations(standing, posed.PoseValuesOf(prop.EntityIndex))
                    : (0, []);

            string standNames = standGroup < skinned.Models.Count
                ? string.Join(
                    ", ",
                    standBlend.Take(3).Select(entry =>
                        StudioAnimation.Name(skinned.Models[standGroup], entry.Animation)))
                : "no group";

            // **The GRID's own dimensions, not just how many corners were chosen.** A 3x3 aim
            // matrix that reports one blended animation is a grid we failed to read; a genuinely
            // 1x1 sequence is a different finding entirely, and only groupsize tells them apart.
            StudioBlendGrid? grid = skinned.GridOf(standing);

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"    if standing: sequence {standing} '{skinned.LabelOf(standing)}' " +
                    $"grid {(grid is null ? "none" : $"{grid.GroupX}x{grid.GroupY}")} " +
                    $"blends {standBlend.Count}: {standNames}"));

            if (reported > 1)
            {
                continue;
            }

            // **Which sequences on this model DO carry a grid, once.** If none is nine cells wide
            // then no sequence is an aim matrix and the cells are reached some other way; if one
            // is, then the question is only why nothing selects it. Two different answers needing
            // two different fixes, and the count of grids is what separates them.
            int grids = 0;
            List<string> widest = [];

            for (int candidate = 0; candidate < skinned.Sequences.Count; candidate++)
            {
                if (skinned.GridOf(candidate) is not { } found || !found.Blends)
                {
                    continue;
                }

                grids++;

                if (found.GroupX * found.GroupY >= 9 && widest.Count < 5)
                {
                    // **The ACTIVITY, because that is how the engine reaches a sequence.**
                    // `SelectWeightedSequence` matches an activity and picks among ties by
                    // actweight — so if the aim matrix and the plain stand both claim
                    // ACT_MP_STAND_PRIMARY, which one is chosen is a weighting question and not a
                    // lookup failure. If the matrix claims a DIFFERENT activity, it is selected by
                    // something else entirely.
                    widest.Add(
                        $"{skinned.LabelOf(candidate)} " +
                        $"{found.GroupX.ToString(CultureInfo.InvariantCulture)}x" +
                        $"{found.GroupY.ToString(CultureInfo.InvariantCulture)} " +
                        $"act '{skinned.ActivityOf(candidate)}' " +
                        $"w{skinned.ActivityWeightOf(candidate).ToString(CultureInfo.InvariantCulture)}");
                }
            }

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"    {grids} of {skinned.Sequences.Count} sequences carry a grid; " +
                    $"nine or wider: {(widest.Count == 0 ? "NONE" : string.Join(", ", widest))}"));

            // **The aim matrices claim no activity, so something must LAYER them.** An autolayer
            // names a sequence directly rather than through an activity, which is exactly the
            // shape needed — and B294 implemented that mechanism. If the movement sequences
            // autolayer the matrices then the aim is already reachable; if they do not, the link
            // is somewhere else again.
            foreach (int check in new[] { frame.Sequence, standing })
            {
                if (check < 0)
                {
                    continue;
                }

                IReadOnlyList<StudioAutoLayer> layers = skinned.AutoLayersOf(check);

                string targets = layers.Count == 0
                    ? string.Empty
                    : ": " + string.Join(
                        ", ",
                        layers.Select(entry =>
                            skinned.LabelOf(skinned.RelativeSequence(check, entry.Sequence))
                            + " flags 0x" + entry.Flags.ToString("X4", CultureInfo.InvariantCulture)
                            + (skinned.IsDelta(skinned.RelativeSequence(check, entry.Sequence))
                                ? " seq-DELTA"
                                : " seq-absolute")
                            + (skinned.AnimationIsDelta(
                                skinned.RelativeSequence(check, entry.Sequence))
                                ? " anim-DELTA"
                                : " anim-absolute")));

                output.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"    '{skinned.LabelOf(check)}' declares {layers.Count} autolayers{targets}"));

                // **What the layer actually CARRIES, which the flags cannot say.** A delta whose
                // root bone holds a large rotation, applied through a weight list that does not
                // zero the root, turns the whole player over — so the two numbers that decide it
                // are the weight at bone 0 and the rotation the layer samples there.
                foreach (StudioAutoLayer entry in layers)
                {
                    int target = skinned.RelativeSequence(check, entry.Sequence);

                    IReadOnlyList<float> weights = skinned.BoneWeights(target);

                    IReadOnlyList<StudioBonePose> sampled =
                        skinned.Locals(target, 0, 0f, posed.PoseValuesOf(prop.EntityIndex));

                    StudioBonePose root = sampled.FirstOrDefault(one => one.Bone == 0);

                    output.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"      layer '{skinned.LabelOf(target)}' post {skinned.IsPost(target)} " +
                            $"weights {weights.Count} " +
                            $"[0]={(weights.Count > 0 ? weights[0] : -1f):F2} " +
                            $"moves {sampled.Count} bones, " +
                            $"root q ({root.Rotation.X:F2},{root.Rotation.Y:F2}," +
                            $"{root.Rotation.Z:F2},{root.Rotation.W:F2}) " +
                            $"p ({root.Position.X:F1},{root.Position.Y:F1},{root.Position.Z:F1})"));
                }
            }
        }

        // **A tick scan, because one tick cannot answer the question that is left** (B296). The
        // solving rules live on the aim-matrix idles, and whether a player is in one at a given
        // moment is a fact about that moment. Loading the map dominates the cost, so the scan walks
        // ticks inside one load rather than being a shell loop over the whole probe.
        Scan(output, timeline, game, assets, mapName);
    }

    /// <summary>How many ticks the scan samples across the demo.</summary>
    private const int Samples = 24;

    /// <summary>Walks the demo looking for a tick where an IK rule actually solves.</summary>
    /// <remarks>
    /// **The verification a single tick cannot give.** Every piece of the IK path is tested in
    /// isolation and the wiring demonstrably reads real rules — but the rules that DO work sit on
    /// the aim-matrix idles, and whether any player is in one is a property of the moment sampled.
    /// A zero from one tick is a fact about that tick.
    ///
    /// **It reports the first tick that solves and then stops**, because the question is whether
    /// production ever reaches the solver, not how often.
    /// </remarks>
    private static void Scan(
        TextWriter output,
        DemoTimeline timeline,
        GameContent game,
        MapAssets assets,
        string mapName)
    {
        int span = timeline.LastTick - timeline.FirstTick;

        if (span <= 0)
        {
            return;
        }

        int step = Math.Max(1, span / Samples);
        int ruledAnywhere = 0;

        for (int sample = 0; sample < Samples; sample++)
        {
            int at = timeline.FirstTick + (sample * step);

            List<SceneProp> props = [];
            timeline.PropsAt(at, props);

            List<ScenePlayer> players = [];
            timeline.PlayersAt(at, players);

            PlayerProps.Add(
                players, props, new GameAppearance(game.Classes, null), (_, _, _, body) => body);

            new WeaponPropModels().Resolve(props, players, game.Weapons.For);

            EntityModelSet models = new() { Geometry = assets.Geometry };

            models.Add(props, assets.Geometry);

            // **Nothing on the wire carries a player's sequence, so this step CHOOSES it.** Without
            // it every player sits at sequence 0 — the reference pose — and the probe reads the
            // body model's own two animations rather than the animation model's thousand. That is
            // exactly the wrong answer this scan gave first time, and it looked like a production
            // defect (B296).
            models.UpdateClientSideAnimations(props);

            List<ModelInstance> instances = [];
            models.Instances(props, instances, seconds: at * timeline.IntervalPerTick);

            (int _, int ruled, int weighed) = models.IkWork;

            ruledAnywhere += ruled;

            if (models.SolvedIkChains > 0)
            {
                output.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"IK SOLVED at tick {at}: {models.SolvedIkChains} chains, " +
                        $"{ruled} SELF rules, {weighed} weighed, {players.Count} players"));

                return;
            }
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"IK scan: no chain solved across {Samples} ticks of {mapName} from " +
                $"{timeline.FirstTick} to {timeline.LastTick}; " +
                $"{ruledAnywhere} SELF rules were read in total"));
    }

    /// <summary>Where one bone of a posed instance ended up, in world space.</summary>
    private static Vector3 Stray(
        IReadOnlyList<float[]> skeleton, PropModels.SkinnedModel model, int bone) =>
        bone < 0 || bone >= skeleton.Count || bone >= model.Bones.Count
            ? default
            : Apply(skeleton[bone], BindPosition(model.Bones[bone]));

    /// <summary>Where a bone sits in the model's bind pose.</summary>
    /// <remarks>
    /// <c>poseToBone</c> takes a point from the model's bind space into the bone's own, so its
    /// inverse takes the bone's origin back out — and the translation of that inverse IS the bone's
    /// bind position. Reading <c>StudioBone.Position</c> instead would give the offset from its
    /// PARENT, which is a different quantity and one that needs the whole chain walked to use.
    /// </remarks>
    private static Vector3 BindPosition(StudioBone bone)
    {
        if (bone.PoseToBone.Length < 12)
        {
            return default;
        }

        Span<float> inverted = stackalloc float[12];

        StudioBones.Invert(bone.PoseToBone.Span, inverted);

        return new Vector3(inverted[3], inverted[7], inverted[11]);
    }

    /// <summary>Puts a point through a 3x4 matrix — <c>VectorTransform</c>.</summary>
    private static Vector3 Apply(ReadOnlySpan<float> matrix, Vector3 point) =>
        new(
            (matrix[0] * point.X) + (matrix[1] * point.Y) + (matrix[2] * point.Z) + matrix[3],
            (matrix[4] * point.X) + (matrix[5] * point.Y) + (matrix[6] * point.Z) + matrix[7],
            (matrix[8] * point.X) + (matrix[9] * point.Y) + (matrix[10] * point.Z) + matrix[11]);
}
