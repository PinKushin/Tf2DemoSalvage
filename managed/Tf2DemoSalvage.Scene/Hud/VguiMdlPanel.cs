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

    /// <summary>The material forced on the whole draw (<c>g_pStudioRender->ForcedMaterialOverride</c>), by VMT path, or null (B754).</summary>
    public string? MaterialOverride { get; set; }

    /// <summary>The war paint a <c>WeaponSkin</c> material binds, the item it draws being the proxy's (B754), or null.</summary>
    public WeaponSkin? WeaponSkin { get; set; }

    /// <summary>The mesh face this CMDL's draw last flexed (B779), kept with it so a dropped merge drops it.</summary>
    internal PanelMeshFlex MeshFlex { get; } = new();
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

    /// <summary>
    /// The root model's bones set up as the next paint will (`m_RootMDL.m_MDL.SetUpBones( m_RootMDL.m_MDLToWorld, … )`),
    /// then `ConcatTransforms( matBoneToWorld[ attach.localbone ], attach.local, out )` — an attachment's world matrix.
    /// </summary>
    /// <param name="attachment">The attachment's index.</param>
    /// <returns>A row-major 3x4, or null without a model or attachment.</returns>
    protected float[]? RootAttachmentToWorld(int attachment)
    {
        if (_root.Path is not { } rootPath || MdlCache.FindMdl(rootPath) is not { Skinned: { } rootModel } rootFrames
            || rootFrames.Attachments is not { } attachments || attachment < 0 || attachment >= attachments.Count)
        {
            return null;
        }

        AnimatingEntity root = PoseRoot(rootPath, rootModel);
        StudioAttachment point = attachments[attachment];
        float[] world = new float[12];

        StudioBones.Concatenate(root.Bones.Bone(point.Bone).ToArray(), [.. point.Local], world);

        return world;
    }

    /// <summary>The root posed at its sequence, pose parameters, transform and cycle, its bones set up.</summary>
    private AnimatingEntity PoseRoot(string rootPath, PropModels.SkinnedModel rootModel)
    {
        AnimatingEntity root = EntityFor(rootPath, rootModel);

        if (root.Pose is SkeletonPose rootPose)
        {
            float[] poseValues = new float[rootModel.PoseParameters.Count];

            Array.Copy(_poseParameters, poseValues, Math.Min(poseValues.Length, MaxStudioPoseParam));

            rootPose.Sequence = _root.Sequence;
            rootPose.EntityTransform = _rootMdlToWorld;
            rootPose.PoseValues = poseValues;
            rootPose.Cycle = CycleAt(rootModel, _root.Sequence, poseValues, CycleTime);
        }

        root.SetupBones(FullBoneMask, CycleTime);

        return root;
    }

    /// <summary>`Studio_FindAttachment` (bone_setup.cpp): the first attachment of that name, case ignored, or -1.</summary>
    /// <param name="studioHdr">The model.</param>
    /// <param name="name">The attachment's name.</param>
    /// <returns>Its index, or -1.</returns>
    protected static int FindAttachment(PropModels.ModelFrames studioHdr, string name)
    {
        ArgumentNullException.ThrowIfNull(studioHdr);

        IReadOnlyList<StudioAttachment> attachments = studioHdr.Attachments ?? [];

        for (int index = 0; index < attachments.Count; index++)
        {
            if (string.Equals(attachments[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>`Studio_BoneIndexByName` (bone_setup.cpp): the bone of that name, case ignored, or -1.</summary>
    /// <param name="model">The model, or null.</param>
    /// <param name="name">The bone's name.</param>
    /// <returns>Its index, or -1.</returns>
    protected static int BoneIndexByName(PropModels.SkinnedModel? model, string name)
    {
        IReadOnlyList<StudioBone> bones = model?.Bones ?? [];

        for (int index = 0; index < bones.Count; index++)
        {
            if (string.Equals(bones[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

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
    public void ClearMergeMDLs()
    {
        // The faces kept for them go with them (B725 review 1), or a panel re-dressed each frame keeps one per old merge.
        foreach (VguiMdl merge in _merges)
        {
            _faces.Remove(merge);
        }

        _merges.Clear();
    }

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

        AnimatingEntity root = PoseRoot(rootPath, rootModel);

        // **The root's eyes look at the panel's camera** (B725): `m_bLookAtCamera` is true from the constructor
        // (`mdlpanel.cpp:82`). Its one setter, `SetLookAtCamera` (:135), is called in `game/client/tf` only by the
        // Mann Co. collection-crafting menu's `ReturnModel` (`collection_crafting_panel.cpp:113`), a menu panel this viewer
        // does not build, and otherwise only by the tools' model picker (`mdlpicker.cpp:321`); `CMDLPanel` reads no `.res`
        // key for it. So every panel here keeps it, and `OnPaint3D` sets a world-space view target at the
        // camera's position (:445-454); a CMDL is no C_BaseFlex, so nothing clamps it. `CMDL::Draw` takes a world-space target
        // as it stands (see CmdlFace).
        float[]? rootWeights = RootFlexWeights(rootPath, rootFrames);
        StudioFace? rootFace = CmdlFace(
            _root, rootFrames, root.Bones, (renderContext.Eye.X, renderContext.Eye.Y, renderContext.Eye.Z), rootWeights);

        ModelInstance rootDrawn = Drawn(rootPath, rootFrames, _root, Skinned(rootModel.Bones, root.Bones), rootWeights) with { Face = rootFace };

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

            // A merge's CMDL keeps its constructor's view target, (0, 0, 0) in model space, carried by its identity
            // `m_MDLToWorld` (`SetMergeMDL`, mdlpanel.cpp:853) to the world origin; its flex controls stay zero.
            float[] mergeWeights = ZeroControlWeights(mergePath, mergeFrames);
            StudioFace? mergeFace = CmdlFace(merge, mergeFrames, merged.Bones, (0f, 0f, 0f), mergeWeights);

            renderContext.Models.Add(
                Drawn(mergePath, mergeFrames, merge, Skinned(mergeModel.Bones, merged.Bones), mergeWeights) with { Face = mergeFace });

            RenderingMergedModel(renderContext, mergeFrames, mergePath, merged.Bones);
        }

        RenderingRootModel(renderContext, rootFrames, rootPath, root.Bones);

        PostPaint3D(renderContext);
    }

    /// <summary>
    /// <c>CMDL::SetupBonesWithBoneMerge</c> onto an already-drawn merge model, each bone then scaled
    /// (<c>MatrixScaleBy</c>), and <c>CMDL::Draw</c> — a second-level merge, as
    /// <c>CTFPlayerModelPanel::RenderStatTrack</c> (tf_playermodelpanel.cpp:1539-1581) draws a StatTrak module on the weapon.
    /// </summary>
    /// <param name="renderContext">What is drawn this frame.</param>
    /// <param name="mdl">The model to draw.</param>
    /// <param name="parentPath">The drawn merge model it bone-merges onto.</param>
    /// <param name="scale">The factor every bone matrix's axes are scaled by.</param>
    /// <returns>Whether it was drawn.</returns>
    protected bool DrawBoneMergedOnto(VguiRenderContext renderContext, VguiMdl mdl, string parentPath, float scale)
    {
        ArgumentNullException.ThrowIfNull(renderContext);
        ArgumentNullException.ThrowIfNull(mdl);

        if (mdl.Path is not { } path || MdlCache.FindMdl(path) is not { Skinned: { } model } frames
            || !_entities.TryGetValue(parentPath, out AnimatingEntity? parent))
        {
            return false;
        }

        AnimatingEntity merged = EntityFor(path, model);

        if (merged.Pose is SkeletonPose pose)
        {
            pose.Sequence = mdl.Sequence;
        }

        merged.Follows = parent;
        merged.SetupBones(FullBoneMask, CycleTime);

        float[][] skinned = new float[merged.Bones.Count][];

        for (int bone = 0; bone < skinned.Length; bone++)
        {
            // `MatrixScaleBy( flScale, matrix )`: the three axis columns, not the origin.
            float[] boneToWorld = merged.Bones.Bone(bone).ToArray();

            for (int row = 0; row < 3; row++)
            {
                boneToWorld[(row * 4) + 0] *= scale;
                boneToWorld[(row * 4) + 1] *= scale;
                boneToWorld[(row * 4) + 2] *= scale;
            }

            skinned[bone] = new float[12];
            StudioBones.Concatenate(boneToWorld, model.Bones[bone].PoseToBone.Span, skinned[bone]);
        }

        // A CMDL draw like the others, so its mesh flexes at its zero controls (B779).
        renderContext.Models.Add(Drawn(path, frames, mdl, skinned, ZeroControlWeights(path, frames)));

        return true;
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
    /// <returns>The cycle, which each animation turns into its own frame (B742).</returns>
    public static float CycleAt(
        PropModels.SkinnedModel model, int sequence, IReadOnlyList<float> poseValues, double cycleTime)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(poseValues);

        float raw = (float)(cycleTime * model.BlendedCyclesPerSecond(sequence, poseValues));

        return model.Realtime(sequence)
            ? StudioSequences.ClampCycle(raw, loops: true)
            : StudioSequences.ClampCycle(raw, model.Loops(sequence));
    }

    /// <summary>
    /// The studio render context's one view target, shared with the world's models through the cache
    /// (<see cref="IMdlCache.ViewTarget"/>), or this panel's own where the cache has none (B725 5d).
    /// </summary>
    protected internal StudioViewTarget SharedViewTarget => MdlCache.ViewTarget ?? _ownViewTarget;

    /// <summary>How many CMDL faces this panel keeps, for the test that clearing the merges drops theirs.</summary>
    internal int KeptFaces => _faces.Count;

    private readonly StudioViewTarget _ownViewTarget = new();

    /// <summary>Each drawn CMDL's face values, kept and rewritten each paint: the HUD paints, then the frame draws it (B725).</summary>
    private readonly Dictionary<VguiMdl, StudioFace> _faces = [];

    /// <summary>Each model's descriptor weights at zero flex controls, by path, computed once.</summary>
    private readonly Dictionary<string, float[]> _zeroControlWeights = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What <c>CMDL::Draw</c> hands the studio render for one model's eyes and mouth (B725 5f, 5g), or null for a model with
    /// neither.
    /// </summary>
    /// <param name="mdl">The CMDL.</param>
    /// <param name="frames">Its model.</param>
    /// <param name="bones">Its bone-to-world.</param>
    /// <param name="target">The world-space view target the draw sets.</param>
    /// <param name="weights">The descriptor weights the draw's flex rules give, or null where they are not modelled (no
    /// mouth is set up then).</param>
    /// <returns>The face, written into this CMDL's own.</returns>
    /// <remarks>
    /// **Read** from TF2's x64 <c>client.dll</c>, where tier3's <c>CMDL</c> is linked (disassembled): <c>CMDL::CMDL</c>
    /// (<c>0x180936e20</c>) zeroes <c>m_pFlexControls</c> (0x600 bytes at +0x20), <c>m_vecViewTarget</c> (+0x620) and
    /// <c>m_bWorldSpaceViewTarget</c> (+0x62c). <c>CMDL::Draw</c> (<c>0x180936f70</c>) hands <c>SetEyeViewTarget</c> the target
    /// as it stands when world-space, else <c>VectorTransform( m_vecViewTarget, rootToWorld )</c> (<c>0x1809370b8-0x180937108</c>),
    /// unclamped; then, for a model with flex descriptors, <c>LockFlexWeights</c> and <c>RunFlexRules( m_pFlexControls, … )</c>
    /// (<c>0x180937138-0x1809371a7</c>) — the descriptor weights the mesh draw's <c>R_MouthSetupVertexShader</c> reads.
    /// </remarks>
    private StudioFace? CmdlFace(
        VguiMdl mdl, PropModels.ModelFrames frames, Animation.Animating.BoneAccessor bones, (float X, float Y, float Z) target, float[]? weights)
    {
        // Every CMDL draw sets the render context's one target first, eyes or none (`0x1809370f4-0x180937108`, B725 5d).
        SharedViewTarget.Target = target;

        bool mouthed = frames is { HasTeeth: true, Mouths.Count: > 0 } && weights is not null;

        if (frames.Eyeballs.Count == 0 && !mouthed)
        {
            return null;
        }

        if (!_faces.TryGetValue(mdl, out StudioFace? face) || face.Eyes.Length != frames.Eyeballs.Count)
        {
            face = new StudioFace(new EyeShading[frames.Eyeballs.Count]);
            _faces[mdl] = face;
        }

        StudioEyes.FillEyes(face, frames, bones, target);
        face.Mouth = mouthed && frames.Mouths[0].Bone is >= 0 and var mouthBone && mouthBone < bones.Count
            ? StudioEyes.Mouth(frames.Mouths[0], bones.Bone(mouthBone), weights)
            : null;

        return face;
    }

    /// <summary>
    /// The root CMDL's descriptor weights as its draw computes them from the flex controls <c>SetupFlexWeights</c> leaves
    /// (<c>mdlpanel.cpp:459</c>): CMDLPanel's own is empty (<c>mdlpanel.h:101</c>), so the controls are the constructor's
    /// zeros. Null where a panel's override is not modelled.
    /// </summary>
    /// <remarks>
    /// **Zero only for a model whose controllers are linked.** FETCH1 reads <c>src[ localToGlobal ]</c> (<c>client.dll</c>
    /// <c>0x180317816</c>), and <c>localToGlobal</c> lives in the shared model data, −1 until a C_BaseFlex (or
    /// CTFPlayerModelPanel) links it (<c>c_baseflex.cpp:1132-1142</c>). Unlinked, a rule reads <c>m_pFlexControls[ −1 ]</c>,
    /// the float before the array: <c>m_flTime</c> (+0x1c; the array is +0x20, <c>CMDL::CMDL</c> <c>0x180936e20</c>). Whether
    /// a model was linked depends on what the session drew before (B725 5f); taken as linked, the common case for a model a
    /// player also wears.
    /// </remarks>
    /// <param name="path">The root's model path.</param>
    /// <param name="frames">The root's model.</param>
    /// <returns>One weight per descriptor, or null.</returns>
    protected virtual float[]? RootFlexWeights(string path, PropModels.ModelFrames frames)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(frames);

        return ZeroControlWeights(path, frames);
    }

    /// <summary><c>RunFlexRules</c> over all-zero controls (<c>CMDL::CMDL</c>'s), kept per model path.</summary>
    private float[] ZeroControlWeights(string path, PropModels.ModelFrames frames)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(frames);

        if (!_zeroControlWeights.TryGetValue(path, out float[]? weights))
        {
            weights = FaceFlex.Rules(frames.Flex, new float[frames.Flex.Controllers.Count]);
            _zeroControlWeights[path] = weights;
        }

        return weights;
    }

    /// <summary>One model as <c>CMDL::Draw</c> draws it: its skin row from the model's own table
    /// (<c>g_skinref[m_nSkin]</c>, an out-of-range family falling back to zero as the engine does) and its body number
    /// over its body parts, and its mesh flexed by the descriptor weights its <c>RunFlexRules</c> wrote (B779).</summary>
    private ModelInstance Drawn(string path, PropModels.ModelFrames frames, VguiMdl mdl, float[][] bones, float[]? weights)
    {
        IReadOnlyDictionary<int, int>? skinSwap = null;

        if (frames.SkinSwaps is { Count: > 0 } swaps)
        {
            skinSwap = swaps[mdl.Skin >= 0 && mdl.Skin < swaps.Count ? mdl.Skin : 0];
        }

        (float[]? flex, FlexSlots? flexRange) = mdl.MeshFlex.For(MdlCache, path, frames, weights);

        return new ModelInstance(
            path,
            Identity4x4,
            Ambient,
            Sun,
            Bones: bones,
            SkinSwap: skinSwap,
            BodyParts: frames.BodyParts,
            Body: mdl.Body,
            MaterialOverride: mdl.MaterialOverride,
            Paint: mdl.Paint,
            Locals: Locals,
            WeaponSkin: mdl.WeaponSkin,
            Flex: flex,
            FlexRange: flexRange);
    }

    /// <summary><c>AngleMatrix( angRot, vecPos, matrix )</c>: a row of <c>(forward[i], left[i], up[i], origin[i])</c>
    /// per axis — <c>matrix3x4_t</c>'s own layout, which <c>StudioBones.Concatenate</c> already assumes.</summary>
    internal static float[] AngleMatrix3x4((float X, float Y, float Z) angles, (float X, float Y, float Z) origin)
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

        AnimatingEntity created = new(model.NewSkeleton(), _clock);

        _entities[modelPath] = created;

        return created;
    }

    /// <summary><see cref="BoneSkinning.Fill"/>, unbuffered: a model panel poses a handful of models.</summary>
    internal static float[][] Skinned(IReadOnlyList<StudioBone> bones, BoneAccessor accessor)
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
