using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Audio;

/// <summary>One <c>env_soundscape</c> as the map places it.</summary>
/// <param name="Name">The soundscape it names, such as <c>Gorge.Inside</c>.</param>
/// <param name="Index">Its position in the client's list, or -1 when the name is unknown.</param>
/// <param name="X">Where the entity is, in world units.</param>
/// <param name="Y">Where the entity is.</param>
/// <param name="Z">Where the entity is.</param>
/// <param name="Radius">
/// How far it reaches. **-1 means unlimited**, which is what every entity on cp_process uses — so
/// on that map visibility alone decides, not range.
/// </param>
/// <param name="Positions">
/// Where its numbered position targets are, for a script's <c>"position" "3"</c>. Empty where the
/// map sets none.
/// </param>
public readonly record struct SoundscapePlacement(
    string Name,
    int Index,
    float X,
    float Y,
    float Z,
    float Radius,
    IReadOnlyList<(float X, float Y, float Z)> Positions);

/// <summary>
/// Which soundscape a listener is standing in, decided the way the engine decides it.
/// </summary>
/// <remarks>
/// **The map is the source, not the demo.** A SourceTV recording carries the SourceTV camera's
/// soundscape rather than the spectated player's, because `m_audio` is sent only to the client that
/// owns the entity — so a viewer following a player has to work it out from the map, as the server
/// does. Measured: the STV recording of cp_process carries two samples and one index while the POV
/// recording of the same session carries 64 across three (B173).
///
/// It also removes a scaling problem the owner named: *"i really dont want to have to make manual
/// dumps like that for every map, so we need to figure out how to do this right... and probably
/// looking at bsps instead of making me manually do it"*. Every map carries its own answer.
///
/// **Two classes, and only one names a soundscape.** `env_soundscape` carries a `soundscape` key;
/// `env_soundscape_proxy` carries `MainSoundscapeName`, the targetname of a real one whose index it
/// copies — `CEnvSoundscapeProxy` does exactly that at <c>soundscape.cpp:52</c>. cp_process has 4
/// of the first and 40 of the second.
/// </remarks>
public sealed class SoundscapePlacements
{
    private readonly List<SoundscapePlacement> _placements;

    private SoundscapePlacements(List<SoundscapePlacement> placements) => _placements = placements;

    /// <summary>Every placed soundscape, in the map's own entity order.</summary>
    /// <remarks>
    /// **Map order, because the engine's selection depends on it.** `UpdateForPlayer` walks the
    /// entity list carrying state forward, so a different order can settle on a different entity
    /// where two are equally close and both visible.
    /// </remarks>
    public IReadOnlyList<SoundscapePlacement> Placements => _placements;

    /// <summary>Reads a map's soundscape entities and resolves them against the catalog.</summary>
    /// <param name="entities">The map's entity lump, already parsed.</param>
    /// <param name="catalog">The client's soundscape list, for name to index.</param>
    /// <returns>The placements.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static SoundscapePlacements From(
        IReadOnlyList<BspEntity> entities, SoundscapeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(catalog);

        // Index by name once, since a proxy resolves through it and there are forty of them.
        Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < catalog.Count; index++)
        {
            // **Last wins**, matching the client's own backward search
            // (`c_soundscape.cpp:352` walks from the end), so a later file redefining a name
            // overrides an earlier one exactly as it does in the engine.
            byName[catalog.Soundscapes[index].Name] = index;
        }

        // A proxy names its master by targetname, so the masters have to be known first.
        Dictionary<string, string> masters = new(StringComparer.OrdinalIgnoreCase);

        foreach (BspEntity entity in entities)
        {
            if (entity.ClassName.Equals("env_soundscape", StringComparison.OrdinalIgnoreCase) &&
                entity.TryGetValue("targetname", out string target) &&
                entity.TryGetValue("soundscape", out string named))
            {
                masters[target] = named;
            }
        }

        List<SoundscapePlacement> placements = [];

        foreach (BspEntity entity in entities)
        {
            string? name = null;

            if (entity.ClassName.Equals("env_soundscape", StringComparison.OrdinalIgnoreCase) &&
                entity.TryGetValue("soundscape", out string own))
            {
                name = own;
            }
            else if (entity.ClassName.Equals(
                         "env_soundscape_proxy", StringComparison.OrdinalIgnoreCase) &&
                     entity.TryGetValue("MainSoundscapeName", out string master) &&
                     masters.TryGetValue(master, out string? resolved))
            {
                name = resolved;
            }

            if (name is null || Origin(entity) is not { } origin)
            {
                continue;
            }

            placements.Add(new SoundscapePlacement(
                name,

                // **-1 for a name the catalog does not hold, rather than dropping the entity.** A
                // map naming a soundscape this install lacks is a fact worth being able to report;
                // silently omitting it would look identical to the map having no ambience there.
                byName.TryGetValue(name, out int index) ? index : -1,
                origin.X,
                origin.Y,
                origin.Z,
                Radius(entity),
                Targets(entity, entities)));
        }

        return new SoundscapePlacements(placements);
    }

    /// <summary>The soundscape a listener at a point is in, or <c>null</c> when none reaches.</summary>
    /// <param name="x">The listener, in world units.</param>
    /// <param name="y">The listener.</param>
    /// <param name="z">The listener.</param>
    /// <param name="clear">Whether a segment between two points is unobstructed.</param>
    /// <param name="current">
    /// The placement chosen last time, which the engine favours — pass <c>null</c> on the first
    /// call.
    /// </param>
    /// <returns>The chosen placement, or <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clear"/> is null.</exception>
    /// <remarks>
    /// **A transcription of `CEnvSoundscape::UpdateForPlayer` (<c>soundscape.cpp:258</c>), not a
    /// summary of it.** Summarising it as "the nearest visible one wins" is nearly right and drops
    /// the hysteresis, which is what stops the ambience flickering between two rooms on a threshold:
    ///
    /// <code>
    /// range = (playerPosition - EarPosition()).Length()
    /// if ( current == this )
    ///     currentDistance = range; bInRange = withinRadius &amp;&amp; traceClear
    /// else if ( (!bInRange || range &lt; currentDistance) &amp;&amp; withinRadius &amp;&amp; traceClear )
    ///     current = this; bInRange = true; currentDistance = range
    /// </code>
    ///
    /// The state carries forward through the walk, so an entity considered later compares against
    /// whatever was taken earlier in the same pass — which is why the entity ORDER is preserved.
    ///
    /// **The trace is a delegate rather than a BSP reference**, so this type stays testable without
    /// a map: the rule and the geometry are separate questions, and the rule is the one with the
    /// hysteresis bug in it if there is one.
    /// </remarks>
    public SoundscapePlacement? Choose(
        float x,
        float y,
        float z,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool> clear,
        SoundscapePlacement? current = null)
    {
        ArgumentNullException.ThrowIfNull(clear);

        SoundscapePlacement? chosen = current;
        float currentDistance = float.MaxValue;
        bool inRange = false;

        foreach (SoundscapePlacement placement in _placements)
        {
            float dx = placement.X - x;
            float dy = placement.Y - y;
            float dz = placement.Z - z;

            float range = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            // `m_flRadius > range || m_flRadius == -1` — unlimited when negative.
            bool withinRadius = placement.Radius < 0f || placement.Radius > range;

            if (chosen is { } held && held == placement)
            {
                currentDistance = range;
                inRange = withinRadius &&
                    clear((placement.X, placement.Y, placement.Z), (x, y, z));

                continue;
            }

            if ((inRange && range >= currentDistance) || !withinRadius)
            {
                continue;
            }

            if (!clear((placement.X, placement.Y, placement.Z), (x, y, z)))
            {
                continue;
            }

            chosen = placement;
            inRange = true;
            currentDistance = range;
        }

        return chosen;
    }

    /// <summary>An entity's origin, or null when it declares none.</summary>
    private static (float X, float Y, float Z)? Origin(BspEntity entity) =>
        entity.TryGetValue("origin", out string origin) ? Vector(origin) : null;

    /// <summary>An entity's radius; -1, meaning unlimited, when it declares none.</summary>
    private static float Radius(BspEntity entity) =>
        entity.TryGetValue("radius", out string radius) &&
        float.TryParse(radius, NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
            ? read
            : -1f;

    /// <summary>Where an entity's numbered position targets are.</summary>
    /// <remarks>
    /// **`position0` to `position7` name other entities**, and the engine looks each up by
    /// targetname — `m_positionNames[NUM_AUDIO_LOCAL_SOUNDS]` in `soundscape.h:62`. A soundscape's
    /// `"position" "3"` then plays at whatever entity `position3` named, which is how one soundscape
    /// scatters its loops across a whole map.
    ///
    /// Resolved here against the same entity list, since the targets are ordinary map entities with
    /// an origin. A name that resolves to nothing is skipped rather than defaulted: a sound placed
    /// at the world origin would be audible from the wrong side of the map.
    /// </remarks>
    private static List<(float X, float Y, float Z)> Targets(
        BspEntity entity, IReadOnlyList<BspEntity> entities)
    {
        List<(float X, float Y, float Z)> targets = [];

        for (int slot = 0; slot < 8; slot++)
        {
            if (!entity.TryGetValue(
                    $"position{slot.ToString(CultureInfo.InvariantCulture)}", out string named) ||
                named.Length == 0)
            {
                continue;
            }

            foreach (BspEntity candidate in entities)
            {
                if (candidate.TryGetValue("targetname", out string target) &&
                    target.Equals(named, StringComparison.OrdinalIgnoreCase) &&
                    Origin(candidate) is { } origin)
                {
                    targets.Add(origin);
                    break;
                }
            }
        }

        return targets;
    }

    /// <summary>Reads a Valve "x y z" triple.</summary>
    private static (float X, float Y, float Z)? Vector(string text)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 3 &&
            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                ? (x, y, z)
                : null;
    }
}
