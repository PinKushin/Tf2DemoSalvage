using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// <c>CPotteryWheelPanel</c> (<c>public/matsys_controls/potterywheelpanel.h:38</c>,
/// <c>vgui2/matsys_controls/potterywheelpanel.cpp</c>): a panel with its own camera and lights that paints a 3D scene
/// into its rectangle, in its place in the vgui paint order. What it draws is <see cref="OnPaint3D"/>, which is pure
/// virtual in Valve's own class (<c>potterywheelpanel.h:88</c>) and abstract here.
/// </summary>
/// <remarks>
/// Owns only camera and lighting state: <c>m_Camera</c> (:148), <c>m_CameraPivot</c> (:149), <c>m_Lights</c> (:151),
/// <c>m_vecAmbientCube</c> (:152), <c>m_vecCameraOffset</c> (:155). The model and its clock are
/// <see cref="VguiMdlPanel"/>'s; the <c>.res</c> <c>model</c> block is <see cref="VguiBaseModelPanel"/>'s.
/// </remarks>
public abstract class VguiPotteryWheelPanel : VguiEditablePanel
{
    /// <summary><c>CPotteryWheelPanel( parent, name )</c> (<c>potterywheelpanel.h:44</c>).</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    protected VguiPotteryWheelPanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
    }

    /// <summary><c>m_Camera.m_flZNear</c> (<c>potterywheelpanel.cpp:250</c>).</summary>
    public const float NearZ = 3f;

    /// <summary><c>m_Camera.m_flZFar</c>: <c>16384.0f * 1.73205080757f</c> (<c>potterywheelpanel.cpp:251</c>).</summary>
    public static readonly float FarZ = 16384f * 1.73205080757f;

    /// <summary><c>m_Camera.m_flFOV</c> (<c>potterywheelpanel.cpp:252</c>), until a <c>.res</c> file overrides it.</summary>
    public const float DefaultFieldOfView = 30f;

    /// <summary><c>m_CameraPivot</c>'s translation — <c>SetCameraPositionAndAngles</c>'s <c>vecPos</c>
    /// (<c>potterywheelpanel.cpp:637-647</c>). Identity until a caller moves it.</summary>
    public (float X, float Y, float Z) CameraPivotOrigin { get; set; }

    /// <summary><c>m_CameraPivot</c>'s rotation — <c>SetCameraPositionAndAngles</c>'s <c>angDir</c>.</summary>
    public (float Pitch, float Yaw, float Roll) CameraPivotAngles { get; set; }

    /// <summary><c>m_vecCameraOffset</c>, default <c>(100, 0, 0)</c> (<c>potterywheelpanel.cpp:248</c>) — the
    /// camera's position relative to the pivot, in the PIVOT's own local axes.</summary>
    public (float X, float Y, float Z) CameraOffset { get; set; } = (100f, 0f, 0f);

    /// <summary><c>m_Camera.m_flFOV</c>: horizontal field of view in degrees. <c>CBaseModelPanel</c>'s <c>fov</c>
    /// overrides it (<c>basemodel_panel.cpp:56</c>).</summary>
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
    /// <remarks>
    /// **Structurally one, where Valve's own <c>m_Lights</c> can hold several directional entries up to
    /// <c>MAX_LIGHT_COUNT</c>.** <c>ModelInstance.Sun</c> — what the renderer actually draws a model's directional
    /// light with — is a single <see cref="SunLight"/>, the same as every world prop draws with; a second directional
    /// slot needs a new shader path (<c>docs/HANDOFF-hud.md</c>). The first entry wins.
    /// </remarks>
    public SunLight? Sun { get; set; } = new(1f, 1f, 1f, 0f, 0f, -1f);

    /// <summary>
    /// <c>m_Lights[1..]</c>: every <c>point</c>/<c>spot</c> entry <c>ParseLightsFromKV</c> parsed, up to
    /// <see cref="LocalLights.MaximumLocalLights"/> — the SAME structure a world prop's nearby BSP lights use
    /// (<c>ModelInstance.Locals</c>, already drawn by <c>Device3D.DrawPanelModels</c>: <c>locals: instance.Locals</c>).
    /// </summary>
    public IReadOnlyList<LocalLight> Locals { get; set; } = [];

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
    /// <c>CPotteryWheelPanel::Paint</c> (<c>potterywheelpanel.cpp:842</c>): <c>Begin3DPaint</c> on the panel's
    /// rectangle, the panel's own camera and lights, <see cref="OnPaint3D"/>, <c>End3DPaint</c> — emitted as one
    /// <see cref="IVguiSurface.Paint3D"/> call in this panel's place in the paint order.
    /// </summary>
    public override void Paint(IVguiSurface surface, VguiContext context) =>
        Paint3D(surface, CameraPivotOrigin, CameraPivotAngles, CameraOffset);

    /// <summary><see cref="Paint"/>'s body with the pivot and offset given rather than read, so
    /// <see cref="VguiBaseModelPanel"/>'s <c>force_pos</c> can paint through a reset camera for one frame without
    /// overwriting the stored one (see its <c>Paint</c>).</summary>
    private protected void Paint3D(
        IVguiSurface surface,
        (float X, float Y, float Z) pivotOrigin,
        (float Pitch, float Yaw, float Roll) pivotAngles,
        (float X, float Y, float Z) offset)
    {
        ArgumentNullException.ThrowIfNull(surface);

        List<ModelInstance> models = [];

        OnPaint3D(models);

        if (models.Count == 0)
        {
            return;
        }

        ((float X, float Y, float Z) origin, (float Pitch, float Yaw, float Roll) angles) =
            ComputeCameraTransform(pivotOrigin, pivotAngles, offset);

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

    /// <summary><c>virtual void OnPaint3D() = 0</c> (<c>potterywheelpanel.h:88</c>): draw the scene. Here, add what
    /// would be drawn to <paramref name="renderContext"/>, which <see cref="Paint"/> hands to the surface in one call.</summary>
    /// <param name="renderContext">The models drawn this frame, in draw order.</param>
    protected abstract void OnPaint3D(IList<ModelInstance> renderContext);

    /// <summary>
    /// <c>ParseLightsFromKV</c> (<c>potterywheelpanel.cpp:392-460</c>), all three kinds: the first <c>directional</c>
    /// entry becomes <see cref="Sun"/> (only one — see its own remarks); every <c>point</c>/<c>spot</c> entry becomes
    /// a <see cref="LocalLight"/> in <see cref="Locals"/>, up to <see cref="LocalLights.MaximumLocalLights"/>.
    /// </summary>
    /// <param name="lightsBlock">The <c>lights</c> block.</param>
    /// <remarks>
    /// **<c>SetupRenderState</c>'s per-light transform bug (potterywheelpanel.cpp:723-731) is not separately coded,
    /// and that is not a gap.** For light <c>i</c>, Valve writes `pDesc[i] = m_Lights[i].m_Desc` (correct) then
    /// `VectorTransform( ..., pDesc-&gt;m_Position )` — `pDesc` with no index, always `pDesc[0]` — so every light
    /// after the first is SENT with its RAW, untransformed position/direction rather than one rotated by its own
    /// `m_LightToWorld`. The only thing that moves `m_LightToWorld` away from identity is
    /// `CPotteryWheelManip`/mouse dragging a light in the editor, which this project does not model at all (no
    /// mouse input reaches a panel here — the same exclusion `VguiPanel`'s own remarks state). With
    /// `m_LightToWorld` always identity, "transformed" and "raw" are the SAME NUMBERS, so Valve's buggy indexing
    /// and its intended one are indistinguishable — and parsing straight into world-space values here (no separate
    /// transform stage at all) reproduces exactly that. The divergence only becomes observable if this project ever
    /// grows light manipulation, at which point this note is where to look.
    /// </remarks>
    public void ParseLightsFromKV(KeyValuesTree lightsBlock)
    {
        ArgumentNullException.ThrowIfNull(lightsBlock);

        // Valve's own loop (`FOR_EACH_SUBKEY`) replaces `m_nLightCount` unconditionally at the end, even when it
        // finds zero usable entries — so this block being present at all REPLACES every light, and only a matching
        // entry gives one a new value.
        Sun = null;

        List<LocalLight> locals = [];

        // `nLightCount` counts every entry actually initialised — directional included, since it and point/spot
        // all share Valve's one `m_Lights[MAX_LIGHT_COUNT]` array — so the break below is checked before dispatching
        // on the entry's TYPE, not only before a point/spot one.
        int count = 0;

        foreach (KeyValuesTree entry in lightsBlock.Children)
        {
            if (count >= LocalLights.MaximumLocalLights)
            {
                // `Assert( nLightCount < MAX_LIGHT_COUNT ); if ( nLightCount >= MAX_LIGHT_COUNT ) break;`
                // (potterywheelpanel.cpp:397-399) — the WHOLE loop stops here, whatever kind the next entry is.
                break;
            }

            string? kind = entry.Find("name")?.Value;

            if (string.Equals(kind, "directional", StringComparison.OrdinalIgnoreCase))
            {
                if (Sun is null)
                {
                    (float Red, float Green, float Blue) color = Vector3Of(entry.Find("color")?.Value);
                    (float X, float Y, float Z) direction = Normalized(Vector3Of(entry.Find("direction")?.Value));

                    Sun = new SunLight(color.Red, color.Green, color.Blue, direction.X, direction.Y, direction.Z);
                }

                count++;
                continue;
            }

            if (string.Equals(kind, "point", StringComparison.OrdinalIgnoreCase))
            {
                locals.Add(ParsePointLight(entry));
                count++;
            }
            else if (string.Equals(kind, "spot", StringComparison.OrdinalIgnoreCase))
            {
                locals.Add(ParseSpotLight(entry));
                count++;
            }

            // `AssertMsg1( 0, "Failed to initialize light with type '%s'", pType )` (:454) for anything else —
            // an assert in Valve's own debug build, silently skipped here as everywhere else this project reads
            // a `.res` file leniently, and `nLightCount` is not advanced for it either (Valve's own `continue`s
            // for the two known types both `++nLightCount` before continuing; an unrecognised type falls through
            // to the assert and never reaches that increment).
        }

        Locals = locals;
    }

    /// <summary><c>LightDesc_t::InitPoint</c> then the <c>point</c> overrides (potterywheelpanel.cpp:415-429):
    /// origin, colour, attenuation (0/1/2) and <c>maxDistance</c> (range).</summary>
    private static LocalLight ParsePointLight(KeyValuesTree entry)
    {
        (float X, float Y, float Z) origin = Vector3Of(entry.Find("origin")?.Value);
        (float Red, float Green, float Blue) color = Vector3Of(entry.Find("color")?.Value);
        (float Constant, float Linear, float Quadratic) attenuation = NormalizedAttenuation(
            Vector3Of(entry.Find("attenuation")?.Value));
        float range = ResFloatOrDefault(entry, "maxDistance", 0f);

        return new LocalLight(
            origin.X, origin.Y, origin.Z,
            color.Red, color.Green, color.Blue,
            attenuation.Constant, attenuation.Linear, attenuation.Quadratic,
            range);
    }

    /// <summary>
    /// <c>LightDesc_t::InitSpot</c> then the <c>spot</c> overrides (potterywheelpanel.cpp:431-451): origin, colour,
    /// attenuation, <c>maxDistance</c>, <c>direction</c> (overwriting <c>InitSpot</c>'s own look-at-derived one), and
    /// <c>exponent</c> (falloff).
    /// </summary>
    /// <remarks>
    /// **<c>inner_cone_angle</c>/<c>outer_cone_angle</c> are read straight into <c>InitSpot</c>'s <c>float
    /// inner_cone_boundary</c>/<c>outer_cone_boundary</c> parameters, which <c>lightdesc.h:71-72</c> documents as
    /// RADIANS** ("cone boundaries in radians") — <c>GetFloat</c> does no unit conversion, so a `.res` author must
    /// already write radians despite the key names reading like degrees. Ported literally: no `* PI / 180`. This
    /// project's <see cref="LocalLight.SpotInner"/>/<see cref="LocalLight.SpotOuter"/> want cosines
    /// (<c>m_ThetaDot</c>/<c>m_PhiDot</c>, <c>RecalculateDerivedValues</c>), so the conversion this method does add
    /// is <c>cos()</c> of that same (radian) value — not a degrees-to-radians step.
    /// </remarks>
    private static LocalLight ParseSpotLight(KeyValuesTree entry)
    {
        (float X, float Y, float Z) origin = Vector3Of(entry.Find("origin")?.Value);
        (float Red, float Green, float Blue) color = Vector3Of(entry.Find("color")?.Value);
        (float Constant, float Linear, float Quadratic) attenuation = NormalizedAttenuation(
            Vector3Of(entry.Find("attenuation")?.Value));
        float range = ResFloatOrDefault(entry, "maxDistance", 0f);
        (float X, float Y, float Z) direction = Normalized(Vector3Of(entry.Find("direction")?.Value));
        float innerRadians = ResFloatOrDefault(entry, "inner_cone_angle", 0f);
        float outerRadians = ResFloatOrDefault(entry, "outer_cone_angle", 0f);
        float exponent = ResFloatOrDefault(entry, "exponent", 5f);

        return new LocalLight(
            origin.X, origin.Y, origin.Z,
            color.Red, color.Green, color.Blue,
            attenuation.Constant, attenuation.Linear, attenuation.Quadratic,
            range,
            direction,
            MathF.Cos(innerRadians),
            MathF.Cos(outerRadians),
            exponent,
            Spot: true);
    }

    /// <summary>
    /// The three attenuation coefficients Valve writes unchecked from the <c>attenuation</c> string — except that
    /// all three at zero is left as (0, 0, 0) here to (1, 0, 0) instead, a guard this method adds rather than one
    /// <c>ParseLightsFromKV</c> itself has, so a light this project's own shader divides by does not receive the
    /// same all-zero degenerate case <see cref="LocalLight"/>'s other producer (the BSP world-light reader) already
    /// guards against — see its own remarks on why an unguarded zero is a divide-by-zero in the shading formula.
    /// </summary>
    private static (float Constant, float Linear, float Quadratic) NormalizedAttenuation((float X, float Y, float Z) attenuation) =>
        attenuation is (0f, 0f, 0f) ? (1f, 0f, 0f) : attenuation;

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

    /// <summary><c>GetInt( name, fallback )</c>'s shape — shared with <see cref="VguiBaseModelPanel"/>'s <c>.res</c>
    /// parsing, hence <c>private protected</c>.</summary>
    private protected static int ResIntOrDefault(KeyValuesTree tree, string name, int fallback) =>
        tree.Find(name)?.Value is { } value && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;

    /// <summary><c>GetFloat( name, fallback )</c>'s shape — see <see cref="ResIntOrDefault"/>.</summary>
    private protected static float ResFloatOrDefault(KeyValuesTree tree, string name, float fallback) =>
        tree.Find(name)?.Value is { } value && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;

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
}
