using System;
using System.Buffers;
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
/// **The arithmetic is Valve's, from <c>mathlib/lightdesc.cpp</c>**, and four details of it are the
/// ones a reimplementation gets wrong:
///
/// * <c>dist2</c> is CLAMPED to a minimum of one (<c>MaxSIMD( Four_Ones, dist2 )</c>), not offset by
///   one. The ambient reconstruction in <see cref="AmbientSamples.At"/> uses <c>dist + 1</c> and the
///   two are easy to conflate now that both live here.
/// * a zero constant term starts the falloff at <c>Four_Epsilons</c> rather than zero, which is what
///   keeps the reciprocal finite for a light with no constant attenuation — every point light on
///   cp_process is exactly that case.
/// * the range cull is strict: light survives where <c>dist2 &lt; range²</c>, and a range of zero
///   means no cull at all.
/// * a spotlight's cone is zeroed OUTSIDE <c>phiDot</c> after the exponent is applied, deliberately,
///   "to mask out any invalid results from pow function".
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
/// <remarks>
/// **The attenuation terms are normalised on the way out**, so a consumer never has to know about
/// vrad's all-zero rule. A light with all three below `EQUAL_EPSILON` leaves here with
/// <c>Constant = 1</c>, which is what vrad writes and what stops a reciprocal running to infinity —
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
    bool Directional = false);

/// <summary>Everything lighting a point: the bounce cube, and the direct lights near it.</summary>
/// <param name="Cube">The leaf's ambient cube — bounce, plus the dim surface lights vrad folded in.</param>
/// <param name="Locals">Up to four direct lights, strongest first.</param>
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
    /// <returns>The cube and exactly <see cref="Count"/> local lights.</returns>
    public PointLighting Lighting(Func<int, float>? styleScale = null) =>
        Count == 0 ? PointLighting.Bounce(Cube) : new PointLighting(Cube, Array.AsReadOnly(LocalLights.Slotted(this, styleScale)));
}

public static class LocalLights
{
    /// <summary>How many lights the engine carries as true local lights.</summary>
    /// <remarks>
    /// <c>LightDesc_t m_LocalLightDescs[4]</c>. Kept as the count of strongest contributors used
    /// here so the two stay comparable, even though this implementation folds them into the cube
    /// rather than passing them separately.
    /// </remarks>
    public const int MaximumLocalLights = 4;

    /// <summary>
    /// Below this, an attenuation term counts as absent — Valve's <c>EQUAL_EPSILON</c>.
    /// </summary>
    /// <remarks>
    /// <c>#define EQUAL_EPSILON 0.001</c>, from <c>public/mathlib/mathlib.h</c>. Used here for the
    /// same test vrad applies when it decides a light has no falloff at all.
    /// </remarks>
    private const float AttenuationEpsilon = 0.001f;

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

        // **The strongest four by contribution, not the first four in file order.** A map lists its
        // lights in the order the compiler emitted them, which has nothing to do with what is near
        // anything. Strength here carries no n·d — the six faces disagree about direction and a single
        // ranking has to serve all of them — but it does carry a spotlight's cone, which is the light's
        // own direction and the same for every face (B665).
        Span<int> chosen = stackalloc int[MaximumLocalLights];
        Span<float> strengths = stackalloc float[MaximumLocalLights];

        int count = Choose(lights, x, y, z, chosen, strengths, style, traceKeeps: null, reached: null);

        if (count == 0)
        {
            return cube;
        }

        return new AmbientCube(
            Face(cube.PositiveX, lights, chosen, count, x, y, z, 1f, 0f, 0f, style),
            Face(cube.NegativeX, lights, chosen, count, x, y, z, -1f, 0f, 0f, style),
            Face(cube.PositiveY, lights, chosen, count, x, y, z, 0f, 1f, 0f, style),
            Face(cube.NegativeY, lights, chosen, count, x, y, z, 0f, -1f, 0f, style),
            Face(cube.PositiveZ, lights, chosen, count, x, y, z, 0f, 0f, 1f, style),
            Face(cube.NegativeZ, lights, chosen, count, x, y, z, 0f, 0f, -1f, style));
    }

    /// <summary>Every style at its stored value.</summary>
    private static readonly Func<int, float> Steady = static _ => 1f;

    /// <summary>The strongest lights at a point, as the engine's <c>locallight[]</c> would hold.</summary>
    /// <param name="lights">Every world light the map carries.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="into">Where to put them; at most <see cref="MaximumLocalLights"/> are written.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <param name="traceKeeps">
    /// The light cache's visibility trace by index into <paramref name="lights"/> (<see cref="TraceKeeps"/>); null traces
    /// none, which is what a caller passing `FUN_1801b9570` flags bit 0 asks for (B666).
    /// </param>
    /// <returns>How many were written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lights"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="into"/> is too short.</exception>
    /// <remarks>
    /// **The same selection <see cref="AddTo"/> makes, extracted so the two cannot disagree.** A
    /// caller passing these to a shader and a caller folding them into a cube must be talking about
    /// the same four lights, or a model would take one set of lights in its diffuse and another in
    /// its highlight.
    ///
    /// **These are ADDED to the cube, never blended with it**, and each light belongs to exactly one
    /// of the two — `PixelShaderDoLightingLinear` accumulates `linearColor += ambient` and then
    /// `+=` each light. So a caller that passes these on must NOT also call <see cref="AddTo"/>:
    /// see `LocalLightConformanceTests`, which exists to make that mistake fail rather than merely
    /// look like a lighting change.
    ///
    /// **There is no double-count with the baked cube either**, and that was checked rather than
    /// assumed. vrad's `ComputeAmbientFromSphericalSamples` builds a leaf cube from rays cast at
    /// surfaces — bounce — plus `AddEmitSurfaceLights`, and Valve's comment there says why only
    /// those: *"there are a ton of them and they are often so dim that they get filtered out by
    /// r_worldlightmin"*. Point and spot lamps are not in the lump; they are meant to arrive at
    /// runtime, which is what this returns them for.
    /// </remarks>
    public static int Strongest(
        IReadOnlyList<BspWorldLight> lights, float x, float y, float z, Span<LocalLight> into, Func<int, float>? styleScale = null,
        Func<int, bool>? traceKeeps = null)
    {
        ArgumentNullException.ThrowIfNull(lights);

        Func<int, float> style = styleScale ?? Steady;

        if (into.Length < MaximumLocalLights)
        {
            throw new ArgumentException(
                $"Room for {MaximumLocalLights} lights is needed.", nameof(into));
        }

        Span<int> chosen = stackalloc int[MaximumLocalLights];
        Span<float> strengths = stackalloc float[MaximumLocalLights];

        int count = Choose(lights, x, y, z, chosen, strengths, style, traceKeeps, reached: null);

        Write(lights, chosen[..count], into, style);

        return count;
    }

    /// <summary>The chosen lights as the shader takes them, at their style values.</summary>
    private static void Write(IReadOnlyList<BspWorldLight> lights, ReadOnlySpan<int> chosen, Span<LocalLight> into, Func<int, float> style)
    {
        for (int slot = 0; slot < chosen.Length; slot++)
        {
            into[slot] = Local(lights[chosen[slot]], style);
        }
    }

    /// <summary>One light as the shader takes it, at its style value.</summary>
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
            surface || light.Kind == WorldLightKind.Spotlight);
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

    /// <summary>Which lights <see cref="Strongest"/> would take, as indices into <paramref name="lights"/>, strongest first.</summary>
    /// <param name="lights">The lights to rank.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="chosen">Where to put them; at most <see cref="MaximumLocalLights"/> are written.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <param name="traceKeeps">The visibility trace by index (<see cref="TraceKeeps"/>); null traces none.</param>
    /// <returns>How many were written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lights"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="chosen"/> is too short.</exception>
    /// <remarks>
    /// For a caller that keeps a ranked set and ranks later lights against it — a static prop's handle caching its styled
    /// lights (`FUN_1801ba590`'s `+0x88`, B607). Each light's strength is its own, so the strongest of a kept set plus
    /// more are the strongest of everything plus more.
    /// </remarks>
    public static int StrongestIndices(
        IReadOnlyList<BspWorldLight> lights, float x, float y, float z, Span<int> chosen, Func<int, float>? styleScale = null,
        Func<int, bool>? traceKeeps = null)
    {
        ArgumentNullException.ThrowIfNull(lights);

        if (chosen.Length < MaximumLocalLights)
        {
            throw new ArgumentException($"Room for {MaximumLocalLights} lights is needed.", nameof(chosen));
        }

        Span<float> strengths = stackalloc float[MaximumLocalLights];

        return Choose(lights, x, y, z, chosen, strengths, styleScale ?? Steady, traceKeeps, reached: null);
    }

    /// <summary>Ranks the local lights at a point and keeps the strongest few.</summary>
    /// <remarks>
    /// `traceKeeps` is the visibility trace by index (<see cref="TraceKeeps"/>); null traces none, as a caller passing flags bit
    /// 0 — a live dlight, a static prop's handle list — skips it. `reached`, when given, collects every light that passed
    /// the cutoff and the trace, chosen or not: the fold's set.
    /// </remarks>
    private static int Choose(
        IReadOnlyList<BspWorldLight> lights,
        float x, float y, float z,
        Span<int> chosen,
        Span<float> strengths,
        Func<int, float> style,
        Func<int, bool>? traceKeeps,
        List<int>? reached)
    {
        // Nothing folded: only the slots matter, so trace strongest-down until they are decided (same lights, fewer traces).
        if (traceKeeps is { } slotsOnly && reached is null)
        {
            return ChooseStrongestFirst(lights, x, y, z, chosen, strengths, style, slotsOnly);
        }

        int count = 0;

        for (int index = 0; index < lights.Count; index++)
        {
            if (!Candidate(lights[index], x, y, z, style, out float strength))
            {
                continue;
            }

            // **Then the visibility trace, where `FUN_1801b9570` makes it**: after the brightest-channel cutoff and before
            // anything is ranked (B666). A light it rejects has a ratio of zero, so it takes no slot and is not folded.
            if (traceKeeps is { } keeps && !keeps(index))
            {
                continue;
            }

            reached?.Add(index);

            if (!Ranks(lights[index], strength))
            {
                continue;
            }

            Insert(chosen, strengths, ref count, index, strength);
        }

        return count;
    }

    /// <summary>
    /// Whether a light passes the trace function's cutoff (<see cref="Admitted"/>), and the strength it is ranked by.
    /// </summary>
    /// <remarks>
    /// **Ranked by luminance times falloff** (B453): `engine.dll` `FUN_1801b60a0` weighs the intensity by Rec.601's 0.299,
    /// 0.587, 0.114 (`0x18047be38`) — not by its brightest channel, which ranked a saturated blue lamp above a
    /// brighter-looking green one.
    ///
    /// **A NaN strength is refused here, for every kind.** It was the two `r_worldlightmin` comparisons' side effect until a surface light
    /// was exempted from them; one reaching a slot would break <see cref="Outranks"/>'s order and poison a cached cube. An
    /// addition of ours: the engine has no such test, and a lump holding a NaN is a stranger's.
    ///
    /// `strength` is meaningful only when this returns true: a light it rejects is given 0, without its luminance being
    /// computed.
    /// </remarks>
    private static bool Candidate(BspWorldLight light, float x, float y, float z, Func<int, float> style, out float strength)
    {
        if (!Admitted(light, x, y, z, style, out float falloff))
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

    /// <summary>
    /// The ranking's order, the one definition of it: stronger first, and of two equal lights the EARLIER in the list.
    /// </summary>
    /// <remarks>
    /// <see cref="Insert"/> sees lights in index order, so for it the tie never favours the newcomer — the engine's
    /// evict-the-weakest order. <see cref="ChooseStrongestFirst"/> sorts by the same rule. A NaN strength never reaches
    /// it: <see cref="Candidate"/> refuses one first.
    /// </remarks>
    private static bool Outranks(float strength, int index, float other, int otherIndex) =>
        strength > other || (!(strength < other) && index < otherIndex);

    /// <summary><see cref="Outranks"/> as a sort's comparison, cached so a sort allocates nothing.</summary>
    private static readonly Comparison<(float Strength, int Index)> StrongerFirst = static (a, b) =>
        (Outranks(b.Strength, b.Index, a.Strength, a.Index) ? 1 : 0) - (Outranks(a.Strength, a.Index, b.Strength, b.Index) ? 1 : 0);

    /// <summary>
    /// <see cref="Choose"/> for a caller that folds nothing: every admitted light ranked first, then traced strongest-down
    /// until the slots are full.
    /// </summary>
    /// <remarks>
    /// **The same lights as tracing every one, in the same order** — pinned by
    /// `LocalLightVisibilityConformanceTests.Strongest_SixLampsTheStrongestBlocked_TakesTheSameLightsAsTracingEveryOne`.
    /// A light that is not traced is one that could not take a slot: four seen lights that outrank it are already in. The
    /// order is <see cref="Outranks"/>, the one <see cref="Insert"/> keeps. A light under the luminance minimum is never
    /// traced: it would take no slot either way. The engine traces every admitted light (B666); this changes the cost, not
    /// the answer. A caller that folds (<see cref="Split"/>) still traces every admitted light, since every seen loser is
    /// added to the cube.
    ///
    /// **Allocates nothing**: a list of up to <see cref="StackCandidates"/> lights is ranked on the stack, sorted by a cached
    /// comparison — a static prop under a live dlight ranks this way every frame (`StaticPropFullLightingAt`). Only a
    /// longer list rents, and only it can allocate when the pool has been trimmed.
    /// </remarks>
    private static int ChooseStrongestFirst(
        IReadOnlyList<BspWorldLight> lights,
        float x, float y, float z,
        Span<int> chosen,
        Span<float> strengths,
        Func<int, float> style,
        Func<int, bool> traceKeeps)
    {
        // On the stack up to a bound, so the common case owes nothing to the pool: `ArrayPool.Shared` drops what it holds
        // under memory pressure, and a ranking that rented would then allocate afresh (B667). Sized to the list, which bounds
        // the candidates, so a six-lamp list clears 48 bytes rather than the bound's 512.
        (float Strength, int Index)[]? pooled =
            lights.Count > StackCandidates ? ArrayPool<(float Strength, int Index)>.Shared.Rent(lights.Count) : null;
        Span<(float Strength, int Index)> buffer = pooled is null ? stackalloc (float Strength, int Index)[lights.Count] : pooled;

        try
        {
            int candidates = 0;

            for (int index = 0; index < lights.Count; index++)
            {
                if (Candidate(lights[index], x, y, z, style, out float strength) && Ranks(lights[index], strength))
                {
                    buffer[candidates++] = (strength, index);
                }
            }

            Span<(float Strength, int Index)> ranked = buffer[..candidates];
            ranked.Sort(StrongerFirst);

            int count = 0;

            for (int at = 0; at < ranked.Length && count < MaximumLocalLights; at++)
            {
                if (traceKeeps(ranked[at].Index))
                {
                    chosen[count] = ranked[at].Index;
                    strengths[count] = ranked[at].Strength;
                    count++;
                }
            }

            return count;
        }
        finally
        {
            if (pooled is not null)
            {
                ArrayPool<(float Strength, int Index)>.Shared.Return(pooled);
            }
        }
    }

    /// <summary>
    /// The longest list <see cref="ChooseStrongestFirst"/> ranks on the stack, at most 512 bytes of it; a longer list rents
    /// from the pool instead.
    /// </summary>
    internal const int StackCandidates = 64;

    /// <summary>Whether a light reaches a point at all: a local kind, positive falloff, and the trace function's `r_worldlightmin`.</summary>
    /// <remarks>
    /// `engine.dll` `FUN_1801b8e20` returns before anything is added when the BRIGHTEST channel times the falloff is below
    /// `r_worldlightmin` — a different test from the ranking's luminance one, and the only one that drops a light outright.
    ///
    /// **The falloff is the trace function's ratio, cone included** (B665): `FUN_1801b60a0` ranks by luminance times the
    /// ratio `FUN_1801b99a0` → `FUN_1801b9570` returns, which carries a spotlight's cone, so a spot whose cone misses the
    /// point takes no slot. Ranked by distance alone, such spots evicted the lamps that do reach it. A surface light skips
    /// the minimum: `FUN_1801b9570` guards it with `type != 0`.
    /// </remarks>
    private static bool Admitted(BspWorldLight light, float x, float y, float z, Func<int, float> style, out float falloff)
    {
        falloff = IsLocal(light.Kind) ? Ratio(light, x, y, z) * style(light.Style) : 0f;

        return falloff > 0f && (light.Kind == WorldLightKind.Surface ||
            falloff * Math.Max(light.Intensity.Red, Math.Max(light.Intensity.Green, light.Intensity.Blue)) >= WorldLightMinimum);
    }

    /// <summary>Rec.601 luminance, the lightcache's ranking weight (`engine.dll` `0x18047be38`).</summary>
    private static float Luminance(BspWorldLight light) =>
        (0.299f * light.Intensity.Red) + (0.587f * light.Intensity.Green) + (0.114f * light.Intensity.Blue);

    /// <summary>
    /// The strongest four as local lights, and every other light reaching the point folded into <paramref name="cube"/>.
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
    /// "ambient, and lights that aren't in locallight[]". Every light admitted, seen and not chosen is folded, which is
    /// the same set the engine's evict-the-weakest order arrives at; one the trace rejects is neither (B666).
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
        WriteSlots(state, into, styleScale ?? Steady);

        return state.Count;
    }

    /// <summary>A state from a whole list at once, as <see cref="Split"/> ranks it: the strongest four in slots, strongest first, the rest folded.</summary>
    /// <param name="lights">Every world light the map carries.</param>
    /// <param name="x">World position being lit.</param>
    /// <param name="y">World position being lit.</param>
    /// <param name="z">World position being lit.</param>
    /// <param name="cube">The cube it starts from.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <param name="traceKeeps">The visibility trace by index (<see cref="TraceKeeps"/>); null traces none.</param>
    /// <returns>The state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lights"/> is null.</exception>
    public static LightingState Build(
        IReadOnlyList<BspWorldLight> lights, float x, float y, float z, AmbientCube cube,
        Func<int, float>? styleScale = null, Func<int, bool>? traceKeeps = null)
    {
        ArgumentNullException.ThrowIfNull(lights);

        Func<int, float> style = styleScale ?? Steady;
        Span<int> chosen = stackalloc int[MaximumLocalLights];
        Span<float> strengths = stackalloc float[MaximumLocalLights];
        List<int> rest = [];

        // One pass, so each light is traced once: the chosen go to the slots, the rest of what it reached to the cube.
        int count = Choose(lights, x, y, z, chosen, strengths, style, traceKeeps, rest);
        LightingState state = new(cube) { Count = count };

        for (int slot = 0; slot < count; slot++)
        {
            state.Slots[slot] = lights[chosen[slot]];
            state.Strengths[slot] = strengths[slot];
        }

        ReadOnlySpan<int> taken = chosen[..count];

        foreach (int index in rest)
        {
            if (!taken.Contains(index))
            {
                state.Cube = Fold(state.Cube, lights[index], x, y, z, style);
            }
        }

        return state;
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
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static void Add(
        LightingState state, BspWorldLight light, float x, float y, float z, Func<int, float>? styleScale = null, bool dynamicLight = false)
    {
        ArgumentNullException.ThrowIfNull(state);

        Func<int, float> style = styleScale ?? Steady;

        if (!Candidate(light, x, y, z, style, out float strength))
        {
            return;
        }

        BspWorldLight folded = light;

        if (Ranks(light, strength))
        {
            if (state.Count < MaximumLocalLights)
            {
                state.Slots[state.Count] = light;
                state.Strengths[state.Count] = strength;
                state.Count++;

                return;
            }

            float below = dynamicLight ? DynamicLightEvictionThreshold : strength;
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
            }
        }

        state.Cube = Fold(state.Cube, folded, x, y, z, style);
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
    /// <returns>The cube; the state is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static AmbientCube FoldSlots(LightingState state, float x, float y, float z, Func<int, float>? styleScale = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        AmbientCube cube = state.Cube;

        for (int slot = 0; slot < state.Count; slot++)
        {
            cube = Fold(cube, state.Slots[slot], x, y, z, styleScale);
        }

        return cube;
    }

    /// <summary>A state's slots as the shader takes them.</summary>
    /// <param name="state">The state.</param>
    /// <param name="styleScale">Each light style's value over 264; one for every style when null.</param>
    /// <returns>Exactly <see cref="LightingState.Count"/> local lights.</returns>
    internal static LocalLight[] Slotted(LightingState state, Func<int, float>? styleScale)
    {
        LocalLight[] locals = new LocalLight[state.Count];

        WriteSlots(state, locals, styleScale ?? Steady);

        return locals;
    }

    private static void WriteSlots(LightingState state, Span<LocalLight> into, Func<int, float> style)
    {
        for (int slot = 0; slot < state.Count; slot++)
        {
            into[slot] = Local(state.Slots[slot], style);
        }
    }

    /// <summary>Whether a light's contribution at a point is positive, as a static prop's handle list admits it.</summary>
    /// <param name="light">The world light.</param>
    /// <param name="x">The handle's lighting origin.</param>
    /// <param name="y">The handle's lighting origin.</param>
    /// <param name="z">The handle's lighting origin.</param>
    /// <returns>True when it reaches.</returns>
    /// <remarks>
    /// `engine.dll` `FUN_1801b99a0` → `FUN_1801b9570` with flags `9|2`: the value 8 skips the style scale and the value 1
    /// the visibility trace (<see cref="TraceKeeps"/>; B666's read, superseding B424's "2 skips the trace"), so this is the
    /// distance falloff (radius cutoff included), a spotlight's or surface light's cone, and
    /// `r_worldlightmin` for every kind but `emit_surface`. Sky and sky-ambient lights never reach a point this way.
    /// `FUN_1801b99a0`'s pre-culls against the prop's bounds are skipped: for an origin inside the bounds they reject
    /// nothing the point test keeps. ponytail: add them if a lighting origin outside its bounds ever matters.
    /// </remarks>
    public static bool Reaches(BspWorldLight light, float x, float y, float z)
    {
        if (!IsLocal(light.Kind))
        {
            return false;
        }

        float falloff = Ratio(light, x, y, z);

        if (falloff <= 0f)
        {
            return false;
        }

        return light.Kind == WorldLightKind.Surface ||
            falloff * Math.Max(light.Intensity.Red, Math.Max(light.Intensity.Green, light.Intensity.Blue)) >= WorldLightMinimum;
    }

    /// <summary>
    /// A light's ratio at a point, as `FUN_1801b9570` computes it without the style or the trace: the distance falloff,
    /// times a spotlight's or surface light's cone.
    /// </summary>
    private static float Ratio(BspWorldLight light, float x, float y, float z)
    {
        float falloff = Distance(light, x, y, z);

        if (light.Kind is not (WorldLightKind.Spotlight or WorldLightKind.Surface))
        {
            return falloff;
        }

        float dx = light.Origin.X - x;
        float dy = light.Origin.Y - y;
        float dz = light.Origin.Z - z;
        float length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        return length > 0f ? falloff * Cone(light, dx / length, dy / length, dz / length) : falloff;
    }

    /// <summary>vrad's all-zero rule, applied once so no caller has to know it; a surface light's terms are the engine's 0/0/1.</summary>
    private static (float Constant, float Linear, float Quadratic) Normalised(BspWorldLight light)
    {
        if (light.Kind == WorldLightKind.Surface)
        {
            return (0f, 0f, 1f);
        }

        float constant = light.ConstantAttenuation;

        if (constant < AttenuationEpsilon &&
            light.LinearAttenuation < AttenuationEpsilon &&
            light.QuadraticAttenuation < AttenuationEpsilon)
        {
            constant = 1f;
        }

        return (constant, light.LinearAttenuation, light.QuadraticAttenuation);
    }

    /// <summary>Whether a light casts direct light from a position.</summary>
    /// <remarks>
    /// **The sun is excluded deliberately.** <c>emit_skylight</c> is directional and reaches only
    /// what can trace to a sky surface — <c>bspfile.h</c> calls it "directional light with no
    /// falloff (surface must trace to SKY texture)" — so treating it as a positional light would
    /// put the sun wherever its origin happens to be recorded. It is applied on its own path.
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
    /// Distance falloff times the style, as <c>LightDesc_t::ComputeLightAtPoints</c> computes it — what a folded light's
    /// cube face is scaled by, which applies the cone itself (<see cref="Face"/>).
    /// </summary>
    /// <remarks>
    /// **Times the light's style value over 264**, as the lightcache multiplies it (`engine.dll` `0x1801b8e20`): a
    /// switchable lamp turned off adds nothing. The ranking takes the style the same way, on <see cref="Ratio"/> rather
    /// than on this (<see cref="Admitted"/>).
    /// </remarks>
    private static float Falloff(BspWorldLight light, float x, float y, float z, Func<int, float> style) =>
        Distance(light, x, y, z) * style(light.Style);

    /// <summary>The distance falloff alone.</summary>
    private static float Distance(BspWorldLight light, float x, float y, float z)
    {
        float dx = light.Origin.X - x;
        float dy = light.Origin.Y - y;
        float dz = light.Origin.Z - z;

        float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);

        // **Clamped, not offset**: MaxSIMD( Four_Ones, dist2 ). The ambient reconstruction next
        // door uses 1 / (dist + 1) and the two are easy to confuse.
        distanceSquared = Math.Max(1f, distanceSquared);

        // `FUN_1800efe40` case 0: inverse square, whatever the attenuation terms hold; out only strictly past the radius.
        if (light.Kind == WorldLightKind.Surface)
        {
            return light.Radius != 0f && distanceSquared > light.Radius * light.Radius ? 0f : 1f / distanceSquared;
        }

        // Strictly less than, and a range of zero means no cutoff at all. Every light on
        // cp_process stores zero, so reading it as a real radius extinguishes the whole map.
        if (light.Radius != 0f && distanceSquared >= light.Radius * light.Radius)
        {
            return 0f;
        }

        float constant = light.ConstantAttenuation;
        float linear = light.LinearAttenuation;
        float quadratic = light.QuadraticAttenuation;

        // **A light with no attenuation at all is a constant-1 light, not an epsilon one.** vrad
        // normalises exactly this case when it builds a light (`lightmap.cpp`):
        //
        //     if ( constant_attn < EQUAL_EPSILON && linear_attn < EQUAL_EPSILON &&
        //          quadratic_attn < EQUAL_EPSILON )
        //         constant_attn = 1;
        //
        // EQUAL_EPSILON is 0.001 (`mathlib.h`). This was first transcribed from
        // `ComputeLightAtPoints`, whose `else falloff = Four_Epsilons` branch guards the
        // reciprocal rather than describing the light — and rendering it as float.Epsilon, the
        // smallest denormal, made 1/falloff infinite. Four capture points reported a luminance of
        // ∞ and the unit tests all passed, because none of them used a light with no attenuation.
        if (constant < AttenuationEpsilon &&
            linear < AttenuationEpsilon &&
            quadratic < AttenuationEpsilon)
        {
            constant = 1f;
        }

        float falloff = constant;

        if (linear != 0f)
        {
            falloff += linear * MathF.Sqrt(distanceSquared);
        }

        if (quadratic != 0f)
        {
            falloff += quadratic * distanceSquared;
        }

        return 1f / falloff;
    }

    /// <summary>Adds every chosen light's contribution to one face of the cube.</summary>
    private static (float Red, float Green, float Blue) Face(
        (float Red, float Green, float Blue) face,
        IReadOnlyList<BspWorldLight> lights,
        ReadOnlySpan<int> chosen,
        int count,
        float x, float y, float z,
        float normalX, float normalY, float normalZ,
        Func<int, float> style)
    {
        for (int slot = 0; slot < count; slot++)
        {
            face = FaceOf(face, lights[chosen[slot]], x, y, z, normalX, normalY, normalZ, style);
        }

        return face;
    }

    /// <summary>One light's contribution to one face of the cube.</summary>
    private static (float Red, float Green, float Blue) FaceOf(
        (float Red, float Green, float Blue) face,
        BspWorldLight light,
        float x, float y, float z,
        float normalX, float normalY, float normalZ,
        Func<int, float> style)
    {
        float dx = light.Origin.X - x;
        float dy = light.Origin.Y - y;
        float dz = light.Origin.Z - z;

        float length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        if (length <= 0f)
        {
            return face;
        }

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

    /// <summary>A spotlight's angular attenuation for a direction, or zero outside the cone.</summary>
    /// <remarks>
    /// <c>dot2</c> is negated because <paramref name="dx"/> points from the surface toward the
    /// light while the light's normal points the way it shines.
    ///
    /// **It is then used TWICE, which is the part this originally missed (B122).** vrad multiplies
    /// the falloff by <c>dot2</c> as a plain cosine — a spotlight dims away from its axis everywhere,
    /// not only in the penumbra — and separately applies the fringe between the inner and outer
    /// cones (<c>lightmap.cpp:1929</c>-1942):
    ///
    /// <code>
    /// out.m_flFalloff = MulSIMD( out.m_flFalloff, dot2 );
    /// mult = ( dot2 - stopdot2 ) / ( stopdot - stopdot2 ), clamped
    /// </code>
    ///
    /// Returning only the fringe left a light at full strength anywhere inside its inner cone. An
    /// on-axis test cannot see that, because there <c>dot2</c> is one — which is why the suite held
    /// the wrong behaviour while passing.
    ///
    /// <c>emit_surface</c> takes the same cosine once and no fringe, and is lit only past 0.01
    /// (`FUN_1800efbf0` case 0, `Engine_WorldLightAngle`, `leaf_ambient_lighting.cpp:68-72`: "behind light surface").
    ///
    /// The mask is applied AFTER the exponent, as Valve does and for their stated reason: it masks
    /// "any invalid results from pow function". Zeroing first would leave a negative scale to be
    /// raised to a power.
    /// </remarks>
    private static float Cone(BspWorldLight light, float dx, float dy, float dz)
    {
        float dot2 = -((dx * light.Normal.X) + (dy * light.Normal.Y) + (dz * light.Normal.Z));

        if (light.Kind == WorldLightKind.Surface)
        {
            return dot2 > 0.01f ? dot2 : 0f;
        }

        float spread = light.StopDot - light.StopDot2;

        // Hard falloff instead of divide by zero, which is the engine's own comment on this case.
        float oneOverSpread = spread > 1.0e-10f ? 1f / spread : 1f;

        float cone = Math.Min((dot2 - light.StopDot2) * oneOverSpread, 1f);

        if (light.Exponent is not (0f or 1f))
        {
            cone = MathF.Pow(cone, light.Exponent);
        }

        // The cosine and the fringe together, in that order, as the engine composes them.
        return dot2 > light.StopDot2 ? dot2 * cone : 0f;
    }

    /// <summary>Keeps the strongest lights seen so far, brightest first.</summary>
    private static void Insert(
        Span<int> chosen, Span<float> strengths, ref int count, int index, float strength)
    {
        int at = count < MaximumLocalLights ? count : MaximumLocalLights - 1;

        if (count == MaximumLocalLights && !Outranks(strength, index, strengths[at], chosen[at]))
        {
            return;
        }

        strengths[at] = strength;
        chosen[at] = index;

        if (count < MaximumLocalLights)
        {
            count++;
        }

        for (int slot = at; slot > 0 && Outranks(strengths[slot], chosen[slot], strengths[slot - 1], chosen[slot - 1]); slot--)
        {
            (strengths[slot], strengths[slot - 1]) = (strengths[slot - 1], strengths[slot]);
            (chosen[slot], chosen[slot - 1]) = (chosen[slot - 1], chosen[slot]);
        }
    }
}
