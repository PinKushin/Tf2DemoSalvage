using System;
using System.Collections.Generic;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The light a map casts at a world position: its bounce, its lamps and its sun.</summary>
/// <remarks>
/// **This is the engine's query, and it belongs behind an interface rather than on the window.**
/// <c>IVEngineClient</c> declares
///
/// <code>
/// // Computes light due to dynamic lighting at a point
/// // If the normal isn't specified, then it'll return the maximum lighting
/// // If pBoxColors is specified (it's an array of 6), then it'll copy the light contribution at each box side.
/// virtual void ComputeLighting( const Vector&amp; pt, const Vector* pNormal, bool bClamp, Vector&amp; color, Vector *pBoxColors=NULL ) = 0;
/// </code>
///
/// at <c>src/public/cdll_int.h:392</c> — six box sides IS an ambient cube — and client code asks
/// for it as <c>engine-&gt;ComputeLighting( pos, NULL, true, vecColor )</c>
/// (<c>c_impact_effects.cpp:486</c>, <c>c_rope.cpp:2053</c>, <c>proxypupil.cpp:89</c>). Not one
/// caller owns the lighting data; they ask the level for it.
///
/// Ours was <c>MainForm.LightAt</c> and <c>MainForm.SunAt</c>, handed to <see cref="MapAssets"/> and
/// <see cref="EntityModelSet"/> as delegates. Three fields of map state lived on the form for them,
/// nothing could test them without an STA thread and a device, and a second frontend would have had
/// to reimplement both (B188, B184, D90).
///
/// **The two halves are separate because the sun is conditional and the rest is not.** Valve
/// defines a sky light as a "directional light with no falloff (surface must trace to SKY
/// texture)", so <see cref="SunAt"/> can answer "no sun here" for a point that
/// <see cref="ComputeLighting"/> still lights.
/// </remarks>
public sealed class LevelLighting
{
    private readonly BspLeafTree? _leaves;
    private readonly IReadOnlyList<AmbientSamples> _ambient;
    private readonly IReadOnlyList<BspWorldLight> _worldLights;
    private readonly BspWorldLight? _sun;
    private readonly ILogger _render;

    /// <summary>Places already reported, so the line does not repeat per frame.</summary>
    /// <remarks>
    /// **Bounded at the report limit rather than growing for the life of the map.** The original
    /// added every place it was asked about and only then checked the limit, so the set kept
    /// growing after it had stopped reporting — one entry per distinct integer position of every
    /// model, for as long as playback ran. The reported lines are identical either way; this
    /// version simply stops remembering places it will never report.
    /// </remarks>
    private readonly HashSet<(int X, int Y, int Z)> _reportedLightTerms = [];

    /// <summary>How many places to report light terms for before falling silent.</summary>
    /// <remarks>Public so the test asserts against this value rather than a copy of it.</remarks>
    public const int LightTermReportLimit = 40;

    /// <summary>Creates a light source for one map.</summary>
    /// <param name="leaves">The BSP tree, or null when the map carried none.</param>
    /// <param name="ambient">Per-leaf ambient samples.</param>
    /// <param name="worldLights">Every light the compiler recorded, not only the sun.</param>
    /// <param name="sun">The single directional light, when the map has one.</param>
    /// <param name="render">Where the light terms are reported.</param>
    /// <exception cref="ArgumentNullException">An argument other than the map data is null.</exception>
    public LevelLighting(
        BspLeafTree? leaves,
        IReadOnlyList<AmbientSamples> ambient,
        IReadOnlyList<BspWorldLight> worldLights,
        BspWorldLight? sun,
        ILogger render)
    {
        ArgumentNullException.ThrowIfNull(ambient);
        ArgumentNullException.ThrowIfNull(worldLights);
        ArgumentNullException.ThrowIfNull(render);

        _leaves = leaves;
        _ambient = ambient;
        _worldLights = worldLights;
        _sun = sun;
        _render = render;
    }

    /// <summary>The light a map that has not been read casts, which is none.</summary>
    /// <param name="render">Where the light terms would be reported.</param>
    /// <returns>A source that answers unlit everywhere.</returns>
    /// <remarks>
    /// **A real object rather than a null field**, so every caller asks the same question of the
    /// same type whether or not a map is open — the null-object shape D83 settled on after a null
    /// default hid a missed wiring for 193 call sites.
    /// </remarks>
    public static LevelLighting Unlit(ILogger render) => new(null, [], [], null, render);

    /// <summary>The light one level casts.</summary>
    /// <param name="level">The lumps already read.</param>
    /// <param name="render">Where the light terms are reported.</param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is null.</exception>
    /// <remarks>
    /// Built from <see cref="MapLevel"/> rather than from the file, because the lighting lumps are
    /// read once with everything else — the engine's <c>LevelInitPreEntity</c> shape
    /// (<c>igamesystem.h:39</c>), where a system initialises itself from the level it is handed.
    /// </remarks>
    public static LevelLighting From(MapLevel level, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(level);

        return new LevelLighting(
            level.Leaves, level.Ambient, level.WorldLights, level.Sun, render);
    }

    /// <summary>The ambient light at a world position.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The cube, or a default one where the map cannot say.</returns>
    /// <remarks>
    /// **The leaf decides, which is how the engine does it.** A model takes the light measured
    /// inside the leaf it stands in, so two crates either side of a doorway are lit differently
    /// without either carrying a lightmap.
    ///
    /// An unlit answer is returned as a default cube, which the shader reads as "no cube supplied"
    /// and draws at full brightness rather than black — a model lit by a measurement nobody made is
    /// worse than one that is merely too bright.
    /// </remarks>
    public AmbientCube ComputeLighting(float x, float y, float z)
    {
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return default;
        }

        int leaf = tree.LeafAt(x, y, z);

        // **Blended, as Mod_LeafAmbientColorAtPos blends it.** vrad thins a leaf's samples down to
        // the ones an inverse-squared-distance average cannot already predict, so the stored set
        // only reconstructs the original lighting when it is interpolated. Taking the nearest read
        // back whichever survivor of that thinning was closest, which is why one capture point on
        // cp_process drew at 0.10 while its mirror image on a symmetric map drew at 0.39.
        AmbientCube bounced = leaf >= 0 && leaf < _ambient.Count
            ? _ambient[leaf].At(x, y, z)
            : default;

        // **And the direct term, which is the other half of what the engine gives a model.**
        // istudiorender.h describes the cube as "ambient, and lights that aren't in locallight[]",
        // so a cube carrying a nearby lamp's light is the shape the engine itself produces for
        // every light past the nearest four. Without this a prop out of daylight is lit by the
        // bounce alone, which is why anything indoors read as though it were in shade (B95).
        //
        // **Kept for callers that want one number**, and NOT what a model is drawn with any more —
        // see LightingAt, which hands the nearest four to the shader instead so they can shade
        // against a normal. A caller must take one or the other: the engine adds the cube and the
        // local lights, so a light in both is counted twice.
        AmbientCube lit = LocalLights.AddTo(bounced, _worldLights, x, y, z);

        // **The two terms reported apart, because one number cannot say which is missing.** Every
        // model on z1800 sampled between 0.09 and 0.12 in a room with three ceiling lamps overhead,
        // and the single figure is consistent with two unrelated faults: no light near enough to be
        // chosen, or lights chosen that contribute nothing once attenuated. A log that names only
        // the total makes those indistinguishable — see
        // docs/memory/a-log-must-name-what-it-measured.md.
        ReportLightTerms(bounced, lit, x, y, z);

        return lit;
    }

    /// <summary>The bounce cube and the direct lights at a point, as the engine keeps them.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The cube without direct light folded in, and the nearest lights beside it.</returns>
    /// <remarks>
    /// **This is what a model is drawn with, and <see cref="ComputeLighting"/> is not.** The cube
    /// here is vrad's own: `ComputeAmbientFromSphericalSamples` builds it from rays cast at
    /// surfaces — bounce — plus the dim `emit_surface` lights, and Valve's comment there says why
    /// only those. Point and spot lamps are absent from the lump because they are meant to arrive
    /// at runtime, which is what the second half of this is.
    ///
    /// **The difference is not brightness, it is direction.** A lamp folded into a cube arrives
    /// from all six faces at once, so a model takes no N·L falloff from it and can cast no
    /// highlight from it — which is why our phong is gated on the sun and why a weapon indoors has
    /// no specular term at all (B170).
    ///
    /// **Nothing here folds anything in**, deliberately. `PixelShaderDoLightingLinear` accumulates
    /// the cube and then each light, so a light appearing in both would be counted twice — and that
    /// mistake reads as a lighting change rather than as a bug.
    /// </remarks>
    public PointLighting LightingAt(float x, float y, float z)
    {
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return PointLighting.None;
        }

        int leaf = tree.LeafAt(x, y, z);

        AmbientCube bounced = leaf >= 0 && leaf < _ambient.Count
            ? _ambient[leaf].At(x, y, z)
            : default;

        if (_worldLights.Count == 0)
        {
            return PointLighting.Bounce(bounced);
        }

        LocalLight[] nearest = new LocalLight[LocalLights.MaximumLocalLights];

        int found = LocalLights.Strongest(_worldLights, x, y, z, nearest);

        if (found == 0)
        {
            return PointLighting.Bounce(bounced);
        }

        // Trimmed rather than passed with a count, so a consumer cannot read past what was found —
        // the shader takes a live flag per slot and this keeps the two saying the same thing.
        Array.Resize(ref nearest, found);

        // **Reported from what is already in hand.** `ComputeLighting`'s ReportLightTerms compares
        // the cube against the folded one, which would mean computing the fold here purely to log
        // it — the expensive-argument case CA1873 exists for. What matters on this path is a
        // different question anyway: how many lights were chosen and how near the strongest is,
        // because "no lamp near enough" and "lamps chosen that contribute nothing" look identical
        // in a single brightness figure.
        if (_render.IsEnabled(LogLevel.Debug))
        {
            _render.LogDebug(
                "local lights at ({X}, {Y}, {Z}): {Count} chosen, strongest at ({LX}, {LY}, {LZ}) " +
                "intensity {Intensity}",
                x, y, z, found,
                nearest[0].X, nearest[0].Y, nearest[0].Z,
                Math.Max(nearest[0].Red, Math.Max(nearest[0].Green, nearest[0].Blue)));
        }

        return new PointLighting(bounced, nearest);
    }

    /// <summary>The sun reaching a world position, or null when it does not.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The sun where the sky is visible, otherwise null.</returns>
    /// <remarks>
    /// **The trace is the feature, not an optimisation.** Valve describes a sky light as a
    /// "directional light with no falloff (surface must trace to SKY texture)" — applied without
    /// that condition it lights the inside of every building, which is worse than the shade this
    /// is meant to fix.
    ///
    /// Traced towards the sun, which is against the direction its light travels.
    /// </remarks>
    public SunLight? SunAt(float x, float y, float z)
    {
        if (_sun is not { } sun || _leaves is not { } tree)
        {
            return null;
        }

        if (!tree.SeesSky(x, y, z, -sun.Normal.X, -sun.Normal.Y, -sun.Normal.Z))
        {
            return null;
        }

        return new SunLight(
            sun.Intensity.Red,
            sun.Intensity.Green,
            sun.Intensity.Blue,
            sun.Normal.X,
            sun.Normal.Y,
            sun.Normal.Z);
    }

    /// <summary>Says what the bounce gave and what the direct lights added, once per place.</summary>
    /// <remarks>
    /// **Debug, and the work is behind the same guard as the write** (B191). This runs for every
    /// model every time one moves, and the stall that froze playback for 120 ms every few seconds
    /// was one per-frame line at <c>Information</c> reaching a per-line disk flush. A release run
    /// (<c>developer 0</c>) does not admit <c>Debug</c>, so with the guard in place this costs a
    /// level check and nothing else — no string, and no set entry.
    ///
    /// Sampled rather than per call: the question it answers is about a PLACE rather than about a
    /// frame.
    /// </remarks>
    private void ReportLightTerms(AmbientCube bounced, AmbientCube lit, float x, float y, float z)
    {
        if (_worldLights.Count == 0 ||
            _reportedLightTerms.Count >= LightTermReportLimit ||
            !_render.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        if (!_reportedLightTerms.Add(((int)x, (int)y, (int)z)))
        {
            return;
        }

        _render.LogDebug(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"light terms at ({x:0},{y:0},{z:0}): bounce {AmbientCube.Luminance(bounced):0.####}, " +
                $"with direct {AmbientCube.Luminance(lit):0.####}, " +
                $"{_worldLights.Count} world lights on the map"));
    }
}
