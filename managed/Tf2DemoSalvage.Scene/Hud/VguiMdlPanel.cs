using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// <c>CMDLPanel : CPotteryWheelPanel</c> (<c>public/matsys_controls/mdlpanel.h:40</c>,
/// <c>vgui2/matsys_controls/mdlpanel.cpp</c>): one root model, with merged models bone-merged onto it, posed at a
/// sequence on its own cycle clock.
/// </summary>
/// <remarks>
/// **The MDL cache is injected, never set later.** Valve reaches it as the global <c>vgui::MDLCache()</c>
/// (<c>SetMDL</c>, mdlpanel.cpp:185); which cache backs it is this project's adapter concern, so the constructor
/// takes it and a panel cannot exist that would silently paint nothing for want of one.
///
/// **Posing reuses <see cref="AnimatingEntity"/>/<see cref="SkeletonPose"/> directly, not
/// <c>EntityModelSet.Simulate</c>.** That machinery is keyed by entity index and walked once a frame for every drawn
/// prop; a model panel is neither — it is drawn from `.res`, at most a few per HUD, and keyed by its own model paths
/// instead. <see cref="EntityFor"/> is this panel's own small version of <c>EntityModelSet.EntityFor</c>
/// (<c>EntityModels.cs:1462</c>), keyed the same way but by path rather than by an entity that does not exist.
/// </remarks>
public class VguiMdlPanel : VguiPotteryWheelPanel
{
    /// <summary><c>CMDLPanel( parent, name )</c> (<c>mdlpanel.h:46</c>).</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    /// <param name="findMdl"><c>vgui::MDLCache()-&gt;FindMDL</c>: a model by path, or null when it cannot be resolved
    /// (<c>MDLHANDLE_INVALID</c>). Asked again on every paint, so a model precached after the panel was built is
    /// drawn from the next frame.</param>
    public VguiMdlPanel(VguiPanel? parent, string? name, Func<string, PropModels.SkinnedModel?> findMdl)
        : base(parent, name)
    {
        ArgumentNullException.ThrowIfNull(findMdl);

        _findMdl = findMdl;
    }

    /// <summary><c>BONE_USED_BY_ANYTHING</c> — every bone this panel might read, built every time.</summary>
    /// <remarks>
    /// A model panel draws a handful of models at most a few times a second between them, never
    /// per-frame for hundreds of entities, so there is no budget to protect the way there is in
    /// <see cref="EntityModelSet"/> — narrowing the mask would only risk leaving a hitbox or an
    /// attachment unbuilt for no measured benefit.
    /// </remarks>
    private const int FullBoneMask = StudioBoneFlags.UsedByAnything;

    private static readonly float[] Identity3x4 = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    private static readonly float[] Identity4x4 =
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    private readonly Func<string, PropModels.SkinnedModel?> _findMdl;
    private readonly BoneFrameCounter _clock = new();
    private readonly Dictionary<string, AnimatingEntity> _entities = new(StringComparer.Ordinal);

    /// <summary><c>m_RootMDL.m_MDLToWorld</c> (<c>mdlpanel.h:116</c>): identity from the constructor
    /// (<c>SetIdentityMatrix</c>, mdlpanel.cpp:75) until <see cref="SetModelAnglesAndPosition"/>.</summary>
    private float[] _rootMdlToWorld = Identity3x4;

    /// <summary>The root model's path (<c>m_RootMDL.m_MDL</c>'s handle, by name), or null to draw nothing.</summary>
    public string? ModelName { get; set; }

    /// <summary><c>m_RootMDL.m_MDL.m_nSequence</c>, or −1 for the model's sequence 0.</summary>
    public int Sequence { get; set; } = -1;

    /// <summary><c>m_RootMDL.m_MDL.m_nSkin</c> (<c>SetSkin</c>, mdlpanel.h:80), or −1 for its default.</summary>
    public int Skin { get; set; } = -1;

    /// <summary><c>m_aMergeMDLs</c> (<c>mdlpanel.h:124</c>): model paths bone-merged onto the root, in order.</summary>
    public IList<string> MergeModels { get; } = [];

    /// <summary><c>GetAutoPlayTime()</c>, set by whoever drives this panel's paint (there is no wall clock at this layer).</summary>
    public double RealTimeSeconds { get; set; }

    /// <summary><c>m_RootMDL.m_flCycleStartTime</c> (<c>mdlpanel.h:118</c>).</summary>
    public double CycleStartTime { get; set; }

    /// <summary><c>m_flTime = GetAutoPlayTime() - m_flCycleStartTime</c> (<c>mdlpanel.cpp:638</c>).</summary>
    public double CycleTime => RealTimeSeconds - CycleStartTime;

    /// <summary>The model paths this panel needs precached, root first.</summary>
    /// <remarks>
    /// A caller precaches these into the same <see cref="EntityModelSet"/> the scene uploads from — <c>Precache</c>
    /// (<c>EntityModels.cs:4911</c>) sets <c>Grown</c>, and <c>MomentScene.Pack</c> already uploads whenever that is
    /// true (<c>MomentScene.cs:633</c>), so a model a panel asks for mid-demo reaches the GPU on the very next frame
    /// with no change needed there.
    /// </remarks>
    public IEnumerable<string> ModelsToPrecache()
    {
        if (ModelName is { Length: > 0 } root)
        {
            yield return root;
        }

        foreach (string merge in MergeModels)
        {
            yield return merge;
        }
    }

    /// <summary><c>CMDLPanel::SetModelAnglesAndPosition</c> (<c>mdlpanel.h:89</c>, mdlpanel.cpp:233-237):
    /// <c>AngleMatrix( angRot, vecPos, m_RootMDL.m_MDLToWorld )</c>.</summary>
    /// <param name="angles">The model's rotation.</param>
    /// <param name="origin">The model's position.</param>
    public virtual void SetModelAnglesAndPosition((float X, float Y, float Z) angles, (float X, float Y, float Z) origin) =>
        _rootMdlToWorld = AngleMatrix3x4(angles, origin);

    /// <summary><c>SetIdentityMatrix( m_RootMDL.m_MDLToWorld )</c> — the constructor's state (mdlpanel.cpp:75).</summary>
    private protected void ResetModelToWorld() => _rootMdlToWorld = Identity3x4;

    /// <summary><c>m_RootMDL.m_MDL.GetStudioHdr()</c>: the root model, or null for <c>MDLHANDLE_INVALID</c>.</summary>
    private protected PropModels.SkinnedModel? RootStudioHdr() =>
        ModelName is { Length: > 0 } modelName ? _findMdl(modelName) : null;

    /// <summary>
    /// <c>CMDLPanel::OnPaint3D</c> (<c>mdlpanel.cpp:415-520</c>): <see cref="PrePaint3D"/>, root <c>SetUpBones</c>
    /// and draw, each merge model <c>SetupBonesWithBoneMerge</c> onto it and drawn then
    /// <see cref="RenderingMergedModel"/>, then <see cref="RenderingRootModel"/>, then <see cref="PostPaint3D"/>.
    /// </summary>
    /// <remarks>
    /// **Frame stepping** is <see cref="FrameAt"/> fed by <see cref="CycleTime"/> — <c>CMDLPanel::OnTick</c>'s
    /// <c>m_flTime</c> (mdlpanel.cpp:638), turned into a frame and fraction here rather than inside <c>StudioRender</c>.
    /// </remarks>
    protected override void OnPaint3D(IList<ModelInstance> renderContext)
    {
        ArgumentNullException.ThrowIfNull(renderContext);

        if (ModelName is not { Length: > 0 } modelName || _findMdl(modelName) is not { } rootModel)
        {
            return;
        }

        PrePaint3D(renderContext);

        AnimatingEntity root = EntityFor(modelName, rootModel);

        if (root.Pose is SkeletonPose rootPose)
        {
            int sequence = Sequence >= 0 ? Sequence : 0;

            rootPose.Sequence = sequence;
            rootPose.EntityTransform = _rootMdlToWorld;
            rootPose.PoseValues = MoveXPoseValues(rootModel.PoseParameters);

            (rootPose.Frame, rootPose.FrameFraction) = FrameAt(rootModel, sequence, rootPose.PoseValues, CycleTime);
        }

        root.SetupBones(FullBoneMask, CycleTime);

        ModelInstance rootDrawn = new(
            modelName,
            Identity4x4,
            Ambient,
            Sun,
            Bones: Skinned(rootModel.Bones, root.Bones),
            SkinSwap: Skin >= 0 ? new Dictionary<int, int> { [0] = Skin } : null,
            Locals: Locals);

        renderContext.Add(rootDrawn);

        foreach (string mergeName in MergeModels)
        {
            if (_findMdl(mergeName) is not { } mergeModel)
            {
                continue;
            }

            AnimatingEntity merged = EntityFor(mergeName, mergeModel);
            merged.Follows = root;
            merged.SetupBones(FullBoneMask, CycleTime);

            ModelInstance mergeDrawn = new(
                mergeName, Identity4x4, Ambient, Sun, Bones: Skinned(mergeModel.Bones, merged.Bones), Locals: Locals);

            renderContext.Add(mergeDrawn);

            RenderingMergedModel(renderContext, mergeModel, mergeDrawn);
        }

        RenderingRootModel(renderContext, rootModel, rootDrawn);

        PostPaint3D(renderContext);
    }

    /// <summary><c>virtual void PrePaint3D( IMatRenderContext* )</c> (<c>mdlpanel.h:136</c>): empty here.
    /// <c>CTFPlayerModelPanel</c> overrides it (<c>tf_playermodelpanel.h:79</c>).</summary>
    /// <param name="renderContext">The models drawn this frame so far.</param>
    protected virtual void PrePaint3D(IList<ModelInstance> renderContext)
    {
    }

    /// <summary><c>virtual void PostPaint3D( IMatRenderContext* )</c> (<c>mdlpanel.h:137</c>): empty here.
    /// <c>CTFPlayerModelPanel</c> overrides it (<c>tf_playermodelpanel.h:80</c>).</summary>
    /// <param name="renderContext">The models drawn this frame.</param>
    protected virtual void PostPaint3D(IList<ModelInstance> renderContext)
    {
    }

    /// <summary><c>virtual void RenderingRootModel( pRenderContext, pStudioHdr, mdlHandle, pWorldMatrix )</c>
    /// (<c>mdlpanel.h:138</c>): notified after the merges are drawn (mdlpanel.cpp:509). Empty here;
    /// <c>CTFPlayerModelPanel</c> overrides it (<c>tf_playermodelpanel.h:81</c>).</summary>
    /// <param name="renderContext">The models drawn this frame.</param>
    /// <param name="studioHdr">The root model.</param>
    /// <param name="drawn">What was drawn for it: its path (<c>mdlHandle</c>) and bone-to-world matrices
    /// (<c>pWorldMatrix</c>).</param>
    protected virtual void RenderingRootModel(
        IList<ModelInstance> renderContext, PropModels.SkinnedModel studioHdr, ModelInstance drawn)
    {
    }

    /// <summary><c>virtual void RenderingMergedModel( ... )</c> (<c>mdlpanel.h:139</c>): notified after each merge
    /// model is drawn (mdlpanel.cpp:505). Empty here; <c>CTFPlayerModelPanel</c> overrides it
    /// (<c>tf_playermodelpanel.h:82</c>).</summary>
    /// <param name="renderContext">The models drawn this frame so far.</param>
    /// <param name="studioHdr">The merge model.</param>
    /// <param name="drawn">What was drawn for it.</param>
    protected virtual void RenderingMergedModel(
        IList<ModelInstance> renderContext, PropModels.SkinnedModel studioHdr, ModelInstance drawn)
    {
    }

    /// <summary>
    /// <c>StandardBlendingRules</c>' realtime/closed-form split (<c>EntityModels.cs:763-788</c>), fed by
    /// <see cref="CycleTime"/> instead of a demo-time advance — this panel has no per-tick integration, only a
    /// clock that has run continuously since <see cref="CycleStartTime"/>, which is exactly what the REALTIME
    /// branch already assumes for a sequence carrying that flag. A sequence that does NOT carry it still uses the
    /// same closed form, because there is nothing here that behaves differently from one frame to the next: no
    /// discontinuity to preserve across, unlike a player whose playback rate can change mid-cycle.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="sequence">Its chosen sequence.</param>
    /// <param name="poseValues">This model's current pose parameters, for <c>BlendedCyclesPerSecond</c>.</param>
    /// <param name="cycleTime">Seconds since the sequence started.</param>
    /// <returns>The frame and how far past it, as <see cref="SkeletonPose.Frame"/>/<see cref="SkeletonPose.FrameFraction"/> want them.</returns>
    public static (int Frame, float Fraction) FrameAt(
        PropModels.SkinnedModel model, int sequence, IReadOnlyList<float> poseValues, double cycleTime)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(poseValues);

        float raw = (float)(cycleTime * model.BlendedCyclesPerSecond(sequence, poseValues));

        // `cycle = cycle - (int)cycle` (bone_setup.cpp's STUDIO_REALTIME branch) and `ClampCycle( x, true )` are the
        // same function for every x >= 0 — `docs/memory` already establishes this equivalence — so the realtime
        // branch is expressed as a call to the same helper rather than a second implementation of `- (int)`.
        float phase = model.Realtime(sequence)
            ? StudioSequences.ClampCycle(raw, loops: true)
            : StudioSequences.ClampCycle(raw, model.Loops(sequence));

        return StudioSequences.FrameAt(phase, model.Frames(sequence), model.Loops(sequence));
    }

    /// <summary><c>SetupModelAnimDefaults</c> (<c>basemodel_panel.cpp:175</c>): <c>SetPoseParameterByName( "move_x",
    /// 1.0f )</c>, unconditional — "so the run activity works" — before it even checks whether the model has any
    /// authored animations. Everything else stays at raw zero, normalised, exactly as an unset pose parameter is
    /// stored (`EntityModelSet.Filled`'s own comment, <c>EntityModels.cs:6157</c>).</summary>
    /// <remarks>
    /// **Applied by <see cref="OnPaint3D"/> for every panel, including a bare <see cref="VguiMdlPanel"/>**, where
    /// Valve applies it only from <c>CBaseModelPanel::SetMDL</c>/<c>OnTick</c> into <c>m_PoseParameters</c>
    /// (mdlpanel.h:158). A known divergence, carried unchanged through the class split; see
    /// <c>docs/HANDOFF-hud.md</c>.
    /// </remarks>
    public static float[] MoveXPoseValues(IReadOnlyList<StudioPoseParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        float[] values = new float[parameters.Count];

        for (int index = 0; index < values.Length; index++)
        {
            values[index] = StudioBlendGrid.Normalize(
                parameters[index],
                string.Equals(parameters[index].Name, "move_x", StringComparison.OrdinalIgnoreCase) ? 1f : 0f);
        }

        return values;
    }

    /// <summary><c>AngleMatrix( angRot, vecPos, matrix )</c>: a row of <c>(forward[i], left[i], up[i], origin[i])</c>
    /// per axis — <c>matrix3x4_t</c>'s own layout, which <c>StudioBones.Concatenate</c> already assumes.</summary>
    private static float[] AngleMatrix3x4((float X, float Y, float Z) angles, (float X, float Y, float Z) origin)
    {
        FreeCamera basis = new() { Origin = origin, Angles = (angles.X, angles.Y, angles.Z) };

        ((float X, float Y, float Z) forward, (float X, float Y, float Z) right, (float X, float Y, float Z) up) =
            basis.Basis();

        (float X, float Y, float Z) left = (-right.X, -right.Y, -right.Z);

        return
        [
            forward.X, left.X, up.X, origin.X,
            forward.Y, left.Y, up.Y, origin.Y,
            forward.Z, left.Z, up.Z, origin.Z,
        ];
    }

    /// <summary>This panel's own posing entry for one model path — <see cref="EntityModelSet.EntityFor"/>'s shape, keyed
    /// by path because a model panel has no entity index (<c>docs/HANDOFF-hud.md</c>, "our pieces").</summary>
    private AnimatingEntity EntityFor(string modelPath, PropModels.SkinnedModel model)
    {
        if (_entities.TryGetValue(modelPath, out AnimatingEntity? existing))
        {
            return existing;
        }

        AnimatingEntity created = new(new SkeletonPose(model.Bones, model.Locals), _clock);

        _entities[modelPath] = created;

        return created;
    }

    /// <summary><see cref="BoneSkinning.Fill"/>, unbuffered: a model panel poses a handful of models, not hundreds
    /// a frame, so there is no allocation to avoid the way <c>EntityModelSet.Skinning</c> does.</summary>
    private static float[][] Skinned(IReadOnlyList<StudioBone> bones, BoneAccessor accessor)
    {
        float[][] skinned = new float[accessor.Count][];

        for (int bone = 0; bone < skinned.Length; bone++)
        {
            skinned[bone] = new float[12];
        }

        BoneSkinning.Fill(bones, accessor, skinned);

        return skinned;
    }
}
