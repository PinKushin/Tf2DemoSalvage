using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The game's surface table — what a material is like to touch (B58).
/// </summary>
/// <remarks>
/// **Shipped data, not code, which is the source this project keeps forgetting it has.** Every
/// surface a `.phy` solid or a map brush names resolves through
/// `scripts/surfaceproperties*.txt`, and the fields are Valve's own:
///
/// <code>
/// struct surfacephysicsparams_t
/// {
///     float friction;
///     float elasticity;   // collision elasticity - used to compute coefficient of restitution
///     float density;      // physical density (in kg / m^3)
///     float thickness;    // material thickness if not solid (sheet materials) in inches
///     float dampening;
/// };
/// </code>
///
/// `vphysics_interface.h:882`. The engine parses the same files through
/// `IPhysicsSurfaceProps::ParseSurfaceData` and hands the index to `CreatePolyObject` beside the
/// solid — `physprops-&gt;GetSurfaceIndex( solid.surfaceprop )`, `ragdoll_shared.cpp:194`.
///
/// **Why it matters here: without friction a corpse never stops.** A body resting on a slope with
/// no tangential force slides down it forever, and two of the eight corpses measured on
/// `koth_harvest_final` had slid hundreds of units off the map before this existed.
///
/// **Named `SurfaceTable` and not `SurfaceProperties`** because `Content.Bsp` already has a
/// `SurfaceProperties` — the BSP's per-face FLAGS, an unrelated thing with the same English name.
///
/// **Only what physics needs is kept.** The files carry sounds, footstep names, audio reflectivity
/// and game-movement factors; reading those here would be inventing consumers.
/// </remarks>
public sealed class SurfaceTable
{
    private readonly Dictionary<string, (float Friction, float Elasticity)> _surfaces =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An empty table, for a game folder that has none.</summary>
    public static SurfaceTable Empty { get; } = new();

    /// <summary>How many surfaces the table holds.</summary>
    public int Count => _surfaces.Count;

    /// <summary>Reads one <c>surfaceproperties</c> file into this table.</summary>
    /// <param name="text">The file's bytes.</param>
    /// <returns>This table, so several files can be chained.</returns>
    /// <remarks>
    /// **Several files, one table, and later ones WIN.** The engine parses
    /// `surfaceproperties.txt` and then every `surfaceproperties_*.txt` the mod ships, each
    /// overriding what came before — which is how TF2 changes `flesh` without editing HL2's copy.
    ///
    /// **A surface may name a parent through `base`, and that is not resolved here.** The files TF2
    /// ships spell out `friction` on the entries a ragdoll uses; an entry that only inherits one
    /// gets the default below rather than a wrong number, and the gap is stated rather than
    /// guessed at.
    /// </remarks>
    public SurfaceTable Read(ReadOnlySpan<byte> text)
    {
        string surface = string.Empty;
        float friction = DefaultFriction;
        float elasticity = DefaultElasticity;
        bool open = false;

        void Close()
        {
            if (open && surface.Length > 0)
            {
                _surfaces[surface] = (friction, elasticity);
            }

            friction = DefaultFriction;
            elasticity = DefaultElasticity;
        }

        KeyValuesReader.Read(text, (key, value, depth) =>
        {
            if (value is null)
            {
                Close();

                surface = key;
                open = true;
            }
            else if (depth > 0)
            {
                if (string.Equals(key, "friction", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float read))
                {
                    friction = read;
                }
                else if (string.Equals(key, "elasticity", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float bounce))
                {
                    elasticity = bounce;
                }
            }

            return true;
        });

        // **The last block has no successor to close it**, the same shape as `PhysicsModel`'s own
        // reader and for the same reason: a file ending on its final surface would lose it.
        Close();

        return this;
    }

    /// <summary>The friction of one surface, or the default when the table does not name it.</summary>
    /// <param name="surface">The <c>surfaceprop</c> name a solid or a brush declares.</param>
    /// <returns>Its coefficient of friction.</returns>
    public float FrictionOf(string? surface) =>
        surface is not null && _surfaces.TryGetValue(surface, out (float Friction, float _) found)
            ? found.Friction
            : DefaultFriction;

    /// <summary>
    /// <c>g_PhysDefaultObjectParams</c>'s friction, for a surface the table does not name.
    /// </summary>
    /// <remarks>
    /// **Valve's own default and not a guess** — `physics_shared.cpp` initialises
    /// `objectparams_t` with `1.0f` for friction, so an object whose surface is unknown behaves as
    /// the engine's would.
    /// </remarks>
    public const float DefaultFriction = 1f;

    /// <summary>The same default for elasticity.</summary>
    private const float DefaultElasticity = 1f;
}
