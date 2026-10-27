using System;
using System.Collections.Generic;
using System.Numerics;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>Every attachment of one entity, resolved once — Valve's <c>PutAttachment</c>.</summary>
    /// <remarks>
    /// **Cleared per PASS rather than per frame**, in `Simulate` beside `_propsByEntity`. The
    /// viewmodel pass corrects its attachments for the viewmodel's projection and the world pass
    /// does not, so a table carried between them would be wrong for one of the two. The reserved
    /// viewmodel entity indices mean the two passes cannot collide today; clearing anyway is what
    /// keeps that from becoming load-bearing.
    /// </remarks>
    private readonly Dictionary<int, float[][]> _attachments = [];

    /// <summary>Resolves every attachment a wearer declares, once.</summary>
    /// <remarks>
    /// **<c>SetupBones_AttachmentHelper</c>, `c_baseanimating.cpp:2055`**, loop and all:
    ///
    /// <code>
    ///   for (int i = 0; i &lt; hdr-&gt;GetNumAttachments(); i++)
    ///   {
    ///       const mstudioattachment_t &amp;pattachment = hdr-&gt;pAttachment( i );
    ///       int iBone = hdr-&gt;GetAttachmentBone( i );
    ///       if ( (pattachment.flags &amp; ATTACHMENT_FLAG_WORLD_ALIGN) == 0 )
    ///           ConcatTransforms( GetBone( iBone ), pattachment.local, world );
    ///       else
    ///           ...position only, identity rotation...
    ///       FormatViewModelAttachment( i, world );
    ///       PutAttachment( i + 1, world );
    ///   }
    /// </code>
    ///
    /// **The correction is inside the loop, applied to every attachment**, which is why it lives
    /// here rather than at the one call site that used to need it.
    /// </remarks>
    private float[][] AttachmentsOf(
        int entity, AnimatingEntity wearer, IReadOnlyList<StudioAttachment> attachments)
    {
        if (_attachments.TryGetValue(entity, out float[][]? cached))
        {
            return cached;
        }

        float[][] resolved = new float[attachments.Count][];

        for (int index = 0; index < attachments.Count; index++)
        {
            StudioAttachment attachment = attachments[index];

            if (attachment.Bone < 0 || attachment.Bone >= wearer.Bones.Count)
            {
                continue;
            }

            float[] placement = MatrixConvention.ToBoneMatrix(
                AttachmentPlacement.Matrix(
                    wearer.Bones.Bone(attachment.Bone).ToArray(),
                    attachment.Local,
                    PropTransform.Identity.ToMatrix(),
                    attachment.IsWorldAligned));

            // **`FormatViewModelAttachment`, a virtual whose base body is EMPTY** — nothing for a
            // world model, everything for a viewmodel, which is why a null projection here is that
            // empty body rather than a missing feature. Only the POSITION moves: Valve reads the
            // matrix's translation, corrects it and writes it back with `PositionMatrix`, leaving
            // the rotation the bone gave it.
            if (ViewmodelProjection is { } projection && IsViewmodel(entity))
            {
                (float x, float y, float z) = ViewmodelAttachment.Correct(
                    (placement[3], placement[7], placement[11]),
                    projection.Eye,
                    projection.Right,
                    projection.Up,
                    projection.Forward,
                    projection.WorldFieldOfView,
                    projection.ViewmodelFieldOfView);

                placement[3] = x;
                placement[7] = y;
                placement[11] = z;
            }

            resolved[index] = placement;
        }

        _attachments[entity] = resolved;

        return resolved;
    }

    /// <summary>The demo time the last pass simulated to, which an attachment asked between passes is posed at.</summary>
    private double _simulatedSeconds;

    /// <summary>Where one of an entity's named attachments is — <c>LookupAttachment</c> then <c>GetAttachment</c>.</summary>
    /// <param name="entity">The entity, such as a player's active weapon.</param>
    /// <param name="name">The attachment's name; <c>FireBullet</c> asks for <c>muzzle</c>.</param>
    /// <returns>Its world position, or null when the entity is not posed here or its model has no such point.</returns>
    /// <remarks>
    /// **Case-insensitive, and absence is an answer.** `Studio_FindAttachment` compares with `stricmp`, and
    /// `LookupAttachment` returns 0 for a miss, which `GetAttachment` refuses, so `FireBullet` keeps its own start.
    /// Resolved through the same per-pass table the worn items use, so a weapon asked twice in one pass costs one
    /// bone setup — `SetupBones( …, BONE_USED_BY_ATTACHMENT, … )` is what `GetAttachment` runs.
    /// </remarks>
    public (float X, float Y, float Z)? AttachmentPosition(int entity, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Attachment(entity, attachments => Named(attachments, name));
    }

    /// <summary>Where an entity's attachment is by the engine's own number — `GetAttachment( iAttachment )`, 1-based.</summary>
    /// <param name="entity">The entity, such as a sentry gun.</param>
    /// <param name="number">The attachment number a dispatch carries; 0 is none.</param>
    /// <returns>Its world position, or null when the entity is not posed here or has no such attachment.</returns>
    public (float X, float Y, float Z)? AttachmentPosition(int entity, int number) =>
        Attachment(entity, attachments => number >= 1 && number <= attachments.Count ? number - 1 : -1);

    /// <summary>An attachment as a particle control point: `PATTACH_POINT_FOLLOW`'s origin and orientation.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="number">The attachment number, 1-based.</param>
    /// <returns>The point, or null when the entity is not posed here or has no such attachment.</returns>
    /// <remarks>
    /// `MatrixVectors( attachmentToWorld, &amp;forward, &amp;right, &amp;up )`: forward is the matrix's first column, up its third,
    /// and right the NEGATED second, because a Source matrix's second axis points left.
    /// </remarks>
    public ParticleControlPoint? AttachmentPoint(int entity, int number) =>
        PointOf(AttachmentMatrix(entity, attachments => number >= 1 && number <= attachments.Count ? number - 1 : -1));

    /// <summary>A named attachment as a particle control point — `PATTACH_POINT_FOLLOW` on `LookupAttachment( name )`.</summary>
    /// <param name="entity">The entity, such as a weapon.</param>
    /// <param name="name">The attachment's name, such as <c>muzzle</c>.</param>
    /// <returns>The point, or null when the entity is not posed here or its model has no such attachment.</returns>
    public ParticleControlPoint? AttachmentPoint(int entity, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return PointOf(AttachmentMatrix(entity, attachments => Named(attachments, name)));
    }

    /// <summary>`Studio_FindAttachment`: the first attachment of that name, compared without case; −1 for none.</summary>
    private static int Named(IReadOnlyList<StudioAttachment> attachments, string name)
    {
        for (int index = 0; index < attachments.Count; index++)
        {
            if (string.Equals(attachments[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>An attachment's 3×4 matrix as a control point: forward, the negated left column as right, up.</summary>
    private static ParticleControlPoint? PointOf(float[]? matrix) =>
        matrix is null
            ? null
            : new ParticleControlPoint(
                new Vector3(matrix[3], matrix[7], matrix[11]),
                new Vector3(matrix[0], matrix[4], matrix[8]),
                new Vector3(-matrix[1], -matrix[5], -matrix[9]),
                new Vector3(matrix[2], matrix[6], matrix[10]));

    /// <summary>`GetAttachment` for the attachment <paramref name="pick"/> chooses, −1 for none.</summary>
    private (float X, float Y, float Z)? Attachment(int entity, Func<IReadOnlyList<StudioAttachment>, int> pick) =>
        AttachmentMatrix(entity, pick) is { } placement ? (placement[3], placement[7], placement[11]) : null;

    /// <summary>The attachment's world matrix, 3×4 row-major, for the attachment <paramref name="pick"/> chooses.</summary>
    private float[]? AttachmentMatrix(int entity, Func<IReadOnlyList<StudioAttachment>, int> pick) =>
        InScene(entity) is null ? null : PosedAttachmentMatrix(entity, pick);

    /// <summary>An entity's readable bones by usage bit, for a test of what a query built; 0 for one not held.</summary>
    internal int ReadableBonesOf(int entity) => _entities.TryGetValue(entity, out AnimatingEntity? animating) ? animating.ReadableBones : 0;

    /// <summary>Attachment queries that reached a bone setup, ever (finding 82).</summary>
    public long AttachmentQueries { get; private set; }

    /// <summary>Skeletons those queries built, counted at the build (finding 82).</summary>
    public long AttachmentBuilds { get; private set; }

    /// <summary>Queries that built a skeleton the entity had already built this frame (finding 82).</summary>
    public long AttachmentRebuilds { get; private set; }

    /// <summary>Time spent in those queries' bone setups, in stopwatch ticks (finding 82).</summary>
    public long AttachmentTicks { get; private set; }

    /// <summary>Of <see cref="AttachmentBuilds"/>: roots' builds, by the meter at the query (finding 105 A6).</summary>
    public long AttachmentRootBuilds { get; private set; }

    /// <summary>Of those: roots a threaded pre-pass would take (finding 105 A6).</summary>
    public long AttachmentEligibleBuilds { get; private set; }

    /// <summary>The eligible roots' own ticks in those queries (finding 105 A6).</summary>
    public long AttachmentEligibleTicks { get; private set; }

    /// <summary>The attachment matrix of an entity posed in the pass in flight, which the scene does not list yet.</summary>
    /// <remarks>For a move child's parent during `Simulate` (B551); everything asked from outside goes through
    /// <see cref="AttachmentMatrix"/>, which refuses an entity that has left the scene.</remarks>
    private float[]? PosedAttachmentMatrix(int entity, Func<IReadOnlyList<StudioAttachment>, int> pick)
    {
        // Stryker disable all : a mutant that empties the guard body leaves the out variables unassigned
        // below (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (!_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Attachments is not { Count: > 0 } attachments)
        {
            return null;
        }

        // Stryker restore all
        int point = pick(attachments);

        if (point < 0)
        {
            return null;
        }

        // **Counted where the bones are asked for** (finding 82, B243): how many queries, how many built a skeleton, how
        // many of those rebuilt one already built this frame, and the time, all carried from this call.
        long askedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        int buildsBefore = SkeletonPose.PoseBuilds;

        // **"This frame" is the engine's, which begins at the world pass** (`InvalidateBoneCaches`, once in `OnRenderStart`,
        // cdll_client_int.cpp:2163). The viewmodel and undrawn passes advance our clock again, so `BuiltThisFrame` read here,
        // after them, was false for every entity and this counter could only read 0 (finding 105 D).
        bool builtBefore = animating.BuiltSince(_worldBoneFrame);
        BoneSetupMeter meter = _boneFrames.Meter;
        (int rootsBefore, int eligibleBefore, long eligibleTicksBefore) = (meter.RootBuilds, meter.EligibleBuilds, meter.EligibleTicks);
        // **`CalcAttachments` is `SetupBones( NULL, -1, BONE_USED_BY_ATTACHMENT, gpGlobals->curtime )`** (finding 82,
        // `c_baseanimating.cpp:2088-2094`): an attachment asks for the attachment bones, and `SetupBones` widens that by
        // what is readable and by last frame's mask (`:2920`). This asked for every bone, so an entity nothing drew this
        // frame had its whole skeleton built to place one effect.
        bool posed = animating.SetupBones(StudioBoneFlags.UsedByAttachment, _simulatedSeconds);
        int built = SkeletonPose.PoseBuilds - buildsBefore;

        // Only queries from outside the pass — effects, decals, sounds — which is the cost finding 82 is ranking.
        if (!_simulating)
        {
            AttachmentQueries++;
            AttachmentBuilds += built;
            AttachmentRebuilds += builtBefore && built > 0 ? 1 : 0;
            AttachmentTicks += System.Diagnostics.Stopwatch.GetTimestamp() - askedAt;

            // By caller (finding 105 A6): the roots these queries build, which `g_PreviousBoneSetups` would list for the next
            // frame's pre-pass whatever asked (c_baseanimating.cpp:2897), and the eligible ones' own time.
            AttachmentRootBuilds += meter.RootBuilds - rootsBefore;
            AttachmentEligibleBuilds += meter.EligibleBuilds - eligibleBefore;
            AttachmentEligibleTicks += meter.EligibleTicks - eligibleTicksBefore;
        }

        return posed ? AttachmentsOf(entity, animating, attachments)[point] : null;
    }

    /// <summary>How far along a ray an entity's posed hitboxes let it get — <c>C_BaseAnimating::TestHitboxes</c>.</summary>
    /// <param name="entity">The entity, such as a player.</param>
    /// <param name="start">`ray.m_Start`.</param>
    /// <param name="delta">`ray.m_Delta`.</param>
    /// <param name="mask">The trace's contents mask.</param>
    /// <returns>The fraction, or null when it is not hit, not posed here, or has no hitboxes.</returns>
    /// <remarks>
    /// Set 0, which is `m_nHitboxSet` for every TF2 player; the bones as the last pass posed them, which is
    /// `GetBoneCache` after `SetupBones( …, BONE_USED_BY_HITBOX, … )`.
    /// </remarks>
    public float? TraceHitboxes(int entity, System.Numerics.Vector3 start, System.Numerics.Vector3 delta, int mask)
    {
        // Stryker disable all : a mutant that empties the guard body leaves the out variables unassigned
        // below (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (InScene(entity) is null ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Hitboxes is not { Count: > 0 } sets ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        // Stryker restore all
        return StudioHitboxes.Trace(sets[0], bone => animating.Bones.Bone(bone).ToArray(), start, delta, mask);
    }

    /// <summary>
    /// An entity's hitboxes as the last pass posed them — <c>CParticleSystemQuery::GetControllingObjectHitBoxInfo</c>
    /// (`particlesystemquery.cpp:392-462`), for a particle effect that is born on and rides a model (B538).
    /// </summary>
    /// <param name="entity">The entity, such as a player.</param>
    /// <returns>
    /// Set 0's boxes on their bones, in set order, and the origin the entity had when they were posed; null when it is
    /// not posed here or has none.
    /// </returns>
    /// <remarks>
    /// **The origin is the pose's own, not the frame's.** The frame a seek lands on asks before its models are posed
    /// again, and bones from the tick before paired with this tick's origin put a two-second catch-up hundreds of
    /// units off its player — seen, as a patient's pluses hanging where he had stood a minute earlier.
    /// </remarks>
    public (IReadOnlyList<ModelHitbox> Boxes, System.Numerics.Vector3 PosedAt)? PosedHitboxes(int entity)
    {
        // Stryker disable all : a mutant that empties the guard body leaves the out variables unassigned
        // below (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (InScene(entity) is not { } seen ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Hitboxes is not { Count: > 0 } sets ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        // Stryker restore all
        ModelHitbox[] posed = new ModelHitbox[sets[0].Count];

        for (int index = 0; index < posed.Length; index++)
        {
            StudioHitbox box = sets[0][index];

            posed[index] = ModelHitbox.On(box.Min, box.Max, animating.Bones.Bone(box.Bone));
        }

        return (posed, new System.Numerics.Vector3(seen.Pose.X, seen.Pose.Y, seen.Pose.Z));
    }

    /// <summary>How far a point lies outside one of an entity's posed hitboxes, in the box's own frame — an instrument.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="box">The box's index in set 0, as a server `Impact`'s `m_nHitBox` names it.</param>
    /// <param name="point">The world point, such as the server's hit.</param>
    /// <returns>The distance from the box's surface, zero inside it, and the offset from its centre in its bone's
    /// frame; null when the entity or box is not there.</returns>
    public (float Outside, System.Numerics.Vector3 FromCentre)? HitboxGap(int entity, int box, System.Numerics.Vector3 point)
    {
        if (InScene(entity) is null ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Hitboxes is not { Count: > 0 } sets || box < 0 || box >= sets[0].Count ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        StudioHitbox hitbox = sets[0][box];
        ReadOnlySpan<float> m = animating.Bones.Bone(hitbox.Bone);
        System.Numerics.Vector3 relative = point - new System.Numerics.Vector3(m[3], m[7], m[11]);

        // Into the bone's frame: the rotation's transpose.
        System.Numerics.Vector3 local = new(
            (m[0] * relative.X) + (m[4] * relative.Y) + (m[8] * relative.Z),
            (m[1] * relative.X) + (m[5] * relative.Y) + (m[9] * relative.Z),
            (m[2] * relative.X) + (m[6] * relative.Y) + (m[10] * relative.Z));

        System.Numerics.Vector3 clamped = System.Numerics.Vector3.Clamp(local, hitbox.Min, hitbox.Max);

        return (System.Numerics.Vector3.Distance(local, clamped), local - ((hitbox.Min + hitbox.Max) * 0.5f));
    }

    /// <summary>
    /// The point on one of an entity's posed hitboxes nearest a viewer — <c>ComputeBeamEntPosition</c>'s hitbox branch
    /// (`view_beams.cpp:233-257`), for a beam flagged <c>FBEAM_USE_HITBOXES</c>.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="number">The hitbox, ONE-based as the beam's attachment index carries it: <c>pHitbox( nAttachment - 1 )</c>.</param>
    /// <param name="viewer"><c>MainViewOrigin()</c>.</param>
    /// <returns>The world point, or null when the entity is not posed here or has no such box.</returns>
    /// <remarks>
    /// <code>
    /// VectorITransform( vecViewPt, *hitboxbones[ pHitbox-&gt;bone ], vecLocalViewPt );
    /// CalcClosestPointOnAABB( pHitbox-&gt;bbmin, pHitbox-&gt;bbmax, vecLocalViewPt, vecLocalClosestPt );
    /// VectorTransform( vecLocalClosestPt, *hitboxbones[ pHitbox-&gt;bone ], pt );
    /// </code>
    ///
    /// Set 0, which is <c>m_nHitboxSet</c> for everything TF2 draws; the guard is the engine's
    /// <c>set-&gt;numhitboxes &gt;= nAttachment &amp;&amp; nAttachment &gt; 0</c>.
    /// </remarks>
    public System.Numerics.Vector3? HitboxPointNearest(int entity, int number, System.Numerics.Vector3 viewer)
    {
        if (InScene(entity) is null ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Hitboxes is not { Count: > 0 } sets || number < 1 || number > sets[0].Count ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        StudioHitbox hitbox = sets[0][number - 1];
        ReadOnlySpan<float> m = animating.Bones.Bone(hitbox.Bone);
        System.Numerics.Vector3 relative = viewer - new System.Numerics.Vector3(m[3], m[7], m[11]);

        // Into the bone's frame — the rotation's transpose, which is `VectorITransform`.
        System.Numerics.Vector3 local = new(
            (m[0] * relative.X) + (m[4] * relative.Y) + (m[8] * relative.Z),
            (m[1] * relative.X) + (m[5] * relative.Y) + (m[9] * relative.Z),
            (m[2] * relative.X) + (m[6] * relative.Y) + (m[10] * relative.Z));

        System.Numerics.Vector3 closest = System.Numerics.Vector3.Clamp(local, hitbox.Min, hitbox.Max);

        return new System.Numerics.Vector3(
            (m[0] * closest.X) + (m[1] * closest.Y) + (m[2] * closest.Z) + m[3],
            (m[4] * closest.X) + (m[5] * closest.Y) + (m[6] * closest.Z) + m[7],
            (m[8] * closest.X) + (m[9] * closest.Y) + (m[10] * closest.Z) + m[11]);
    }

    /// <summary>Each pass's entities as its last run saw them, by pass name — see <see cref="Instances"/>.</summary>
    private readonly Dictionary<string, Dictionary<int, SceneProp>> _scenes = new(StringComparer.Ordinal);

    /// <summary>Drops every pass's scene, so a frame starts with none and a pass it skips holds nothing.</summary>
    /// <remarks>
    /// **A skipped pass must not keep the last frame's props.** The undrawn and hidden passes are skipped on an empty
    /// list, the viewmodel's when first person is off; each kept its previous scene, and <see cref="InScene"/> answers
    /// from the first pass holding an entity — so a zoomed recorder answered with his stale undrawn prop, and one who
    /// left while zoomed stayed in the scene for good. Called once a frame before the first pass; the dictionaries are
    /// kept, so this allocates nothing.
    /// </remarks>
    internal void BeginScenes()
    {
        foreach (Dictionary<int, SceneProp> scene in _scenes.Values)
        {
            scene.Clear();
        }
    }

    /// <summary>The entity as the latest pass that holds it saw it, or null when no pass does — it has left the scene.</summary>
    private SceneProp? InScene(int entity)
    {
        foreach (Dictionary<int, SceneProp> scene in _scenes.Values)
        {
            if (scene.TryGetValue(entity, out SceneProp? prop))
            {
                return prop;
            }
        }

        // A static prop is in the level whether or not a view reached it this frame (finding 83 item 1).
        return _staticByEntity.GetValueOrDefault(entity);
    }

    /// <summary>Whether an entity has been posed by a pass here, so a question about its attachments has an answer.</summary>
    /// <param name="entity">The entity index.</param>
    /// <returns><c>true</c> once a pass has built its skeleton.</returns>
    public bool IsPosed(int entity) => _entities.ContainsKey(entity);

    /// <summary>
    /// An entity's model, skinning matrices and body as the last pass drew it — the pose `CModelRender::AddDecal` sets up
    /// bones for when a decal lands (B415); null for an entity not in this pass or with no skinned model.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The model path, its pose-to-world matrices, `m_nBody` and the model's body parts.</returns>
    /// <remarks>
    /// **Posed on demand, drawn or not** — `CModelRender::AddDecal` calls `SetupBones` itself, so a player out of view
    /// still takes the decal. Reading only what the draw pass left refused 60 of 100 f12 hits and projected the rest of
    /// an off-screen player's onto the pose he last drew with. `SetupBones` is cached per frame, so a drawn entity costs
    /// nothing more.
    /// </remarks>
    public (string Model, IReadOnlyList<float[]> Bones, int Body, IReadOnlyList<(int Base, int Count)>? Parts)? SkinningOf(int entity)
    {
        if (InScene(entity) is not { } prop ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames) ||
            frames.Skinned is not { } skinned ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        return (prop.ModelPath, Skinning(entity, skinned.Bones, animating.Bones), prop.Pose.Body, frames.BodyParts);
    }

    /// <summary>Whether an entity is one of the viewmodel pass's own.</summary>
    /// <remarks>
    /// **The reserved indices rather than a new flag**, because <see cref="ViewmodelScene"/> already
    /// owns that numbering and a second answer to "is this a viewmodel" is a second thing to
    /// disagree with.
    /// </remarks>
    private static bool IsViewmodel(int entity) =>
        entity is ViewmodelScene.ArmsEntityIndex
            or ViewmodelScene.WeaponEntityIndex
            or ViewmodelScene.OffHandEntityIndex;

    /// <summary>Where each entity's light is sampled, so a worn item can borrow its wearer's.</summary>
    private readonly Dictionary<int, (float X, float Y, float Z)> _lightPoints = [];

    /// <summary>Every DRAWABLE entity's placement, for items that have no bones to carry one.</summary>
    /// <remarks>
    /// **A model this project bakes rather than skins still gets worn on things**, and it has no
    /// skeleton to fold its wearer's placement into — so it needs the wearer's transform the way
    /// everything did before D88. Two viewer tests caught this within minutes of the swap, which is
    /// exactly what they are for.
    ///
    /// **The engine has no such case, and that is worth being precise about rather than glossing.**
    /// Valve's equivalent is <c>STUDIOHDR_FLAGS_STATIC_PROP</c>, which still goes through
    /// <c>SetupBones</c> and gets ONE bone — <c>MatrixCopy( parentTransform, GetBoneForWrite( 0 ) )</c>
    /// at <c>c_baseanimating.cpp:2953</c>. Ours is a different distinction: a performance choice
    /// about which models are worth skinning at all, made by this project and not by the format. So
    /// the fallback is ours to carry, and it is declared here rather than left to look like Valve's.
    ///
    /// Keyed only for props that pass the drawable and batch checks, so "the wearer is not being
    /// drawn" is answerable without depending on which order the draw loop reaches them in.
    /// </remarks>
    private readonly Dictionary<int, PropTransform> _drawnPlacements = [];

    /// <summary>Where EVERY entity is this frame, drawn or not, for resolving a parent.</summary>
    /// <remarks>
    /// **Separate from `_drawnPlacements` because they answer different questions**
    /// (B231). That one says "is my wearer on screen", which is what `if ( baseDrawn )` asks before
    /// hanging a hat; this one says "where is my parent", which `CalcAbsolutePosition` asks with no
    /// regard for whether the parent renders. Every gate on `cp_fulgur` hangs off an invisible
    /// `func_door`, so the second map is the only one that can place them.
    /// </remarks>
    private readonly Dictionary<int, PropTransform> _parentPlacements = [];

    /// <summary>This entity's animating object, rebuilt when its model changes.</summary>
    private AnimatingEntity EntityFor(SceneProp prop, PropModels.SkinnedModel skinned)
    {
        if (_entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? existing) &&
            _entityModels.TryGetValue(prop.EntityIndex, out string? was) &&
            string.Equals(was, prop.ModelPath, StringComparison.Ordinal))
        {
            return existing;
        }

        // The factory rather than nothing, so the bone-merge pairing reaches the log. That line was
        // deleted with EntityModelSet.Merge and its absence left a viewer run unable to say whether
        // weapons had paired with the players holding them.
        // **Counted because a spike needs a cause and this is the only per-moment discontinuity in
        // the pose path.** Everything else here runs for every entity every frame; this runs the
        // first time an entity is seen and when its model changes. Five of seven slow moments were
        // dominated by bone work at 50-162 ms against a 3 ms median, and a median that low says the
        // steady path is not what spikes (B189).
        EntitiesBuilt++;

        AnimatingEntity animating = new(
            new SkeletonPose(skinned.Bones, skinned.LocalsInto), _boneFrames, _loggers);

        _entities[prop.EntityIndex] = animating;
        _entityModels[prop.EntityIndex] = prop.ModelPath;

        // **`C_BaseAnimating::OnNewModel`'s pose-parameter half** (`c_baseanimating.cpp:1130`),
        // fired from the one place that knows an entity's model has just been resolved. Only the
        // model says which parameters wrap, and the interpolation that needs to know runs a layer
        // below models — so it is told rather than left to look.
        ModelResolved?.Invoke(
            prop.EntityIndex, LoopingPoseParameters(skinned), skinned.StaticProp);

        return animating;
    }

    /// <summary>The pose parameters an entity was last posed with.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <returns>The values the blend received, or empty if the entity has not been posed.</returns>
    /// <remarks>
    /// **The value CARRIED, not a second computation of it** (B243). This reports the array
    /// `Simulate` handed to the skeleton, so a probe or a test reading it is asking what the blend
    /// actually used rather than re-deriving what it should have been — the distinction that made
    /// eight diagnostics lie in two sessions.
    /// </remarks>
    public IReadOnlyList<float> PoseValuesOf(int entityIndex) =>
        _entities.TryGetValue(entityIndex, out AnimatingEntity? animating) &&
        animating.Pose is SkeletonPose posed
            ? posed.PoseValues
            : [];
}
