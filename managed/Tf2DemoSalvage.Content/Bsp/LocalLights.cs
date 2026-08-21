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
    /// <returns>The cube with direct light added to each face.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lights"/> is null.</exception>
    public static AmbientCube AddTo(
        AmbientCube cube, IReadOnlyList<BspWorldLight> lights, float x, float y, float z)
    {
        ArgumentNullException.ThrowIfNull(lights);

        if (lights.Count == 0)
        {
            return cube;
        }

        // **The strongest four by contribution, not the first four in file order.** A map lists its
        // lights in the order the compiler emitted them, which has nothing to do with what is near
        // anything. Strength here is the falloff alone — distance and range — because the six faces
        // disagree about direction and a single ranking has to serve all of them.
        Span<int> chosen = stackalloc int[MaximumLocalLights];
        Span<float> strengths = stackalloc float[MaximumLocalLights];
        int count = 0;

        for (int index = 0; index < lights.Count; index++)
        {
            BspWorldLight light = lights[index];

            if (!IsLocal(light.Kind))
            {
                continue;
            }

            float falloff = Falloff(light, x, y, z);

            if (falloff <= 0f)
            {
                continue;
            }

            // Ranked by the light it would cast at its brightest channel, so a dim lamp close by
            // does not displace a floodlight just beyond it.
            float strength = falloff * Math.Max(
                light.Intensity.Red, Math.Max(light.Intensity.Green, light.Intensity.Blue));

            Insert(chosen, strengths, ref count, index, strength);
        }

        if (count == 0)
        {
            return cube;
        }

        return new AmbientCube(
            Face(cube.PositiveX, lights, chosen, count, x, y, z, 1f, 0f, 0f),
            Face(cube.NegativeX, lights, chosen, count, x, y, z, -1f, 0f, 0f),
            Face(cube.PositiveY, lights, chosen, count, x, y, z, 0f, 1f, 0f),
            Face(cube.NegativeY, lights, chosen, count, x, y, z, 0f, -1f, 0f),
            Face(cube.PositiveZ, lights, chosen, count, x, y, z, 0f, 0f, 1f),
            Face(cube.NegativeZ, lights, chosen, count, x, y, z, 0f, 0f, -1f));
    }

    /// <summary>Whether a light casts direct light from a position.</summary>
    /// <remarks>
    /// **The sun is excluded deliberately.** <c>emit_skylight</c> is directional and reaches only
    /// what can trace to a sky surface — <c>bspfile.h</c> calls it "directional light with no
    /// falloff (surface must trace to SKY texture)" — so treating it as a positional light would
    /// put the sun wherever its origin happens to be recorded. It is applied on its own path.
    ///
    /// **<c>emit_surface</c> is excluded too, and the map proves why.** All 108 of cp_process's
    /// surface lights carry attenuation terms of exactly zero, with intensities around 7,000 to
    /// 8,300. A light with no falloff reaches everywhere at full strength — vrad's normalisation
    /// turns the all-zero case into <c>constant_attn = 1</c>, so four of them dominated every model
    /// on the map and the middle capture point drew at a luminance of 6.3 with no lamp anywhere
    /// near it.
    ///
    /// That absent falloff is the evidence rather than an inconvenience: an area light for the
    /// radiosity solver has no distance term because it is never evaluated at a distance. It is
    /// resolved at compile time into the lightmaps and into the leaf ambient cube, so applying it
    /// again at runtime both double-counts it and never attenuates it.
    /// </remarks>
    private static bool IsLocal(WorldLightKind kind) =>
        kind is WorldLightKind.Point or WorldLightKind.Spotlight or WorldLightKind.QuakeLight;

    /// <summary>
    /// Distance falloff, as <c>LightDesc_t::ComputeLightAtPoints</c> computes it.
    /// </summary>
    private static float Falloff(BspWorldLight light, float x, float y, float z)
    {
        float dx = light.Origin.X - x;
        float dy = light.Origin.Y - y;
        float dz = light.Origin.Z - z;

        float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);

        // **Clamped, not offset**: MaxSIMD( Four_Ones, dist2 ). The ambient reconstruction next
        // door uses 1 / (dist + 1) and the two are easy to confuse.
        distanceSquared = Math.Max(1f, distanceSquared);

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
        float normalX, float normalY, float normalZ)
    {
        float red = face.Red;
        float green = face.Green;
        float blue = face.Blue;

        for (int slot = 0; slot < count; slot++)
        {
            BspWorldLight light = lights[chosen[slot]];

            float dx = light.Origin.X - x;
            float dy = light.Origin.Y - y;
            float dz = light.Origin.Z - z;

            float length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (length <= 0f)
            {
                continue;
            }

            dx /= length;
            dy /= length;
            dz /= length;

            // max(0, delta . normal): a face turned away from the light receives nothing, which is
            // what stops a lamp lighting the far side of everything it touches.
            float strength = Math.Max(0f, (dx * normalX) + (dy * normalY) + (dz * normalZ));

            if (strength <= 0f)
            {
                continue;
            }

            if (light.Kind is WorldLightKind.Spotlight or WorldLightKind.Surface)
            {
                strength *= Cone(light, dx, dy, dz);

                if (strength <= 0f)
                {
                    continue;
                }
            }

            float scale = strength * Falloff(light, x, y, z) * IntensityScale;

            red += scale * light.Intensity.Red;
            green += scale * light.Intensity.Green;
            blue += scale * light.Intensity.Blue;
        }

        return (red, green, blue);
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
    /// <c>emit_surface</c> takes the same cosine (<c>lightmap.cpp:1907</c>) and no fringe. Both kinds
    /// come through here, and the fringe terms of a surface light are zero, so the arithmetic is the
    /// same for it either way. Only spotlights matter in practice: a surface light carries no falloff
    /// terms at all and is excluded before this — all eight of `koth_harvest_final`'s are.
    ///
    /// The mask is applied AFTER the exponent, as Valve does and for their stated reason: it masks
    /// "any invalid results from pow function". Zeroing first would leave a negative scale to be
    /// raised to a power.
    /// </remarks>
    private static float Cone(BspWorldLight light, float dx, float dy, float dz)
    {
        float dot2 = -((dx * light.Normal.X) + (dy * light.Normal.Y) + (dz * light.Normal.Z));

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

        if (count == MaximumLocalLights && strength <= strengths[at])
        {
            return;
        }

        strengths[at] = strength;
        chosen[at] = index;

        if (count < MaximumLocalLights)
        {
            count++;
        }

        for (int slot = at; slot > 0 && strengths[slot] > strengths[slot - 1]; slot--)
        {
            (strengths[slot], strengths[slot - 1]) = (strengths[slot - 1], strengths[slot]);
            (chosen[slot], chosen[slot - 1]) = (chosen[slot - 1], chosen[slot]);
        }
    }
}
