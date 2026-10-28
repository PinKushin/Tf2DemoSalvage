using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>Told when an entity's model is resolved, with the facts the interpolator needs.</summary>
    /// <remarks>
    /// **A callback rather than a call, because this layer must not know what is listening.** The
    /// only consumer is the interpolator, which lives under the scene rather than beside it; the
    /// window wires the two together where it already registers every other system.
    ///
    /// **Entity, then which parameters wrap, then whether the model is a static prop** — the three things
    /// `C_BaseAnimating::OnNewModel` and `PostDataUpdate` need from the studio header and cannot get
    /// anywhere else (<c>c_baseanimating.cpp:1124</c>, <c>:4740</c>).
    /// </remarks>
    public Action<int, IReadOnlyList<bool>, bool>? ModelResolved { get; set; }

    /// <summary>Which of a model's pose parameters wrap, one entry per parameter it declares.</summary>
    /// <remarks>
    /// **Its LENGTH is the model's parameter count and is load-bearing** (B383) — it is what sizes the
    /// interpolation history, because `SetMaxCount( hdr->GetNumPoseParameters() )` precedes the
    /// per-parameter `SetLooping` in the engine. Of a sentry gun's two parameters only <c>aim_yaw</c>
    /// loops, so an all-false array is the common case and must still be two long.
    /// </remarks>
    private static bool[] LoopingPoseParameters(PropModels.SkinnedModel model)
    {
        IReadOnlyList<StudioPoseParameter> parameters = model.PoseParameters;

        // **Always the model's parameter COUNT, even when none of them loops** (B383). This returned an
        // empty array in that case — an allocation saved, and the count destroyed with it. The count is
        // what sizes the interpolation history: `m_iv_flPoseParameter.SetMaxCount(
        // hdr->GetNumPoseParameters() )` comes BEFORE the per-parameter `SetLooping`
        // (`c_baseanimating.cpp:1124`), and they are two calls in the engine because they are two facts.
        // Collapsing them into one `bool[]` and then eliding the all-false case reported every ordinary
        // model as having no parameters at all.
        bool[] looping = new bool[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            looping[index] = parameters[index].Loop != 0f;
        }

        return looping;
    }

    /// <summary>Where a prop stands, as a row-major 3×4, reusing the array it had last frame.</summary>
    /// <remarks>
    /// **A bone-merged entity takes its PARENT's placement, and that is Valve's, not bookkeeping.**
    /// <c>CalcAbsolutePosition</c> branches on it explicitly:
    ///
    /// <code>
    /// if ( IsEffectActive(EF_BONEMERGE) )
    /// {
    ///     MoveToAimEnt();
    ///     return;
    /// }
    /// </code>
    ///
    /// at <c>c_baseentity.cpp:4387</c>, and <c>MoveToAimEnt</c> is
    /// <c>GetAimEntOrigin( GetMoveParent(), … )</c> followed by <c>SetAbsOrigin</c> /
    /// <c>SetAbsAngles</c> (<c>:4294</c>). So a followed entity's ABSOLUTE origin is its parent's,
    /// while its LOCAL origin is the zero <c>FollowEntity</c> wrote — and <c>SetupBones</c> builds
    /// its <c>parentTransform</c> from <c>GetRenderOrigin()</c>, which is the absolute one.
    ///
    /// **This was deleted on 2026-08-24 and put back the same night**, which is the part worth
    /// recording. The old code did it as <c>transform = worn.Where</c> and D88 removed it as
    /// leftover bookkeeping, on the strength of having read <c>BuildTransformations</c> and not
    /// <c>CalcAbsolutePosition</c>. The result was eight of nine weapons drawn at the map origin:
    /// a weapon shares few or no bone names with a player, so the merge places little, and what is
    /// left builds at the entity's own placement — which for a followed entity is (0,0,0) unless
    /// this resolves it.
    ///
    /// The owner's diagnosis was the right one and arrived before the reading did: *"if everything
    /// was following valve then it would work"*. It was not following Valve. It had deleted a piece
    /// of Valve while believing the opposite.
    /// </remarks>
    private float[] PlacementOf(SceneProp prop)
    {
        if (!_placements.TryGetValue(prop.EntityIndex, out float[]? placement))
        {
            placement = new float[12];
            _placements[prop.EntityIndex] = placement;
        }

        // **An unparented object's placement is its own pose, which cannot change** (finding 82): computed once and copied
        // in, the engine's clear `EFL_DIRTY_ABSTRANSFORM`. A parented one follows its parent and is composed every time.
        ResolvedModel resolved = ResolvedFor(prop);

        if (prop.AttachedTo is null && resolved.Placement is { } known)
        {
            known.CopyTo(placement, 0);
            return placement;
        }

        ScenePose pose = Absolute(prop, AnimatingEntity.MaximumFollowDepth);

        // **Through MatrixConvention, because the two forms are not the same shape OR the same
        // layout.** ToMatrix is the shader's row-vector 4×4 with translation in the last ROW;
        // EntityTransform is Valve's matrix3x4_t with translation in column 3. Crossing that by
        // hand is what threw `Destination array was not long enough` on the first frame of
        // playback — and the length was the lucky half, since a transpose of the same size would
        // have drawn a plausible wrong pose instead of failing.
        // Written into the entity's own array, as the engine's `matrix3x4_t` is a local (B574).
        MatrixConvention.ToBoneMatrix(
            new PropTransform(pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale),
            placement);

        if (prop.AttachedTo is null)
        {
            resolved.Placement = [.. placement];
        }

        return placement;
    }

    /// <summary>The pose a prop is actually AT, following its parent chain up.</summary>
    /// <remarks>
    /// **<c>CalcAbsolutePosition</c>'s bone-merge branch** (<c>c_baseentity.cpp:4387</c>): a
    /// followed entity's absolute origin and angles are its parent's, however deep the chain runs.
    /// An attachment on a weapon on a player therefore ends up at the player, which is what lets
    /// its unmatched bones land somewhere sensible.
    ///
    /// Bounded by the same depth <see cref="AnimatingEntity"/> uses, for the same reason: the wire
    /// should never carry a cycle and a demo this project exists to open may carry anything. A
    /// chain that runs past it keeps the prop's own pose, which draws it in the wrong place rather
    /// than not at all — the milder of the two failures, and the one that leaves something to see.
    ///
    /// **The composition is <see cref="ParentChain.Absolute(SceneProp, Func{int, SceneProp?}, int)"/>**, shared with everything outside the model pass that
    /// asks <c>GetAbsOrigin()</c>. The owner's report that drove its third branch — *"that one gate on the left is
    /// rotated 90 degreed"*, from `cp_fulgur`'s setup gates (B241) — is why the angle shortcut there is conditional.
    /// </remarks>
    /// <para>
    /// **Parents come from the pass first and the frame second** (B551): the pass's copy is the one this pass posed, and
    /// <see cref="PlaceFrame"/>'s map supplies a parent the pass does not hold. **Memoized per pass** at the full depth,
    /// cleared where the pass's attachments are, because an attachment-hung child is only as fresh as that table.
    /// </para>
    private ScenePose Absolute(SceneProp prop, int budget)
    {
        bool full = budget == AnimatingEntity.MaximumFollowDepth && !_simulating;

        if (full && _absolute.TryGetValue(prop.EntityIndex, out ScenePose known))
        {
            return known;
        }

        _parentOf ??= ParentOf;
        _attachmentOf ??= (entity, number) =>
            PosedAttachmentMatrix(entity, attachments => number >= 1 && number <= attachments.Count ? number - 1 : -1);

        ScenePose pose = ParentChain.Absolute(prop, _parentOf, budget, _attachmentOf);

        if (full)
        {
            _absolute[prop.EntityIndex] = pose;
        }

        return pose;
    }

    /// <summary>An entity's prop: this pass's copy, else the frame's (<see cref="PlaceFrame"/>).</summary>
    private SceneProp? ParentOf(int entity) =>
        _propsByEntity.TryGetValue(entity, out SceneProp? inPass) ? inPass : _frameProps.GetValueOrDefault(entity);

    /// <summary><see cref="Absolute"/>'s parent lookup, made once rather than per call.</summary>
    private Func<int, SceneProp?>? _parentOf;

    /// <summary><see cref="Absolute"/>'s attachment lookup, made once rather than per call.</summary>
    private Func<int, int, float[]?>? _attachmentOf;

    /// <summary>This frame's props by entity index, so a parent chain can be walked.</summary>
    private readonly Dictionary<int, SceneProp> _propsByEntity = [];

    /// <summary>Every prop of this frame, whatever pass draws it — the parents a pass's own list may lack (B551).</summary>
    private readonly Dictionary<int, SceneProp> _frameProps = [];

    /// <summary>Hands over every prop of the frame before its passes, so a move parent is found in any of them.</summary>
    /// <param name="props">The frame's props, drawn or not.</param>
    /// <remarks>
    /// **A pass's list is not the frame.** `Simulate` indexes only the props of its own pass — drawn, undrawn or
    /// viewmodel — and the scene's visibility rules remove props from those lists, while <c>CalcAbsolutePosition</c>
    /// composes a child onto its parent with no reference to whether the parent is drawn (`c_baseentity.cpp:4393`).
    /// A parent missing from the child's pass gave it its LOCAL pose, which is B551 again.
    ///
    /// **They live until the next call, and nothing else ends them.** `MomentScene` calls this every frame; a seek's
    /// corpse replay (`AdvanceCorpsesAlone`) simulates at its own tick in between and neither claims nor clears them.
    /// </remarks>
    public void PlaceFrame(IEnumerable<SceneProp> props)
    {
        ArgumentNullException.ThrowIfNull(props);

        _frameProps.Clear();
        _absolute.Clear();

        foreach (SceneProp prop in props)
        {
            _frameProps[prop.EntityIndex] = prop;
        }
    }

    /// <summary>Each entity's absolute pose, composed once per pass — the engine's cached <c>m_rgflCoordinateFrame</c>.</summary>
    private readonly Dictionary<int, ScenePose> _absolute = [];

    /// <summary>
    /// Whether `Simulate` is still bringing the pass's entities up to date — so a parent's bones may not be posed yet and
    /// nothing composed from them is kept. Defensive: no construction tried (a child listed before a moving,
    /// attachment-bearing parent) shows a stale answer without it, so dropping it survives every test; kept on review.
    /// </summary>
    private bool _simulating;

    /// <summary>Bone-to-world folded with the model's bind pose, which is what the shader skins by.</summary>
    /// <remarks>
    /// **The one place <c>poseToBone</c> is applied, and it belongs here** — finding 35 section 7a.
    /// <c>IStudioRender::DrawModel</c> takes bone-to-world and nothing else
    /// (<c>istudiorender.h:329</c>); the composition happens at the boundary that owns the vertices.
    /// Keeping it out of the pose path is what leaves one array per entity with nothing to choose
    /// wrongly between.
    /// </remarks>
    private float[][] Skinning(int entity, IReadOnlyList<StudioBone> bones, BoneAccessor accessor)
    {
        // **Reused per entity, because this ran once per bone per drawn prop per FRAME.** At around
        // 250 props of eighty bones that is twenty thousand twelve-float arrays every frame, and
        // the allocating overload was being called for every one of them.
        //
        // Measured 2026-08-25: gen0 was collecting 34 times a second. The first attempt at this
        // fixed FromQuaternion and the O(n²) override scan and bought about 20 ms of the 545 —
        // because THIS was the cost, and it was left alone. A plausible fix to the wrong line reads
        // exactly like a fix that did not help.
        //
        // Safe to reuse because an instance list is consumed within the frame that built it, and
        // the viewmodel's entities carry their own indices (4096-4098) so they cannot collide with
        // a world entity's buffer.
        //
        // **One buffer per entity per pose half** (B581 rung 3): the frame in flight draws from the other half's.
        Dictionary<int, float[][]> skinning = _skinning[PoseHalf];

        if (!skinning.TryGetValue(entity, out float[][]? buffer) ||
            buffer.Length != accessor.Count)
        {
            buffer = new float[accessor.Count][];

            for (int bone = 0; bone < buffer.Length; bone++)
            {
                buffer[bone] = new float[12];
            }

            skinning[entity] = buffer;
        }

        BoneSkinning.Fill(bones, accessor, buffer);

        return buffer;
    }

    /// <summary>Each entity's skinning matrices, reused between frames, one set per pose half.</summary>
    private readonly Dictionary<int, float[][]>[] _skinning = [[], []];

    /// <summary>The pose half this frame's pose writes (<see cref="PoseHalves"/>, B581 rung 3): 0 or 1; 0 without a device.</summary>
    /// <remarks>Selects the per-entity buffers the draw reads by reference — skinning matrices and face streams — so a
    /// pose never rewrites what the frame in flight is drawing. Instance lists and their matrices are per list, and the
    /// scene hands each half its own list.</remarks>
    internal int PoseHalf { get; set; }
}
