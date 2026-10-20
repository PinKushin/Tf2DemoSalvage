using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>Brings every corpse's simulation up to the tick, seen or not (B58).</summary>
    /// <param name="props">Every prop this moment carries, before any cull.</param>
    /// <param name="seconds">The demo clock, for a seed pose.</param>
    /// <remarks>
    /// **Before the cull, because the engine's physics does not look at the view.** A client ragdoll
    /// lives in `physenv`, which `PhysicsSimulate` steps every frame for every object in it
    /// (`physics.cpp:447`); visibility decides only what is drawn. This advance sat inside the draw
    /// loop, so a corpse behind the camera stopped dead and, on coming back into view, replayed every
    /// tick it had missed in one frame — 135, 151 and 551 ms frames on f12.
    ///
    /// **Before `SetupBones` as well**, because `Advance` attaches the hook that call runs — the
    /// engine's order, where `m_pRagdoll` is set by the time `BuildTransformations` tests it.
    ///
    /// **The tick is the demo's, not a count of calls**, so drawing one moment twice steps nothing.
    /// Only a corpse whose model declares joints; `entry.Ragdoll` is null for all but 37 models.
    /// </remarks>
    private void AdvanceCorpses(IReadOnlyList<SceneProp> props, double seconds)
    {
        if (HoldsCorpses)
        {
            return;
        }
        _corpseRequests.Clear();

        for (int index = 0; index < props.Count; index++)
        {
            SceneProp prop = props[index];

            bool gib = prop.ClassName == RagdollProps.GibClassName;

            // Stryker disable all : a mutant that empties the guard body leaves 'entry' and
            // 'corpse' unassigned below (CS0165), and Safe Mode then drops every mutation in
            // this method — B410.
            if ((prop.ClassName != RagdollProps.RagdollClassName && !gib) ||
                !_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry) ||
                entry.Ragdoll is not { } corpse)
            {
                continue;
            }

            // Stryker restore all

            _entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating);

            if (gib)
            {
                // **A gib carries its throw in `Force` and its spin in `RagdollVelocity`** (`RagdollProps.Gibs`), the two arguments of the
                // `AddVelocity` that makes it fly, and it is created at its own pose. **It needs no animating entity**: every TF2 gib
                // model is BAKED, drawn by its entity transform, which the simulation places (B409).
                _corpseRequests.Add(new CorpseRequest(
                    prop.EntityIndex, corpse, animating, prop.FirstTick, null, null, prop.Force,
                    Gib: true, Spin: prop.RagdollVelocity, Spawn: (new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z), prop.Pose.Yaw)));

                continue;
            }

            if (entry.Skinned is null || animating is null)
            {
                continue;
            }

            _corpseRequests.Add(new CorpseRequest(
                prop.EntityIndex, corpse, animating, prop.FirstTick, prop.Force, prop.ForceBone, prop.RagdollVelocity));
        }

        // **One environment for every corpse, stepped once** (D179): each corpse joins it at its death tick as it steps forward.
        Corpses.Advance(_corpseRequests, (int)CurrentTick, IntervalPerTick, seconds);
    }

    /// <summary>Runs the corpse path alone over these corpses at a moment — what <see cref="CorpseRecord"/> drives in the background (D181).</summary>
    /// <param name="corpses">The corpses alive at <see cref="CurrentTick"/>; their sequences are resolved in place.</param>
    /// <param name="seconds">Demo time.</param>
    /// <exception cref="ArgumentNullException"><paramref name="corpses"/> is null.</exception>
    /// <remarks>
    /// **The same three steps a live frame runs for a corpse, in the same order** — <see cref="UpdateClientSideAnimations"/>, then
    /// the bone clock and <c>Simulate</c>, then <c>AdvanceCorpses</c> — and nothing else, so the record's corpses are seeded and
    /// stepped by the production path rather than by a copy of it.
    /// </remarks>
    internal void AdvanceCorpsesAlone(List<SceneProp> corpses, double seconds)
    {
        ArgumentNullException.ThrowIfNull(corpses);

        UpdateClientSideAnimations(corpses);
        _boneFrames.Advance();

        // This set's frame has no world collation to sweep its resolutions, and every tick's corpse is a new object (finding
        // 82): without this the record's set held every object of the demo, 0.7 GB on f12, and the pause rose with it.
        SweepResolved();
        Simulate(corpses, seconds);
        AdvanceCorpses(corpses, seconds);
    }

    /// <summary>
    /// While true, passes step no corpse — for a seek's model-decal replay, which poses players at ticks it jumps between
    /// and would otherwise make the corpse environment catch up across every gap (15.7 of 17.8 s on f12). A bullet cannot
    /// decal a corpse, and the frame at the target steps them as a seek always has.
    /// </summary>
    public bool HoldsCorpses { get; set; }

    /// <summary>The moment's corpses, gathered for one advance — kept to be reused rather than allocated per frame.</summary>
    private readonly List<CorpseRequest> _corpseRequests = [];

    /// <summary>The map's BSP tree, for the visibility half of the cull.</summary>
    /// <remarks>
    /// Null until a map is read, and null leaves the cull frustum-only rather than culling
    /// everything — the safe direction this whole path takes.
    /// </remarks>
    public BspLeafTree? Tree
    {
        get => _tree;
        set
        {
            // **A new map is a new leaf system** (`LevelInitPreEntity`): every handle names the old tree's leaves.
            _tree = value;
            _handleOf.Clear();
            _filedBoxes.Clear();
            _filedLast.Clear();
            UnfileStaticProps(removing: false);
            _leafSystem = value is { IsEmpty: false } tree ? new ClientLeafSystem(tree.LeafCount, tree.EnumerateLeavesInBox) : OneLeaf();
            _everyLeaf = value is { IsEmpty: false } leaves ? [.. Enumerable.Range(0, leaves.LeafCount)] : PlaceZero;
        }
    }

    /// <summary>The box the engine would cull this model by, placed — <c>CalcRenderableWorldSpaceAABB</c>.</summary>
    /// <param name="prop">The entity being drawn.</param>
    /// <returns>Its world-space box, or an empty one when the model carries no bounds.</returns>
    /// <remarks>
    /// **`DefaultRenderBoundsWorldspace` (`clientleafsystem.cpp:342`), both of its branches.** A
    /// bone-merged entity is culled by its WEARER's box bloated by its own reach; everything else by
    /// its own bounds placed at its render origin and angles.
    ///
    /// **`IsFollowingEntity` is `EF_BONEMERGE &amp;&amp; MOVETYPE_NONE &amp;&amp; GetMoveParent()`**
    /// (`c_baseentity.cpp:3176`), and `GetFollowedEntity` is then just the move parent. This project
    /// records that relation as `SceneProp.AttachedTo`, so the test here is whether the wearer is
    /// known and drawable.
    ///
    /// **Valve recurses** — a parent that is itself following resolves through
    /// `CalcRenderableWorldSpaceAABB_Fast`, which calls itself. One level is taken here because
    /// nothing in TF2 merges onto a merged item, and a cycle in demo data would otherwise hang the
    /// viewer; a wearer that is itself worn falls back to its own placed box.
    ///
    /// **Called once per prop BEFORE the pose now** (B254), which it can be because it reads render
    /// bounds and placement rather than bones.
    /// </remarks>
    private (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) WorldBoxFor(
        SceneProp prop)
    {
        StudioBox local = Scaled(
            _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? sized)
                ? sized.RenderBoundsFor(prop.Pose.Sequence)
                : default,
            prop.Pose.Scale);

        if (prop.AttachedTo is { } wearer &&
            _propsByEntity.TryGetValue(wearer, out SceneProp? parent))
        {
            return WorldSpaceBounds.Following(
                Placed(parent),
                local,

                // GetLocalOrigin: a merged item's own pose, which is (0,0,0) by construction for
                // almost all of them and is used only to grow the bloat when it is not.
                (prop.Pose.X, prop.Pose.Y, prop.Pose.Z));
        }

        return Placed(prop, local);
    }

    /// <summary>The render bounds grown by the model's scale, as the last two lines of
    /// <c>C_BaseAnimating::GetRenderBounds</c> do.</summary>
    /// <param name="local">The box the header and sequence produced.</param>
    /// <param name="scale">The entity's <c>m_flModelScale</c>.</param>
    /// <returns>The scaled box.</returns>
    /// <remarks>
    /// **Valve's own last step, and skipping it was a live defect.**
    ///
    /// <code>
    /// // Scale this up depending on if our model is currently scaling
    /// const float flScale = GetModelScale();
    /// theMaxs *= flScale;
    /// theMins *= flScale;
    /// </code>
    ///
    /// Scale is decoded, interpolated and applied when this project DRAWS a model — so a scaled
    /// model was being drawn at its real size and culled by a box at its authored one. A giant
    /// draws far outside a box a tenth its size and vanishes at the edge of the screen.
    ///
    /// **Both corners are multiplied, not just the extent**, so a box that is not centred on its
    /// origin moves as well as growing. That is Valve's arithmetic and it is the correct one: the
    /// model's geometry scales about its origin, so its bounds must too.
    /// </remarks>
    private static StudioBox Scaled(StudioBox local, float scale) =>

        // The shortcut is for the overwhelmingly common case and nothing else; a scale that is not
        // exactly one simply takes the multiply, which for 1.0 would be an identity anyway. The
        // analyzer's usual objection — that float equality is a trap — does not apply to a branch
        // whose two sides compute the same answer.
        Math.Abs(scale - 1f) < float.Epsilon
            ? local
            : new StudioBox(
                local.MinX * scale, local.MinY * scale, local.MinZ * scale,
                local.MaxX * scale, local.MaxY * scale, local.MaxZ * scale);

    /// <summary>One entity's own placed box, ignoring anything it may be attached to.</summary>
    private (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Placed(
        SceneProp prop) =>
        Placed(
            prop,
            Scaled(
                _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? sized)
                    ? sized.RenderBoundsFor(prop.Pose.Sequence)
                    : default,
                prop.Pose.Scale));

    private static (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Placed(
        SceneProp prop, StudioBox local) =>
        WorldSpaceBounds.Placed(
            local,
            (prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
            (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll));

    /// <summary>A value for each pose parameter the model declares, in its own order.</summary>
    /// <remarks>
    /// **Matched by NAME rather than by position**, because a pose parameter's index is a property
    /// of the model: a scout and a heavy declare their own lists and there is no guarantee
    /// <c>move_x</c> lands at the same index in both. Filling an array positionally works right up
    /// until a class orders them differently, and then that class alone animates from the wrong
    /// input — the kind of defect that looks like a bad animation rather than a bad lookup.
    ///
    /// Anything this project does not compute stays at zero, which is what the engine leaves an
    /// unset parameter at.
    /// </remarks>
    private static float[] PoseValues(PropModels.SkinnedModel model, ScenePose pose, int sequence)
    {
        IReadOnlyList<StudioPoseParameter> parameters = model.PoseParameters;

        if (parameters.Count == 0)
        {
            return [];
        }

        // **What the entity itself sent wins, and for most animating entities that is everything**
        // (B269). `CBaseAnimating` networks the whole array (`server/baseanimating.cpp:243`) and
        // `C_BaseAnimating::GetPoseParameters` (`c_baseanimating.cpp:1401`) hands it straight to
        // the blend, so a sentry's aim comes off the wire and nothing below this line applies to
        // one. The derivation underneath is `CBasePlayerAnimState`, which exists precisely because
        // `tf_player.cpp:769` EXCLUDES the array for players — so the two paths are the engine's
        // own split rather than a preference between them, and an entity can only be on one side.
        //
        // Indexed directly, because the wire's index IS the model's: `GetNumPoseParameters` counts
        // the virtual model's merged list, whose first entries are the root model's own in order
        // (`CVirtualModel::AppendPoseParameters`).
        if (pose.PoseParameters.Count > 0)
        {
            return Sent(parameters.Count, pose.PoseParameters);
        }

        float[] values = Filled(parameters, pose.MoveX, pose.MoveY, pose.EyePitch, pose.AimYaw);

        // **The speed scaling, and it happens HERE rather than in the scene layer because only this
        // side can open a model.** ComputePoseParam_MoveYaw finishes with
        //
        //     float flMaxSpeed = GetSequenceGroundSpeed( GetSequence() );
        //     if ( flMaxSpeed > flSpeed ) { x *= flSpeed / flMaxSpeed; y *= flSpeed / flMaxSpeed; }
        //
        // which pulls a player moving slower than their animation was authored for back towards the
        // middle of the blend grid. Without it a scout walking at 100 units a second animated with
        // the same full-magnitude stride as one sprinting at 400.
        //
        // **The two-pass shape is Valve's, not an accident of this port.** The engine sets move_x
        // and move_y, reads the ground speed WITH THOSE IN PLACE — the parameters choose which
        // cells of the grid are blended, so they choose whose authored speed is being asked about —
        // and only then rescales and sets them again. Reading the speed first would ask about
        // whichever cells the previous frame happened to leave behind.
        // Stryker disable once : a mutant that empties the guard body leaves 'speed'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (pose.Speed is not { } speed || speed <= 0f)
        {
            return values;
        }

        float authored = model.GroundSpeed(sequence, values);

        // Valve's guard is `if ( flMaxSpeed > flSpeed )`, so a player moving FASTER than their
        // animation was authored for is left alone rather than scaled past the edge of the grid.
        if (authored <= speed)
        {
            return values;
        }

        float scale = speed / authored;

        return Filled(
            parameters, pose.MoveX * scale, pose.MoveY * scale, pose.EyePitch, pose.AimYaw);
    }

    /// <summary>The values an entity sent, sized to the model that will consume them.</summary>
    /// <remarks>
    /// **The two counts can legitimately differ**, and neither direction is an error. The array is
    /// a fixed 24 slots on the server (`MAXSTUDIOPOSEPARAM`) of which only the model's own are
    /// meaningful, so more values than parameters is the ordinary case; fewer happens when a delta
    /// has named only the low elements of an entity whose model wants more, and the engine's
    /// unsent slots read as the zero `OnNewModel` left them at (`c_baseanimating.cpp:1134`).
    /// </remarks>
    private static float[] Sent(int count, IReadOnlyList<float> values)
    {
        float[] sized = new float[count];

        for (int index = 0; index < count && index < values.Count; index++)
        {
            sized[index] = values[index];
        }

        return sized;
    }

    /// <summary>Every pose parameter's stored value, given the two this project computes.</summary>
    /// <remarks>
    /// Anything not computed stays at a raw zero, which is what the engine leaves an unset
    /// parameter at — and note that zero is normalised like any other value, so a parameter running
    /// −1 to 1 lands in the MIDDLE of its range rather than at the bottom.
    /// </remarks>
    private static float[] Filled(
        IReadOnlyList<StudioPoseParameter> parameters,
        float moveX,
        float moveY,
        float? eyePitch,
        float? aimYaw)
    {
        float[] values = new float[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            float raw = parameters[index].Name switch
            {
                "move_x" => moveX,
                "move_y" => moveY,

                // **Negated, which is the whole of ComputePoseParam_AimPitch:**
                // `SetPoseParameter( m_iAimPitch, -flAimPitch )` with flAimPitch the eye pitch. The
                // sign lives here rather than in the stored value, so what the scene carries still
                // matches what the wire said.
                //
                // Zero when the recording sent no eye angles, which is level — the same answer this
                // gave before aiming existed, rather than a guess at where they were looking.
                "body_pitch" => -(eyePitch ?? 0f),

                // **Already negated**, unlike body_pitch above. The twist is computed where the
                // feet are simulated, because it is the difference between two values only that
                // state machine holds — and SetPoseParameter( m_iAimYaw, -flAimYaw ) is applied
                // there with them.
                "body_yaw" => aimYaw ?? 0f,

                _ => 0f,
            };

            // Stored normalised, as the engine stores it - see StudioBlendGrid.Normalize.
            values[index] = StudioBlendGrid.Normalize(parameters[index], raw);
        }

        return values;
    }


    /// <summary>Which axis a model is longest along, named for the log.</summary>
    /// <remarks>
    /// "z, upright" is the expected answer for anything that stands up. Anything else on a
    /// humanoid means the model is on its side.
    /// </remarks>
    private static string Tallest(float spanX, float spanY, float spanZ)
    {
        if (spanZ >= spanX && spanZ >= spanY)
        {
            return "z, upright";
        }

        // **Only flagged when the model is clearly long in the wrong direction.** A medkit is
        // 24 by 17 by 23 and legitimately near-cubic; calling that "on its side" cries wolf on a
        // correct model, which is how a real warning stops being read.
        string axis = spanX >= spanY ? "x" : "y";

        return MathF.Max(spanX, spanY) > spanZ * 1.5f ? axis + ", ON ITS SIDE" : axis;
    }
}
