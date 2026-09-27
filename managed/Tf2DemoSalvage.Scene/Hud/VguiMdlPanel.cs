using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// <c>CMDL</c> (<c>public/matsys_controls/mdlpanel.h</c>'s <c>MDLData_t</c>) as a model panel holds one: a model, its
/// skin, its body number and, for the root, its sequence — plus <c>m_MDLToWorld</c>.
/// </summary>
public sealed class VguiMdl
{
    /// <summary>The model's path — <c>m_MDL</c>'s handle by name — or null for <c>MDLHANDLE_INVALID</c>.</summary>
    public string? Path { get; internal set; }

    /// <summary><c>m_nSkin</c>: 0 from <c>CMDL</c>'s constructor.</summary>
    public int Skin { get; set; }

    /// <summary><c>m_nBody</c>: 0 from <c>CMDL</c>'s constructor.</summary>
    public int Body { get; set; }

    /// <summary><c>m_nSequence</c>: 0 from <c>CMDL</c>'s constructor.</summary>
    public int Sequence { get; set; }

    /// <summary><c>m_bDisabled</c> (merge models only, mdlpanel.cpp:481).</summary>
    public bool Disabled { get; set; }

    /// <summary>The <c>ItemTintColor</c> proxy's colour from <c>m_pProxyData</c>, the item it draws (mdlpanel.cpp:845), or null.</summary>
    public (float Red, float Green, float Blue)? Paint { get; set; }
}

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
/// prop; a model panel is neither, so <see cref="EntityFor"/> keys the same objects by path.
/// </remarks>
public class VguiMdlPanel : VguiPotteryWheelPanel
{
    /// <summary><c>MAXSTUDIOPOSEPARAM</c> (studio.h): the size of <c>m_PoseParameters</c>.</summary>
    public const int MaxStudioPoseParam = 24;

    /// <summary><c>CMDLPanel( parent, name )</c> (<c>mdlpanel.h:46</c>).</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    /// <param name="mdlCache"><c>vgui::MDLCache()</c>. Asked again on every paint, so a model precached after the
    /// panel was built is drawn from the next frame.</param>
    public VguiMdlPanel(VguiPanel? parent, string? name, IMdlCache mdlCache)
        : base(parent, name)
    {
        ArgumentNullException.ThrowIfNull(mdlCache);

        MdlCache = mdlCache;
    }

    /// <summary><c>BONE_USED_BY_ANYTHING</c> — every bone this panel might read, built every time.</summary>
    /// <remarks>
    /// A model panel draws a handful of models, never per-frame for hundreds of entities, so there is no budget to
    /// protect the way there is in <see cref="EntityModelSet"/>.
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

    private readonly BoneFrameCounter _clock = new();
    private readonly Dictionary<string, AnimatingEntity> _entities = new(StringComparer.Ordinal);
    private readonly VguiMdl _root = new();
    private readonly List<VguiMdl> _merges = [];

    /// <summary><c>m_PoseParameters</c> (<c>mdlpanel.h:158</c>), normalised as <c>Studio_SetPoseParameter</c>
    /// stores them.</summary>
    private readonly float[] _poseParameters = new float[MaxStudioPoseParam];

    /// <summary><c>m_RootMDL.m_MDLToWorld</c> (<c>mdlpanel.h:116</c>): identity from the constructor
    /// (<c>SetIdentityMatrix</c>, mdlpanel.cpp:75) until <see cref="SetModelAnglesAndPosition"/>.</summary>
    private float[] _rootMdlToWorld = Identity3x4;

    /// <summary><c>vgui::MDLCache()</c>.</summary>
    protected IMdlCache MdlCache { get; }

    /// <summary><c>m_RootMDL.m_MDL</c>.</summary>
    public VguiMdl RootMdl => _root;

    /// <summary>The root model's path, or null for <c>MDLHANDLE_INVALID</c>.</summary>
    public string? ModelName => _root.Path;

    /// <summary><c>m_RootMDL.m_MDL.m_nSequence</c>.</summary>
    public int Sequence => _root.Sequence;

    /// <summary><c>m_RootMDL.m_MDL.m_nSkin</c>.</summary>
    public int Skin => _root.Skin;

    /// <summary><c>m_RootMDL.m_MDL.m_nBody</c>.</summary>
    public int Body => _root.Body;

    /// <summary><c>m_aMergeMDLs</c> (<c>mdlpanel.h:124</c>), in order.</summary>
    public IReadOnlyList<VguiMdl> MergeMdls => _merges;

    /// <summary><c>m_PoseParameters</c>.</summary>
    public IReadOnlyList<float> PoseParameters => _poseParameters;

    /// <summary><c>GetAutoPlayTime()</c>, set by whoever drives this panel's paint (there is no wall clock at this layer).</summary>
    public double RealTimeSeconds { get; set; }

    /// <summary><c>m_RootMDL.m_flCycleStartTime</c> (<c>mdlpanel.h:118</c>).</summary>
    public double CycleStartTime { get; set; }

    /// <summary><c>m_flTime = GetAutoPlayTime() - m_flCycleStartTime</c> (<c>mdlpanel.cpp:638</c>).</summary>
    public double CycleTime => RealTimeSeconds - CycleStartTime;

    /// <summary>The model paths this panel needs precached, root first.</summary>
    /// <returns>The root's path, then each merge model's.</returns>
    public virtual IEnumerable<string> ModelsToPrecache()
    {
        if (_root.Path is { Length: > 0 } root)
        {
            yield return root;
        }

        foreach (VguiMdl merge in _merges)
        {
            if (merge.Path is { Length: > 0 } path)
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// <c>CMDLPanel::SetMDL( pMDLName )</c> (<c>mdlpanel.cpp:153-198</c>): the root model by path — an unloaded or
    /// empty one is <c>MDLHANDLE_INVALID</c> — with the cycle clock zeroed and the pose parameters at the model's
    /// defaults.
    /// </summary>
    /// <param name="modelName">The model's path, or null.</param>
    public virtual void SetMDL(string? modelName)
    {
        _root.Path = string.IsNullOrEmpty(modelName) ? null : modelName;

        // `m_RootMDL.m_flCycleStartTime = 0.f;` (:169), then `SetPoseParameters( NULL, 0 )` (:172).
        CycleStartTime = 0d;
        SetPoseParameters(null);
    }

    /// <summary><c>SetSequence( nSequence, bResetSequence )</c> (<c>mdlpanel.cpp:541</c>).</summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="resetSequence">Whether to restart the cycle clock at <c>GetAutoPlayTime()</c>.</param>
    public void SetSequence(int sequence, bool resetSequence = false)
    {
        _root.Sequence = sequence;

        if (resetSequence)
        {
            CycleStartTime = RealTimeSeconds;
        }
    }

    /// <summary><c>SetSkin</c> (<c>mdlpanel.cpp:623</c>).</summary>
    /// <param name="skin">The skin family.</param>
    public void SetSkin(int skin) => _root.Skin = skin;

    /// <summary><c>SetBody</c>: <c>m_RootMDL.m_MDL.m_nBody = nBody</c> (<c>mdlpanel.h</c>).</summary>
    /// <param name="body">The body number.</param>
    public void SetBody(int body) => _root.Body = body;

    /// <summary>
    /// <c>SetPoseParameters( pPoseParameters, nCount )</c> (<c>mdlpanel.cpp:556-571</c>): copied in, or with null
    /// <c>Studio_CalcDefaultPoseParameters</c> — each one <c>Studio_SetPoseParameter( i, 0 )</c>.
    /// </summary>
    /// <param name="poseParameters">The values, or null for the model's defaults.</param>
    public void SetPoseParameters(IReadOnlyList<float>? poseParameters)
    {
        if (poseParameters is not null)
        {
            for (int index = 0; index < Math.Min(MaxStudioPoseParam, poseParameters.Count); index++)
            {
                _poseParameters[index] = poseParameters[index];
            }

            return;
        }

        if (RootStudioHdr() is not { } model)
        {
            return;
        }

        for (int index = 0; index < Math.Min(MaxStudioPoseParam, model.PoseParameters.Count); index++)
        {
            _poseParameters[index] = StudioBlendGrid.Normalize(model.PoseParameters[index], 0f);
        }
    }

    /// <summary><c>SetPoseParameterByName</c> (<c>mdlpanel.cpp:577-596</c>): the first parameter of that name, case
    /// ignored, through <c>Studio_SetPoseParameter</c>.</summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="value">Its value in the parameter's own units.</param>
    /// <returns>Whether the root model has such a parameter.</returns>
    public bool SetPoseParameterByName(string name, float value)
    {
        if (RootStudioHdr() is not { } model)
        {
            return false;
        }

        for (int index = 0; index < Math.Min(MaxStudioPoseParam, model.PoseParameters.Count); index++)
        {
            if (string.Equals(model.PoseParameters[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _poseParameters[index] = StudioBlendGrid.Normalize(model.PoseParameters[index], value);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>SetMergeMDL( pMDLName, pProxyData, nSkin )</c> (<c>mdlpanel.cpp:833-883</c>): refused without a root model,
    /// else appended with <c>m_nSkin</c> set only when <paramref name="skin"/> is not −1.
    /// </summary>
    /// <param name="modelName">The model's path.</param>
    /// <param name="skin">The skin, or −1 to leave the model's 0.</param>
    /// <returns>The merge model, or null when there is no root to merge onto.</returns>
    public VguiMdl? SetMergeMDL(string? modelName, int skin = -1)
    {
        if (_root.Path is null)
        {
            return null;
        }

        VguiMdl merge = new() { Path = string.IsNullOrEmpty(modelName) ? null : modelName };

        if (skin != -1)
        {
            merge.Skin = skin;
        }

        _merges.Add(merge);

        return merge;
    }

    /// <summary><c>GetMergeMDL( handle )</c> (<c>mdlpanel.cpp:918</c>): the first merge model of that path.</summary>
    /// <param name="modelName">The model's path.</param>
    /// <returns>The merge model, or null.</returns>
    public VguiMdl? GetMergeMDL(string modelName)
    {
        foreach (VguiMdl merge in _merges)
        {
            if (string.Equals(merge.Path, modelName, StringComparison.Ordinal))
            {
                return merge;
            }
        }

        return null;
    }

    /// <summary><c>ClearMergeMDLs</c> (<c>mdlpanel.cpp:948</c>).</summary>
    public void ClearMergeMDLs() => _merges.Clear();

    /// <summary><c>CMDLPanel::SetModelAnglesAndPosition</c> (<c>mdlpanel.h:89</c>, mdlpanel.cpp:233-237):
    /// <c>AngleMatrix( angRot, vecPos, m_RootMDL.m_MDLToWorld )</c>.</summary>
    /// <param name="angles">The model's rotation.</param>
    /// <param name="origin">The model's position.</param>
    public virtual void SetModelAnglesAndPosition((float X, float Y, float Z) angles, (float X, float Y, float Z) origin) =>
        _rootMdlToWorld = AngleMatrix3x4(angles, origin);

    /// <summary><c>m_RootMDL.m_MDL.GetStudioHdr()</c>: the root model, or null for <c>MDLHANDLE_INVALID</c>.</summary>
    /// <returns>The skinned model.</returns>
    protected PropModels.SkinnedModel? RootStudioHdr() =>
        _root.Path is { } path ? MdlCache.FindMdl(path)?.Skinned : null;

    /// <summary>
    /// <c>CMDLPanel::OnPaint3D</c> (<c>mdlpanel.cpp:415-520</c>): <see cref="PrePaint3D"/>, root <c>SetUpBones</c> with
    /// <c>m_PoseParameters</c> and draw, each enabled merge model <c>SetupBonesWithBoneMerge</c> onto it and drawn then
    /// <see cref="RenderingMergedModel"/>, then <see cref="RenderingRootModel"/>, then <see cref="PostPaint3D"/>.
    /// </summary>
    protected override void OnPaint3D(VguiRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(renderContext);

        // `if ( m_RootMDL.m_MDL.GetMDL() == MDLHANDLE_INVALID ) return;` (:417).
        if (_root.Path is not { } rootPath || MdlCache.FindMdl(rootPath) is not { Skinned: { } rootModel } rootFrames)
        {
            return;
        }

        PrePaint3D(renderContext);

        AnimatingEntity root = EntityFor(rootPath, rootModel);

        if (root.Pose is SkeletonPose rootPose)
        {
            float[] poseValues = new float[rootModel.PoseParameters.Count];

            Array.Copy(_poseParameters, poseValues, Math.Min(poseValues.Length, MaxStudioPoseParam));

            rootPose.Sequence = _root.Sequence;
            rootPose.EntityTransform = _rootMdlToWorld;
            rootPose.PoseValues = poseValues;

            (rootPose.Frame, rootPose.FrameFraction) = FrameAt(rootModel, _root.Sequence, poseValues, CycleTime);
        }

        root.SetupBones(FullBoneMask, CycleTime);

        ModelInstance rootDrawn = Drawn(rootPath, rootFrames, _root, Skinned(rootModel.Bones, root.Bones));

        renderContext.Models.Add(rootDrawn);

        foreach (VguiMdl merge in _merges)
        {
            if (merge.Disabled || merge.Path is not { } mergePath
                || MdlCache.FindMdl(mergePath) is not { Skinned: { } mergeModel } mergeFrames)
            {
                continue;
            }

            AnimatingEntity merged = EntityFor(mergePath, mergeModel);
            merged.Follows = root;
            merged.SetupBones(FullBoneMask, CycleTime);

            renderContext.Models.Add(Drawn(mergePath, mergeFrames, merge, Skinned(mergeModel.Bones, merged.Bones)));

            RenderingMergedModel(renderContext, mergeFrames, mergePath, merged.Bones);
        }

        RenderingRootModel(renderContext, rootFrames, rootPath, root.Bones);

        PostPaint3D(renderContext);
    }

    /// <summary><c>virtual void PrePaint3D( IMatRenderContext* )</c> (<c>mdlpanel.h:136</c>): empty here.</summary>
    /// <param name="renderContext">What is drawn this frame so far.</param>
    protected virtual void PrePaint3D(VguiRenderContext renderContext)
    {
    }

    /// <summary><c>virtual void PostPaint3D( IMatRenderContext* )</c> (<c>mdlpanel.h:137</c>): empty here.</summary>
    /// <param name="renderContext">What is drawn this frame.</param>
    protected virtual void PostPaint3D(VguiRenderContext renderContext)
    {
    }

    /// <summary><c>virtual void RenderingRootModel( pRenderContext, pStudioHdr, mdlHandle, pWorldMatrix )</c>
    /// (<c>mdlpanel.h:138</c>): notified after the merges are drawn (mdlpanel.cpp:509). Empty here.</summary>
    /// <param name="renderContext">What is drawn this frame.</param>
    /// <param name="studioHdr">The root model.</param>
    /// <param name="mdlHandle">Its path.</param>
    /// <param name="worldMatrix">Its bone-to-world matrices.</param>
    protected virtual void RenderingRootModel(
        VguiRenderContext renderContext, PropModels.ModelFrames studioHdr, string mdlHandle, BoneAccessor worldMatrix)
    {
    }

    /// <summary><c>virtual void RenderingMergedModel( ... )</c> (<c>mdlpanel.h:139</c>): notified after each merge
    /// model is drawn (mdlpanel.cpp:505). Empty here.</summary>
    /// <param name="renderContext">What is drawn this frame so far.</param>
    /// <param name="studioHdr">The merge model.</param>
    /// <param name="mdlHandle">Its path.</param>
    /// <param name="worldMatrix">Its bone-to-world matrices.</param>
    protected virtual void RenderingMergedModel(
        VguiRenderContext renderContext, PropModels.ModelFrames studioHdr, string mdlHandle, BoneAccessor worldMatrix)
    {
    }

    /// <summary>
    /// <c>StandardBlendingRules</c>' realtime/closed-form split (<c>EntityModels.cs:763-788</c>), fed by
    /// <see cref="CycleTime"/> — a panel has one clock that has run continuously since <see cref="CycleStartTime"/>.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="sequence">Its chosen sequence.</param>
    /// <param name="poseValues">This model's current pose parameters, for <c>BlendedCyclesPerSecond</c>.</param>
    /// <param name="cycleTime">Seconds since the sequence started.</param>
    /// <returns>The frame and how far past it.</returns>
    public static (int Frame, float Fraction) FrameAt(
        PropModels.SkinnedModel model, int sequence, IReadOnlyList<float> poseValues, double cycleTime)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(poseValues);

        float raw = (float)(cycleTime * model.BlendedCyclesPerSecond(sequence, poseValues));

        float phase = model.Realtime(sequence)
            ? StudioSequences.ClampCycle(raw, loops: true)
            : StudioSequences.ClampCycle(raw, model.Loops(sequence));

        return StudioSequences.FrameAt(phase, model.Frames(sequence), model.Loops(sequence));
    }

    /// <summary>One model as <c>CMDL::Draw</c> draws it: its skin row from the model's own table
    /// (<c>g_skinref[m_nSkin]</c>, an out-of-range family falling back to zero as the engine does) and its body number
    /// over its body parts.</summary>
    private ModelInstance Drawn(string path, PropModels.ModelFrames frames, VguiMdl mdl, float[][] bones)
    {
        IReadOnlyDictionary<int, int>? skinSwap = null;

        if (frames.SkinSwaps is { Count: > 0 } swaps)
        {
            skinSwap = swaps[mdl.Skin >= 0 && mdl.Skin < swaps.Count ? mdl.Skin : 0];
        }

        return new ModelInstance(
            path,
            Identity4x4,
            Ambient,
            Sun,
            Bones: bones,
            SkinSwap: skinSwap,
            BodyParts: frames.BodyParts,
            Body: mdl.Body,
            Paint: mdl.Paint,
            Locals: Locals);
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
    /// by path because a model panel has no entity index.</summary>
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

    /// <summary><see cref="BoneSkinning.Fill"/>, unbuffered: a model panel poses a handful of models.</summary>
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
