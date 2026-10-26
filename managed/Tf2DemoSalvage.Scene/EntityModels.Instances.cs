using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>The running simulation behind every corpse being drawn.</summary>
    /// <remarks>
    /// **Public so a probe or a test can ask what it did** — how many corpses are live, how many
    /// steps have run, and how many rebuilds a seek cost. Those are the only numbers that say the
    /// physics is running at all, because a corpse that is simulating and one that is not both draw.
    /// </remarks>
    public CorpsePhysics Corpses { get; } = new();

    /// <summary>Seconds per demo tick, which is the physics timestep.</summary>
    /// <remarks>
    /// **`PhysicsLevelInit` sets the solver's step to `gpGlobals-&gt;interval_per_tick`** under
    /// Valve's own comment — *"Always run client physics at this rate - helps keep ragdolls
    /// stable"* (`physics.cpp:177-180`). A viewer drawing at three hundred frames a second must not
    /// step physics three hundred times a second, so the corpse clock is derived from this and
    /// never from how often a frame was built.
    ///
    /// **Default 1/66 is TF2's**, and a demo carrying a different rate should set it — a wrong
    /// value does not fail, it makes corpses fall at the wrong speed.
    /// </remarks>
    public float IntervalPerTick { get; set; } = 1f / 66f;

    /// <summary>The DEMO's tick being drawn — absolute, not an offset into playback.</summary>
    /// <remarks>
    /// **The distinction is load-bearing and it cost two wrong corpses.** A demo's ticks do not
    /// start at zero (`docs/memory/demo-ticks-do-not-start-at-zero.md`), so `seconds / interval` is
    /// how far into PLAYBACK a frame is, while a corpse's `FirstTick` is an absolute tick from the
    /// file. Comparing the two froze every corpse whose death tick was numerically past the offset
    /// — it seeded at the drawn tick and stepped nothing — and made every earlier one simulate
    /// hundreds of ticks that never happened.
    ///
    /// **Set by the caller each frame** rather than derived here, because only the caller knows
    /// which tick it asked the timeline for.
    /// </remarks>
    public double CurrentTick { get; set; }

    /// <summary>The bone-clock frame the last world pass ran on (finding 102).</summary>
    private long _worldBoneFrame;

    /// <summary>Each instance list's matrices, rewritten in place when that list is rebuilt (B585, <see cref="InstanceMatrices"/>).</summary>
    private readonly Dictionary<ICollection<ModelInstance>, InstanceMatrices> _matrices =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Where each model stands at this moment.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="into">Filled with one entry per drawable entity; cleared first.</param>
    /// <param name="lightAt">The ambient cube at a world position, or null to leave models unlit.</param>
    /// <param name="sunAt">The sun at a world position, or null to apply no direct light.</param>
    /// <param name="seconds">Demo time, for advancing animation cycles.</param>
    /// <param name="frustum">
    /// The view being drawn, so a prop off screen is rejected before it is posed — the engine's
    /// order (B254). The default culls nothing.
    /// </param>
    /// <param name="views">
    /// The views' leaf lists collation walks (B262): only what a view collates is posed. The default — no
    /// view walk at all — collates every leaf, so the frustum alone decides.
    /// </param>
    /// <param name="pass">
    /// Which pass is drawing, for the tally's report — <c>world</c> or <c>viewmodel</c>. One
    /// <see cref="DrawTally"/> serves both and its line could not say which it counted, so a
    /// viewmodel pass (which is given no frustum, and therefore always reports nothing culled)
    /// read as evidence about the world cull.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// One matrix per entity, which is all that changes between frames. The geometry it points at
    /// was uploaded once and stays where it is.
    /// </remarks>
    public void Instances(
        IReadOnlyList<SceneProp> props,
        ICollection<ModelInstance> into,
        Func<float, float, float, PointLighting>? lightAt = null,
        Func<float, float, float, SunLight?>? sunAt = null,
        double seconds = 0d,
        ViewFrustum frustum = default,
        RenderableViews views = default,
        string pass = WorldPass)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        if (!_matrices.TryGetValue(into, out InstanceMatrices? matrices))
        {
            matrices = new InstanceMatrices();
            _matrices[into] = matrices;
        }

        matrices.Reset();
        _reports.BeginPass();
        Culled = 0;
        CulledByVisibility = 0;
        Unjudgeable = 0;
        Posed = 0;
        Simulated = 0;

        // **Only the world pass clears it, and that is a defect this rename exposed** (B385). The
        // viewmodel pass calls straight back into here with its own two props, so an unconditional
        // clear left the set holding the arms and the gun and NOTHING ELSE — every corpse in the match
        // reported unseen on any frame with a viewmodel drawn, expiring each on the long timer. The
        // set the interpolation list used to read had the same hole, which is most of why first person
        // and SourceTV behaved differently.
        if (string.Equals(pass, WorldPass, StringComparison.Ordinal))
        {
            _inView.Clear();
        }

        // **There is no ordering here any more, and that is the change** (D88, B181). The engine has
        // none either: a merged entity asks its parent for bones where it stands
        // (`bone_merge_cache.cpp:130`) and `SetupBones` being idempotent within a frame makes a
        // repeat an integer comparison. What this replaced was six fields and a depth sort solving a
        // problem Valve's structure never creates.
        //
        // Two phases instead, which is the engine's own split
        // (`cdll_client_int.cpp:2206-2210`): bring every entity's state up to date, then build
        // bones on demand while drawing. State first is what lets a merge reach an entity the draw
        // loop has not got to yet.
        _boneFrames.Advance();

        // The world pass's bone frame, for an instrument read after the viewmodel and undrawn passes have advanced it.
        if (string.Equals(pass, WorldPass, StringComparison.Ordinal))
        {
            _worldBoneFrame = _boneFrames.Frame;
        }

        // **Timed as a phase because it IS one** — Valve's first of two
        // (`cdll_client_int.cpp:2206-2210`) — and because everything else in the pose path has now
        // been measured flat while the total swings 3 ms to 136 ms. Splitting SetupBones (1 ms) and
        // Skinning (0.2 ms) out left 127 ms in the remainder, and this is the larger half of it: a
        // pass over EVERY prop, not just the animated ones (B189).
        long simulatedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        int buildsBefore = SkeletonPose.PoseBuilds;

        Simulate(props, seconds, fireEvents: FiresEvents(pass));

        if (string.Equals(pass, WorldPass, StringComparison.Ordinal))
        {
            BeginFaces(props, seconds);
        }

        // **After the pass, not during it** (B347). Every barrel in this frame differences against
        // the SAME previous time, which is what one `gpGlobals->frametime` means; advancing it per
        // prop would give the first minigun the whole frame and the rest nothing.
        _barrelSeconds = seconds;

        SimulateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - simulatedAt;
        BuildsSimulating += SkeletonPose.PoseBuilds - buildsBefore;

        long advancedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        int corpseBuildsBefore = SkeletonPose.PoseBuilds;

        // **The world pass only**: `physenv` is stepped once a frame (`CPhysicsSystem::PhysicsSimulate`) whatever is drawn, and
        // the undrawn and viewmodel passes carry no corpse — advancing on their empty lists retired every corpse, and the next
        // frame rebuilt the world and replayed from the death (~220 steps a frame on gullywash_f9).
        if (string.Equals(pass, WorldPass, StringComparison.Ordinal))
        {
            AdvanceCorpses(props, seconds);
        }

        AdvanceTicks += System.Diagnostics.Stopwatch.GetTimestamp() - advancedAt;
        BuildsCorpses += SkeletonPose.PoseBuilds - corpseBuildsBefore;

        // **What exists, per pass, kept past the next pass** (B415). `_propsByEntity` is the pass in flight, and the viewmodel
        // pass replaces the world's; a question asked between frames — a decal's bones, an effect's attachment — must still
        // find a world entity after the viewmodel pass, and must not find one that has left the scene.
        if (!_scenes.TryGetValue(pass, out Dictionary<int, SceneProp>? scene))
        {
            scene = [];
            _scenes[pass] = scene;
        }

        scene.Clear();

        foreach (SceneProp prop in props)
        {
            scene[prop.EntityIndex] = prop;
        }

        // **Simulated and in the scene, but not posed, when nothing will draw him** — `C_BasePlayer::ShouldDraw` is false for
        // the local player in first person, so the engine reaches his `SetupBones` only from a monitor's `ForceTempForceDraw`
        // (`viewrender.cpp:3266-3287`) or `CModelRender::AddDecal`; our decal queries call `SetupBones` themselves.
        // **Attachments resolved while the pass was simulated are resolved again for the draw** (B551): a parent's bones
        // asked for then may predate its own update. Defensive like `_simulating` — no test reaches a stale one.
        _attachments.Clear();

        if (PosesNothing(pass))
        {
            // Counted, with the reason, like every other prop that does not draw: `ShouldDraw` refused it.
            _tally.Begin(props.Count, pass);

            for (int at = 0; at < props.Count; at++)
            {
                _tally.NotDrawn();
            }

            _tally.Report();
            return;
        }

        // **Every prop that does not draw is counted with its reason.** A silent `continue` here is
        // how "all the props went away" became a guessing game: the scene said 14 models, the map
        // showed one, and nothing in between reported which test rejected the other thirteen.
        //
        // Four categories, per the project's rule: asked for, what we have, what was produced, what
        // is missing and why.
        bool world = string.Equals(pass, WorldPass, StringComparison.Ordinal);

        _tally.Begin(props.Count + (world && GathersStaticProps ? _drawableStatics : 0), pass);

        // **Collated before anything is posed, in collation order** (B254, B262): `BuildRenderablesList` over each
        // view's leaves, the frustum test inside it, and only what a view collates reaches `DrawModel` and so
        // `SetupBones`. The box needs no bones — `WorldBoxFor` is `CalcRenderableWorldSpaceAABB`.
        long collatedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        Collate(props, frustum, views, pass);

        CollateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - collatedAt;

        // The static props the gather reached, and no others (finding 83 item 1).
        if (world)
        {
            long staticsAt = System.Diagnostics.Stopwatch.GetTimestamp();

            SimulateStaticsInView(seconds, scene);

            SimulateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - staticsAt;
        }

        foreach ((SceneProp prop, int place, int? bucket, bool inSky, (float, float, float, float, float, float)? collatedBox) in _ordered)
        {
            // **The model is read by object, once** (B583), as the engine reads `GetModelPtr`: this loop probed
            // `_frames` and `_byModel` on the path six times per prop, and the resolution already holds both.
            ResolvedModel resolved = ResolvedFor(prop);
            PropModels.ModelFrames? frames = resolved.Frames;

            (int frame, int _, float blend) = SelectWith(frames, prop, seconds);

            int skin = prop.Pose.Skin;

            // **Recorded HERE, where the main view's collation answered, and not at bone setup** (B385). The
            // corpse fade asks `IsBoxInViewCluster` and then `CullBox` (`c_tf_player.cpp:1350`).
            if (!inSky)
            {
                _inView.Add(prop.EntityIndex);
            }

            // **`C_BaseEntity::ShouldDraw`'s first test** (`c_baseentity.cpp:1447`): *"Some
            // rendermodes prevent rendering"*, and `kRenderNone` is the one. Eighteen `func_door`s
            // on `cp_fulgur` declare it, their brushwork is painted `METAL/CHICKEN_WIRE001`, and
            // drawing it stood a coarse wire panel in the doorway in front of the grate props.
            //
            // **HERE and not in `EntityState.IsDrawn`, which is where it went first and broke the
            // gates completely** (B240). That property decides whether an entity is in the scene at
            // all; this decides whether it is drawn. Valve keeps them apart for a reason the gates
            // demonstrate: every grate prop is PARENTED to one of those invisible doors, and
            // `CalcAbsolutePosition` composes a child onto its parent's transform without asking
            // whether the parent renders. Remove the parent from the scene and the child has
            // nothing to hang off.
            //
            // Only this mode refuses. Every other value is a blend that still draws.
            if (prop.Pose.RenderMode == RenderModes.None)
            {
                _tally.NotDrawn();
                continue;
            }

            // `Batches(path, frame)`'s clamp, on the resolved frames.
            if (resolved.Batches is not { Count: > 0 } packed || packed[Math.Clamp(frame, 0, packed.Count - 1)].Count == 0)
            {
                _tally.NoGeometry(prop.ModelPath);
                continue;
            }

            _tally.Drawn();

            // **A parented prop is placed by its PARENT's transform, and this loop never asked**
            // (B241). `Simulate` above composes the chain for a SKINNED model — `posed.
            // EntityTransform = PlacementOf(prop)` — and every BAKED prop came through here and was
            // placed at its own pose, which for a parented entity is its LOCAL offset. Every setup
            // gate's grate is a `CDynamicProp` on a `func_door` with a local origin of (0,0,0), so
            // all six drew at the map origin. The door's brushwork stood where they should have
            // been, which is why nobody could see it until `kRenderNone` stopped drawing that.
            ScenePose pose = prop.Pose;

            // **The pose here is the wire's, and for a move child that is LOCAL** — `SendProxy_Origin`
            // sends `GetLocalOrigin()` (`server/baseentity.cpp:199`). B241 once read
            // `door_grate003_top draws at (5416, -2168, 552)` as proof the pose was already world; it was
            // a child of a `func_door`, and a brush door with no origin brush sits at (0, 0, 0), where local and
            // world coincide (interpolated: that door's origin was not re-measured). A
            // parented, non-bone-merged prop is composed below through `Absolute` (B551), not here.
            //
            // **A gib is drawn where its physics object is** (B409): `C_PhysPropClientside`'s origin and angles follow the object,
            // and a gib model is baked, so its entity transform is the whole of its pose.
            if (prop.ClassName == RagdollProps.GibClassName &&
                Corpses.Placements.TryGetValue(prop.EntityIndex, out (Vector3 Origin, (float Pitch, float Yaw, float Roll) Angles) placed))
            {
                pose = pose with
                {
                    X = placed.Origin.X,
                    Y = placed.Origin.Y,
                    Z = placed.Origin.Z,
                    Pitch = placed.Angles.Pitch,
                    Yaw = placed.Angles.Yaw,
                    Roll = placed.Angles.Roll,
                };
            }

            PropTransform transform = new(
                pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale);

            // **Lit, logged and counted by collaborators rather than here** (B181). Each of these
            // was sixty to eighty lines inside this loop, and none of them is about posing a model —
            // which is how the body reached two hundred lines of code with five jobs in it and the
            // engine's stage boundaries invisible.
            ModelLight lit = _lighting.For(prop, lightAt, sunAt);

            AmbientCube? light = lit.Light;
            SunLight? sun = lit.Sun;
            IReadOnlyList<LocalLight> locals = lit.Locals;

            // **A static prop with a colour mesh draws its colours PLUS its styled and dynamic lights** (B426, B424,
            // B425): `engine.dll` `0x1800f1bd0` on DX9 keeps the mesh and lights it with `FUN_1801ba590` flags 6 or 7 —
            // no cube, no sun, no style-0 light, since the colours hold those. With none reaching, the colours alone.
            //
            // **Unless its model is flagged bump mapped — any material with `$phong` on or a `$bumpmap`** (B514):
            // `engine.dll FUN_1800eb1a0` then refuses static lighting, no colour mesh is drawn (the phong shader could
            // not read one: `skin_ps20b.fxc:220-222` passes a zero static colour), and the state is the handle's full
            // one, flags 7.
            long bakedAt = System.Diagnostics.Stopwatch.GetTimestamp();

            bool fullyLit =
                prop.EntityIndex >= PropModels.FirstStaticPropEntityIndex &&
                frames is not null &&
                StaticPropVertexLighting.RefusesColourMesh(frames.StudioFlags);

            float[]? baked = fullyLit ? null : BakedColoursOf(prop, pose, lit.X, lit.Y, lit.Z);

            if (fullyLit && StaticPropFullLighting is { } fullLighting)
            {
                (PointLighting state, sun) = fullLighting(prop.EntityIndex, lit.X, lit.Y, lit.Z);
                light = state.Cube;
                locals = state.Locals;
            }

            if (baked is not null)
            {
                locals = StaticPlusDynamicLights?.Invoke(prop.EntityIndex, lit.X, lit.Y, lit.Z) ?? [];
                light = locals.Count > 0 ? LevelLighting.NoStaticState : null;
                sun = null;
            }

            BakedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - bakedAt;

            // **The point the cube was sampled at, carried so the RENDERER can choose a cubemap
            // from it** (B170). `ModelLighting.For` already resolved where this model is — via the
            // model's own `illumposition` — and the reflection needs the same answer. Taking it
            // from here rather than recomputing is what keeps lighting and reflection agreeing
            // about a model's position instead of drifting apart.
            (float X, float Y, float Z) origin = (lit.X, lit.Y, lit.Z);

            // **The last unmeasured thing in this loop, and it runs three times per prop per
            // frame.** Everything else has been split and come back at a millisecond or less while
            // the total holds a deterministic ~130 ms (B189). Reporting is the remaining candidate
            // and it is the one that builds strings.
            long reportedAt = System.Diagnostics.Stopwatch.GetTimestamp();

            _reports.BrushMoved(prop, seconds);

            if (lightAt is not null)
            {
                _reports.Lit(prop, lit, skin);
            }

            _reports.Animating(prop, frame, packed.Count, blend);

            ReportTicks += System.Diagnostics.Stopwatch.GetTimestamp() - reportedAt;

            // **A skinned model is posed here, per instance.** Its geometry was uploaded once and
            // unposed, so the matrices are what puts it in a pose at all - without them it draws
            // in whatever position the artist modelled it, which for a player is lying on its
            // side.
            IReadOnlyList<float[]>? bones = null;

            if (frames?.Skinned is { } skinned &&
                _entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating))
            {
                // **One call, and it builds this entity's parents too if it merges onto any.**
                // Everything the old code did with a sort happens inside here now.
                //
                // BONE_USED_BY_ANYTHING because this project draws one level of detail and asks for
                // everything; a narrower mask is the optimisation the accessor exists to allow and
                // is not worth guessing at before something measures it.
                // A corpse's simulation was brought up to this tick by `AdvanceCorpses`, before the
                // cull, which is what attached the ragdoll hook this `SetupBones` runs.
                long setupAt = System.Diagnostics.Stopwatch.GetTimestamp();
                int drawBuildsBefore = SkeletonPose.PoseBuilds;

                bool setUp = animating.SetupBones(StudioBoneFlags.UsedByAnything, seconds);

                SetupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - setupAt;
                BuildsDrawing += SkeletonPose.PoseBuilds - drawBuildsBefore;
                Posed++;

                if (!setUp)
                {
                    // The wearer is not being drawn — dead, out of the visible set, or a model that
                    // failed to load. Valve's `if ( baseDrawn )`: drawing the item anyway leaves it
                    // hanging at the map origin, which is worse than not drawing it.
                    continue;
                }

                long skinAt = System.Diagnostics.Stopwatch.GetTimestamp();

                bones = Skinning(prop.EntityIndex, skinned.Bones, animating.Bones);

                SkinTicks += System.Diagnostics.Stopwatch.GetTimestamp() - skinAt;

                // **Only the viewmodel, because only it is measured every frame** (B222). The
                // viewmodel entities carry their own indices, so this cannot pick up a world prop
                // and pay for two hundred of them.
                // **Guarded on the WORK, not just the write.** This walks a sixteenth of the model's
                // corners every frame and applies three matrices to each; a production run must not
                // pay for a diagnostic. Same rule as B191/CA1873 elsewhere in this file — and the
                // owner's, stated plainly: *"logs cannot live in the production app or it will slow
                // it down too much"*.
                if (prop.EntityIndex >= ViewmodelScene.ArmsEntityIndex &&
                    _props.IsEnabled(LogLevel.Debug))
                {
                    ReportPosedSize(prop.ModelPath, bones, prop.Pose.Body);
                }

                // **The bones are already in world space**, so the model matrix must not place the
                // model a second time (finding 35 section 7a). This is where the merged item's
                // `transform = worn.Where` used to be, and it is gone rather than moved: an item
                // takes its wearer's bones and those already carry the wearer's placement.
                transform = PropTransform.Identity;
            }

            long placingAt = System.Diagnostics.Stopwatch.GetTimestamp();
            int placeBuildsBefore = SkeletonPose.PoseBuilds;

            // **Applies the matrices the GPU is about to use, on the processor, and reports the
            // result.** A skinned model that draws wrong could be a bad pose or a bad shader, and
            // an overhead camera cannot tell them apart. If these extents stand the model up, the
            // pose is right and the fault is in the drawing; if they do not, the pose is the
            // fault and the shader is innocent.
            // **The IsEnabled guard covers the WORK, not just the write** (B191, CA1873). Below
            // this, extents are walked over every corner and a SECOND full skeleton is built for
            // the corner comparison — both purely to produce a diagnostic line. A production run
            // was paying for both and then discarding the result.
            if (bones is { Count: > 0 } &&
                _props.IsEnabled(LogLevel.Debug) &&
                !_reportedPoses.Contains(prop.ModelPath))
            {
                _reportedPoses.Add(prop.ModelPath);
                ReportPosedExtents(prop.ModelPath, bones);

                // **The same frame posed WITHOUT the blend, side by side.** Resolving the blend
                // grid was this project's change and taking the grid's corner is what came before
                // it. Three animations mixed at wrong weights crumple a skeleton - a run forward
                // blended halfway against a run backward is not a stand, it is a heap - and that
                // is indistinguishable from a broken decode unless both are measured together.
                if (_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? both) &&
                    both.Skinned is { } plain)
                {
                    ReportPosedExtents(
                        prop.ModelPath,
                        plain.Skeleton(Math.Max(0, prop.Pose.Sequence), 0).Matrices,
                        prop.ModelPath + " CORNER, no pose parameters");
                }
            }

            // **A merged entity takes its wearer's matrices, not its own pose.** This is what
            // EF_BONEMERGE means: the client walks the child's bones, finds the parent's bone of
            // the same name, and uses that matrix outright. A hat has a `bip_head` bone and no
            // animation of its own — posing it from its own rest skeleton puts it at the player's
            // feet facing north, which is what "cosmetics do not work" looked like.
            //
            // Bones the parent does not have keep the child's own, which is the same fallback
            // Remap's −1 already means: an item with a part the player has no bone for keeps the
            // shape the artist gave it rather than collapsing to the origin.
            // **Valve's third branch, and it had no home here until B231**
            // (`c_baseentity.cpp:4393`). An entity with a parent that is NOT bone-merged builds an
            // entity-to-parent matrix from its own local angles and origin and concatenates its
            // parent's transform onto it. Everything with a parent used to take the bone path
            // below, so a `prop_dynamic` hung on a `func_door` searched for a skeleton brushwork
            // does not have and was dropped — every gate on `cp_fulgur` is exactly that pairing.
            //
            // Placed BEFORE the bone-merge branch reads `_drawnPlacements`, because the two are
            // alternatives rather than stages: the engine returns from whichever one applies.
            if (prop is { AttachedTo: { } parent, BoneMerged: false } && ParentOf(parent) is not null)
            {
                // `GetParentToWorldTransform` prefers the parent's ATTACHMENT when one is named and
                // resolvable, and falls back to the parent's own transform otherwise
                // (`c_baseentity.cpp:4330`) — both inside `Absolute` (B551). Brushwork has no
                // attachments, so the fallback is the path every gate takes.
                //
                // **The whole chain, not one level** (B551): the parent's transform is its own
                // `EntityToWorldTransform`, composed by the same function (`:4342`). Concatenating onto
                // the parent's raw pose put a grandchild at its parent's LOCAL offset. And the parent is
                // looked up in the FRAME, not this pass, so an undrawn parent still carries its child.
                ScenePose placedAt = Absolute(prop, AnimatingEntity.MaximumFollowDepth);
                transform = new PropTransform(
                    placedAt.X, placedAt.Y, placedAt.Z,
                    placedAt.Pitch, placedAt.Yaw, placedAt.Roll,
                    prop.Pose.Scale);

                // **Says where the composition actually PUT it, once per model.** Every input to
                // this was measured and correct — the parent is the right `func_door`, at the right
                // world position, and it moves — while the prop stayed invisible. The one thing
                // never measured was the output, which is the shape this project keeps meeting:
                // three correct measurements locating the fourth
                // (`docs/memory/nothing-is-closed.md#measure-every-hop-before-blaming-one`).
                // **Keyed on the composed POSITION, not on the model, so it says whether the child
                // FOLLOWS.** Deduped per model it fired once and proved only that the composition ran —
                // which is what it was written for (B231, a prop dropped for want of a parent placement).
                // The owner's report is a door that never opens while its parent demonstrably moves, and
                // that is a question about the second, third and hundredth frame. One line per distinct
                // height answers it and still cannot flood: a door has about thirty of them.
                // Stryker disable all : the String mutator wraps the interpolated literal in a
                // ternary that cannot bind to string.Create's interpolated-string handler
                // (CS1620), and Safe Mode then drops every mutation in this method — B410.
                if (_render.IsEnabled(LogLevel.Debug) &&
                    _reportedFrames.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{prop.ModelPath}#{prop.EntityIndex}#placed#{transform.OriginZ:0}")))
                {
                    _render.LogDebug(
                        "{Message}",
                        $"{prop.ModelPath} composed onto {parent}"
                        + $"{(prop.AttachmentPoint is { } point ? $" attachment {point}" : string.Empty)}: local "
                        + $"({prop.Pose.X:0} {prop.Pose.Y:0} {prop.Pose.Z:0}) = "
                        + $"({transform.OriginX:0} {transform.OriginY:0} {transform.OriginZ:0})");
                }

                // Stryker restore all
            }
            else if (prop.AttachedTo is { } wearer)
            {
                // **Says WHY a parented prop was dropped, which nothing did** (B231). A prop that
                // is not bone-merged and whose parent is not in `_parentPlacements` falls to this
                // branch, fails the same lookup against the drawn set, and `continue`s — leaving no
                // census line, no warning, and a model missing from the map with nothing to read.
                // The gates and the spawn locker are exactly that, and three rebuilds were spent
                // guessing at it.
                if (!prop.BoneMerged && _render.IsEnabled(LogLevel.Debug) &&
                    _reportedFrames.Add(prop.ModelPath + "#parent"))
                {
                    _render.LogDebug(
                        "{Message}",
                        $"{prop.ModelPath} is parented to {wearer} and not bone-merged, and that "
                        + $"entity has no placement this frame "
                        + $"(parents known: {_parentPlacements.Count}, drawn: {_drawnPlacements.Count})");
                }

                if (!_drawnPlacements.TryGetValue(wearer, out PropTransform stands))
                {
                    // The wearer is not being drawn — dead, out of the visible set, or a model that
                    // failed to load. Valve's `if ( baseDrawn )`: drawing the item anyway leaves it
                    // at the map origin, which is worse than not drawing it. A SKINNED item is
                    // already refused by SetupBones returning false; this is the same rule for one
                    // with no skeleton to refuse with.
                    PlaceTicks += System.Diagnostics.Stopwatch.GetTimestamp() - placingAt;
                    BuildsPlacing += SkeletonPose.PoseBuilds - placeBuildsBefore;
                    continue;
                }

                if (bones is null)
                {
                    // A model this project baked rather than skinned has no bones to carry a
                    // placement, so it takes its wearer's outright — which is what every worn item
                    // did before D88, and is still correct for one that cannot be merged onto
                    // anything.
                    transform = stands;
                }

                // **Lit where its wearer stands — and no longer patched HERE** (B189; the outside
                // audit's finding 6). A merged item's own pose is (0,0,0) by construction, so an
                // override in this loop used to replace the cube, the lamps and the reflection
                // origin with wearer-point samples. It went past `ModelLighting.For`'s cache — so
                // every worn item re-traced its lighting every frame — and it missed the SUN,
                // which stayed sampled at the map origin, leaving every cosmetic without direct
                // light while its wearer stood in it. `IlluminationPoint` now answers the wearer's
                // point for a bone-merged prop, so the `For` call above samples cube, lamps, sun
                // and reflection origin at the right place through the one exact-point cache.
                // `WornLightTicks` still exists and now stays zero, which is the truth: the work
                // moved into the `lighting` column with every other sample.

                // Guarded before `FirstTime`, so a production run does not even build the
                // `path + "#worn"` key — a string allocated per worn prop per frame (B191).
                if (bones is { Count: > 0 } &&
                    _props.IsEnabled(LogLevel.Debug) &&
                    _reports.FirstTime(prop.ModelPath + "#worn"))
                {
                    ReportPosedExtents(prop.ModelPath, bones, prop.ModelPath + " WORN");
                }
            }

            // **A model posed by BONES is placed by those bones, and its matrix is identity**
            // (B241). `IStudioRender::DrawModel` takes bone-to-world matrices and nothing else
            // (`istudiorender.h:329`); Valve has no separate entity transform for a studio model,
            // because `SetupBones` folds the placement into every bone before the draw. So a
            // non-identity matrix beside real bones applies the placement TWICE.
            //
            // **This project already wrote the rule down and did not enforce it.**
            // `WorldRenderer.DrawModel`: *"A baked model is put in the world by its matrix … a
            // SKINNED model is put there by its bones and its matrix stays at identity."* It held
            // only by accident — a player's pose is (0,0,0) and a merged item's is (0,0,0) by
            // construction — so nothing broke until a skinned prop arrived carrying a real origin.
            //
            // A setup gate's grate is exactly that: `CDynamicProp`, one bone, and a networked origin
            // of (5416 −2168 552). Bone and matrix each placed it there, so it drew about ten
            // thousand units off the map, and the doorway you could see straight through was the
            // only symptom. Measured before this line existed:
            //
            //   demo               draws at (0, 0, 0)          bones 84
            //   windowed_door      draws at (0, 0, 0)          bones 1
            //   door_grate003_top  draws at (5416, -2168, 552) bones 1
            //
            // Two props of the same kind parented the same way, placed by two mechanisms.
            if (bones is { Count: > 0 })
            {
                transform = PropTransform.Identity;
            }

            // **Hoisted out of the argument list**, because two arguments now want it: the body
            // parts and the two-pass flag are both facts about the MODEL rather than the entity.
            // Looking it up twice would be two dictionary probes per model per frame for one answer.
            PropModels.ModelFrames? parts = frames;

            // **`C_BaseEntity::ComputeFxBlend`, once per entity per frame** (B221). The engine
            // computes this on the entity and caches it by frame count; here the frame IS this
            // call, so computing it at the one place a `ModelInstance` is built gives the same
            // once-per-frame guarantee without a cache to go stale.
            //
            // `seconds` is demo time, which is `gpGlobals->curtime` for a viewer — the pulses,
            // strobes and flickers are functions of it, and the entity index de-syncs them.
            // **The distance fade, which `Compute` has always taken and nobody ever supplied**
            // (B268). `GetClientSideFade` on `C_BaseAnimating` is `UTIL_ComputeEntityFade`, and
            // `ComputeFxBlend` multiplies its answer into the blend — so leaving it at the default
            // 255 drew every entity at full alpha however far away it was, with the multiply
            // sitting there correct and unreached.
            //
            // Measured at the prop's illumination point, which is where this loop already decided
            // the model IS — the same point the cubemap and the lighting use, so all three agree
            // about its position rather than each deriving one.
            //
            // **A static prop takes its own entry instead** (B430): `engine.dll` `FUN_180202c60` writes
            // the prop's alpha from the entry `UnserializeModels` built, measured from its render
            // origin — the placement, not the illumination point. The factor is 1 for the reason
            // `EntityFade.DistanceAlpha` gives: no local player's zoom to look through. Its screen fades —
            // the 0x20 branch and the level fade — come with it through `StaticPropFade.Opacity` (B432).
            long fadingAt = System.Diagnostics.Stopwatch.GetTimestamp();
            PlaceComposeTicks += fadingAt - placingAt;

            byte fade = 255;

            if (ViewOrigin is { } eye)
            {
                fade = prop.StaticFade is not null || prop.StaticScreen is not null
                    ? StaticPropFade.Opacity(
                        prop.StaticFade, prop.StaticScreen ?? default, (prop.Pose.X, prop.Pose.Y, prop.Pose.Z), eye, 1f,
                        ScreenView, LevelScreenFade)
                    : EntityFade.DistanceAlpha(
                        prop.Pose.FadeMinimumDistance,
                        prop.Pose.FadeMaximumDistance,
                        EntityFade.Distance(eye, origin));
            }

            FxBlendResult fx = FxBlend.Compute(
                prop.Pose.RenderFx,
                prop.Pose.RenderMode,
                prop.Pose.RenderAlpha,
                prop.EntityIndex,
                (float)seconds,
                clientSideFade: fade);

            // **An areaportal window ignores all of that and uses its distance blend** (B358).
            // `C_FuncAreaPortalWindow::ComputeFxBlend` sets `m_nRenderFXBlend = 255` with the
            // comment *"We reset our blend down below"*, and `DrawModel` then calls
            // `render->SetBlend( GetDistanceBlend() )` — so the entity's renderamt, rendermode and
            // renderfx decide nothing. Harvest's windows say `rendermode 0` and `renderamt 255`,
            // which is exactly why they drew as solid black panels.
            //
            // **Overwritten rather than multiplied in**, because the engine replaces the blend
            // rather than modulating it; a multiply would keep whatever `ComputeFxBlend` returned
            // and reintroduce the renderamt this class exists to ignore.
            if (prop.Pose.PortalWindow is { } window && ViewOrigin is { } watching)
            {
                (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) =
                    collatedBox ?? WorldBoxFor(prop);

                float portalBlend = AreaPortalWindow.Blend(
                    AreaPortalWindow.Distance(
                        watching, (minX, minY, minZ), (maxX, maxY, maxZ)),
                    window.FadeStart,
                    window.FadeEnd,
                    window.TranslucencyLimit);

                // 255 rather than `RenderGroups.FullyOpaque`, which lives in the render layer this
                // one is beneath. `render->SetBlend` takes 0..1 and the byte is ours.
                fx = fx with { Blend = (int)MathF.Round(portalBlend * FullyOpaqueAlpha) };

                PortalWindowsFaded++;
            }

            long buildingAt = System.Diagnostics.Stopwatch.GetTimestamp();
            PlaceFadeTicks += buildingAt - fadingAt;

            CloakBind cloak = Cloak?.Invoke(prop) ?? default;

            // `C_TFPlayer::DrawModel` returns before drawing anything once `GetEffectiveInvisibilityLevel() >= 1`
            // (c_tf_player.cpp:6938) — eyes and all, which carry no cloak pass of their own.
            if (cloak.Undrawn)
            {
                PlaceTicks += System.Diagnostics.Stopwatch.GetTimestamp() - placingAt;
                BuildsPlacing += SkeletonPose.PoseBuilds - placeBuildsBefore;
                continue;
            }

            (float Red, float Green, float Blue)? paint = Paint?.Invoke(prop);
            float burn = BurnLevel?.Invoke(prop) ?? 0f;
            (float Red, float Green, float Blue) urine = UrineTint?.Invoke(prop) ?? (1f, 1f, 1f);
            long boundAt = System.Diagnostics.Stopwatch.GetTimestamp();
            PlaceBindsTicks += boundAt - buildingAt;

            float[] matrix = matrices.Next(transform);
            long matrixAt = System.Diagnostics.Stopwatch.GetTimestamp();
            PlaceMatrixTicks += matrixAt - boundAt;
            IReadOnlyDictionary<int, int>? swap = SkinSwap(prop.ModelPath, skin);
            (float Red, float Green, float Blue)? tint = EntityTint(prop.ModelPath);
            (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) bounds = collatedBox ?? WorldBoxFor(prop);
            long swappedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            PlaceSwapTicks += swappedAt - matrixAt;
            float[]? flex = FlexFor(prop, parts, seconds);
            PlaceLookupsTicks += System.Diagnostics.Stopwatch.GetTimestamp() - boundAt;
            PlaceFlexTicks += System.Diagnostics.Stopwatch.GetTimestamp() - swappedAt;

            into.Add(new ModelInstance(
                prop.ModelPath,
                matrix,
                light,

                // Cached alongside the cube, because the sun costs more than it looks: it traces a
                // ray through the BSP to ask whether the sky is visible from here. It was also
                // being asked TWICE per model — once here and once for the wearer record — and
                // neither answer could differ.
                sun,
                frame,
                blend,
                bones,
                swap,
                parts?.BodyParts,
                prop.Pose.Body,

                // **`ForcedMaterialOverride` — gold or ice, and nothing else sets it** (B325).
                // `C_TFRagdoll::InternalDrawModel` forces it around the base call
                // (`c_tf_player.cpp:1281-1290`), and each wearable applies its own copy through
                // `GetEconWeaponMaterialOverride` (`econ_entity.cpp:1793`).
                MaterialOverride: prop.MaterialOverride,

                // **TF2's paint, per entity** (B330). Asked here rather than carried on the prop
                // because it needs the econ resolution and the item schema, which this layer
                // reaches through a delegate exactly as it does for attached models.
                Paint: paint,

                // **How alight the wearer is** (B336). Zero rather than null, because
                // `CProxyBurnLevel` has no absent state — it answers 0 for a player who is not
                // burning, and a nullable here would invite a caller to treat "no scene" as
                // different from "not on fire" when the engine does not.
                Burn: burn,
                Urine: urine,

                // `CullMode( MATERIAL_CULLMODE_CW )` around a flipped viewmodel's draw (`c_baseviewmodel.cpp:374`), B515.
                Mirrored: prop.FlipViewModel,
                Origin: origin,

                // Only a brush entity has one; everything else answers null (B219).
                Tint: tint,

                // The lamps near this model, which its cube no longer carries (B170).
                Locals: locals,

                // The placed box the engine culls and buckets by — CalcRenderableWorldSpaceAABB.
                WorldBounds: bounds,

                // **$mostlyopaque, off the model's own header.** A model this side never loaded
                // answers false, which is the engine's answer for a model with no flag — and the
                // conservative one, since it draws the model in one pass rather than two.
                TwoPass: parts?.TwoPass ?? false,

                // **`GetFxBlend()` and `m_nRenderMode`, the two inputs `RenderGroups.For` has taken
                // since D114 and never been given** (B221). Until now every caller passed
                // `FullyOpaque` and `Normal`, so a cloaked spy drew solid and nothing could fade.
                Alpha: fx.Blend,
                RenderMode: prop.Pose.RenderMode,
                EntityIndex: prop.EntityIndex,
                BakedColours: baked,
                LeafPlace: place,
                SizeBucket: bucket,
                InSky: inSky,
                Cloak: cloak,
                Flex: flex));

            long instancedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            PlaceBuildTicks += instancedAt - buildingAt;
            PlaceTicks += instancedAt - placingAt;
            BuildsPlacing += SkeletonPose.PoseBuilds - placeBuildsBefore;

            // **The item's `attached_models`, drawn on the item's own transform and bones.**
            // `DrawEconEntityAttachedModels` (`econ_entity.cpp:103`) copies the parent's
            // `ClientModelRenderInfo_t` whole and swaps only `pModel`:
            //
            //     infoAttached = *pInfo;
            //     infoAttached.pRenderable = pEnt;
            //     infoAttached.pModel      = attachedModel.m_pModel;
            //     modelrender->DrawModelSetup( infoAttached, &state, NULL, &pBoneToWorld );
            //
            // So an attachment is NOT a separate entity and names no attachment point — it is
            // another mesh posed by the item's skeleton, which is why the Degreaser's pilot light
            // sits where the Degreaser does without the schema saying where.
            //
            // Everything else is copied for the same reason the engine copies it: same light, same
            // blend, same render mode, same skin. A pilot light on a cloaked spy's flamethrower
            // fades with the flamethrower.
            //
            // **The one thing NOT copied is the material override, and that is the engine's rule
            // rather than an omission** (B325). An econ entity that applies its own override sets a
            // flag with it —
            //
            //     modelrender->ForcedMaterialOverride( pOverrideMaterial );
            //     flags |= STUDIO_NO_OVERRIDE_FOR_ATTACH; // Don't apply override materials to attachments.
            //
            // `c_baseanimating.cpp:3438-3439` — and `DrawEconEntityAttachedModels` reads it back,
            // clearing the override for the duration of the loop and restoring it afterwards
            // (`econ_entity.cpp:110-117`, `146-147`). So a hat on a golden corpse turns gold and the
            // extra mesh bolted to that hat does not. Read-from-source. Both sites that raise the
            // flag are econ overrides, which is exactly what a corpse's wearables carry.
            // Stryker disable once : a mutant that empties the guard body leaves 'attachmentsFor'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (Attachments is not { } attachmentsFor)
            {
                continue;
            }

            foreach (string attachment in attachmentsFor(prop))
            {
                _ = _frames.TryGetValue(attachment, out PropModels.ModelFrames? attachedParts);

                // **Says that an attachment was EMITTED, and whether it had geometry to emit** —
                // the two failures look identical on screen and neither is an error. An attachment
                // the schema does not declare draws nothing; one declared but never packed also
                // draws nothing, and that second case shipped for an hour because the packing set
                // and the asset loader are different lists (B195).
                //
                // Reports what this draw USED rather than re-deriving it (B243): the parent it
                // rides, and whether frames were found for the attachment itself.
                if (_props.IsEnabled(LogLevel.Debug) &&
                    _reports.FirstTime(attachment + "#attached"))
                {
                    _props.LogDebug(
                        "{Message}",
                        $"{attachment} attached to {prop.ModelPath} (entity {prop.EntityIndex}, "
                        + $"item {prop.ItemDefinitionIndex}), "
                        + $"{(attachedParts is null ? "NO FRAMES — it was never packed" : "posed on its bones")}");
                }

                into.Add(new ModelInstance(
                    attachment,
                    matrices.Next(transform),
                    light,
                    sun,
                    frame,
                    blend,
                    bones,
                    SkinSwap(attachment, skin),
                    attachedParts?.BodyParts,
                    prop.Pose.Body,

                    // Drawn from the item's `OnPostInternalDrawModel`, inside the cull its item set (B515).
                    Mirrored: prop.FlipViewModel,
                    Origin: origin,
                    Tint: EntityTint(attachment),
                    Locals: locals,
                    WorldBounds: bounds,
                    TwoPass: attachedParts?.TwoPass ?? false,
                    Alpha: fx.Blend,
                    RenderMode: prop.Pose.RenderMode,

                    // **The WEARER's index, because an attached model is not an entity** — it is an
                    // extra model drawn on the item's own transform and bones
                    // (`econ_entity.cpp:103`), so it has no index of its own. That still separates
                    // two players in the same hat, which is what B356 needs: they are two props.
                    EntityIndex: prop.EntityIndex,

                    // Drawn with its item, so collated where the item was (`DrawEconEntityAttachedModels`).
                    LeafPlace: place,
                    SizeBucket: bucket,
                    InSky: inSky,

                    // The item's own bind, as the engine binds the attached mesh with the item's renderable.
                    Cloak: cloak));
            }
        }

        _tally.Report();
    }
}
