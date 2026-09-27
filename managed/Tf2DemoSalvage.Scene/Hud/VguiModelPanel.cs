using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// One animation a <see cref="VguiModelPanel"/>'s <c>.res</c> file names — the <c>animation</c> block under <c>model</c>.
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

/// <summary>One <c>attached_model</c> block under a <see cref="VguiModelPanel"/>'s <c>model</c> block.</summary>
/// <remarks>
/// <c>CBaseModelPanel::ParseModelAttachInfo</c> (<c>basemodel_panel.cpp:148-159</c>): a second model named beside
/// the root's own, with its own skin. Parsed here; drawing it is <c>CTFPlayerModelPanel</c>'s wearables/weapon
/// concern (step 4), not this step's.
/// </remarks>
/// <param name="ModelName">The attached model's path.</param>
/// <param name="Skin">Its skin, or −1 for its default (<c>GetInt( "skin", -1 )</c>, :158).</param>
public readonly record struct ModelPanelAttachment(string ModelName, int Skin);

/// <summary>
/// <c>CPotteryWheelPanel</c> + <c>CMDLPanel</c> + <c>CBaseModelPanel</c>'s portable half: a panel that draws one root
/// model, with merged models bone-merged onto it, under its own camera and lights.
/// </summary>
/// <remarks>
/// **Three Valve classes folded into one.** <c>CPotteryWheelPanel</c> owns the camera and lights,
/// <c>CMDLPanel</c> owns the model handle and the cycle clock, <c>CBaseModelPanel</c> (<c>game_controls/basemodel_panel.cpp</c>)
/// owns the <c>.res</c> <c>model</c> block — <c>CTFPlayerModelPanel</c> is the only thing between here and a HUD element,
/// and none of the three intermediate classes has state a class portrait or a match-start door needs kept apart.
///
/// **Posing reuses <see cref="AnimatingEntity"/>/<see cref="SkeletonPose"/> directly, not
/// <c>EntityModelSet.Simulate</c>.** That machinery is keyed by entity index and walked once a frame for every drawn
/// prop; a model panel is neither — it is drawn from `.res`, at most a few per HUD, and keyed by its own model
/// paths instead. <see cref="EntityFor"/> is this panel's own small version of
/// <c>EntityModelSet.EntityFor</c> (<c>EntityModels.cs:1462</c>), keyed the same way but by path rather than by an
/// entity that does not exist.
///
/// **Frame stepping is not wired.** <c>CMDLPanel::OnTick</c> (<c>mdlpanel.cpp:638</c>) sets
/// <c>m_flTime = GetAutoPlayTime() - m_flCycleStartTime</c> and the engine's own compressed-animation reader turns that
/// into a frame and a blend fraction deep inside <c>StudioRender</c>. This project's <see cref="SkeletonPose"/> takes an
/// explicit frame and fraction instead of a time, and nothing at this layer knows a sequence's authored frame rate —
/// only the animation blocks the <c>Locals</c> delegate reads do. So <see cref="Paint"/> poses at frame 0 of the chosen
/// sequence: correct bones, correct merge, correct lighting, no motion. <see cref="CycleTime"/> is tracked and exposed
/// so a future frame-rate-aware stepper has somewhere to read from; see <c>docs/HANDOFF-hud.md</c>.
/// </remarks>
public class VguiModelPanel : VguiPanel
{
    /// <summary><c>CPotteryWheelPanel( parent, name )</c>.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiModelPanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
    }

    /// <summary><c>m_Camera.m_flZNear</c> (<c>potterywheelpanel.cpp:250</c>).</summary>
    public const float NearZ = 3f;

    /// <summary><c>m_Camera.m_flZFar</c>: <c>16384.0f * 1.73205080757f</c> (<c>potterywheelpanel.cpp:251</c>).</summary>
    public static readonly float FarZ = 16384f * 1.73205080757f;

    /// <summary><c>m_Camera.m_flFOV</c> (<c>potterywheelpanel.cpp:252</c>), until a <c>.res</c> file overrides it.</summary>
    public const float DefaultFieldOfView = 30f;

    /// <summary><c>BONE_USED_BY_ANYTHING</c> — every bone this panel might read, built every time.</summary>
    /// <remarks>
    /// A model panel draws a handful of models at most a few times a second between them, never
    /// per-frame for hundreds of entities, so there is no budget to protect the way there is in
    /// <see cref="EntityModelSet"/> — narrowing the mask would only risk leaving a hitbox or an
    /// attachment unbuilt for no measured benefit.
    /// </remarks>
    private const int FullBoneMask = StudioBoneFlags.UsedByAnything;

    private readonly BoneFrameCounter _clock = new();
    private readonly Dictionary<string, AnimatingEntity> _entities = new(StringComparer.Ordinal);

    /// <summary><c>m_CameraPivot</c>'s translation — <c>SetCameraPositionAndAngles</c>'s <c>vecPos</c>
    /// (<c>potterywheelpanel.cpp:637-647</c>). Identity until <see cref="ForcePosition"/> or a caller moves it.</summary>
    public (float X, float Y, float Z) CameraPivotOrigin { get; set; }

    /// <summary><c>m_CameraPivot</c>'s rotation — <c>SetCameraPositionAndAngles</c>'s <c>angDir</c>.</summary>
    public (float Pitch, float Yaw, float Roll) CameraPivotAngles { get; set; }

    /// <summary><c>m_vecCameraOffset</c>, default <c>(100, 0, 0)</c> (<c>potterywheelpanel.cpp:248</c>) — the
    /// camera's position relative to the pivot, in the PIVOT's own local axes.</summary>
    public (float X, float Y, float Z) CameraOffset { get; set; } = (100f, 0f, 0f);

    /// <summary><c>m_bForcePos</c> — <c>force_pos</c> in the <c>.res</c> <c>model</c> block
    /// (<c>basemodel_panel.cpp:90</c>). When set, <c>PerformLayout</c> (:381-387) resets the pivot and offset to the
    /// world origin and moves the MODEL to <see cref="ModelAngles"/>/<see cref="ModelOrigin"/> instead of leaving it
    /// at the identity <c>CMDLPanel</c> itself starts at (<c>SetIdentityMatrix( m_RootMDL.m_MDLToWorld )</c>,
    /// <c>mdlpanel.cpp:75</c>).</summary>
    public bool ForcePosition { get; set; }

    /// <summary><c>m_angModelPoseRot</c> — the model's own rotation, applied only when <see cref="ForcePosition"/>
    /// is set (<c>SetModelAnglesAndPosition</c>, <c>mdlpanel.cpp:233-237</c>: <c>AngleMatrix( angRot, vecPos,
    /// m_RootMDL.m_MDLToWorld )</c>). Identity otherwise, matching <c>CMDLPanel</c>'s own constructor.</summary>
    public (float X, float Y, float Z) ModelAngles { get; set; }

    /// <summary><c>m_vecOriginOffset</c> — the model's own position, under the same condition as
    /// <see cref="ModelAngles"/>.</summary>
    public (float X, float Y, float Z) ModelOrigin { get; set; }

    /// <summary>Horizontal field of view in degrees. <c>fov</c> in the <c>.res</c> file overrides it (<c>basemodel_panel.cpp:56</c>).</summary>
    public float FieldOfView { get; set; } = DefaultFieldOfView;

    /// <summary><c>m_vecAmbientCube</c>: 0.4 on all six faces from <c>CreateDefaultLights</c> and never overridden by a
    /// <c>.res</c> file — <c>ParseLightsFromKV</c> (<c>potterywheelpanel.cpp:392</c>) only ever touches
    /// <c>m_Lights</c>.</summary>
    public AmbientCube Ambient { get; set; } = new(
        (0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f),
        (0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f),
        (0.4f, 0.4f, 0.4f), (0.4f, 0.4f, 0.4f));

    /// <summary>
    /// <c>m_Lights[0]</c>: one white directional light down <c>(0, 0, -1)</c> (<c>potterywheelpanel.cpp:316-333</c>), or
    /// whatever a <c>.res</c> <c>lights</c> block's first <c>directional</c> entry replaced it with — or null when a
    /// <c>lights</c> block was given and named no <c>directional</c> entry, since Valve's own list REPLACES itself on
    /// every parse (<c>m_nLightCount = nLightCount</c>, potterywheelpanel.cpp:459) rather than leaving the old light
    /// standing beside whatever the block actually named.
    /// </summary>
    public SunLight? Sun { get; set; } = new(1f, 1f, 1f, 0f, 0f, -1f);

    /// <summary>The root model's path, or null to draw nothing.</summary>
    public string? ModelName { get; set; }

    /// <summary>Which sequence the root model plays, or −1 for the model's sequence 0.</summary>
    public int Sequence { get; set; } = -1;

    /// <summary>The root model's skin, or −1 for its default.</summary>
    public int Skin { get; set; } = -1;

    /// <summary>Model paths bone-merged onto the root, in order.</summary>
    public IList<string> MergeModels { get; } = [];

    /// <summary><c>GetAutoPlayTime()</c>, set by whoever drives this panel's paint (there is no wall clock at this layer).</summary>
    public double RealTimeSeconds { get; set; }

    /// <summary><c>m_flCycleStartTime</c>.</summary>
    public double CycleStartTime { get; set; }

    /// <summary><c>m_flTime = GetAutoPlayTime() - m_flCycleStartTime</c> (<c>mdlpanel.cpp:638</c>).</summary>
    public double CycleTime => RealTimeSeconds - CycleStartTime;

    /// <summary>Looks a model up by path, or null when it cannot be resolved — the panel's model source.</summary>
    /// <remarks>
    /// **A delegate rather than an <see cref="EntityModelSet"/> reference**, because this panel has no entity to be
    /// keyed by and no scene frame to be walked in (Dependency Inversion — the panel depends on the ability to look a
    /// model up, not on the class that owns the whole scene's props). Whoever wires the panel into the viewer supplies
    /// one, typically backed by whatever already loads models for <see cref="EntityModelSet"/>.
    /// </remarks>
    public Func<string, PropModels.SkinnedModel?>? ResolveModel { get; set; }

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

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);

        // `CPotteryWheelPanel::ApplySettings` (potterywheelpanel.cpp:267).
        if (block.Find("lights") is { } lights)
        {
            ParseLightsFromKV(lights);
        }
    }

    /// <summary>
    /// <c>ParseLightsFromKV</c> (<c>potterywheelpanel.cpp:392-460</c>): the first <c>directional</c> entry becomes
    /// <see cref="Sun"/>. This is a DIVERGENCE from Valve, not a reading of the same behaviour through a narrower
    /// window — see <c>docs/HANDOFF-hud.md</c>'s "Divergence: ParseLightsFromKV" for the full list of what is
    /// dropped and why (<see cref="ModelInstance"/> has no representation for a point or spot light, and this
    /// project keeps one sun where Valve's own light LIST replaces itself wholesale every parse).
    /// </summary>
    /// <param name="lightsBlock">The <c>lights</c> block.</param>
    public void ParseLightsFromKV(KeyValuesTree lightsBlock)
    {
        ArgumentNullException.ThrowIfNull(lightsBlock);

        // Valve's own loop (`FOR_EACH_SUBKEY`) replaces `m_nLightCount` unconditionally at the end, even when it
        // finds zero usable entries — so this block being present at all REPLACES the light, and only a matching
        // `directional` entry gives it a new value.
        Sun = null;

        foreach (KeyValuesTree entry in lightsBlock.Children)
        {
            if (!string.Equals(entry.Find("name")?.Value, "directional", StringComparison.OrdinalIgnoreCase))
            {
                // Dropped, not read: `point` (:415) carries origin/attenuation/maxDistance, and `spot` (:431) carries
                // all of that plus inner/outer cone angle and falloff — none of which `ModelInstance.Sun` (a plain
                // directional light) has anywhere to put. Listed in docs/HANDOFF-hud.md, not just here.
                continue;
            }

            (float Red, float Green, float Blue) color = Vector3Of(entry.Find("color")?.Value);
            (float X, float Y, float Z) direction = Normalized(Vector3Of(entry.Find("direction")?.Value));

            Sun = new SunLight(color.Red, color.Green, color.Blue, direction.X, direction.Y, direction.Z);

            return;
        }
    }

    /// <summary>
    /// <c>CMDLPanel::OnPaint3D</c> (<c>mdlpanel.cpp:415</c>): root <c>SetUpBones</c>, then each merge model
    /// <c>SetupBonesWithBoneMerge</c> onto it, emitted as one <see cref="IVguiSurface.Paint3D"/> call in this panel's
    /// place in the paint order.
    /// </summary>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (ModelName is not { Length: > 0 } modelName || ResolveModel is not { } resolve)
        {
            return;
        }

        if (resolve(modelName) is not { } rootModel)
        {
            return;
        }

        AnimatingEntity root = EntityFor(modelName, rootModel);

        if (root.Pose is SkeletonPose rootPose)
        {
            int sequence = Sequence >= 0 ? Sequence : 0;

            rootPose.Sequence = sequence;
            rootPose.EntityTransform = ForcePosition ? AngleMatrix3x4(ModelAngles, ModelOrigin) : Identity3x4;
            rootPose.PoseValues = MoveXPoseValues(rootModel.PoseParameters);

            (rootPose.Frame, rootPose.FrameFraction) = FrameAt(rootModel, sequence, rootPose.PoseValues, CycleTime);
        }

        root.SetupBones(FullBoneMask, CycleTime);

        List<ModelInstance> models =
        [
            new ModelInstance(
                modelName,
                Identity4x4,
                Ambient,
                Sun,
                Bones: Skinned(rootModel.Bones, root.Bones),
                SkinSwap: Skin >= 0 ? new Dictionary<int, int> { [0] = Skin } : null),
        ];

        foreach (string mergeName in MergeModels)
        {
            if (resolve(mergeName) is not { } mergeModel)
            {
                continue;
            }

            AnimatingEntity merged = EntityFor(mergeName, mergeModel);
            merged.Follows = root;
            merged.SetupBones(FullBoneMask, CycleTime);

            models.Add(new ModelInstance(
                mergeName, Identity4x4, Ambient, Sun, Bones: Skinned(mergeModel.Bones, merged.Bones)));
        }

        // `PerformLayout`'s `force_pos` branch (basemodel_panel.cpp:381-387): `ResetCameraPivot(); SetCameraOffset(
        // Vector( 0, 0, 0 ) ); SetCameraPositionAndAngles( vec3_origin, vec3_angle );` — the pivot and offset both
        // go to the world origin FOR THIS FRAME, rather than the stored pivot/offset being overwritten, since
        // nothing here re-runs a persisted layout pass once `ForcePosition` is turned off again.
        ((float X, float Y, float Z) origin, (float Pitch, float Yaw, float Roll) angles) = ForcePosition
            ? ComputeCameraTransform((0f, 0f, 0f), (0f, 0f, 0f), (0f, 0f, 0f))
            : ComputeCameraTransform(CameraPivotOrigin, CameraPivotAngles, CameraOffset);

        FreeCamera camera = new()
        {
            Origin = origin,
            Angles = angles,
            FieldOfView = FieldOfView,
            NearZ = NearZ,
            FarZ = FarZ,
            Aspect = Tall > 0 ? (float)Wide / Tall : 1f,
        };

        surface.Paint3D(0, 0, Wide, Tall, camera.ToMatrix(), models);
    }

    /// <summary>
    /// <c>CPotteryWheelPanel::UpdateCameraTransform</c> (<c>potterywheelpanel.cpp:765-773</c>): the camera's
    /// position and angles, built from the pivot and an offset expressed in the pivot's OWN local axes —
    /// <c>ConcatTransforms( m_CameraPivot, offset, worldToCamera )</c> where <c>offset</c> is a pure translation, so
    /// the rotation carries straight through and only the translation is rotated into world space by the pivot.
    /// </summary>
    private static ((float X, float Y, float Z) Origin, (float Pitch, float Yaw, float Roll) Angles) ComputeCameraTransform(
        (float X, float Y, float Z) pivotOrigin, (float Pitch, float Yaw, float Roll) pivotAngles, (float X, float Y, float Z) offset)
    {
        FreeCamera pivot = new() { Origin = pivotOrigin, Angles = pivotAngles };

        ((float X, float Y, float Z) forward, (float X, float Y, float Z) right, (float X, float Y, float Z) up) =
            pivot.Basis();

        (float X, float Y, float Z) left = (-right.X, -right.Y, -right.Z);

        (float X, float Y, float Z) world = (
            (offset.X * forward.X) + (offset.Y * left.X) + (offset.Z * up.X),
            (offset.X * forward.Y) + (offset.Y * left.Y) + (offset.Z * up.Y),
            (offset.X * forward.Z) + (offset.Y * left.Z) + (offset.Z * up.Z));

        return ((pivotOrigin.X + world.X, pivotOrigin.Y + world.Y, pivotOrigin.Z + world.Z), pivotAngles);
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

    private static (float X, float Y, float Z) Vector3Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (0f, 0f, 0f);
        }

        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        float X(int index) => index < parts.Length && float.TryParse(
            parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;

        return (X(0), X(1), X(2));
    }

    private static (float X, float Y, float Z) Normalized((float X, float Y, float Z) v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

        return length > 0f ? (v.X / length, v.Y / length, v.Z / length) : v;
    }

    private static readonly float[] Identity3x4 = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    private static readonly float[] Identity4x4 =
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}

/// <summary>
/// <c>CBaseModelPanel</c> (<c>game_controls/basemodel_panel.cpp</c>): a <see cref="VguiModelPanel"/> whose model,
/// pose and camera come from the <c>.res</c> file's <c>model</c> block rather than being set in code.
/// </summary>
public class VguiBaseModelPanel : VguiModelPanel
{
    /// <summary><c>CBaseModelPanel( parent, name )</c>.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiBaseModelPanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
    }

    /// <summary><c>m_BMPResData.m_bUseSpotlight</c> (<c>basemodel_panel.cpp:99</c>).</summary>
    public bool UseSpotlight { get; private set; }

    /// <summary><c>m_bStartFramed</c> (<c>basemodel_panel.h:238</c>): <c>start_framed</c>, default <c>"0"</c>.</summary>
    public bool StartFramed { get; private set; }

    /// <summary><c>m_BMPResData.m_aAnimations</c> (<c>ParseModelAnimInfo</c>, <c>basemodel_panel.cpp:122</c>).</summary>
    public IReadOnlyList<ModelPanelAnimation> Animations => _animations;

    /// <summary><c>m_BMPResData.m_aAttachModels</c> (<c>ParseModelAttachInfo</c>, <c>basemodel_panel.cpp:148</c>).</summary>
    public IReadOnlyList<ModelPanelAttachment> Attachments => _attachments;

    private readonly List<ModelPanelAnimation> _animations = [];
    private readonly List<ModelPanelAttachment> _attachments = [];

    /// <summary><c>m_nActiveSequence</c> (<c>basemodel_panel.h:234</c>), or −1 for <c>ACT_INVALID</c>.</summary>
    private int _activeSequence = -1;

    /// <summary><c>m_flActiveSequenceDuration</c> — one cycle's length in seconds.</summary>
    private float _activeSequenceDuration;

    /// <summary>The cycle time <see cref="_activeSequence"/> started at, so its expiry is measured from when it was
    /// played rather than from whenever <see cref="Tick"/> next happens to run.</summary>
    private double _activeSequenceStartedAt;

    /// <summary><c>CBaseModelPanel::OnTick</c> runs before <c>CMDLPanel::OnTick</c> every tick (basemodel_panel.cpp:402:
    /// "Cycle stuff gets handled in mdlpanel::OnTick, so we want to fix up what our sequence is before it gets
    /// called"); this panel has no separate tick, so <see cref="Tick"/> runs here, immediately before the cycle it
    /// might reset is read.</summary>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        Tick();
        base.Paint(surface, context);
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

    /// <summary><c>ParseModelResInfo</c> (<c>basemodel_panel.cpp:88</c>).</summary>
    /// <param name="modelBlock">The <c>model</c> block.</param>
    public void ParseModelResInfo(KeyValuesTree modelBlock)
    {
        ArgumentNullException.ThrowIfNull(modelBlock);

        ModelName = modelBlock.Find("modelname")?.Value;
        Skin = ResIntOrDefault(modelBlock, "skin", -1);
        UseSpotlight = ResIntOrDefault(modelBlock, "spotlight", 0) == 1;

        // `m_bForcePos` (basemodel_panel.cpp:90) — whether `PerformLayout` moves the MODEL to these angles/origin at
        // all; see `ForcePosition`'s remarks on `VguiModelPanel` for what happens when it is left false, which is
        // the common case (no stock TF2 HUD `.res` file sets it — the model draws at whatever `AnimatingEntity`'s
        // bind pose already is, and the camera backs away from THAT instead).
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

        // `SetupModelDefaults` -> `SetupModelAnimDefaults` -> `FindDefaultAnim` (basemodel_panel.cpp:164-188), run
        // once every animation has been read — not per-animation as it is parsed. `FindDefaultAnim` (:193) returns
        // the FIRST animation flagged default and stops there; a later one flagged default too is never reached.
        foreach (ModelPanelAnimation animation in _animations)
        {
            if (animation.Default)
            {
                SetModelAnim(animation);
                break;
            }
        }

        if (StartFramed)
        {
            ApplyStartFramed();
        }
    }

    /// <summary>
    /// <c>CBaseModelPanel::LookAtBounds</c> (<c>basemodel_panel.cpp:649-769</c>), NOT fully ported — see
    /// <c>docs/HANDOFF-hud.md</c>'s "Divergence: start_framed" for exactly which half is missing and why. What runs
    /// here is <c>CPotteryWheelPanel::LookAt( float radius )</c> (<c>potterywheelpanel.cpp:668-693</c>) instead: the
    /// camera backs away from the model's origin until a sphere of the given radius fills the frame at the current
    /// field of view, aspect-corrected. <c>LookAtBounds</c> additionally repositions the MODEL (not just the
    /// camera) and reprojects its eight corner points through the actual aspect ratio and rotation rather than
    /// treating it as a sphere — both real differences from what this does, not a rounding error.
    /// </summary>
    private void ApplyStartFramed()
    {
        if (ResolveModel is not { } resolve || ModelName is not { } modelName || resolve(modelName) is not { } model)
        {
            return;
        }

        float radius = BoundingRadius(model.Bones);

        if (radius <= 0f)
        {
            return;
        }

        float aspect = Tall > 0 ? (float)Wide / Tall : 1f;
        float halfFovRadians = FieldOfView * (MathF.PI / 360f);

        // `if ( h < w ) flFOVx = atan( h * tan( flFOVx ) / w );` (potterywheelpanel.cpp:686-688) — the HORIZONTAL
        // half-fov is narrowed to the vertical one when the panel is wider than it is tall, so the fit is against
        // whichever axis is actually the tighter constraint.
        float effectiveHalfFov = aspect > 1f
            ? MathF.Atan(MathF.Tan(halfFovRadians) / aspect)
            : halfFovRadians;

        CameraPivotOrigin = (0f, 0f, 0f);
        CameraOffset = (-(radius / MathF.Sin(effectiveHalfFov)), 0f, 0f);
    }

    /// <summary>A sphere around the origin big enough to hold every bone's bind-pose position.</summary>
    /// <remarks>
    /// **Bones, not vertices — a real divergence from <c>GetBoundingBox</c>, not an equivalent reading of it.**
    /// Valve's bounds come from the model's actual render bounds (<c>studiohdr_t::hull_min</c>/<c>hull_max</c>);
    /// nothing at this layer has vertex data, only <see cref="StudioBone.Position"/> for each bone's rest
    /// placement, which is a smaller box than the mesh that skins to those bones (a bone sits inside the surface
    /// it drives, not on it). Documented in <c>docs/HANDOFF-hud.md</c> rather than silently accepted.
    /// </remarks>
    private static float BoundingRadius(IReadOnlyList<StudioBone> bones)
    {
        float radius = 0f;

        foreach (StudioBone bone in bones)
        {
            float distance = MathF.Sqrt(
                (bone.Position.X * bone.Position.X) +
                (bone.Position.Y * bone.Position.Y) +
                (bone.Position.Z * bone.Position.Z));

            radius = MathF.Max(radius, distance);
        }

        return radius;
    }

    /// <summary><c>ParseModelAnimInfo</c> (<c>basemodel_panel.cpp:122</c>). Pose parameters (<c>pose_parameters</c>,
    /// :136-142) are stored on <c>BMPResAnimData_t::m_pPoseParameters</c> and freed by its destructor — grepped
    /// every <c>tf/</c> and <c>game/client/</c> `.cpp` for another reader and found none, so there is nothing to
    /// port: the shipped SDK stores this value and never consumes it.</summary>
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

    /// <summary><c>ParseModelAttachInfo</c> (<c>basemodel_panel.cpp:148-159</c>).</summary>
    /// <param name="attachBlock">The <c>attached_model</c> block.</param>
    public void ParseModelAttachInfo(KeyValuesTree attachBlock)
    {
        ArgumentNullException.ThrowIfNull(attachBlock);

        _attachments.Add(new ModelPanelAttachment(
            ModelName: attachBlock.Find("modelname")?.Value ?? string.Empty,
            Skin: ResIntOrDefault(attachBlock, "skin", -1)));
    }

    /// <summary>
    /// <c>CBaseModelPanel::SetModelAnim</c> (<c>basemodel_panel.cpp:249-279</c>): an activity first, else the
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
        if (ResolveModel is not { } resolve || ModelName is not { } modelName || resolve(modelName) is not { } model)
        {
            return;
        }

        int found = -1;

        if (animation.Activity is { Length: > 0 } activity)
        {
            IReadOnlyList<StudioSequence> rootSequences = model.Groups.Count > 0 ? model.Groups[0].Sequences : [];

            for (int index = 0; index < rootSequences.Count; index++)
            {
                if (string.Equals(rootSequences[index].Activity, activity, StringComparison.OrdinalIgnoreCase))
                {
                    found = index;
                    break;
                }
            }
        }
        else if (animation.Sequence is { Length: > 0 } sequenceLabel)
        {
            found = model.SequenceByLabel(sequenceLabel);
        }

        if (found < 0)
        {
            return;
        }

        Sequence = found;
        CycleStartTime = RealTimeSeconds;
    }

    /// <summary>
    /// <c>CBaseModelPanel::PlaySequence</c> plays a named sequence temporarily; <c>OnTick</c>
    /// (<c>basemodel_panel.cpp:402-419</c>) reverts to the default animation once it has run one cycle.
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
        if (ResolveModel is not { } resolve || ModelName is not { } modelName || resolve(modelName) is not { } model)
        {
            return;
        }

        int found = model.SequenceByLabel(sequenceName);

        if (found < 0)
        {
            return;
        }

        float cyclesPerSecond = model.CyclesPerSecond(found);

        Sequence = found;
        CycleStartTime = RealTimeSeconds;
        _activeSequence = found;
        _activeSequenceDuration = cyclesPerSecond > 0f ? 1f / cyclesPerSecond : 0f;
        _activeSequenceStartedAt = RealTimeSeconds;
    }

    /// <summary>
    /// <c>CBaseModelPanel::OnTick</c> (<c>basemodel_panel.cpp:402-419</c>): once a sequence <see cref="PlaySequence"/>
    /// started has run its one cycle, revert to the default animation. Called from <see cref="Paint"/>, which is the
    /// only place this panel is asked what time it is.
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

    private static int ResIntOrDefault(KeyValuesTree tree, string name, int fallback) =>
        tree.Find(name)?.Value is { } value && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;

    private static float ResFloatOrDefault(KeyValuesTree tree, string name, float fallback) =>
        tree.Find(name)?.Value is { } value && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;
}
