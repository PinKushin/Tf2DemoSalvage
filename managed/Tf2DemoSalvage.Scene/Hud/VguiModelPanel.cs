using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CModelPanelModelAnimation` (basemodelpanel.h:35): one named animation of a <c>model</c> block.</summary>
/// <param name="Name">`m_pszName`.</param>
/// <param name="Sequence">`m_pszSequence`.</param>
/// <param name="Activity">`m_pszActivity`.</param>
/// <param name="PoseParameters">`m_pPoseParameters`: name and value, in order, or null.</param>
/// <param name="Default">`m_bDefault`.</param>
public sealed record VguiModelPanelAnimation(
    string? Name, string? Sequence, string? Activity, IReadOnlyList<(string Name, float Value)>? PoseParameters, bool Default);

/// <summary>`CModelPanelModelInfo` (basemodelpanel.h:111): what a <c>model</c> block says to draw.</summary>
public sealed class VguiModelPanelModelInfo
{
    /// <summary>`m_pszModelName`.</summary>
    public string? ModelName { get; set; }

    /// <summary>`m_pszModelName_HWM`: the hardware-morph model, used only when `UseHWMorphModels()`.</summary>
    public string? ModelNameHwm { get; set; }

    /// <summary>`m_nSkin`: -1 leaves the model's own.</summary>
    public int Skin { get; set; } = -1;

    /// <summary>`m_vecAbsAngles`.</summary>
    public (float X, float Y, float Z) AbsAngles { get; set; }

    /// <summary>`m_vecOriginOffset`.</summary>
    public (float X, float Y, float Z) OriginOffset { get; set; }

    /// <summary>`m_vecFramedOriginOffset`.</summary>
    public (float X, float Y, float Z) FramedOriginOffset { get; set; }

    /// <summary>`m_vecViewportOffset`.</summary>
    public (float X, float Y) ViewportOffset { get; set; }

    /// <summary>`m_pszVCD`.</summary>
    public string? Vcd { get; set; }

    /// <summary>`m_bUseSpotlight`.</summary>
    public bool UseSpotlight { get; set; }

    /// <summary>`m_mapBodygroupValues`: bodygroup index to value.</summary>
    public SortedDictionary<int, int> BodygroupValues { get; } = [];

    /// <summary>`m_Animations`.</summary>
    public Collection<VguiModelPanelAnimation> Animations { get; } = [];

    /// <summary>`m_AttachedModelsInfo`: path and skin.</summary>
    public Collection<(string? ModelName, int Skin)> AttachedModels { get; } = [];
}

/// <summary>
/// `CModelPanel` (game/client/game_controls/basemodelpanel.cpp): an `EditablePanel` that draws one client-side model,
/// with bone-merged attachments, from a <c>.res</c> <c>model</c> block — the match doors and the round sign. Not a
/// `CBaseModelPanel`: no pottery-wheel camera, and its own lights (<c>Paint</c>, :542).
/// </summary>
/// <remarks>
/// **Not ported:** a <c>vcd</c> (`SetupVCD`, :282): a client-only `C_SceneEntity` playing a choreo scene on the model,
/// which needs a `.vcd`/`scenes.image` event player and flex weights this project has neither of. No HUD `CModelPanel`
/// block sets one.
/// </remarks>
public class VguiModelPanel : VguiEditablePanel
{
    /// <summary>`VIEW_NEARZ` (view_shared.h), `view.zNear` (:618).</summary>
    public const float NearZ = 7f;

    /// <summary>`view.zFar` (:619).</summary>
    public const float FarZ = 1000f;

    private const int FullBoneMask = StudioBoneFlags.UsedByAnything;

    private static readonly float[] Identity4x4 =
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    /// <summary>`static Vector white[6]` (:638): 0.4 on every face.</summary>
    private static readonly AmbientCube Grey = new(
        (0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f));

    private readonly IMdlCache _mdlCache;
    private readonly BoneFrameCounter _clock = new();
    private bool _panelDirty = true;
    private int _defaultAnimation;
    private PanelModel? _model;

    /// <summary>`CModelPanel( parent, name )` (:45).</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    /// <param name="mdlCache">The models, by path.</param>
    public VguiModelPanel(VguiPanel? parent, string? name, IMdlCache mdlCache)
        : base(parent, name) =>
        _mdlCache = mdlCache ?? throw new ArgumentNullException(nameof(mdlCache));

    /// <inheritdoc/>
    public override string ClassName => "CModelPanel";

    /// <summary>`m_nFOV`: 54 (:47) until the <c>.res</c> says <c>fov</c>.</summary>
    public int FieldOfView { get; set; } = 54;

    /// <summary>`m_bStartFramed`.</summary>
    public bool StartFramed { get; private set; }

    /// <summary>`m_bAllowOffscreen`.</summary>
    public bool AllowOffscreen { get; private set; }

    /// <summary>`m_pModelInfo`, or null before a <c>model</c> block.</summary>
    public VguiModelPanelModelInfo? ModelInfo { get; private set; }

    /// <summary>`m_hModel != NULL`.</summary>
    public bool HasModel => _model is not null;

    /// <summary>The model's `GetSequence()`, or -1 with no model.</summary>
    public int Sequence => _model?.Sequence ?? -1;

    /// <summary>The model's `GetCycle()`.</summary>
    public float Cycle => _model?.Cycle ?? 0f;

    /// <summary>The model's `m_nSkin`.</summary>
    public int ModelSkin => _model?.Skin ?? 0;

    /// <summary>The model's `m_nBody`.</summary>
    public int ModelBody => _model?.Body ?? 0;

    /// <summary>`gpGlobals->frametime`, set by whoever drives the frame.</summary>
    public float FrameTime { get; set; }

    /// <summary>The paths this panel draws, for precaching.</summary>
    /// <returns>The model, then each attached model.</returns>
    public IEnumerable<string> ModelsToPrecache()
    {
        if (ModelInfo is not { } info)
        {
            yield break;
        }

        if (info.ModelName is { Length: > 0 } model)
        {
            yield return model;
        }

        foreach ((string? attached, _) in info.AttachedModels)
        {
            if (attached is { Length: > 0 })
            {
                yield return attached;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>`CModelPanel::ApplySettings` (:77).</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);

        FieldOfView = Int(block, "fov", 54);
        StartFramed = Int(block, "start_framed", 0) != 0;
        AllowOffscreen = Int(block, "allow_offscreen", 0) != 0;

        foreach (KeyValuesTree data in block.Children)
        {
            if (string.Equals(data.Name, "model", StringComparison.OrdinalIgnoreCase))
            {
                ParseModelInfo(data);
            }
        }
    }

    /// <summary>`ParseModelInfo` (:113).</summary>
    /// <param name="data">The <c>model</c> block.</param>
    public void ParseModelInfo(KeyValuesTree data)
    {
        ArgumentNullException.ThrowIfNull(data);

        VguiModelPanelModelInfo info = new()
        {
            ModelName = data.Find("modelname")?.Value,
            ModelNameHwm = data.Find("modelname_hwm")?.Value,
            Skin = Int(data, "skin", -1),
            AbsAngles = (Float(data, "angles_x", 0f), Float(data, "angles_y", 0f), Float(data, "angles_z", 0f)),
            OriginOffset = (Float(data, "origin_x", 110f), Float(data, "origin_y", 5f), Float(data, "origin_z", 5f)),
            FramedOriginOffset = (Float(data, "frame_origin_x", 110f), Float(data, "frame_origin_y", 5f), Float(data, "frame_origin_z", 5f)),
            Vcd = data.Find("vcd")?.Value,
            UseSpotlight = Int(data, "spotlight", 0) == 1,
        };

        ModelInfo = info;

        foreach (KeyValuesTree entry in data.Children)
        {
            if (string.Equals(entry.Name, "animation", StringComparison.OrdinalIgnoreCase))
            {
                OnAddAnimation(entry);
            }
            else if (string.Equals(entry.Name, "attached_model", StringComparison.OrdinalIgnoreCase))
            {
                info.AttachedModels.Add((entry.Find("modelname")?.Value, Int(entry, "skin", -1)));
            }
        }

        _panelDirty = true;
    }

    /// <summary>`OnAddAnimation` (:163).</summary>
    /// <param name="data">The <c>animation</c> block.</param>
    public void OnAddAnimation(KeyValuesTree data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (ModelInfo is not { } info)
        {
            return;
        }

        List<(string Name, float Value)>? poses = null;

        foreach (KeyValuesTree entry in data.Children)
        {
            if (string.Equals(entry.Name, "pose_parameters", StringComparison.OrdinalIgnoreCase))
            {
                poses = [];

                foreach (KeyValuesTree pose in entry.Children)
                {
                    poses.Add((pose.Name, PanelLayout.Atof(pose.Value ?? "0")));
                }
            }
        }

        VguiModelPanelAnimation animation = new(
            data.Find("name")?.Value, data.Find("sequence")?.Value, data.Find("activity")?.Value, poses, Int(data, "default", 0) == 1);

        info.Animations.Add(animation);

        if (animation.Default)
        {
            _defaultAnimation = info.Animations.Count - 1;
        }
    }

    /// <summary>`CModelPanel::OnCommand` (:98): "animation &lt;name&gt;" updates the model and plays it.</summary>
    /// <param name="command">The command.</param>
    public override void OnCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.StartsWith("animation", StringComparison.OrdinalIgnoreCase))
        {
            UpdateModel();

            // `command + 9 + 1`: past "animation" and the separator.
            SetSequence(command.Length > 10 ? command[10..] : string.Empty);
            return;
        }

        base.OnCommand(command);
    }

    /// <summary>`SetPanelDirty`.</summary>
    public void SetPanelDirty() => _panelDirty = true;

    /// <summary>`SetSkin` (:747).</summary>
    /// <param name="skin">The skin.</param>
    public void SetSkin(int skin)
    {
        if (ModelInfo is { } info)
        {
            info.Skin = skin;
            _panelDirty = true;
        }
    }

    /// <summary>`SetBodyGroup` (:367): recorded by index, for the next `SetupModel`.</summary>
    /// <param name="bodyGroupName">The bodygroup's name.</param>
    /// <param name="group">Its value.</param>
    public void SetBodyGroup(string bodyGroupName, int group)
    {
        if (ModelInfo is not { } info || _model is not { } model)
        {
            return;
        }

        int index = _mdlCache.FindBodygroup(model.Path, bodyGroupName);

        if (index == -1)
        {
            return;
        }

        info.BodygroupValues[index] = group;
        _panelDirty = true;
    }

    /// <summary>`SetSequence( pszName )` (:708): a named animation's sequence, else the name itself.</summary>
    /// <param name="name">The animation or sequence.</param>
    /// <returns>Whether the model has it.</returns>
    public bool SetSequence(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (ModelInfo is not { } info)
        {
            return false;
        }

        // `FindAnimByName` (:692), `FStrEq`: the friendly name first, else the name is the sequence.
        string? sequenceName = name;

        foreach (VguiModelPanelAnimation animation in info.Animations)
        {
            if (string.Equals(animation.Name, name, StringComparison.Ordinal))
            {
                sequenceName = animation.Sequence;
                break;
            }
        }

        if (_model is not { } model || sequenceName is null)
        {
            return false;
        }

        int sequence = model.Skinned.SequenceByLabel(sequenceName);

        if (sequence == -1)
        {
            return false;
        }

        model.Sequence = sequence;
        model.Cycle = 0f;
        return true;
    }

    /// <summary>`UpdateModel` (:520): a dirty panel sets its model up again.</summary>
    public void UpdateModel()
    {
        if (_panelDirty)
        {
            SetupModel();
            _panelDirty = false;
        }
    }

    /// <summary>`SetupModel` (:387): a fresh model — skin, bodygroups, the default animation — and its attachments.</summary>
    private void SetupModel()
    {
        if (ModelInfo is not { } info)
        {
            return;
        }

        _model = null;

        if (DrawnModelName is not { Length: > 0 } path || _mdlCache.FindMdl(path) is not { Skinned: { } skinned } frames)
        {
            return;
        }

        PanelModel model = new(path, frames, skinned, NewEntity(skinned));

        if (info.Skin >= 0)
        {
            model.Skin = info.Skin;
        }

        foreach ((int group, int value) in info.BodygroupValues)
        {
            model.Body = _mdlCache.SetBodygroup(path, group, value, model.Body);
        }

        float curTime = HudViewport.Of(this)?.State.CurTime ?? 0f;

        if (info.Animations.Count > 0 && _defaultAnimation >= 0 && _defaultAnimation < info.Animations.Count)
        {
            VguiModelPanelAnimation animation = info.Animations[_defaultAnimation];
            int sequence = -1;

            if (animation.Activity is { Length: > 0 } activity)
            {
                sequence = skinned.ForActivity(activity);
            }
            else if (animation.Sequence is { Length: > 0 } label)
            {
                sequence = skinned.SequenceByLabel(label);
            }

            if (sequence != -1)
            {
                model.Sequence = sequence;
                model.Cycle = 0f;

                foreach ((string name, float value) in animation.PoseParameters ?? [])
                {
                    model.SetPoseParameter(name, value);
                }

                model.AnimTime = curTime;
            }
        }

        foreach ((string? attachedPath, int skin) in info.AttachedModels)
        {
            if (attachedPath is null || _mdlCache.FindMdl(attachedPath) is not { Skinned: { } attachedModel } attachedFrames)
            {
                continue;
            }

            model.Attached.Add(new PanelModel(attachedPath, attachedFrames, attachedModel, NewEntity(attachedModel)) { Skin = skin >= 0 ? skin : 0 });
        }

        _model = model;

        // `CalculateFrameDistance` (:905): `m_flFrameDistance = 0`, then only a started-framed panel frames its model.
        if (StartFramed)
        {
            CalculateFrameDistanceInternal(info, frames.HeaderBounds);
        }
    }

    /// <summary>
    /// `GetModelName` (:344): the HWM model when `UseHWMorphModels()` and it loads, else the plain one. `UseHWMorphModels`
    /// returns false unconditionally (baseplayer_shared.cpp:94-105, its convar branch commented out), so it is the plain one.
    /// </summary>
    public string? DrawnModelName => ModelInfo?.ModelName;

    /// <summary>
    /// `CalculateFrameDistanceInternal` (:796): the origin offset and viewport offset that fit the model's bounds, rotated
    /// by the panel's angles, into the field of view.
    /// </summary>
    /// <remarks>
    /// **Interpolated:** `modelinfo->GetModelRenderBounds` is engine.dll, outside the SDK; taken as the studio header's box —
    /// the clipping box when authored, else the hull — the same box `C_BaseAnimating::GetRenderBounds` starts from
    /// (c_baseanimating.cpp:4548-4559) before it adds a sequence's.
    /// </remarks>
    private void CalculateFrameDistanceInternal(VguiModelPanelModelInfo info, StudioBox bounds)
    {
        (float X, float Y, float Z) center = ((bounds.MaxX + bounds.MinX) * 0.5f, (bounds.MaxY + bounds.MinY) * 0.5f, (bounds.MaxZ + bounds.MinZ) * 0.5f);
        (float X, float Y, float Z) min = (bounds.MinX - center.X, bounds.MinY - center.Y, bounds.MinZ - center.Z);
        (float X, float Y, float Z) max = (bounds.MaxX - center.X, bounds.MaxY - center.Y, bounds.MaxZ - center.Z);

        (float X, float Y, float Z)[] corners =
        [
            (max.X, max.Y, max.Z), (min.X, max.Y, max.Z), (max.X, min.Y, max.Z), (min.X, min.Y, max.Z),
            (max.X, max.Y, min.Z), (min.X, max.Y, min.Z), (max.X, min.Y, min.Z), (min.X, min.Y, min.Z),
        ];

        // `AngleMatrix( angPanelAngles, matRotation )` and `VectorTransform` with it.
        float[] rotation = VguiMdlPanel.AngleMatrix3x4(info.AbsAngles, (0f, 0f, 0f));

        (float X, float Y, float Z) Rotate((float X, float Y, float Z) v) => (
            (v.X * rotation[0]) + (v.Y * rotation[1]) + (v.Z * rotation[2]),
            (v.X * rotation[4]) + (v.Y * rotation[5]) + (v.Z * rotation[6]),
            (v.X * rotation[8]) + (v.Y * rotation[9]) + (v.Z * rotation[10]));

        (float X, float Y, float Z)[] xformed = Array.ConvertAll(corners, corner => Rotate(corner));

        // `VectorTransform( -vecTranslateCenter, … )`, and `vecTranslateCenter = -vecCenter`.
        (float X, float Y, float Z) xformCenter = Rotate(center);

        float width = Wide;
        float height = Tall;
        float tanFovX = MathF.Tan(FieldOfView * 0.5f * (MathF.PI / 180f));

        // `CalcFovY( ( m_nFOV * 0.5f ), flW/flH )`: the half angle, as Valve passes it.
        float tanFovY = MathF.Tan(VguiBaseModelPanel.CalcFovY(FieldOfView * 0.5f, height > 0f ? width / height : 1f) * (MathF.PI / 180f));
        float distance = 0f;

        foreach ((float X, float Y, float Z) point in xformed)
        {
            // `fabs( z / tanY - x )` and `fabs( y / tanX - x )`: the whole difference, unlike CBaseModelPanel's.
            float distanceZ = MathF.Abs((point.Z / tanFovY) - point.X);
            float distanceY = MathF.Abs((point.Y / tanFovX) - point.X);

            distance = MathF.Max(distance, MathF.Max(distanceZ, distanceY));
        }

        // "Scale the object down by 10%", then "Add the framing offset".
        distance *= 1.10f;
        xformCenter = (xformCenter.X + info.FramedOriginOffset.X, xformCenter.Y + info.FramedOriginOffset.Y, xformCenter.Z + info.FramedOriginOffset.Z);
        info.OriginOffset = (distance - xformCenter.X, -xformCenter.Y, -xformCenter.Z);

        (float X, float Y) screenMin = (99999f, 99999f);
        (float X, float Y) screenMax = (-99999f, -99999f);

        foreach ((float X, float Y, float Z) point in xformed)
        {
            float cameraX = point.X + distance;
            float screenX = ((point.Y / (tanFovX * cameraX) * 0.5f) + 0.5f) * width;
            float screenY = ((point.Z / (tanFovY * cameraX) * 0.5f) + 0.5f) * height;

            screenMin = (MathF.Min(screenMin.X, screenX), MathF.Min(screenMin.Y, screenY));
            screenMax = (MathF.Max(screenMax.X, screenX), MathF.Max(screenMax.Y, screenY));
        }

        screenMin = (Math.Clamp(screenMin.X, 0f, width), Math.Clamp(screenMin.Y, 0f, height));
        screenMax = (Math.Clamp(screenMax.X, 0f, width), Math.Clamp(screenMax.Y, 0f, height));

        // "Offset the view port based on the calculated model 2D center and the center of the viewport."
        (float X, float Y) screenCenter = ((screenMax.X + screenMin.X) * 0.5f, (screenMax.Y + screenMin.Y) * 0.5f);

        info.ViewportOffset = (-((width * 0.5f) - screenCenter.X), -((height * 0.5f) - screenCenter.Y));
    }

    /// <summary>`CModelPanel::Paint` (:542).</summary>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        base.Paint(surface, context);

        HudViewport? viewport = HudViewport.Of(this);

        if (viewport is not { State.HasLocalPlayer: true } || ModelInfo is not { } info)
        {
            return;
        }

        UpdateModel();

        if (_model is not { } model || Tall <= 0)
        {
            return;
        }

        // "prevent x from being pushed off the left side of the screen" (:564).
        int screenX = ScreenX();
        int left = !AllowOffscreen && screenX < 0 ? -screenX : 0;
        float widthRatio = ((float)Wide / Tall) / (4f / 3f);
        (float X, float Y, float Z) extra = (0f, 0f, 0f);

        // "HACK! HACK! to get our player models to appear the way they do in 4/3" (:578).
        if (model.Path.Contains("models/player/", StringComparison.Ordinal))
        {
            if (widthRatio > 1.05f)
            {
                extra = (-60f, 0f, 0f);
            }
            else if (widthRatio < 0.95f)
            {
                extra = (15f, 0f, 0f);
            }
        }

        (float X, float Y, float Z) origin = (info.OriginOffset.X + extra.X, info.OriginOffset.Y + extra.Y, info.OriginOffset.Z + extra.Z);

        if (model.Sequence != -1)
        {
            model.FrameAdvance(FrameTime, viewport.State.CurTime);
        }

        FreeCamera camera = new()
        {
            Origin = (0f, 0f, 0f),
            Angles = (0f, 0f, 0f),
            FieldOfView = ScaleFovByWidthRatio(FieldOfView, widthRatio),
            NearZ = NearZ,
            FarZ = FarZ,
            Aspect = (float)Wide / Tall,
        };

        IReadOnlyList<LocalLight> locals = [];

        if (info.UseSpotlight)
        {
            // `LightDesc_t spotLight( vec3_origin + Vector( 0, 0, 200 ), Vector( 1, 1, 1 ), GetAbsOrigin() + Vector( 0, 0,
            // ( vecMaxs.z - vecMins.z ) * 0.75 ), 0.035, 0.873 )` (:655), through `InitSpot` (lightdesc.h:148).
            StudioBox bounds = model.Frames.RenderBoundsFor(model.Sequence);
            (float X, float Y, float Z) target = (origin.X, origin.Y, origin.Z + ((bounds.MaxZ - bounds.MinZ) * 0.75f));
            (float X, float Y, float Z) direction = (target.X, target.Y, target.Z - 200f);
            float length = MathF.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

            direction = length > 0f ? (direction.X / length, direction.Y / length, direction.Z / length) : direction;
            locals = [new LocalLight(0f, 0f, 200f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, direction, MathF.Cos(0.035f), MathF.Cos(0.873f), 5f, Spot: true)];
        }

        List<ModelInstance> drawn = [model.Draw(VguiMdlPanel.AngleMatrix3x4(info.AbsAngles, origin), null, locals)];

        foreach (PanelModel attached in model.Attached)
        {
            drawn.Add(attached.Draw(null, model.Entity, locals));
        }

        surface.Paint3D(
            left + (int)info.ViewportOffset.X, (int)info.ViewportOffset.Y, left + (int)info.ViewportOffset.X + Wide, (int)info.ViewportOffset.Y + Tall,
            camera.ToMatrix(), drawn);
    }

    /// <summary>`ScaleFOVByWidthRatio` (view.cpp): the 4:3 horizontal field of view widened for this aspect.</summary>
    /// <param name="fovDegrees">The 4:3 field of view.</param>
    /// <param name="ratio">The width ratio against 4:3.</param>
    /// <returns>Degrees.</returns>
    public static float ScaleFovByWidthRatio(float fovDegrees, float ratio)
    {
        float halfAngleRadians = fovDegrees * (0.5f * MathF.PI / 180f);
        float t = MathF.Tan(halfAngleRadians) * ratio;

        return 180f / MathF.PI * MathF.Atan(t) * 2f;
    }

    private int ScreenX()
    {
        int x = 0;

        for (VguiPanel? panel = this; panel is not null; panel = panel.Parent)
        {
            x += panel.X;
        }

        return x;
    }

    private AnimatingEntity NewEntity(PropModels.SkinnedModel model) => new(new SkeletonPose(model.Bones, model.Locals), _clock);

    private static float Float(KeyValuesTree tree, string name, float fallback) =>
        tree.Find(name)?.Value is { } value && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;

    /// <summary>`CModelPanelModel` or an attached `C_BaseAnimating`: the client-side entity a panel draws.</summary>
    private sealed class PanelModel(string path, PropModels.ModelFrames frames, PropModels.SkinnedModel skinned, AnimatingEntity entity)
    {
        private float[]? _poseValues;

        public string Path { get; } = path;

        public PropModels.ModelFrames Frames { get; } = frames;

        public PropModels.SkinnedModel Skinned { get; } = skinned;

        public AnimatingEntity Entity { get; } = entity;

        public List<PanelModel> Attached { get; } = [];

        public int Sequence { get; set; }

        public float Cycle { get; set; }

        public float AnimTime { get; set; }

        public int Skin { get; set; }

        public int Body { get; set; }

        private float[] PoseValues => _poseValues ??= DefaultPoses(Skinned);

        /// <summary>`SetPoseParameter( name, value )`: the first parameter of that name.</summary>
        public void SetPoseParameter(string name, float value)
        {
            for (int index = 0; index < Skinned.PoseParameters.Count; index++)
            {
                if (string.Equals(Skinned.PoseParameters[index].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    PoseValues[index] = StudioBlendGrid.Normalize(Skinned.PoseParameters[index], value);
                    return;
                }
            }
        }

        /// <summary>`C_BaseAnimating::FrameAdvance( flInterval )` (c_baseanimating.cpp:5464), playback rate 1.</summary>
        public void FrameAdvance(float interval, float curTime)
        {
            if (interval == 0f)
            {
                interval = curTime - AnimTime;

                if (interval <= 0.001f)
                {
                    return;
                }
            }

            if (AnimTime == 0f)
            {
                interval = 0f;
            }

            float cycle = Cycle + (interval * Skinned.BlendedCyclesPerSecond(Sequence, PoseValues));

            AnimTime = curTime;

            if (cycle < 0f || cycle >= 1f)
            {
                if (Skinned.Loops(Sequence))
                {
                    cycle -= (int)cycle;
                }
                else
                {
                    cycle = cycle < 0f ? 0f : 1f;
                }
            }

            Cycle = cycle;
        }

        /// <summary>`DrawModel( STUDIO_RENDER )`: posed at its cycle, or bone-merged onto <paramref name="follows"/>.</summary>
        public ModelInstance Draw(float[]? modelToWorld, AnimatingEntity? follows, IReadOnlyList<LocalLight> locals)
        {
            if (Entity.Pose is SkeletonPose pose)
            {
                pose.Sequence = Sequence;
                pose.PoseValues = PoseValues;

                if (modelToWorld is not null)
                {
                    pose.EntityTransform = modelToWorld;
                }

                (pose.Frame, pose.FrameFraction) = StudioSequences.FrameAt(Cycle, Skinned.Frames(Sequence), Skinned.Loops(Sequence));
            }

            Entity.Follows = follows;
            Entity.SetupBones(FullBoneMask, AnimTime);

            IReadOnlyDictionary<int, int>? skinSwap = null;

            if (Frames.SkinSwaps is { Count: > 0 } swaps)
            {
                skinSwap = swaps[Skin >= 0 && Skin < swaps.Count ? Skin : 0];
            }

            return new ModelInstance(
                Path,
                Identity4x4,
                Grey,
                null,
                Bones: VguiMdlPanel.Skinned(Skinned.Bones, Entity.Bones),
                SkinSwap: skinSwap,
                BodyParts: Frames.BodyParts,
                Body: Body,
                Locals: locals);
        }

        private static float[] DefaultPoses(PropModels.SkinnedModel model)
        {
            float[] values = new float[model.PoseParameters.Count];

            for (int index = 0; index < values.Length; index++)
            {
                values[index] = StudioBlendGrid.Normalize(model.PoseParameters[index], 0f);
            }

            return values;
        }
    }
}
