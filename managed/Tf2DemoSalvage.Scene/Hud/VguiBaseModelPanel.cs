using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// One animation a <see cref="VguiBaseModelPanel"/>'s <c>.res</c> file names — <c>BMPResAnimData_t</c>
/// (<c>basemodel_panel.h:17</c>), the <c>animation</c> block under <c>model</c>.
/// </summary>
/// <remarks>
/// <c>CBaseModelPanel::ParseModelAnimInfo</c> (<c>basemodel_panel.cpp:122</c>). Pose parameters
/// (<c>pose_parameters</c>, :138) are not read here: no stock HUD model block sets one.
/// </remarks>
/// <param name="Name">The animation's own name, by which it is picked later.</param>
/// <param name="Sequence">A sequence label, or null to pick by activity instead.</param>
/// <param name="Activity">An activity name, or null to pick by label instead.</param>
/// <param name="Default">Whether this is the one played until something else is picked.</param>
public readonly record struct ModelPanelAnimation(string Name, string? Sequence, string? Activity, bool Default);

/// <summary>One <c>attached_model</c> block under a <see cref="VguiBaseModelPanel"/>'s <c>model</c> block —
/// <c>BMPResAttachData_t</c> (<c>basemodel_panel.h:62</c>).</summary>
/// <remarks>
/// <c>CBaseModelPanel::ParseModelAttachInfo</c> (<c>basemodel_panel.cpp:148-159</c>): a second model named beside
/// the root's own, with its own skin. Parsed here; drawing it is <c>CTFPlayerModelPanel</c>'s wearables/weapon
/// concern (step 4), not this step's.
/// </remarks>
/// <param name="ModelName">The attached model's path.</param>
/// <param name="Skin">Its skin, or −1 for its default (<c>GetInt( "skin", -1 )</c>, :158).</param>
public readonly record struct ModelPanelAttachment(string ModelName, int Skin);

/// <summary>
/// <c>CBaseModelPanel : CMDLPanel</c> (<c>game/client/game_controls/basemodel_panel.h:150</c>,
/// <c>basemodel_panel.cpp</c>): a <see cref="VguiMdlPanel"/> whose model, pose and camera come from the <c>.res</c>
/// file's <c>model</c> block rather than being set in code.
/// </summary>
public class VguiBaseModelPanel : VguiMdlPanel
{
    /// <summary><c>CBaseModelPanel( parent, name )</c> (<c>basemodel_panel.h:157</c>).</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    /// <param name="mdlCache">See <see cref="VguiMdlPanel"/>'s constructor.</param>
    public VguiBaseModelPanel(VguiPanel? parent, string? name, IMdlCache mdlCache)
        : base(parent, name, mdlCache)
    {
    }

    /// <summary><c>ACT_IDLE</c> (ai_activity.h): 1, which <c>SetMDL</c> passes to <c>SetSequence</c> as a SEQUENCE
    /// index (basemodel_panel.cpp:309).</summary>
    private const int ActIdle = 1;

    /// <summary><c>m_BMPResData.m_bUseSpotlight</c> (<c>basemodel_panel.h:95</c>, cpp:99) — stored and never read in
    /// the shipped SDK.</summary>
    public bool UseSpotlight { get; private set; }

    /// <summary><c>m_BMPResData.m_pszModelName</c> (cpp:91): stored, not loaded — <c>CBaseModelPanel</c> never calls
    /// <c>SetMDL</c> with it; a subclass that wants it does.</summary>
    public string? ResModelName { get; private set; }

    /// <summary><c>m_BMPResData.m_nSkin</c> (cpp:98): stored, like <see cref="ResModelName"/>.</summary>
    public int ResSkin { get; private set; } = -1;

    /// <summary><c>m_bStartFramed</c> (<c>basemodel_panel.h:238</c>): <c>start_framed</c>, default <c>"0"</c>.</summary>
    public bool StartFramed { get; private set; }

    /// <summary><c>m_bForcePos</c> (<c>basemodel_panel.h:220</c>) — <c>force_pos</c> in the <c>.res</c> <c>model</c>
    /// block (<c>basemodel_panel.cpp:90</c>). When set, <c>PerformLayout</c> (:381-387) resets the pivot and offset to
    /// the world origin and moves the MODEL to <see cref="ModelAngles"/>/<see cref="ModelOrigin"/> instead of leaving
    /// it at the identity <c>CMDLPanel</c> itself starts at (mdlpanel.cpp:75).</summary>
    public bool ForcePosition { get; set; }

    /// <summary><c>m_angPlayer</c> (<c>basemodel_panel.h:216</c>) — the model's own rotation, applied only when
    /// <see cref="ForcePosition"/> is set.</summary>
    public (float X, float Y, float Z) ModelAngles { get; set; }

    /// <summary><c>m_vecPlayerPos</c> (<c>basemodel_panel.h:217</c>) — the model's own position, under the same
    /// condition as <see cref="ModelAngles"/>.</summary>
    public (float X, float Y, float Z) ModelOrigin { get; set; }

    /// <summary><c>m_BMPResData.m_aAnimations</c> (<c>basemodel_panel.h:97</c>, <c>ParseModelAnimInfo</c> cpp:122).</summary>
    public IReadOnlyList<ModelPanelAnimation> Animations => _animations;

    /// <summary><c>m_BMPResData.m_aAttachModels</c> (<c>basemodel_panel.h:98</c>, <c>ParseModelAttachInfo</c> cpp:148).</summary>
    public IReadOnlyList<ModelPanelAttachment> Attachments => _attachments;

    private readonly List<ModelPanelAnimation> _animations = [];
    private readonly List<ModelPanelAttachment> _attachments = [];

    /// <summary><c>m_nActiveSequence</c> (<c>basemodel_panel.h:234</c>), or −1 for <c>ACT_INVALID</c>.</summary>
    private int _activeSequence = -1;

    /// <summary><c>m_flActiveSequenceDuration</c> (<c>basemodel_panel.h:235</c>) — one cycle's length in seconds.</summary>
    private float _activeSequenceDuration;

    /// <summary>The cycle time <see cref="_activeSequence"/> started at, so its expiry is measured from when it was
    /// played rather than from whenever <see cref="Tick"/> next happens to run.</summary>
    private double _activeSequenceStartedAt;

    /// <summary>
    /// <c>CBaseModelPanel::OnTick</c> runs before <c>CMDLPanel::OnTick</c> every tick (basemodel_panel.cpp:402:
    /// "Cycle stuff gets handled in mdlpanel::OnTick, so we want to fix up what our sequence is before it gets
    /// called"); this panel has no separate tick, so <see cref="Tick"/> runs here, immediately before the cycle it
    /// might reset is read.
    /// </summary>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        Tick();
        base.Paint(surface, context);
    }

    /// <summary>
    /// <c>CBaseModelPanel::SetMDL( handle, pProxyData )</c> (basemodel_panel.cpp:284-317): <c>SetSequence( ACT_IDLE )</c>,
    /// the base's <c>SetMDL</c>, <c>SetupModelDefaults()</c>, and a layout.
    /// </summary>
    /// <inheritdoc/>
    public override void SetMDL(string? modelName)
    {
        // "Clear our current sequence" (:308) — `ACT_IDLE`, an activity, passed where a sequence index goes.
        SetSequence(ActIdle);

        base.SetMDL(modelName);

        SetupModelDefaults();

        InvalidateLayout();
    }

    /// <summary>
    /// <c>CBaseModelPanel::PerformLayout</c> (basemodel_panel.cpp:345-398). With <see cref="ForcePosition"/>: camera
    /// pivot reset, offset zeroed, camera at the origin looking down +X, and the MODEL moved to
    /// <see cref="ModelAngles"/>/<see cref="ModelOrigin"/>. With <see cref="StartFramed"/>: <see cref="LookAtBounds"/>
    /// over the root's bounds.
    /// </summary>
    /// <remarks>
    /// **The `allow_manip` branch (:354-379) is not ported**: it builds a camera for mouse manipulation, and no mouse
    /// reaches a panel here; no stock HUD `.res` sets it.
    /// </remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        if (ForcePosition)
        {
            // `ResetCameraPivot(); SetCameraOffset( 0 ); SetCameraPositionAndAngles( vec3_origin, vec3_angle );`
            CameraPivotOrigin = (0f, 0f, 0f);
            CameraPivotAngles = (0f, 0f, 0f);
            CameraOffset = (0f, 0f, 0f);
            SetModelAnglesAndPosition(ModelAngles, ModelOrigin);
        }

        if (StartFramed)
        {
            ApplyStartFramed();
        }
    }

    /// <summary><c>CBaseModelPanel::SetModelAnglesAndPosition</c> (<c>basemodel_panel.h:163</c>, cpp:322-329): the
    /// base's, then cache the pair in <see cref="ModelAngles"/>/<see cref="ModelOrigin"/>.</summary>
    /// <inheritdoc/>
    public override void SetModelAnglesAndPosition((float X, float Y, float Z) angles, (float X, float Y, float Z) origin)
    {
        base.SetModelAnglesAndPosition(angles, origin);

        ModelOrigin = origin;
        ModelAngles = angles;
    }

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);

        // `CBaseModelPanel::ApplySettings` (basemodel_panel.cpp:56): `inResourceData->GetInt( "fov", flFOV )` —
        // GetInt, not GetFloat, so a fractional `fov` truncates toward zero (`atoi`) rather than rounding.
        if (block.Find("fov")?.Value is { } fov)
        {
            FieldOfView = PanelLayout.Atoi(fov);
        }

        // `CPanelAnimationVar( bool, m_bStartFramed, "start_framed", "0" )` (basemodel_panel.h:238) — a top-level
        // key, applied by the generic animation-var mechanism the base `Panel::ApplySettings` runs; read directly
        // here since this panel has no scheme-driven animation vars of its own to declare it through.
        StartFramed = block.Find("start_framed")?.Value == "1";

        // `for ( KeyValues *pData = inResourceData->GetFirstSubKey() ...` (:74): every sub-block named `model`, not
        // only the first — Valve's own loop keeps looking after a match.
        foreach (KeyValuesTree modelBlock in block.Children)
        {
            if (string.Equals(modelBlock.Name, "model", StringComparison.OrdinalIgnoreCase))
            {
                ParseModelResInfo(modelBlock);
            }
        }
    }

    /// <summary><c>ParseModelResInfo</c> (<c>basemodel_panel.h:207</c>, cpp:88).</summary>
    /// <param name="modelBlock">The <c>model</c> block.</param>
    public void ParseModelResInfo(KeyValuesTree modelBlock)
    {
        ArgumentNullException.ThrowIfNull(modelBlock);

        ResModelName = modelBlock.Find("modelname")?.Value;
        ResSkin = ResIntOrDefault(modelBlock, "skin", -1);
        UseSpotlight = ResIntOrDefault(modelBlock, "spotlight", 0) == 1;

        // `m_bForcePos` (basemodel_panel.cpp:90) — whether `PerformLayout` moves the MODEL to these angles/origin.
        ForcePosition = ResIntOrDefault(modelBlock, "force_pos", 0) == 1;

        ModelAngles = (
            ResFloatOrDefault(modelBlock, "angles_x", 0f),
            ResFloatOrDefault(modelBlock, "angles_y", 0f),
            ResFloatOrDefault(modelBlock, "angles_z", 0f));

        ModelOrigin = (
            ResFloatOrDefault(modelBlock, "origin_x", 110f),
            ResFloatOrDefault(modelBlock, "origin_y", 5f),
            ResFloatOrDefault(modelBlock, "origin_z", 5f));

        _animations.Clear();
        _attachments.Clear();

        foreach (KeyValuesTree block in modelBlock.Children)
        {
            if (string.Equals(block.Name, "animation", StringComparison.OrdinalIgnoreCase))
            {
                ParseModelAnimInfo(block);
            }
            else if (string.Equals(block.Name, "attached_model", StringComparison.OrdinalIgnoreCase))
            {
                ParseModelAttachInfo(block);
            }
        }

        // `SetupModelDefaults()` (:116), once every animation has been read.
        SetupModelDefaults();
    }

    /// <summary>
    /// <c>SetupModelDefaults</c> -> <c>SetupModelAnimDefaults</c> (basemodel_panel.cpp:164-188): <c>move_x</c> at 1 "so
    /// the run activity works", then the FIRST animation flagged default (<c>FindDefaultAnim</c>, :193).
    /// </summary>
    private void SetupModelDefaults()
    {
        SetPoseParameterByName("move_x", 1f);

        foreach (ModelPanelAnimation animation in _animations)
        {
            if (animation.Default)
            {
                SetModelAnim(animation);
                break;
            }
        }
    }

    /// <summary>
    /// <c>studiohdr_t::hull_min</c>/<c>hull_max</c> or <c>view_bbmin</c>/<c>view_bbmax</c>
    /// (<c>StudioRenderBounds.Of</c>) fed to <see cref="LookAtBounds"/>.
    /// </summary>
    private void ApplyStartFramed()
    {
        if (RootStudioHdr() is not { } model)
        {
            return;
        }

        ((float X, float Y, float Z) min, (float X, float Y, float Z) max) = model.RenderBounds();

        if (min == default && max == default)
        {
            return;
        }

        LookAtBounds(min, max);
    }

    /// <summary>
    /// <c>CBaseModelPanel::LookAtBounds</c> (<c>basemodel_panel.h:197</c>, cpp:649-769), in full: reprojects the box's
    /// eight corners through the model's own rotation, the panel's aspect ratio and field of view, moves the MODEL
    /// to the distance that makes the box fill the frame, and nudges the camera (not the model) to centre it —
    /// <c>CameraOffset</c>'s X stays 0 here; the fitting distance is baked into where the model is placed,
    /// not into how far the camera backs away.
    /// </summary>
    /// <param name="boundsMin">The box's lower corner, model space.</param>
    /// <param name="boundsMax">The box's upper corner, model space.</param>
    /// <remarks>
    /// **`m_bAllowRotation`/`m_bAllowPitch`'s offset-zeroing branch (:763-766) is not ported** — it exists so a
    /// mouse-draggable panel does not fight the player's own horizontal rotation, and this project has no mouse
    /// input at all (the same exclusion `VguiPanel`'s own remarks state), so the branch's condition is permanently
    /// false here and porting it would be dead code.
    /// </remarks>
    public void LookAtBounds((float X, float Y, float Z) boundsMin, (float X, float Y, float Z) boundsMax)
    {
        (float X, float Y, float Z) center = (
            (boundsMax.X + boundsMin.X) * 0.5f, (boundsMax.Y + boundsMin.Y) * 0.5f, (boundsMax.Z + boundsMin.Z) * 0.5f);

        (float X, float Y, float Z) min = (boundsMin.X - center.X, boundsMin.Y - center.Y, boundsMin.Z - center.Z);
        (float X, float Y, float Z) max = (boundsMax.X - center.X, boundsMax.Y - center.Y, boundsMax.Z - center.Z);

        (float X, float Y, float Z)[] corners =
        [
            (max.X, max.Y, max.Z), (min.X, max.Y, max.Z), (max.X, min.Y, max.Z), (min.X, min.Y, max.Z),
            (max.X, max.Y, min.Z), (min.X, max.Y, min.Z), (max.X, min.Y, min.Z), (min.X, min.Y, min.Z),
        ];

        // `AngleMatrix( m_angModelPoseRot, matRotation )`: rotation only, no translation — the same row layout
        // `SetModelAnglesAndPosition` builds, read here through `FreeCamera.Basis()` rather than a second basis computation.
        ((float X, float Y, float Z) forward, (float X, float Y, float Z) right, (float X, float Y, float Z) up) =
            new FreeCamera { Angles = ModelAngles }.Basis();

        (float X, float Y, float Z) left = (-right.X, -right.Y, -right.Z);

        (float X, float Y, float Z) Rotate((float X, float Y, float Z) v) => (
            (v.X * forward.X) + (v.Y * left.X) + (v.Z * up.X),
            (v.X * forward.Y) + (v.Y * left.Y) + (v.Z * up.Y),
            (v.X * forward.Z) + (v.Y * left.Z) + (v.Z * up.Z));

        (float X, float Y, float Z)[] xformed = new (float X, float Y, float Z)[8];

        for (int point = 0; point < 8; point++)
        {
            xformed[point] = Rotate(corners[point]);
        }

        // `VectorTransform( -vecTranslateCenter, matRotation, vecXFormCenter )` where `vecTranslateCenter =
        // -vecCenter`, so `-vecTranslateCenter = vecCenter` — this is `Rotate(center)`, not a second vector.
        (float X, float Y, float Z) xformCenter = Rotate(center);

        float width = Wide;
        float height = Tall;
        float aspect = height > 0f ? width / height : 1f;

        // `flFOVx = DEG2RAD( m_flFOV * 0.5f )` — HALF the field of view, in radians.
        float fovXRadians = FieldOfView * 0.5f * (MathF.PI / 180f);

        // `flFOVy = DEG2RAD( CalcFovY( m_flFOV * 0.5f, w / h ) )` — `CalcFovY` is fed the SAME half-angle as if it
        // were a full one; ported literally (`CalcFovY`, mathlib_base.cpp:3893), not rationalised.
        float fovYRadians = CalcFovY(FieldOfView * 0.5f, aspect) * (MathF.PI / 180f);

        float tanFovX = MathF.Tan(fovXRadians);
        float tanFovY = MathF.Tan(fovYRadians);

        float distance = 0f;

        foreach ((float X, float Y, float Z) point in xformed)
        {
            float distanceY = MathF.Abs(point.Y / tanFovX) - point.X;
            float distanceZ = MathF.Abs(point.Z / tanFovY) - point.X;

            distance = MathF.Max(distance, MathF.Max(distanceY, distanceZ));
        }

        (float X, float Y) screenMin = (99999f, 99999f);
        (float X, float Y) screenMax = (-99999f, -99999f);

        foreach ((float X, float Y, float Z) point in xformed)
        {
            (float X, float Y, float Z) camera = (point.X + distance, point.Y, point.Z);

            float screenX = ((camera.Y / (tanFovX * camera.X) * 0.5f) + 0.5f) * width;
            float screenY = ((camera.Z / (tanFovY * camera.X) * 0.5f) + 0.5f) * height;

            screenMin = (MathF.Min(screenMin.X, screenX), MathF.Min(screenMin.Y, screenY));
            screenMax = (MathF.Max(screenMax.X, screenX), MathF.Max(screenMax.Y, screenY));
        }

        // `vecModelPos.x = flDist - vecXFormCenter.x; .y = -vecXFormCenter.y; .z = -vecXFormCenter.z;` then
        // `SetModelAnglesAndPosition( m_angModelPoseRot, vecModelPos )` — always, regardless of `force_pos`.
        SetModelAnglesAndPosition(ModelAngles, (distance - xformCenter.X, -xformCenter.Y, -xformCenter.Z));

        (float X, float Y) panelCenter = (width * 0.5f, height * 0.5f);
        (float X, float Y) screenCenter = ((screenMax.X + screenMin.X) * 0.5f, (screenMax.Y + screenMin.Y) * 0.5f);

        float panelCameraX = (((panelCenter.X / width) * 2f) - 0.5f) * (tanFovX * distance);
        float panelCameraY = (((panelCenter.Y / height) * 2f) - 0.5f) * (tanFovY * distance);
        float screenCameraX = (((screenCenter.X / width) * 2f) - 0.5f) * (tanFovX * distance);
        float screenCameraY = (((screenCenter.Y / height) * 2f) - 0.5f) * (tanFovY * distance);

        float cameraOffsetX = panelCameraX - screenCameraX;
        float cameraOffsetY = panelCameraY - screenCameraY;

        // `ResetCameraPivot(); ... SetCameraOffset( Vector( 0.0f, -vecCameraOffset.x, -vecCameraOffset.y ) );` — the
        // camera's OWN distance offset (X) stays zero; the fitting distance lives in the model's position instead.
        CameraPivotOrigin = (0f, 0f, 0f);
        CameraPivotAngles = (0f, 0f, 0f);
        CameraOffset = (0f, -cameraOffsetX, -cameraOffsetY);
    }

    /// <summary><c>CalcFovY</c> (<c>mathlib_base.cpp:3893</c>): the vertical fov a horizontal one implies at an
    /// aspect ratio. Ported for <see cref="LookAtBounds"/>, which is the only caller in this project — everywhere
    /// else derives a vertical field of view through <c>FreeCamera</c>'s own projection instead.</summary>
    private static float CalcFovY(float fovXDegrees, float aspect)
    {
        if (fovXDegrees is < 1f or > 179f)
        {
            fovXDegrees = 90f;
        }

        float value = MathF.Atan(MathF.Tan(fovXDegrees * (MathF.PI / 180f) * 0.5f) / aspect);

        return value * (180f / MathF.PI) * 2f;
    }

    /// <summary><c>ParseModelAnimInfo</c> (<c>basemodel_panel.h:208</c>, cpp:122). Pose parameters
    /// (<c>pose_parameters</c>, :136-142) are stored on <c>BMPResAnimData_t::m_pPoseParameters</c> and freed by its
    /// destructor — grepped every <c>tf/</c> and <c>game/client/</c> `.cpp` for another reader and found none, so there
    /// is nothing to port: the shipped SDK stores this value and never consumes it.</summary>
    /// <param name="animationBlock">The <c>animation</c> block.</param>
    public void ParseModelAnimInfo(KeyValuesTree animationBlock)
    {
        ArgumentNullException.ThrowIfNull(animationBlock);

        _animations.Add(new ModelPanelAnimation(
            Name: animationBlock.Find("name")?.Value ?? string.Empty,
            Sequence: animationBlock.Find("sequence")?.Value,
            Activity: animationBlock.Find("activity")?.Value,
            Default: string.Equals(animationBlock.Find("default")?.Value, "1", StringComparison.Ordinal)));
    }

    /// <summary><c>ParseModelAttachInfo</c> (<c>basemodel_panel.h:209</c>, cpp:148-159).</summary>
    /// <param name="attachBlock">The <c>attached_model</c> block.</param>
    public void ParseModelAttachInfo(KeyValuesTree attachBlock)
    {
        ArgumentNullException.ThrowIfNull(attachBlock);

        _attachments.Add(new ModelPanelAttachment(
            ModelName: attachBlock.Find("modelname")?.Value ?? string.Empty,
            Skin: ResIntOrDefault(attachBlock, "skin", -1)));
    }

    /// <summary>
    /// <c>CBaseModelPanel::SetModelAnim</c> (<c>basemodel_panel.h:174</c>, cpp:249-279): an activity first, else the
    /// <c>sequence</c> label via <c>LookupSequence</c>; either way, <c>SetSequence( iSequence, true )</c> resets the
    /// cycle clock (<c>mdlpanel.cpp:541-549</c>: <c>bResetSequence</c> sets <c>m_flCycleStartTime =
    /// GetAutoPlayTime()</c>).
    /// </summary>
    /// <param name="animation">The animation to play.</param>
    /// <remarks>
    /// **Activity is an EXACT scan, not the weighted selection <c>SequenceWithActivity</c> does.**
    /// <c>FindSequenceFromActivity</c> (:229-244) walks <c>pStudioHdr-&gt;pSeqdesc(i)</c> in order and returns the
    /// first whose <c>pszActivityName()</c> matches — no weight, no randomness — so this scans
    /// <see cref="PropModels.SkinnedModel.Groups"/>'s group 0 the same way rather than reusing the weighted lookup,
    /// which could pick a different sequence than Valve's own linear scan would for a model with more than one
    /// sequence sharing the activity.
    /// </remarks>
    public void SetModelAnim(ModelPanelAnimation animation)
    {
        if (RootStudioHdr() is not { } model)
        {
            return;
        }

        int found = -1;

        if (animation.Activity is { Length: > 0 } activity)
        {
            found = FindSequenceFromActivity(model, activity);
        }
        else if (animation.Sequence is { Length: > 0 } sequenceLabel)
        {
            found = model.SequenceByLabel(sequenceLabel);
        }

        if (found < 0)
        {
            return;
        }

        SetSequence(found, resetSequence: true);
    }

    /// <summary><c>CBaseModelPanel::FindSequenceFromActivity</c> (basemodel_panel.cpp:229-244): the first sequence whose
    /// activity name matches, case ignored, or −1 (<c>ACT_INVALID</c>).</summary>
    /// <param name="model">The root model.</param>
    /// <param name="activity">The activity name.</param>
    /// <returns>The sequence, or −1.</returns>
    protected static int FindSequenceFromActivity(PropModels.SkinnedModel model, string? activity)
    {
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyList<StudioSequence> rootSequences = model.Groups.Count > 0 ? model.Groups[0].Sequences : [];

        for (int index = 0; activity is not null && index < rootSequences.Count; index++)
        {
            if (string.Equals(rootSequences[index].Activity, activity, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// <c>CBaseModelPanel::PlaySequence</c> (<c>basemodel_panel.h:195</c>) plays a named sequence temporarily;
    /// <c>OnTick</c> (<c>basemodel_panel.cpp:402-419</c>) reverts to the default animation once it has run one cycle.
    /// </summary>
    /// <param name="sequenceName">A sequence label.</param>
    /// <remarks>
    /// **Duration is derived, not read, because nothing at this layer exposes <c>Studio_Duration</c> directly.**
    /// One cycle's length is the reciprocal of <see cref="PropModels.SkinnedModel.CyclesPerSecond"/> — the same
    /// quantity <c>Studio_CPS</c> feeds (<c>bone_setup.cpp</c>, cited on <see cref="PropModels.SkinnedModel.BlendedCyclesPerSecond"/>)
    /// — rather than a second reading of the animation's frame count and fps.
    /// </remarks>
    public void PlaySequence(string sequenceName)
    {
        if (RootStudioHdr() is not { } model)
        {
            return;
        }

        int found = model.SequenceByLabel(sequenceName);

        if (found < 0)
        {
            return;
        }

        float cyclesPerSecond = model.CyclesPerSecond(found);

        SetSequence(found, resetSequence: true);
        _activeSequence = found;
        _activeSequenceDuration = cyclesPerSecond > 0f ? 1f / cyclesPerSecond : 0f;
        _activeSequenceStartedAt = RealTimeSeconds;
    }

    /// <summary>
    /// <c>CBaseModelPanel::OnTick</c> (<c>basemodel_panel.h:168</c>, cpp:402-419): once a sequence
    /// <see cref="PlaySequence"/> started has run its one cycle, revert to the default animation. Called from
    /// <see cref="Paint"/>, which is the only place this panel is asked what time it is.
    /// </summary>
    public void Tick()
    {
        if (_activeSequence < 0)
        {
            return;
        }

        if (RealTimeSeconds - _activeSequenceStartedAt < _activeSequenceDuration)
        {
            return;
        }

        _activeSequence = -1;
        _activeSequenceDuration = 0f;

        foreach (ModelPanelAnimation animation in _animations)
        {
            if (animation.Default)
            {
                SetModelAnim(animation);
                break;
            }
        }
    }
}
