using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// The direct light a map's world lights cast on a point, added into its ambient cube.
/// </summary>
/// <remarks>
/// **The engine lights a model with an ambient cube and up to four local lights**, and says so in
/// <c>public/istudiorender.h</c> where the cube is annotated "ambient, and lights that aren't in
/// locallight[]". The cube is the bounce term; these are the direct one. Applying only the cube is
/// why a prop indoors draws as though it were in shade (B95, D37).
///
/// **Folding them into the cube is the engine's own fallback, not a shortcut invented here.** That
/// comment is explicit that everything past the four nearest lights is already accumulated into the
/// cube, so a cube carrying light from a point lamp is a shape the engine produces routinely. Doing
/// it for all of them costs the per-vertex variation a true local light would give — a long wall
/// lit from one end shades evenly rather than falling off along its length — and needs no change to
/// the vertex format or the shader, which a per-light path would.
///
/// **The arithmetic is `engine.dll`'s light cache, not `mathlib/lightdesc.cpp`.** The falloff's account is <see cref="LocalLights"/>'s
/// private `Distance`, the admission's `ReachesBox`, and the fold's `FaceOf`; `lightdesc.cpp`'s clamp at one and range belong to the
/// shader and to `StudioPointLighting`.
/// </remarks>
/// <summary>One world light as the shader needs it, ready to be attenuated and shaded.</summary>
/// <param name="X">Where the light is, world space.</param>
/// <param name="Y">Where the light is.</param>
/// <param name="Z">Where the light is.</param>
/// <param name="Red">Its intensity, linear and in the ambient cube's scale.</param>
/// <param name="Green">Its intensity.</param>
/// <param name="Blue">Its intensity.</param>
/// <param name="Constant">The <c>a0</c> of <c>a0 + a1·d + a2·d²</c>, already normalised.</param>
/// <param name="Linear">The <c>a1</c>.</param>
/// <param name="Quadratic">The <c>a2</c>.</param>
/// <param name="Range">Beyond this the light is cut off; zero means no cutoff.</param>
/// <param name="Direction">Which way a spotlight points, `dworldlight_t.normal`.</param>
/// <param name="SpotInner">`stopdot`: the cosine inside which a spotlight is at full strength.</param>
/// <param name="SpotOuter">`stopdot2`: the cosine outside which it is dark.</param>
/// <param name="SpotExponent">`exponent`: shapes the penumbra between the two.</param>
/// <param name="Spot">Whether it takes a cone: a spotlight, or a surface light (the engine's spot with both dots 0); read only then.</param>
/// <param name="Directional">
/// Whether it is `MATERIAL_LIGHT_DIRECTIONAL`: unattenuated, lighting along <paramref name="Direction"/> (the way it
/// travels). Only a model panel's lights make one; a map's directional light is the sun.
/// </param>
/// <param name="Unstyled">Its intensity before its light style scaled it; null where the style left it as it was, its colour being that.</param>
/// <remarks>
/// **The attenuation terms are normalised on the way out**, so a consumer never has to know about
/// `Mod_LoadWorldlights`' all-zero rule. A point or spot with all three exactly 0 leaves here with
/// <c>Quadratic = 1</c>, what stops a reciprocal running to infinity —
/// a mistake this project made once and measured as a luminance of ∞ at four capture points.
///
/// **Point, spot or surface**, the kinds with a position (B669). The sun is directional and travels its own path.
/// <see cref="LocalLights.IsLocal"/> is where that is decided and why.
/// </remarks>
public readonly record struct LocalLight(
    float X, float Y, float Z,
    float Red, float Green, float Blue,
    float Constant, float Linear, float Quadratic,
    float Range,
    (float X, float Y, float Z) Direction = default,
    float SpotInner = 0f,
    float SpotOuter = 0f,
    float SpotExponent = 0f,
    bool Spot = false,
    bool Directional = false,
    (float Red, float Green, float Blue)? Unstyled = null)
{
    /// <summary>`dworldlight_t.intensity`, before its style: what `r_ambientboost` weighs (`engine.dll` `FUN_1800f2dd0`, B672).</summary>
    public (float Red, float Green, float Blue) Intensity => Unstyled ?? (Red, Green, Blue);
}

/// <summary>Everything lighting a point: the bounce cube, and the direct lights near it.</summary>
/// <param name="Cube">The leaf's ambient cube — bounce, plus the dim surface lights vrad folded in.</param>
/// <param name="Locals">Up to four direct lights, in the engine's slot order (B170): insertion, an evicted slot reused.</param>
/// <remarks>
/// **One type rather than a second delegate beside the first.** Five call sites take a
/// <c>Func&lt;float, float, float, AmbientCube&gt;</c>; adding local lights as a fourth parallel
/// delegate would have made a smell worse, and the two halves are answers to one question anyway —
/// the engine adds them together and each light belongs to exactly one of them.
///
/// **The array is allocated only where lights are found**, and <c>ModelLighting</c> caches
/// the whole result on the sampled position, so a model that has not moved re-samples nothing. A
/// scene where two dozen players are moving allocates two dozen four-element arrays a frame, which
/// is far below the frame budget; a static scene allocates none.
/// </remarks>
public readonly record struct PointLighting(AmbientCube Cube, IReadOnlyList<LocalLight> Locals)
{
    /// <summary>A point with no lighting information at all.</summary>
    public static PointLighting None => new(default, []);

    /// <summary>Just a cube, for a caller with no world lights to offer.</summary>
    /// <param name="cube">The bounce term.</param>
    /// <returns>That cube with no direct lights.</returns>
    public static PointLighting Bounce(AmbientCube cube) => new(cube, []);

    /// <summary>The state these were read from, with its lights' raw colours, for a moving model's average (B676); null when there is none.</summary>
    public LightingState? State { get; init; }

    /// <summary>The style values <see cref="Locals"/> were read with, for the same.</summary>
    public Func<int, float>? Style { get; init; }
}

/// <summary>
/// The light cache's `LightingState_t` while it is being built: the cube, the slots as lights rather than as the shader takes them,
/// and each slot's rank (`engine.dll` `FUN_1801b60a0` keeps those beside the state, `FUN_1801b89a0` reads them to evict).
/// </summary>
public sealed class LightingState
{
    private readonly BspWorldLight[] _slots = new BspWorldLight[LocalLights.MaximumLocalLights];
    private readonly float[] _strengths = new float[LocalLights.MaximumLocalLights];

    /// <summary>A state with no light in a slot.</summary>
    /// <param name="cube">The cube it starts from.</param>
    public LightingState(AmbientCube cube) => Cube = cube;

    /// <summary>The cube, with everything evicted or refused a slot folded in.</summary>
    public AmbientCube Cube { get; set; }

    /// <summary>How many slots hold a light.</summary>
    public int Count { get; set; }

    /// <summary>The slots; only the first <see cref="Count"/> mean anything.</summary>
    internal BspWorldLight[] Slots => _slots;

    /// <summary>Each slot's rank.</summary>
    internal float[] Strengths => _strengths;

    /// <summary>A copy to add to, as `FUN_1801ba590` copies the static state before bit 4 and bit 2 add to it.</summary>
    /// <returns>The copy.</returns>
    public LightingState Clone()
    {
        LightingState copy = new(Cube) { Count = Count };

        Array.Copy(_slots, copy._slots, Count);
        Array.Copy(_strengths, copy._strengths, Count);

        return copy;
    }

    /// <summary>The state as a model draws it.</summary>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <returns>The cube and a local light for each slot but a quake or sky light's (<see cref="LocalLights.Slotted"/>).</returns>
    public PointLighting Lighting(Func<int, float>? styleScale = null) =>
        (Count == 0 ? PointLighting.Bounce(Cube) : new PointLighting(Cube, Array.AsReadOnly(LocalLights.Slotted(this, styleScale))))
        with { State = this, Style = styleScale };

    /// <summary><see cref="Lighting"/> for a state nothing changes after this, whose reader may never want the list: built at its first read.</summary>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <returns>The cube and the slots.</returns>
    /// <remarks>A moving model's dlit state is one such a frame, and its average reads <see cref="PointLighting.State"/> instead (B676).</remarks>
    public PointLighting LightingOnDemand(Func<int, float>? styleScale) =>
        new PointLighting(Cube, Count == 0 ? [] : new SlotsOnDemand(this, styleScale)) with { State = this, Style = styleScale };

    /// <summary>The light in a slot.</summary>
    /// <param name="slot">The slot, below <see cref="Count"/>.</param>
    /// <returns>The light, at its raw colour.</returns>
    public BspWorldLight SlotAt(int slot) => _slots[slot];

    /// <summary>Takes the next free slot, unranked: for a state a caller already knows.</summary>
    /// <param name="light">The light.</param>
    public void Put(in BspWorldLight light) => _slots[Count++] = light;

    /// <summary>A state's slots as the shader takes them, written when first asked for.</summary>
    private sealed class SlotsOnDemand(LightingState state, Func<int, float>? style) : IReadOnlyList<LocalLight>
    {
        private LocalLight[]? _locals;

        private LocalLight[] Locals => _locals ??= LocalLights.Slotted(state, style);

        public int Count => Locals.Length;

        public LocalLight this[int index] => Locals[index];

        public IEnumerator<LocalLight> GetEnumerator() => ((IEnumerable<LocalLight>)Locals).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>The box `FUN_1801b99a0` tests a light against.</summary>
/// <param name="Min">Its least corner, world space.</param>
/// <param name="Max">Its greatest corner, world space.</param>
public readonly record struct LightBounds((float X, float Y, float Z) Min, (float X, float Y, float Z) Max);

public static class LocalLights
{
    /// <summary>How many lights the engine carries as true local lights.</summary>
    /// <remarks>
    /// <c>LightDesc_t m_LocalLightDescs[4]</c>. Kept as the count of strongest contributors used
    /// here so the two stay comparable, even though this implementation folds them into the cube
    /// rather than passing them separately.
    /// </remarks>
    public const int MaximumLocalLights = 4;

    /// <summary>`r_worldlightmin`'s default, `"0.0002"` (`engine.dll` `0x180010110`).</summary>
    private const float WorldLightMinimum = 0.0002f;

    /// <summary>Brings a world light's intensity into the ambient cube's scale.</summary>
    /// <remarks>
    /// **One, because the lump and the cube are already in the same units.** vrad works in 0–255
    /// linear — it builds intensity as <c>pow( r / 255.0, 2.2 ) * 255</c> and then multiplies by the
    /// falloff denominator at a hundred units — but it DIVIDES BY 255 on the way into the file
    /// (<c>lightmap.cpp:1647</c>), under a comment of Valve's asking why:
    ///
    /// <code>
    /// VectorScale( dl->light.intensity, (1.0 / 255.0), wl->intensity );
    /// </code>
    ///
    /// So the lump holds a 0–1 number, and the cube reaches the shader as <c>linear / 255</c>, which
    /// is the same 0–1. Contribution is therefore <c>stored / falloff</c> with no scale at all. The
    /// ratio-at-a-hundred-units factor is not a unit mismatch either: dividing by the falloff at the
    /// lit point cancels it exactly, which is what makes a light read as its authored brightness at
    /// a hundred units.
    ///
    /// **This was 1/255 — the same factor applied a second time, in the same direction** — and that
    /// is why a lamp overhead contributed 0.007 against a bounce of 0.24 (B95). The reasoning
    /// recorded for it was that "<see cref="BspAmbientLight"/> normalises [samples] on decode
    /// (<c>sample[i] / 255f</c>)". It does not, and has not since that divide was removed for making
    /// every cube 255 times too dark; the values land near 0–1 because their exponents are negative.
    ///
    /// **Every unit test agreed with the wrong constant**, because each supplies its own intensity
    /// and writes the divide into its own expected value. The old remarks here said exactly why that
    /// could not work — "a test that supplies its own intensity has no opinion about what units a map
    /// uses" — and then the constant was chosen on one anyway. What settled it was vrad's writer and
    /// two measurements on a real map: `AmbientCubeScaleConformanceTests` for the cube's scale, and
    /// an origin-joined comparison of every decoded light against its authored `_light` key, which
    /// came back short by exactly 255.
    /// </remarks>
    private const float IntensityScale = 1f;

    /// <summary>Adds the strongest world lights' direct contribution to an ambient cube.</summary>
    /// <param name="cube">The leaf's ambient cube at this point.</param>
    /// <param name="lights">Every world light the map carries.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <returns>The cube with direct light added to each face.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lights"/> is null.</exception>
    public static AmbientCube AddTo(
        AmbientCube cube, IReadOnlyList<BspWorldLight> lights, float x, float y, float z, Func<int, float>? styleScale = null)
    {
        ArgumentNullException.ThrowIfNull(lights);

        Func<int, float> style = styleScale ?? Steady;

        if (lights.Count == 0)
        {
            return cube;
        }

        // **The slotted lights of the one ranking, <see cref="Build"/>, and only those**: the lights that lost are dropped, not folded.
        LightingState ranked = Build(lights, x, y, z, default, style);

        for (int slot = 0; slot < ranked.Count; slot++)
        {
            cube = Fold(cube, ranked.Slots[slot], x, y, z, style);
        }

        return cube;
    }

    /// <summary>Every style at its stored value.</summary>
    private static readonly Func<int, float> Steady = static _ => 1f;

    /// <summary>One light as the shader takes it, at its style value (`FUN_1800f2010`), every style at one when none is given.</summary>
    /// <param name="light">The light.</param>
    /// <param name="styleScale">Each light style's value over 264.</param>
    /// <returns>The light for the shader.</returns>
    public static LocalLight Local(in BspWorldLight light, Func<int, float>? styleScale) => Local(light, styleScale ?? Steady);

    private static LocalLight Local(BspWorldLight light, Func<int, float> style)
    {
        (float constant, float linear, float quadratic) = Normalised(light);
        float scale = IntensityScale * style(light.Style);
        bool surface = light.Kind == WorldLightKind.Surface;

        // `engine.dll` `FUN_1800f0e10` case 0: a surface light's `LightDesc_t` is a spot with both dots 0 and falloff 1,
        // whatever its stopdots hold. So the CPU path is flat (`studiorender.dll` `0x180021b20` keeps N·L at or past
        // ThetaDot). The vertex shader's form, `( cos − PhiDot ) · 1`, gives one cosine of its own: *interpolated*, since
        // what shaderapi uploads for a zero spread was not read.
        return new LocalLight(
            light.Origin.X, light.Origin.Y, light.Origin.Z,
            light.Intensity.Red * scale,
            light.Intensity.Green * scale,
            light.Intensity.Blue * scale,
            constant, linear, quadratic,
            light.Radius,
            light.Normal,
            surface ? 0f : light.StopDot,
            surface ? 0f : light.StopDot2,
            surface ? 1f : light.Exponent,
            surface || light.Kind == WorldLightKind.Spotlight,
            // Only where the style moved it, so a steady light is the same value it always was.
            Unstyled: light.Intensity == (light.Intensity.Red * scale, light.Intensity.Green * scale, light.Intensity.Blue * scale) ? null : light.Intensity);
    }

    /// <summary>How far short of a light a trace from a point stops: `(1 − fraction) · distance`, the light cache's measure.</summary>
    /// <param name="light">The light's origin.</param>
    /// <param name="x">The point being lit.</param>
    /// <param name="y">The point being lit.</param>
    /// <param name="z">The point being lit.</param>
    /// <param name="fraction">The trace: from the point to the light's origin, the fraction of it that is clear.</param>
    /// <returns>The blocked length, in units; zero when the ray is clear.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fraction"/> is null.</exception>
    /// <remarks>
    /// *Read in disassembly*, `engine.dll` `FUN_1801b9570` (and `FUN_1801b8e20`, its shadow-cached twin): for a surface,
    /// point, spot or quake light, after the ratio and the brightest-channel `r_worldlightmin` cutoff and unless flags bit
    /// 0 skips it, `IEngineTrace::TraceRay` (vtable `+0x20`) from the POINT to the LIGHT ORIGIN at full length,
    /// `MASK_OPAQUE` `0x4081`, with a filter (`0x18035e800`) whose `GetTraceType` is `TRACE_WORLD_ONLY`: world brushes and
    /// displacements, no entity and no static prop. The fraction is read at `+0x1c` of the result.
    /// </remarks>
    public static float Blocked(
        (float X, float Y, float Z) light, float x, float y, float z,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), float> fraction)
    {
        ArgumentNullException.ThrowIfNull(fraction);

        float dx = light.X - x;
        float dy = light.Y - y;
        float dz = light.Z - z;

        return (1f - fraction((x, y, z), light)) * MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>Whether the light cache's visibility trace keeps a light: <see cref="Blocked"/> at most eight units.</summary>
    /// <param name="light">The world light.</param>
    /// <param name="x">The point being lit.</param>
    /// <param name="y">The point being lit.</param>
    /// <param name="z">The point being lit.</param>
    /// <param name="fraction">The trace, as <see cref="Blocked"/> takes it.</param>
    /// <returns>False when the light is rejected, as `FUN_1801b9570` then returns a ratio of 0.0.</returns>
    /// <remarks>
    /// The slack is the engine's: `(1.0 − fraction) · distance > 8.0` rejects (`0x18035c93c` = 1.0, `0x1803adc70` = 8.0,
    /// both read), so a lamp whose ray stops within eight units of it — against its own fixture or ceiling — still lights.
    /// </remarks>
    public static bool TraceKeeps(
        BspWorldLight light, float x, float y, float z,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), float> fraction) =>
        Kept(Blocked(light.Origin, x, y, z, fraction));

    /// <summary>The rule alone: a light whose trace stops <paramref name="blocked"/> units short of it is kept.</summary>
    /// <param name="blocked">The blocked length, <see cref="Blocked"/>.</param>
    /// <returns>True unless more than eight units are blocked.</returns>
    public static bool Kept(float blocked) => !(blocked > VisibilitySlack);

    /// <summary>How far short of a light its visibility trace may stop and still keep it: `engine.dll` `0x1803adc70`.</summary>
    private const float VisibilitySlack = 8f;

    /// <summary>
    /// Whether a light passes the trace function's cutoff (<see cref="Admitted"/>), and the strength it is ranked by.
    /// </summary>
    /// <remarks>
    /// **Ranked by luminance times falloff** (B453): `engine.dll` `FUN_1801b60a0` weighs the intensity by Rec.601's 0.299,
    /// 0.587, 0.114 (`0x18047be38`) — not by its brightest channel, which ranked a saturated blue lamp above a
    /// brighter-looking green one.
    ///
    /// **A NaN strength is refused here, for every kind.** It was the two `r_worldlightmin` comparisons' side effect until a surface light
    /// was exempted from them; one reaching a slot would break the eviction's comparisons and poison a cached cube. An
    /// addition of ours: the engine has no such test, and a lump holding a NaN is a stranger's.
    ///
    /// `strength` is meaningful only when this returns true: a light it rejects is given 0, without its luminance being
    /// computed.
    ///
    /// **A sky light ranks too, at its luminance** (B170, B676): `0x1801b79e0` takes an `emit_skylight` like a lamp, and its ratio
    /// from `0x1801baeb0` is 1.0 when the trace along `-normal` strikes sky, with no style, box or brightest-channel minimum. So it
    /// is a candidate only where that trace is made (<paramref name="traced"/>), and the trace is the caller's `traceKeeps`; with no
    /// trace no point sees the sun.
    /// </remarks>
    private static bool Candidate(
        BspWorldLight light, float x, float y, float z, Func<int, float> style, bool traced, out float strength, LightBounds? bounds = null)
    {
        if (light.Kind == WorldLightKind.SkyLight)
        {
            strength = traced ? Luminance(light) : 0f;

            return traced && !float.IsNaN(strength);
        }

        if (!Admitted(light, x, y, z, style, bounds, out float falloff))
        {
            strength = 0f;
            return false;
        }

        strength = falloff * Luminance(light);

        return !float.IsNaN(strength);
    }

    /// <summary>
    /// `r_worldlightmin` again, by the ranking's luminance: a light below it takes no slot and is folded into the cube
    /// instead (`JC 0x1801b64f5`; see <see cref="Split"/>). A surface light is exempt: the test is guarded by `type != 0`
    /// (`FUN_1801b60a0`). A NaN strength never reaches this: <see cref="Candidate"/> refuses one.
    /// </summary>
    private static bool Ranks(BspWorldLight light, float strength) =>
        light.Kind == WorldLightKind.Surface || strength >= WorldLightMinimum;

    /// <summary>Whether a light reaches a point at all: a local kind, positive falloff, and the trace function's `r_worldlightmin`.</summary>
    /// <remarks>
    /// `engine.dll` `FUN_1801b8e20` returns before anything is added when the BRIGHTEST channel times the falloff is below
    /// `r_worldlightmin` — a different test from the ranking's luminance one, and the only one that drops a light outright.
    ///
    /// **The ranking's falloff has no cone in it** (B669, correcting B665's interpolation). `FUN_1801b60a0` multiplies the
    /// luminance by `FUN_1801b9570`'s return and keeps `FUN_1800efbf0`'s angle apart for the fold, and that return is the
    /// distance falloff times the style: logged from the running engine, 0.002225 at 449 units from a spot whose fringe there
    /// is 0.0512. What keeps a spot aimed away out of a slot is <see cref="ReachesBox"/>, before any trace. A surface
    /// light skips the minimum: `FUN_1801b9570` guards it with `type != 0`.
    /// </remarks>
    private static bool Admitted(
        BspWorldLight light, float x, float y, float z, Func<int, float> style, LightBounds? bounds, out float falloff)
    {
        falloff = IsLocal(light.Kind) ? Distance(light, x, y, z) * style(light.Style) : 0f;

        return falloff > 0f && (light.Kind == WorldLightKind.Surface ||
            falloff * Math.Max(light.Intensity.Red, Math.Max(light.Intensity.Green, light.Intensity.Blue)) >= WorldLightMinimum) &&
            ReachesBox(light, x, y, z, bounds ?? Cell(x, y, z));
    }

    /// <summary>
    /// `FUN_1801b99a0`'s tests, before any trace, against a box: the light's radius, and a spot's or surface light's cone against a sphere
    /// around the point, radius the distance to the box's far corner.
    /// </summary>
    /// <remarks>
    /// **Which box is the caller's:** the point's 32x32x128 cell in `FUN_1801b60a0` and `FUN_1801b5400`, the prop's bounds in a handle's list
    /// (`FUN_1801b6bf0`) and a static prop's dlight (`FUN_1801b6550`, from `FUN_1801ba590`).
    ///
    /// `DAT_18075b318` read as 0 takes this branch. *Interpolated:* that it is `r_oldlightselection`, since the lights logged from the running
    /// engine fit no other branch; the 83 it traced for #215 are exactly what this admits. Surface and spot: out when the sphere's radius plus
    /// the light's is short of the distance. Then `FUN_1801736b0`. A spot's sine and cosine are of `stopdot2` (*interpolated*: `FUN_18033435c`
    /// and `FUN_18032e370` were not decompiled, and the arguments fit `acos` then `sin`); a surface light's are 1 and 0, a hemisphere. So a
    /// light near a point can take a slot aimed away from it, lighting nothing. Point and spot: out when the light's distance from the box
    /// passes its radius (`FUN_180276b00`).
    ///
    /// **A radius of 0 is a radius here, not "no cutoff".** Load replaces a lump's 0 (<see cref="BspWorldLights.AsLoaded"/>), and where `FUN_1800cbdd0`
    /// answers 0 again, a falloff that never reaches its threshold, `r² &lt; d²` refuses the light from any box it stands outside. Only the
    /// falloff's own case 0 reads 0 as none (<see cref="Distance"/>).
    /// </remarks>
    private static bool ReachesBox(BspWorldLight light, float x, float y, float z, LightBounds box)
    {
        if (light.Kind == WorldLightKind.QuakeLight)
        {
            return true;
        }

        float radius = light.Radius;

        if (light.Kind is WorldLightKind.Surface or WorldLightKind.Spotlight)
        {
            float fromLightX = x - light.Origin.X;
            float fromLightY = y - light.Origin.Y;
            float fromLightZ = z - light.Origin.Z;
            float length = MathF.Sqrt((fromLightX * fromLightX) + (fromLightY * fromLightY) + (fromLightZ * fromLightZ));
            float toFarX = box.Max.X - x;
            float toFarY = box.Max.Y - y;
            float toFarZ = box.Max.Z - z;
            float sphere = MathF.Sqrt((toFarX * toFarX) + (toFarY * toFarY) + (toFarZ * toFarZ));

            if (sphere + radius < length)
            {
                return false;
            }

            float cosine = light.Kind == WorldLightKind.Surface ? 0f : light.StopDot2;
            float sine = light.Kind == WorldLightKind.Surface ? 1f : MathF.Sqrt(MathF.Max(0f, 1f - (cosine * cosine)));

            if (!SphereTouchesCone(fromLightX, fromLightY, fromLightZ, length, sphere, light.Normal, sine, cosine))
            {
                return false;
            }

            if (light.Kind == WorldLightKind.Surface)
            {
                return true;
            }
        }

        float boxX = PastBox(light.Origin.X, box.Min.X, box.Max.X);
        float boxY = PastBox(light.Origin.Y, box.Min.Y, box.Max.Y);
        float boxZ = PastBox(light.Origin.Z, box.Min.Z, box.Max.Z);

        return !(radius * radius < (boxX * boxX) + (boxY * boxY) + (boxZ * boxZ));
    }

    /// <summary>How far past a box a coordinate lies on one axis, by `FUN_180276b00`'s plain compares: an inverted box measures from its min first, and cannot throw.</summary>
    private static float PastBox(float at, float min, float max)
    {
        if (at < min)
        {
            return min - at;
        }

        return at > max ? at - max : 0f;
    }

    /// <summary>`FUN_1801736b0`, a sphere against a cone, with the sphere's centre given from the light.</summary>
    /// <remarks>
    /// **The comparisons are written as the disassembly's `COMISS`/`JC`, because NaN goes the way they send it.** A spot whose `stopdot2` is 1 has
    /// a sine of 0, so the apex is pulled back by infinity, and on any axis the normal has a 0 that is NaN. The first `JC` is taken on unordered:
    /// refused. A `&lt;` that returned false on NaN would admit it.
    /// </remarks>
    private static bool SphereTouchesCone(
        float fromLightX, float fromLightY, float fromLightZ, float length, float sphere, (float X, float Y, float Z) normal, float sine, float cosine)
    {
        float back = sphere / sine;
        float fromApexX = fromLightX + (normal.X * back);
        float fromApexY = fromLightY + (normal.Y * back);
        float fromApexZ = fromLightZ + (normal.Z * back);

        if (!((fromApexX * normal.X) + (fromApexY * normal.Y) + (fromApexZ * normal.Z) >=
            MathF.Sqrt((fromApexX * fromApexX) + (fromApexY * fromApexY) + (fromApexZ * fromApexZ)) * cosine))
        {
            return false;
        }

        // Past the slope's front, the cone's body holds it. Behind it, only the sphere reaching the light itself does.
        return !(-((fromLightX * normal.X) + (fromLightY * normal.Y) + (fromLightZ * normal.Z)) >= length * sine) || length <= sphere;
    }

    /// <summary>The point's cell, as `FUN_1801b60a0` builds it.</summary>
    private static LightBounds Cell(float x, float y, float z)
    {
        (float farX, float farY, float farZ) = (CellFarCorner(x, 5), CellFarCorner(y, 5), CellFarCorner(z, 7));

        return new LightBounds((farX - 32f, farY - 32f, farZ - 128f), (farX, farY, farZ));
    }

    /// <summary>The far edge of a point's cell on one axis, as `FUN_1801b60a0` builds it: the magnitude truncated and shifted down, complemented when negative.</summary>
    /// <param name="value">The coordinate.</param>
    /// <param name="shift">5 for x and y, 7 for z.</param>
    /// <returns>The edge. So -32 and -33 are both in the cell ending at -32, and -31.5 in the one ending at 0.</returns>
    internal static float CellFarCorner(float value, int shift)
    {
        int cell = (int)MathF.Abs(value) >> shift;

        return ((value < 0f ? ~cell : cell) << shift) + (1 << shift);
    }

    /// <summary>Rec.601 luminance, the lightcache's ranking weight (`engine.dll` `0x18047be38`).</summary>
    private static float Luminance(BspWorldLight light) =>
        (0.299f * light.Intensity.Red) + (0.587f * light.Intensity.Green) + (0.114f * light.Intensity.Blue);

    /// <summary>
    /// The four slots as local lights, in the engine's slot order, and every other light reaching the point folded into <paramref name="cube"/>.
    /// </summary>
    /// <param name="lights">Every world light the map carries.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="into">The local lights; at most <see cref="MaximumLocalLights"/> are written.</param>
    /// <param name="cube">The bounce cube, which the lights that take no slot are added to.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <param name="traceKeeps">The visibility trace by index (<see cref="TraceKeeps"/>); null traces none.</param>
    /// <returns>How many local lights were written.</returns>
    /// <remarks>
    /// `engine.dll` `FUN_1801b60a0` (B453): a light that loses its slot, or never wins one, is not dropped — it goes to
    /// `FUN_1801b5db0`, which adds `max(0, n·d) · falloff · intensity` to each face. `istudiorender.h` names the cube
    /// "ambient, and lights that aren't in locallight[]". Every light admitted, seen and not slotted is folded, in the engine's
    /// own sequence (<see cref="Build"/>); one the trace rejects is neither (B666).
    /// </remarks>
    public static int Split(
        IReadOnlyList<BspWorldLight> lights, float x, float y, float z, Span<LocalLight> into, ref AmbientCube cube,
        Func<int, float>? styleScale = null, Func<int, bool>? traceKeeps = null)
    {
        if (into.Length < MaximumLocalLights)
        {
            throw new ArgumentException($"Room for {MaximumLocalLights} lights is needed.", nameof(into));
        }

        LightingState state = Build(lights, x, y, z, cube, styleScale, traceKeeps);

        cube = state.Cube;

        return WriteSlots(state, into, styleScale ?? Steady);
    }

    /// <summary>A state from a list, ranked as `0x1801b79e0` ranks: one light at a time, in list order.</summary>
    /// <param name="lights">Every world light the map carries.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="cube">The cube it starts from.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <param name="traceKeeps">The visibility trace by index (<see cref="TraceKeeps"/>); null traces none, and then no sky light ranks.</param>
    /// <param name="slots">How many take a slot, `min( r_worldlights, MaxNumLights )` (`FUN_1801b79e0`); what loses is folded.</param>
    /// <param name="pass">Which of the list's lights this pass takes: all, the style-0 (`0x1801b7450`) or the styled (`0x1801bbbf0`).</param>
    /// <returns>The state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lights"/> is null.</exception>
    /// <remarks>
    /// **The engine's own sequence, not a ranking that arrives at the same set** (B170): each light admitted and seen takes a free slot,
    /// else evicts the first weakest slot strictly weaker than it (`0x1801ba2e0`), else is folded (<see cref="Rank"/>). So the slots are
    /// in the engine's order, a tie goes to the light already in, and a sun evicted later adds nothing where one refused is folded.
    /// </remarks>
    public static LightingState Build(
        IReadOnlyList<BspWorldLight> lights, float x, float y, float z, AmbientCube cube,
        Func<int, float>? styleScale = null, Func<int, bool>? traceKeeps = null, int slots = MaximumLocalLights,
        LightPass pass = LightPass.All)
    {
        ArgumentNullException.ThrowIfNull(lights);

        Func<int, float> style = styleScale ?? Steady;
        LightingState state = new(cube);
        bool skySeen = false;

        for (int index = 0; index < lights.Count; index++)
        {
            BspWorldLight light = lights[index];
            bool sky = light.Kind == WorldLightKind.SkyLight;

            // **One sky light a state**: `0x1801b79e0` returns at once for a type-3 light once its rank array's `+0x10` is set, which the
            // first sky light the trace keeps sets.
            if (!Takes(pass, light) || (sky && skySeen) || !Candidate(light, x, y, z, style, traceKeeps is not null, out float strength))
            {
                continue;
            }

            // **Then the visibility trace, where `FUN_1801b9570` makes it**: after the brightest-channel cutoff and before anything is
            // ranked (B666). A light it rejects has a ratio of zero, so it takes no slot and is not folded.
            if (traceKeeps is { } keeps && !keeps(index))
            {
                continue;
            }

            skySeen |= sky;
            Rank(state, light, strength, x, y, z, style, strength, slots);
        }

        return state;
    }

    /// <summary>
    /// The styled state merged into a copy of the style-0 one, as `LightcacheGet`'s flag 4 does (`0x1801b71d0`): each of its slots
    /// re-ranked into <paramref name="into"/> in slot order (`0x1801b79e0`), then its cube, the styled lights that lost among
    /// themselves, added.
    /// </summary>
    /// <param name="into">The copy to add to.</param>
    /// <param name="styled">The styled state.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <param name="slots">How many slots there are.</param>
    /// <exception cref="ArgumentNullException">A state is null.</exception>
    /// <remarks>
    /// A slot of <paramref name="styled"/> was admitted and seen from this same point when it was built, and the admission and the
    /// trace answer the same again, so they are not asked twice: it ranks on its strength, recomputed.
    /// </remarks>
    public static void Merge(
        LightingState into, LightingState styled, float x, float y, float z, Func<int, float>? styleScale = null, int slots = MaximumLocalLights)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(styled);

        Func<int, float> style = styleScale ?? Steady;

        for (int slot = 0; slot < styled.Count; slot++)
        {
            BspWorldLight light = styled.Slots[slot];

            if (Candidate(light, x, y, z, style, traced: true, out float strength))
            {
                Rank(into, light, strength, x, y, z, style, strength, slots);
            }
        }

        (AmbientCube to, AmbientCube from) = (into.Cube, styled.Cube);

        into.Cube = new AmbientCube(
            Sum(to.PositiveX, from.PositiveX), Sum(to.NegativeX, from.NegativeX),
            Sum(to.PositiveY, from.PositiveY), Sum(to.NegativeY, from.NegativeY),
            Sum(to.PositiveZ, from.PositiveZ), Sum(to.NegativeZ, from.NegativeZ));
    }

    private static (float Red, float Green, float Blue) Sum((float Red, float Green, float Blue) a, (float Red, float Green, float Blue) b) =>
        (a.Red + b.Red, a.Green + b.Green, a.Blue + b.Blue);

    /// <summary>Whether a pass takes a light: <see cref="LightPass.StyleZero"/> only style 0, <see cref="LightPass.Styled"/> only the rest.</summary>
    private static bool Takes(LightPass pass, BspWorldLight light) => pass switch
    {
        LightPass.StyleZero => light.Style == 0,
        LightPass.Styled => light.Style != 0,
        _ => true,
    };

    /// <summary>
    /// `0x1801b79e0` from the rank on, for a light already admitted: a free slot, else the first weakest slot below <paramref name="below"/>
    /// (`0x1801ba2e0`: strictly below, the first of equals), evicted and folded, else the light itself folded. Under `r_worldlightmin` by
    /// luminance it is folded at once. **An evicted sky light adds nothing** (`0x1801b7d67`..`0x1801b7d78`: the evicting branch zeroes a
    /// type-3 light's direction, and `0x1801b76f0` adds only where the direction's dot with a face is positive); a refused one is folded
    /// along `-normal` (<see cref="FaceOf"/>). The same on <see cref="Add"/>'s routes: `0x1801b6d40` zeroes it at `0x1801b705e`,
    /// `0x1801b7e90` at `0x1801b8077`, and a moving model's dlight reaches `0x1801b79e0` itself through `0x1801b71d0`.
    /// </summary>
    private static void Rank(
        LightingState state, BspWorldLight light, float strength, float x, float y, float z, Func<int, float> style, float below, int slots)
    {
        BspWorldLight folded = light;

        if (Ranks(light, strength))
        {
            if (state.Count < slots)
            {
                state.Slots[state.Count] = light;
                state.Strengths[state.Count] = strength;
                state.Count++;

                return;
            }

            int weakest = -1;

            for (int slot = 0; slot < state.Count; slot++)
            {
                if (state.Strengths[slot] < below)
                {
                    below = state.Strengths[slot];
                    weakest = slot;
                }
            }

            if (weakest >= 0)
            {
                folded = state.Slots[weakest];
                state.Slots[weakest] = light;
                state.Strengths[weakest] = strength;

                if (folded.Kind == WorldLightKind.SkyLight)
                {
                    return;
                }
            }
        }

        state.Cube = Fold(state.Cube, folded, x, y, z, style);
    }

    /// <summary>
    /// One more light into a finished state, as `engine.dll` `FUN_1801b5400` adds a listed lamp and `FUN_1801b6550` a dlight: a free slot if
    /// there is one, else it evicts the weakest slot below its threshold (`FUN_1801b89a0`), in place, and whatever is evicted or
    /// refused goes to the cube (`FUN_1801b5db0`).
    /// </summary>
    /// <param name="state">The state to add to.</param>
    /// <param name="light">The light.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <param name="dynamicLight">
    /// A static prop's dlight (`FUN_1801b6550`'s `param_5`): the threshold is 100000 rather than the light's own strength, so it takes a
    /// slot from all but an enormous lamp. A moving model's dlight comes through `FUN_1801b5890` and has no such rule.
    /// </param>
    /// <param name="bounds">
    /// The box `FUN_1801b99a0` tests against: a static prop's bounds for its dlight (`FUN_1801b6550`, from `FUN_1801ba590`); null for the point's
    /// cell, which `FUN_1801b60a0` and `FUN_1801b5400` pass.
    /// </param>
    /// <param name="slots">How many slots there are: `min( r_worldlights, MaxNumLights )`, in `FUN_1801b6d40` and `FUN_1801b7e90` alike.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static void Add(
        LightingState state, BspWorldLight light, float x, float y, float z, Func<int, float>? styleScale = null, bool dynamicLight = false,
        LightBounds? bounds = null, int slots = MaximumLocalLights)
    {
        ArgumentNullException.ThrowIfNull(state);

        Func<int, float> style = styleScale ?? Steady;

        // Untraced: a dlight or a listed styled lamp, never the sky, which only a ranking with its trace takes (`Build`).
        if (!Candidate(light, x, y, z, style, traced: false, out float strength, bounds))
        {
            return;
        }

        Rank(state, light, strength, x, y, z, style, dynamicLight ? DynamicLightEvictionThreshold : strength, slots);
    }

    /// <summary>A static prop's dlight's threshold to evict a slot: `FUN_1801b6550`'s constant.</summary>
    private const float DynamicLightEvictionThreshold = 100000f;

    /// <summary>One light's fold into a cube at a point: `FUN_1801b5db0`, on each face.</summary>
    /// <param name="cube">The cube.</param>
    /// <param name="light">The light.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <returns>The cube with the light added.</returns>
    public static AmbientCube Fold(AmbientCube cube, BspWorldLight light, float x, float y, float z, Func<int, float>? styleScale = null)
    {
        Func<int, float> style = styleScale ?? Steady;

        return new AmbientCube(
            FaceOf(cube.PositiveX, light, x, y, z, 1f, 0f, 0f, style),
            FaceOf(cube.NegativeX, light, x, y, z, -1f, 0f, 0f, style),
            FaceOf(cube.PositiveY, light, x, y, z, 0f, 1f, 0f, style),
            FaceOf(cube.NegativeY, light, x, y, z, 0f, -1f, 0f, style),
            FaceOf(cube.PositiveZ, light, x, y, z, 0f, 0f, 1f, style),
            FaceOf(cube.NegativeZ, light, x, y, z, 0f, 0f, -1f, style));
    }

    /// <summary>A state's cube with every slot's light folded in at a point: `engine.dll` `FUN_1801b7fd0`, given no normal.</summary>
    /// <param name="state">The state.</param>
    /// <param name="x">The point asked about.</param>
    /// <param name="y">The point asked about.</param>
    /// <param name="z">The point asked about.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <param name="skySeen">
    /// Whether the sky reaches the asked point: `0x1801b9910` calls `0x1801baeb0` with flags 1, which skips a lamp's trace but not the sky
    /// branch's, so a slotted sun is traced again from here and folded only where it strikes sky. Null folds it untraced. Given the
    /// point, so one cached delegate serves every call. Not memoised: `0x1801baeb0`'s sky branch traces on every call.
    /// </param>
    /// <returns>The cube; the state is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static AmbientCube FoldSlots(
        LightingState state, float x, float y, float z, Func<int, float>? styleScale = null,
        Func<BspWorldLight, float, float, float, bool>? skySeen = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        AmbientCube cube = state.Cube;

        for (int slot = 0; slot < state.Count; slot++)
        {
            BspWorldLight light = state.Slots[slot];

            if (light.Kind != WorldLightKind.SkyLight || skySeen is null || skySeen(light, x, y, z))
            {
                cube = Fold(cube, light, x, y, z, styleScale);
            }
        }

        return cube;
    }

    /// <summary>A state's slots as the shader takes them.</summary>
    /// <param name="state">The state.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <returns>A local light for each slot but a quake or sky light's.</returns>
    /// <remarks>
    /// `engine.dll` `FUN_1800f0e10` (`0x1800f2010`, cases 4 and 5): a quake light keeps its slot in the state, and its rank there, and makes no `LightDesc_t`, so it
    /// draws nothing from that slot.
    /// </remarks>
    internal static LocalLight[] Slotted(LightingState state, Func<int, float>? styleScale)
    {
        LocalLight[] locals = new LocalLight[LocalLights.MaximumLocalLights];

        return locals[..WriteSlots(state, locals, styleScale ?? Steady)];
    }

    private static int WriteSlots(LightingState state, Span<LocalLight> into, Func<int, float> style)
    {
        int written = 0;

        for (int slot = 0; slot < state.Count; slot++)
        {
            if (Drawn(state.Slots[slot].Kind))
            {
                into[written++] = Local(state.Slots[slot], style);
            }
        }

        return written;
    }

    /// <summary>
    /// Whether a slot is drawn as a local light: not a quake light, which makes no `LightDesc_t` (`FUN_1800f0e10` cases 4 and 5), and not
    /// the sky light, which holds its slot and is drawn as the sun's own constant (B170): the shader's directional term, as
    /// `FUN_1800f2010` case 3 makes it, so a sunlit model's lamps and sun together are at most the four slots.
    /// </summary>
    private static bool Drawn(WorldLightKind kind) => kind is not (WorldLightKind.QuakeLight or WorldLightKind.SkyLight);

    /// <summary>Whether a light's contribution at a point is positive, as a static prop's handle list admits it.</summary>
    /// <param name="light">The world light.</param>
    /// <param name="x">The handle's lighting origin.</param>
    /// <param name="y">The handle's lighting origin.</param>
    /// <param name="z">The handle's lighting origin.</param>
    /// <param name="bounds">The prop's world box; null tests the point's cell.</param>
    /// <returns>True when it reaches.</returns>
    /// <remarks>
    /// `engine.dll` `FUN_1801b99a0` → `FUN_1801b9570` with flags `9|2`: the value 8 skips the style scale and the value 1
    /// the visibility trace (<see cref="TraceKeeps"/>; B666's read, superseding B424's "2 skips the trace"), so this is
    /// `r_worldlightmin` for every kind but `emit_surface`, a surface light's radius in the falloff, and <see cref="ReachesBox"/>
    /// against `bounds`, the prop's in `FUN_1801b6bf0`, the point's cell where none is given. Sky and sky-ambient lights never
    /// reach a point this way.
    /// </remarks>
    public static bool Reaches(BspWorldLight light, float x, float y, float z, LightBounds? bounds = null) =>
        Admitted(light, x, y, z, Steady, bounds, out _);

    /// <summary>A light's terms as the slot takes them: a surface light's are the engine's 0/0/1, and all zero is quadratic, as <see cref="Distance"/>.</summary>
    private static (float Constant, float Linear, float Quadratic) Normalised(BspWorldLight light)
    {
        if (light.Kind == WorldLightKind.Surface)
        {
            return (0f, 0f, 1f);
        }

        return light is { ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 0f }
            ? (0f, 0f, 1f)
            : (light.ConstantAttenuation, light.LinearAttenuation, light.QuadraticAttenuation);
    }

    /// <summary>Whether a light casts direct light from a position.</summary>
    /// <remarks>
    /// **The sun is not positional.** <c>emit_skylight</c> is directional and reaches only
    /// what can trace to a sky surface — <c>bspfile.h</c> calls it "directional light with no
    /// falloff (surface must trace to SKY texture)" — so it is ranked by its own rule in <see cref="Candidate"/>, not by a falloff
    /// from wherever its origin happens to be recorded.
    ///
    /// **<c>emit_surface</c> is included, and it was excluded here on a wrong reading (B669).** All 108 of cp_process's
    /// surface lights have attenuation terms of exactly zero, and evaluated through the all-zero rule they drew the middle
    /// capture point at a luminance of 6.3. That was read as "an area light is never evaluated at a distance". It is:
    /// `FUN_1800efe40` case 0 gives it `1 / max( 1, d² )` and never reads those terms, as
    /// `Engine_WorldLightDistanceFalloff` does (`leaf_ambient_lighting.cpp:77`). And "already in the leaf cube" holds only
    /// for a caller whose cube came from the leaf, which passes such lights over by their flag
    /// (<see cref="BspWorldLight.InAmbientCube"/>, `FUN_1801b5a50`); a static prop's computed cube holds none.
    /// </remarks>
    private static bool IsLocal(WorldLightKind kind) =>
        kind is WorldLightKind.Surface or WorldLightKind.Point or WorldLightKind.Spotlight or WorldLightKind.QuakeLight;

    /// <summary>
    /// `FUN_1800efe40`'s distance falloff times the style: what a folded light's cube face is scaled by, the cone applied apart (<see cref="FaceOf"/>).
    /// </summary>
    /// <remarks>
    /// **Times the light's style value over 264**, as the lightcache multiplies it (`engine.dll` `0x1801b8e20`): a
    /// switchable lamp turned off adds nothing. The ranking takes this same value (<see cref="Admitted"/>).
    /// </remarks>
    private static float Falloff(BspWorldLight light, float x, float y, float z, Func<int, float> style) =>
        Distance(light, x, y, z) * style(light.Style);

    /// <summary>The distance falloff alone: `engine.dll` `FUN_1800efe40`, every branch (`0x1800f1040` on build 11191711, read there).</summary>
    /// <remarks>
    /// Case 0's reciprocal is `RCPSS`, an estimate good to about 1 part in 2,700 that differs between CPUs; this is the exact one. Its `ADDSS` of 1e-10 is
    /// left out as it cannot move `MAXSS` with 1: under 1 the 1 wins, and at or over 1 the addition rounds away. **A point or spot at d = 0 divides by 0 as the
    /// engine's `DIVSS` does**, to infinity, which ranks it first; `FaceOf` adds nothing for it, as the engine's zero direction does.
    /// </remarks>
    private static float Distance(BspWorldLight light, float x, float y, float z)
    {
        float dx = light.Origin.X - x;
        float dy = light.Origin.Y - y;
        float dz = light.Origin.Z - z;

        float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);

        switch (light.Kind)
        {
            // Inverse square clamped at 1, whatever the attenuation terms hold; out only strictly past the radius, and 0 is none. The radius is
            // read here whichever way the light is reached: this case ignores the flag.
            case WorldLightKind.Surface:
                return light.Radius != 0f && distanceSquared > light.Radius * light.Radius ? 0f : 1f / MathF.Max(1f, distanceSquared);

            // **No radius**: every route here passes `FUN_1801b99a0`, whose `flags | 2` is the third argument that skips this case's cutoff.
            // `ReachesBox` is its test instead. **And no clamp**: the shader's `max( 1, d² )` is `VertexAttenInternal`'s, not this function's.
            case WorldLightKind.Point:
            case WorldLightKind.Spotlight:
                // A lump's all-zero terms are gone by here (`BspWorldLights.AsLoaded`). A light built by hand can still have them: the same answer.
                float quadratic = light is { ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 0f } ? 1f : light.QuadraticAttenuation;

                return 1f / ((MathF.Sqrt(distanceSquared) * light.LinearAttenuation) + light.ConstantAttenuation + (distanceSquared * quadratic));

            // `emit_quakelight`: linear, reaching 0 at a distance of its linear term.
            case WorldLightKind.QuakeLight:
                return MathF.Max(0f, light.LinearAttenuation - MathF.Sqrt(distanceSquared));

            default:
                return 1f;
        }
    }

    /// <summary>What a light adds to a cube face turned squarely at it, the fold's own arithmetic, for a probe that ranks them.</summary>
    /// <param name="light">The light.</param>
    /// <param name="x">The point.</param>
    /// <param name="y">The point.</param>
    /// <param name="z">The point.</param>
    /// <returns>The green channel, the fold's value for that face.</returns>
    public static float StrengthAt(BspWorldLight light, float x, float y, float z)
    {
        float dx = light.Origin.X - x;
        float dy = light.Origin.Y - y;
        float dz = light.Origin.Z - z;
        float length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        return length <= 0f
            ? 0f
            : FaceOf(default, light, x, y, z, dx / length, dy / length, dz / length, static _ => 1f).Green;
    }

    /// <summary>One light's contribution to one face of the cube.</summary>
    private static (float Red, float Green, float Blue) FaceOf(
        (float Red, float Green, float Blue) face,
        BspWorldLight light,
        float x, float y, float z,
        float normalX, float normalY, float normalZ,
        Func<int, float> style)
    {
        // The sun's direction is `-normal`, its falloff and angle both 1 (`FUN_1801baeb0`'s first branch, `FUN_1800f0df0` case 3). Whether
        // it reaches the point is the caller's: a ranking that refused it a slot (B170), and a moving model's average (B676).
        if (light.Kind == WorldLightKind.SkyLight)
        {
            float toward = Math.Max(0f, -((light.Normal.X * normalX) + (light.Normal.Y * normalY) + (light.Normal.Z * normalZ)));

            return (face.Red + (toward * light.Intensity.Red), face.Green + (toward * light.Intensity.Green), face.Blue + (toward * light.Intensity.Blue));
        }

        float dx = light.Origin.X - x;
        float dy = light.Origin.Y - y;
        float dz = light.Origin.Z - z;

        // `FUN_1801baeb0`'s normalize, `1 / sqrt( d² + 1e-10 )`: at d = 0 the direction is (0 0 0), so every face below takes nothing.
        float length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz) + 1e-10f);

        dx /= length;
        dy /= length;
        dz /= length;

        // max(0, delta . normal): a face turned away from the light receives nothing, which is
        // what stops a lamp lighting the far side of everything it touches.
        float strength = Math.Max(0f, (dx * normalX) + (dy * normalY) + (dz * normalZ));

        if (strength <= 0f)
        {
            return face;
        }

        if (light.Kind is WorldLightKind.Spotlight or WorldLightKind.Surface)
        {
            strength *= Cone(light, dx, dy, dz);

            if (strength <= 0f)
            {
                return face;
            }
        }

        float scale = strength * Falloff(light, x, y, z, style) * IntensityScale;

        return (face.Red + (scale * light.Intensity.Red), face.Green + (scale * light.Intensity.Green), face.Blue + (scale * light.Intensity.Blue));
    }

    /// <summary>What a light's direction contributes to its fold: `engine.dll` `FUN_1800efbf0`, with the direction as the surface normal.</summary>
    /// <remarks>
    /// <c>dot2</c> is negated because <paramref name="dx"/> points from the surface toward the
    /// light while the light's normal points the way it shines.
    ///
    /// **A spot takes the fringe alone** (case 2): 1 at or inside `stopdot`, 0 at or outside `stopdot2`, and between them the
    /// ratio, raised to the exponent unless that is 0 or 1. B122 also multiplied by <c>dot2</c>, from vrad's
    /// <c>lightmap.cpp:1929</c>: that is the bake, which the runtime cube does not share. Logged from the running engine, the spot
    /// at (-3400 -1128 486) folds at 1/449 times its fringe of 0.0512, and that cosine made it 22 times too dim (B669).
    ///
    /// <c>emit_surface</c> takes the cosine once and no fringe, and is lit only past 0.01
    /// (case 0, `Engine_WorldLightAngle`, `leaf_ambient_lighting.cpp:68-72`: "behind light surface").
    /// </remarks>
    private static float Cone(BspWorldLight light, float dx, float dy, float dz)
    {
        float dot2 = -((dx * light.Normal.X) + (dy * light.Normal.Y) + (dz * light.Normal.Z));

        if (light.Kind == WorldLightKind.Surface)
        {
            return dot2 > 0.01f ? dot2 : 0f;
        }

        if (!(light.StopDot2 < dot2))
        {
            return 0f;
        }

        if (light.StopDot <= dot2)
        {
            return 1f;
        }

        float fringe = (dot2 - light.StopDot2) / (light.StopDot - light.StopDot2);

        return light.Exponent is 0f or 1f ? fringe : MathF.Pow(fringe, light.Exponent);
    }
}

/// <summary>Which of a list's lights a <see cref="LocalLights.Build"/> pass takes.</summary>
public enum LightPass
{
    /// <summary>Every light: a static prop's state, whose list is already the style-0 lights, and a caller with one list.</summary>
    All,

    /// <summary>Style 0 only, the light cache entry's static state (`0x1801b7450`).</summary>
    StyleZero,

    /// <summary>Every other style, the entry's styled state (`0x1801bbbf0`), merged at the draw (<see cref="LocalLights.Merge"/>).</summary>
    Styled,
}
