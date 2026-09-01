using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>What light reaches one model, and where that was sampled.</summary>
/// <param name="Light">The ambient cube, or null for a lightmapped brush entity.</param>
/// <param name="Sun">The sun, or null where the sky is not visible.</param>
/// <param name="X">Where it was sampled, which a worn item borrows from its wearer.</param>
/// <param name="Y">Where it was sampled.</param>
/// <param name="Z">Where it was sampled.</param>
/// <param name="Locals">
/// The nearest direct lights, at most <see cref="LocalLights.MaximumLocalLights"/>. Empty for a
/// brush entity, whose light is already in its lightmap, and empty where the map has no lamps near
/// enough to matter.
/// </param>
public readonly record struct ModelLight(
    AmbientCube? Light,
    SunLight? Sun,
    float X,
    float Y,
    float Z,
    IReadOnlyList<LocalLight> Locals);

/// <summary>
/// The ambient cube and sun each model is drawn with, cached on the point it was sampled at.
/// </summary>
/// <remarks>
/// **Split out of <see cref="EntityModelSet"/>'s draw loop on 2026-08-24** (B181). It was about
/// sixty of that loop's lines and none of them are about posing a model — the loop had become five
/// subsystems sharing an iteration variable, which is why the engine's stage boundaries were
/// invisible inside it.
///
/// **Lighting is not in Valve's bone path at all**, which is the argument for the seam being here
/// rather than anywhere else: <c>SetupBones</c> touches nothing about light, and the client samples
/// a model's cube through <c>CreateModelInstance</c> and the model render path
/// (finding 35 section 0).
/// </remarks>
public sealed class ModelLighting
{
    private readonly Func<SceneProp, ScenePose, (float X, float Y, float Z)> _illumination;
    private readonly ILogger _render;

    /// <summary>Creates a sampler.</summary>
    /// <param name="illumination">Where a model's light should be sampled, in world space.</param>
    /// <param name="render">Where it reports what drew black.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ModelLighting(
        Func<SceneProp, ScenePose, (float X, float Y, float Z)> illumination, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(illumination);
        ArgumentNullException.ThrowIfNull(render);

        _illumination = illumination;
        _render = render;
    }

    /// <summary>One entity's lighting, and the point it was sampled at.</summary>
    /// <remarks>
    /// The position is held as BITS because the question is whether the model is at the identical
    /// point, not whether it is near where it was — a tolerance would let a slow drift accumulate
    /// without ever refreshing.
    /// </remarks>
    private readonly record struct LitAt(
        int X, int Y, int Z, AmbientCube Light, SunLight? Sun, IReadOnlyList<LocalLight> Locals);

    private readonly Dictionary<int, LitAt> _lit = [];

    /// <summary>Models already reported as drawing unlit.</summary>
    private readonly HashSet<string> _reportedDark = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stopwatch ticks spent lighting, accumulated until the caller resets it.</summary>
    /// <remarks>
    /// **Posing owns about nine hundred milliseconds of every second** (B99), and it did two
    /// different jobs — bone matrices and lighting. This separates them, because the fix differs:
    /// bones are per-frame work an animation genuinely needs, while a stationary model's lighting
    /// cannot have changed since the last frame and was being recomputed anyway.
    /// </remarks>
    public long Ticks { get; set; }

    /// <summary>Samples the light one prop is drawn with, or returns what it had last frame.</summary>
    /// <param name="prop">The prop.</param>
    /// <param name="lightAt">The ambient cube at a world position, or null to leave models unlit.</param>
    /// <param name="sunAt">The sun at a world position, or null to apply no direct light.</param>
    /// <returns>The cube, the sun, and where they were sampled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **A model that has not moved is lit exactly as it was last frame** (B99). Lighting cost
    /// 320 ms of every second against 3.4 ms to draw the whole map, and nearly all of it recomputed
    /// an unchanged answer: a cube is an inverse-squared average over sixteen ambient samples,
    /// <c>LocalLights</c> ranks all 477 of a map's world lights to pick four and evaluates a falloff
    /// per light for six faces, and the sun traces a ray through the BSP to ask whether the sky is
    /// visible.
    ///
    /// **Keyed on the illumination point, compared exactly.** The point is derived from the pose,
    /// and a held pose interpolates to a bit-identical <c>ScenePose</c> — so an entity that has not
    /// moved produces the identical point and one that has moved at all produces a different one.
    /// Keyed on the entity as well, because two models can stand in one place and must not share a
    /// slot. Map lights never move, so nothing else can invalidate this.
    /// </remarks>
    public ModelLight For(
        SceneProp prop,
        Func<float, float, float, PointLighting>? lightAt,
        Func<float, float, float, SunLight?>? sunAt)
    {
        ArgumentNullException.ThrowIfNull(prop);

        ScenePose pose = prop.Pose;

        (float x, float y, float z) = _illumination(prop, pose);

        long started = Stopwatch.GetTimestamp();

        // **A brush entity is lightmapped, so it takes no cube and no sun (B131).** Its faces were
        // lit by vrad exactly as the wall's were and the samples travel on the vertices; the
        // shader's ambient-cube branch OVERWRITES the lightmap sample rather than adding to it, so
        // supplying a cube here is precisely what made an open door a flat panel against a shaded
        // corridor. Null is what LightmappedGeneric means: the light is already in the atlas.
        if (prop.Kind == SceneModelKind.Brush)
        {
            return new ModelLight(null, null, x, y, z, []);
        }

        // Compared as bits rather than as floats, which is what "the identical point" means and is
        // also how it is said without tripping the equality analyser: this is an identity test, not
        // an approximation.
        (int bitsX, int bitsY, int bitsZ) = (
            BitConverter.SingleToInt32Bits(x),
            BitConverter.SingleToInt32Bits(y),
            BitConverter.SingleToInt32Bits(z));

        AmbientCube? light;
        SunLight? sun;
        IReadOnlyList<LocalLight> locals;

        if (_lit.TryGetValue(prop.EntityIndex, out LitAt cached) &&
            cached.X == bitsX && cached.Y == bitsY && cached.Z == bitsZ)
        {
            light = cached.Light;
            sun = cached.Sun;
            locals = cached.Locals;
        }
        else
        {
            PointLighting sampled = lightAt is null ? PointLighting.None : lightAt(x, y, z);

            light = sampled.Cube;
            sun = sunAt?.Invoke(x, y, z);
            locals = sampled.Locals;

            _lit[prop.EntityIndex] = new LitAt(bitsX, bitsY, bitsZ, sampled.Cube, sun, locals);
        }

        Ticks += Stopwatch.GetTimestamp() - started;

        Report(prop, lightAt, light);

        return new ModelLight(light, sun, x, y, z, locals);
    }

    /// <summary>Says once per model when it is drawn with no light at all.</summary>
    /// <remarks>
    /// **A model lit by nothing draws black, and that is worth saying out loud.** The cube comes
    /// from the leaf a model stands in, and a player's origin is at its FEET — so a point resting
    /// exactly on a floor plane can land in the solid leaf below it, which carries no light. It
    /// shows as a player turning black in some places and recovering in others, which reads as a
    /// lighting quirk rather than as a lookup landing in solid.
    ///
    /// Logged with the position, because the defect is positional and a count would not let anyone
    /// go and look at the spot.
    /// </remarks>
    private void Report(
        SceneProp prop, Func<float, float, float, PointLighting>? lightAt, AmbientCube? light)
    {
        if (lightAt is null || light is not { } cube || !IsUnlit(cube) ||
            !_reportedDark.Add(prop.ModelPath))
        {
            return;
        }

        ScenePose pose = prop.Pose;

        _render.LogWarning(
            "{Message}",
            $"{prop.ModelPath} is lit by nothing at ({pose.X:0},{pose.Y:0},{pose.Z:0}); " +
            $"its leaf carries no ambient light, so it draws black");
    }

    /// <summary>Whether a cube carries no light on any face.</summary>
    public static bool IsUnlit(AmbientCube cube) =>
        cube.PositiveX == (0f, 0f, 0f) &&
        cube.NegativeX == (0f, 0f, 0f) &&
        cube.PositiveY == (0f, 0f, 0f) &&
        cube.NegativeY == (0f, 0f, 0f) &&
        cube.PositiveZ == (0f, 0f, 0f) &&
        cube.NegativeZ == (0f, 0f, 0f);
}
