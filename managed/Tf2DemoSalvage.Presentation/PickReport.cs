using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a pick is answered from: the last frame as it was drawn.</summary>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Tick">The whole tick being shown.</param>
/// <param name="Viewing">The camera the last frame was set up with, or null before the first.</param>
/// <param name="ViewportWidth">The viewport's width in pixels.</param>
/// <param name="ViewportHeight">The viewport's height in pixels.</param>
/// <param name="Drawn">The entities the frame drew, or null.</param>
/// <param name="Particles">The particle batches the frame drew.</param>
/// <param name="Bursts">The one-shot effects running.</param>
/// <param name="Effects">The particle systems those bursts run in.</param>
public readonly record struct PickFrame(
    LoadedMap? Loaded,
    int Tick,
    FreeCamera? Viewing,
    int ViewportWidth,
    int ViewportHeight,
    IReadOnlyList<SceneProp>? Drawn,
    IReadOnlyList<ParticleBatch> Particles,
    IReadOnlyList<ParticleBurst> Bursts,
    ParticleEffects Effects);

/// <summary>What lies under a pixel of a frame, as one line of text — the `TF2VIEW_PICK` instrument.</summary>
/// <remarks>
/// **Moved out of `MainForm` whole** (D214, thin view slice 4), where it was `Pick`: the same ray, the same line. The
/// view keeps the request (the environment variable), the frame it hands over, and the log the line goes to.
/// </remarks>
public static class PickReport
{
    /// <summary>The line for a request, or null when there is none to answer.</summary>
    /// <param name="asked">The request: "x y", or "x y pixels" for everything within that many pixels; null for none.</param>
    /// <param name="frame">The last frame as it was drawn.</param>
    /// <returns>The line, or null for no request, a malformed one, or no frame yet.</returns>
    public static string? Describe(string? asked, in PickFrame frame) =>
        DescribeOver(asked, frame, frame.Loaded?.Level, frame.Loaded?.Assets?.ParticleMaterials, frame.Loaded?.Assets?.SpriteMaterials);

    /// <summary>`TF2VIEW_PICK="x y"`: logs what lies under that pixel of the shot — a debugging instrument.</summary>
    /// <remarks>
    /// Casts the camera's ray through the pixel (<see cref="FreeCamera.RayThrough"/>) and logs the world's hit, brushes and
    /// static props, then every drawn model whose origin lies near the ray, nearest the ray first. Built to name a glow seen
    /// on screen without guessing a camera to fly to it.
    /// </remarks>
    /// <param name="asked">The request, or null.</param>
    /// <param name="frame">The last frame as it was drawn; its map is not read here.</param>
    /// <param name="world">The map's geometry, or null for none.</param>
    /// <param name="particleMaterials">The particle materials by name, for naming a quad's.</param>
    /// <param name="spriteMaterials">The entity sprite materials by path, for the same.</param>
    /// <returns>The line, or null.</returns>
    internal static string? DescribeOver(
        string? asked,
        PickFrame frame,
        MapLevel? world,
        IReadOnlyDictionary<string, ParticleMaterial>? particleMaterials,
        IReadOnlyDictionary<string, EngineSprite>? spriteMaterials)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (asked is null ||
            asked.Split(' ', StringSplitOptions.RemoveEmptyEntries) is not [{ } xs, { } ys, .. { } more] ||
            !float.TryParse(xs, NumberStyles.Float, CultureInfo.InvariantCulture, out float px) ||
            !float.TryParse(ys, NumberStyles.Float, CultureInfo.InvariantCulture, out float py) ||
            frame.Viewing is not { } viewing)
        {
            return null;
        }

        (float dx, float dy, float dz) = viewing.RayThrough(px, py, frame.ViewportWidth, frame.ViewportHeight);
        Vector3 eye = new(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z);
        Vector3 ray = new(dx, dy, dz);
        System.Text.StringBuilder report = new();

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        report.Append(CultureInfo.InvariantCulture, $"pick ({px} {py}) at tick {frame.Tick} from ({eye.X:0} {eye.Y:0} {eye.Z:0}) along ({dx:0.000} {dy:0.000} {dz:0.000}): ");

        if (world is { } level)
        {
            Vector3 end = eye + (ray * 16000f);
            BspTrace hit = level.Trace((eye.X, eye.Y, eye.Z), (end.X, end.Y, end.Z), 0f);

            float wall = hit.Fraction * 16000f;
            Vector3 at = eye + (ray * wall);

            string? Owner(ParticleEffect effect) =>
                Enumerable.Range(0, effect.Particles.Count).Any(i => Vector3.Distance(effect.Particles.PositionOf(i), at) < 100f)
                    ? effect.System.Name
                    : effect.Children.Select(Owner).FirstOrDefault(static name => name is not null);

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            report.Append(CultureInfo.InvariantCulture, $"world at {wall:0} units ({at.X:0} {at.Y:0} {at.Z:0}) texinfo {hit.Texinfo} static prop {hit.StaticProp}; ");

            foreach (ParticleBurst burst in frame.Bursts)
            {
                // Only a burst with particles near the ray's hit, within 100 units, named down to the child that owns them.
                // A pick of 500 pixels or wider asks for the whole frame, so every live burst is named.
                // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
                if (frame.Effects.BurstEffect(burst.Key) is not { } effect ||
                    (Owner(effect) ?? (more is [{ } all] && float.Parse(all, CultureInfo.InvariantCulture) >= 500f ? effect.System.Name : null)) is not { } owner)
                {
                    continue;
                }

                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                report.Append(CultureInfo.InvariantCulture, $"{{burst {burst.Definition.Name} child {owner} key {burst.Key} tick {burst.Tick} stop {burst.StopTick} at ({burst.At.At.X:0} {burst.At.At.Y:0} {burst.At.At.Z:0}) forward ({burst.At.Forward.X:0.00} {burst.At.Forward.Y:0.00} {burst.At.Forward.Z:0.00}) end ({burst.End?.At.X:0} {burst.End?.At.Y:0} {burst.End?.At.Z:0}) own particles {effect.Particles.Count}{string.Concat(Enumerable.Range(0, Math.Min(effect.Particles.Count, 6)).Select(i => i * Math.Max(1, effect.Particles.Count / 6)).Select(i => string.Create(CultureInfo.InvariantCulture, $" #{i} age {effect.Particles.AgeOf(i):0.00} at ({effect.Particles.PositionOf(i).X:0} {effect.Particles.PositionOf(i).Y:0} {effect.Particles.PositionOf(i).Z:0}) radius {effect.Particles.RadiusOf(i):0.0} alpha {effect.Particles.AlphaOf(i):0.00}")))}}} ");
            }
        }

        foreach ((SceneProp prop, float along, float off) in (frame.Drawn ?? [])
            .Select(prop =>
            {
                Vector3 origin = new(prop.Pose.X, prop.Pose.Y, prop.Pose.Z);
                float along = Vector3.Dot(origin - eye, ray);

                return (prop, along, off: Vector3.Distance(origin, eye + (ray * along)));
            })
            .Where(static seen => seen.along > 0f && seen.off < 128f)
            .OrderBy(static seen => seen.off)
            .Take(8))
        {
            // The sequence is the DRAWN prop's own — what the frame posed with — beside what chose it (B523).
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            report.Append(CultureInfo.InvariantCulture, $"[{prop.ModelPath} entity {prop.EntityIndex} kind {prop.Kind} {along:0} along, {off:0} off, at ({prop.Pose.X:0.#} {prop.Pose.Y:0.#} {prop.Pose.Z:0.#}) sequence {prop.Pose.Sequence} slot {prop.Pose.Slot ?? "-"} jump {prop.Pose.JumpActivity?.ToString() ?? "-"} flags {prop.Pose.Flags?.ToString(CultureInfo.InvariantCulture) ?? "-"}] ");
        }

        // Every particle and sprite quad whose projected centre lands within 24 pixels of the pick — or within the third
        // number of `TF2VIEW_PICK="x y pixels"`, for a whole effect rather than one spot of it.
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        float within = more is [{ } wide] && float.TryParse(wide, NumberStyles.Float, CultureInfo.InvariantCulture, out float pixels)
            ? pixels
            : 24f;
        float[] m = viewing.ToMatrix();
        int width = frame.ViewportWidth;
        int height = frame.ViewportHeight;

        foreach (ParticleBatch batch in frame.Particles)
        {
            string named = particleMaterials?.FirstOrDefault(pair => pair.Value.Equals(batch.Material)).Key ??
                           spriteMaterials?.FirstOrDefault(pair => pair.Value.Material.Equals(batch.Material)).Key ?? "?";

            for (int i = 0; i + 5 < batch.Corners.Count; i += 6)
            {
                Vector3 centre = Vector3.Zero;

                for (int c = 0; c < 6; c++)
                {
                    centre += new Vector3(batch.Corners[i + c].X, batch.Corners[i + c].Y, batch.Corners[i + c].Z);
                }

                centre /= 6f;
                float w = (centre.X * m[3]) + (centre.Y * m[7]) + (centre.Z * m[11]) + m[15];

                if (w <= 0f)
                {
                    continue;
                }

                float sx = (((centre.X * m[0]) + (centre.Y * m[4]) + (centre.Z * m[8]) + m[12]) / w + 1f) * 0.5f * width;
                float sy = (1f - (((centre.X * m[1]) + (centre.Y * m[5]) + (centre.Z * m[9]) + m[13]) / w)) * 0.5f * height;

                if (MathF.Abs(sx - px) < within && MathF.Abs(sy - py) < within)
                {
                    DetailSpriteVertex first = batch.Corners[i];

                    // The size is the drawn quad's own: its first corner's distance from its centre, in world units.
                    float reach = Vector3.Distance(centre, new Vector3(first.X, first.Y, first.Z));

                    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                    report.Append(CultureInfo.InvariantCulture, $"<quad {named} centre ({centre.X:0} {centre.Y:0} {centre.Z:0}) at ({sx:0} {sy:0}) corner {reach:0.0} out rgba ({first.Red:0.00} {first.Green:0.00} {first.Blue:0.00} {first.Alpha:0.00}) blend {batch.Material.Blend}> ");
                }
            }
        }

        return report.ToString();
    }
}
