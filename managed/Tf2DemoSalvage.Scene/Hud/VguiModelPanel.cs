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

    /// <summary>Where the camera sits, in the panel's own little world.</summary>
    public (float X, float Y, float Z) CameraOrigin { get; set; }

    /// <summary>Pitch, yaw, roll in degrees, Valve's <c>QAngle</c> order.</summary>
    public (float Pitch, float Yaw, float Roll) CameraAngles { get; set; }

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
    /// whatever a <c>.res</c> <c>lights</c> block's first <c>directional</c> entry replaced it with.
    /// </summary>
    public SunLight Sun { get; set; } = new(1f, 1f, 1f, 0f, 0f, -1f);

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
    /// <c>ParseLightsFromKV</c> (<c>potterywheelpanel.cpp:392</c>), narrowed to what this project's lighting carries:
    /// the first <c>directional</c> entry becomes <see cref="Sun"/>. Valve's own version never touches the ambient
    /// cube and can hold several point lights besides; neither exists on <see cref="ModelInstance"/>, so a <c>point</c>
    /// entry — none of the HUD's own <c>.res</c> files write one — is skipped rather than silently misread as another
    /// sun.
    /// </summary>
    /// <param name="lightsBlock">The <c>lights</c> block.</param>
    public void ParseLightsFromKV(KeyValuesTree lightsBlock)
    {
        ArgumentNullException.ThrowIfNull(lightsBlock);

        foreach (KeyValuesTree entry in lightsBlock.Children)
        {
            if (!string.Equals(entry.Find("name")?.Value, "directional", StringComparison.OrdinalIgnoreCase))
            {
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
            rootPose.Sequence = Sequence >= 0 ? Sequence : 0;
            rootPose.EntityTransform ??= Identity3x4;
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

        FreeCamera camera = new()
        {
            Origin = CameraOrigin,
            Angles = CameraAngles,
            FieldOfView = FieldOfView,
            NearZ = NearZ,
            FarZ = FarZ,
            Aspect = Tall > 0 ? (float)Wide / Tall : 1f,
        };

        surface.Paint3D(0, 0, Wide, Tall, camera.ToMatrix(), models);
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

    /// <summary><c>m_BMPResData.m_angModelPoseRot</c> (<c>basemodel_panel.cpp:94</c>).</summary>
    public (float X, float Y, float Z) ModelAngles { get; private set; }

    /// <summary><c>m_BMPResData.m_vecOriginOffset</c>, default <c>(110, 5, 5)</c> (<c>basemodel_panel.cpp:95</c>).</summary>
    public (float X, float Y, float Z) ModelOrigin { get; private set; } = (110f, 5f, 5f);

    /// <summary><c>m_BMPResData.m_bUseSpotlight</c> (<c>basemodel_panel.cpp:99</c>).</summary>
    public bool UseSpotlight { get; private set; }

    /// <summary><c>m_BMPResData.m_aAnimations</c> (<c>ParseModelAnimInfo</c>, <c>basemodel_panel.cpp:122</c>).</summary>
    public IReadOnlyList<ModelPanelAnimation> Animations => _animations;

    private readonly List<ModelPanelAnimation> _animations = [];

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

        ModelAngles = (
            ResFloatOrDefault(modelBlock, "angles_x", 0f),
            ResFloatOrDefault(modelBlock, "angles_y", 0f),
            ResFloatOrDefault(modelBlock, "angles_z", 0f));

        ModelOrigin = (
            ResFloatOrDefault(modelBlock, "origin_x", 110f),
            ResFloatOrDefault(modelBlock, "origin_y", 5f),
            ResFloatOrDefault(modelBlock, "origin_z", 5f));

        CameraAngles = ModelAngles;
        CameraOrigin = ModelOrigin;

        _animations.Clear();

        foreach (KeyValuesTree block in modelBlock.Children)
        {
            if (string.Equals(block.Name, "animation", StringComparison.OrdinalIgnoreCase))
            {
                ParseModelAnimInfo(block);
            }
        }
    }

    /// <summary><c>ParseModelAnimInfo</c> (<c>basemodel_panel.cpp:122</c>). Pose parameters (:138) are not modelled — see
    /// <see cref="ModelPanelAnimation"/>.</summary>
    /// <param name="animationBlock">The <c>animation</c> block.</param>
    public void ParseModelAnimInfo(KeyValuesTree animationBlock)
    {
        ArgumentNullException.ThrowIfNull(animationBlock);

        ModelPanelAnimation animation = new(
            Name: animationBlock.Find("name")?.Value ?? string.Empty,
            Sequence: animationBlock.Find("sequence")?.Value,
            Activity: animationBlock.Find("activity")?.Value,
            Default: string.Equals(animationBlock.Find("default")?.Value, "1", StringComparison.Ordinal));

        _animations.Add(animation);

        if (animation.Default)
        {
            ApplyDefaultAnimation(animation);
        }
    }

    /// <summary>Picks the default animation's sequence, by label when one is named or else by activity.</summary>
    private void ApplyDefaultAnimation(ModelPanelAnimation animation)
    {
        if (ResolveModel is not { } resolve || ModelName is not { } modelName || resolve(modelName) is not { } model)
        {
            return;
        }

        if (animation.Activity is { Length: > 0 } activity)
        {
            int found = model.SequenceWithActivity(activity);

            if (found >= 0)
            {
                Sequence = found;
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
