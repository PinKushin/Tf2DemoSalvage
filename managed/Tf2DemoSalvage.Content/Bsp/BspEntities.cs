using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// One entity from a map's entity lump: an ordered set of key/value pairs.
/// </summary>
/// <remarks>
/// A dictionary rather than a typed record because the lump holds every entity class a map uses,
/// and this project needs two keys out of hundreds. Typing them would be inventing a schema for
/// data nobody here reads.
/// </remarks>
public sealed class BspEntity
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a key, keeping the first value if it repeats.</summary>
    /// <param name="key">Key name.</param>
    /// <param name="value">Key value.</param>
    /// <remarks>
    /// First wins, because Source's own key/value store answers with the first match. A later
    /// duplicate overwriting it would disagree with the engine about the same file.
    /// </remarks>
    internal void Add(string key, string value) => _values.TryAdd(key, value);

    /// <summary>Reads a key.</summary>
    /// <param name="key">Key name, matched case-insensitively.</param>
    /// <returns>The value.</returns>
    /// <exception cref="KeyNotFoundException">The entity has no such key.</exception>
    public string this[string key] => _values[key];

    /// <summary>How many keys the entity carries.</summary>
    public int Count => _values.Count;

    /// <summary>Reads a key if it is present.</summary>
    /// <param name="key">Key name, matched case-insensitively.</param>
    /// <param name="value">The value, when the key exists.</param>
    /// <returns>Whether the key exists.</returns>
    public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);

    /// <summary>Every key this entity carries, with its value.</summary>
    /// <remarks>
    /// **For readers that must not guess which keys matter.** An <c>env_soundscape</c> carries a
    /// name, a radius and up to eight position targets, and which of those a map actually sets
    /// varies — so a reader written against a guessed key list silently ignores whatever it did not
    /// think of. Enumerating is also how a probe reports a class nobody has read before.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>The entity's <c>classname</c>, or empty when it declares none.</summary>
    /// <remarks>
    /// Every entity in a compiled map has one; empty means the lump held a block that did not, which
    /// is worth reading as "not the class you are looking for" rather than throwing.
    /// </remarks>
    public string ClassName => _values.TryGetValue("classname", out string? name) ? name : string.Empty;
}

/// <summary>
/// Reads a map's entity lump.
/// </summary>
/// <remarks>
/// **Lump 0 is the one part of a BSP that is not an array of structs.** It is the text Hammer
/// wrote, carried through compilation almost unchanged:
///
/// <code>
/// {
/// "origin" "-4374 -3786 229.5"
/// "scale" "16"
/// "classname" "sky_camera"
/// }
/// </code>
///
/// It is read here for one reason: <c>sky_camera</c> marks the 3D skybox room. That room is
/// ordinary world geometry placed far from the playable map at a reduced scale, so from directly
/// above it lands in a corner of its own and stretches the bounds an overhead view is fitted to —
/// on <c>cp_process_final</c> it is what pushed the real map into a third of the viewport.
///
/// **The classname is not a naming convention, which is what makes this exact.** <c>sky_camera</c>
/// is an entity class registered in the engine's own code and published through the FGD that
/// Hammer loads, so a mapper picks it from a list. Brush names, texture names and targetnames are
/// all things a community map can choose freely; this one it cannot. A spatial heuristic would be
/// a guess about where the room is, and measuring one — trimming to a vertex percentile — cut a
/// third off the height of real maps, because vertex density is not extent.
///
/// **This is untrusted text out of a downloaded file (D32).** The parser is a hand-written state
/// machine, deliberately without regular expressions: an entity lump is a natural place to hide a
/// catastrophic-backtracking input, and nothing needed here justifies that risk.
/// </remarks>
public static class BspEntities
{
    /// <summary>Classname of the entity that marks a 3D skybox.</summary>
    public const string SkyCameraClass = "sky_camera";

    /// <summary>Reads the entity lump of a whole map file.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>Every complete entity, in file order.</returns>
    /// <exception cref="System.IO.InvalidDataException">The header or the lump is malformed.</exception>
    public static IReadOnlyList<BspEntity> ReadFrom(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        // Through BspLumpData, because the entity lump is LZMA compressed like every other lump in
        // a shipped TF2 map. Reading it raw yields compressed bytes that contain no braces and
        // therefore parse cleanly to nothing - a silent empty result, not an error.
        return Parse(BspLumpData.Read(file, header.Lump(EntityLump)));
    }

    /// <summary>Parses entity text.</summary>
    /// <param name="text">The lump's bytes, already decompressed.</param>
    /// <returns>Every complete entity, in order.</returns>
    /// <remarks>
    /// An unterminated final block is dropped rather than salvaged. Everything before it is still
    /// returned, which is the same rule the demo command reader follows for a truncated tail.
    /// </remarks>
    public static IReadOnlyList<BspEntity> Parse(ReadOnlyMemory<byte> text)
    {
        List<BspEntity> entities = [];
        ReadOnlySpan<byte> span = text.Span;

        BspEntity? current = null;
        string? pendingKey = null;
        int position = 0;

        while (position < span.Length)
        {
            byte character = span[position];

            if (character == (byte)'{')
            {
                current = new BspEntity();
                pendingKey = null;
                position++;
            }
            else if (character == (byte)'}')
            {
                if (current is not null)
                {
                    entities.Add(current);
                    current = null;
                }

                position++;
            }
            else if (character == (byte)'"')
            {
                // Quoted, so a brace INSIDE a value cannot end the block. Real maps carry those in
                // entity io values such as "door,Open,{a},0,-1".
                if (!TryReadQuoted(span, ref position, out string token))
                {
                    break;
                }

                if (pendingKey is null)
                {
                    pendingKey = token;
                }
                else
                {
                    current?.Add(pendingKey, token);
                    pendingKey = null;
                }
            }
            else
            {
                position++;
            }
        }

        return entities;
    }

    /// <summary>Finds the 3D skybox marker, if the map has one.</summary>
    /// <param name="entities">Entities from <see cref="Parse"/>.</param>
    /// <returns>The <c>sky_camera</c> origin, or null when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> is null.</exception>
    /// <remarks>
    /// **Null is a normal answer.** Not every map has a 3D skybox — an indoor map has nothing to
    /// put in one — and a malformed origin is also null rather than a partially-believed position,
    /// because a wrong exclusion box would delete real geometry.
    /// </remarks>
    public static (float X, float Y, float Z)? SkyCameraOrigin(IReadOnlyList<BspEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (BspEntity entity in entities)
        {
            if (!entity.TryGetValue("classname", out string? classname) ||
                !string.Equals(classname, SkyCameraClass, StringComparison.OrdinalIgnoreCase) ||
                !entity.TryGetValue("origin", out string? origin))
            {
                continue;
            }

            if (TryReadVector(origin, out (float X, float Y, float Z) position))
            {
                return position;
            }
        }

        return null;
    }

    /// <summary>The 3D skybox marker's origin AND scale, if the map has one.</summary>
    /// <param name="entities">Entities from <see cref="Parse"/>.</param>
    /// <returns>The origin and scale, or null when there is no <c>sky_camera</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> is null.</exception>
    /// <remarks>
    /// **The scale is half the transform and reading only the origin cannot place the sky.**
    /// `CSkyboxView::DrawInternal` (<c>viewrender.cpp:4888</c>) divides the main view's origin by it
    /// before adding the sky camera's:
    ///
    /// <code>
    ///   if ( m_pSky3dParams-&gt;scale &gt; 0 )
    ///   {
    ///       float scale = 1.0f / m_pSky3dParams-&gt;scale;
    ///       VectorScale( origin, scale, origin );
    ///   }
    ///   VectorAdd( origin, m_pSky3dParams-&gt;origin, origin );
    /// </code>
    ///
    /// So the miniature room is drawn from a camera that moves a sixteenth as far as the player
    /// does, which is what makes distant scenery hold still while near scenery slides past.
    ///
    /// **Sixteen is the default and it is Valve's, not a guess** — `sky_camera`'s `scale` key
    /// defaults to 16 in `base.fgd`, and the guard above treats a non-positive scale as "do not
    /// divide" rather than as an error.
    ///
    /// **Kept beside <see cref="SkyCameraOrigin"/> rather than replacing it**, because that one has
    /// a caller shape of its own; this is the pair a renderer needs.
    /// </remarks>
    public static ((float X, float Y, float Z) Origin, float Scale)? SkyCamera(
        IReadOnlyList<BspEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (BspEntity entity in entities)
        {
            if (!entity.TryGetValue("classname", out string? classname) ||
                !string.Equals(classname, SkyCameraClass, StringComparison.OrdinalIgnoreCase) ||
                !entity.TryGetValue("origin", out string? origin) ||
                !TryReadVector(origin, out (float X, float Y, float Z) position))
            {
                continue;
            }

            float scale = DefaultSkyScale;

            if (entity.TryGetValue("scale", out string? stated) &&
                float.TryParse(stated, NumberStyles.Float, CultureInfo.InvariantCulture, out float read))
            {
                scale = read;
            }

            return (position, scale);
        }

        return null;
    }

    /// <summary>`sky_camera`'s default `scale`, as `base.fgd` declares it.</summary>
    private const float DefaultSkyScale = 16f;

    /// <summary>The map's 2D skybox name — <c>worldspawn</c>'s <c>skyname</c>.</summary>
    /// <param name="entities">Entities from <see cref="Parse"/>.</param>
    /// <returns>The name, or <see cref="DefaultSkyName"/> when the map states none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> is null.</exception>
    /// <remarks>
    /// **`worldspawn` sets a CONVAR, which is why a map without the key still has a sky.**
    /// `CWorld::KeyValue` (<c>server/world.cpp:417</c>):
    ///
    /// <code>
    ///   if ( FStrEq(szKeyName, "skyname") )
    ///   {
    ///       ConVarRef skyname( "sv_skyname" );
    ///       skyname.SetValue( szValue );
    ///   }
    /// </code>
    ///
    /// and `sv_skyname` is declared with a default (<c>movevars_shared.cpp:105</c>):
    /// <c>ConVar sv_skyname( "sv_skyname", "sky_urb01", FCVAR_ARCHIVE | FCVAR_REPLICATED, ... )</c>.
    /// So the fallback is a real sky rather than nothing, and it is Valve's rather than a guess.
    ///
    /// **A CONVAR and not a per-map field also means it persists**: a map that does not set it
    /// keeps whatever the last one did. Reproducing that would need a session-wide value; answering
    /// the default here is the behaviour of a freshly started client, which is what opening a demo
    /// is.
    /// </remarks>
    public static string SkyName(IReadOnlyList<BspEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (BspEntity entity in entities)
        {
            if (entity.TryGetValue("classname", out string? classname) &&
                string.Equals(classname, "worldspawn", StringComparison.OrdinalIgnoreCase) &&
                entity.TryGetValue("skyname", out string? name) &&
                name.Length > 0)
            {
                return name;
            }
        }

        return DefaultSkyName;
    }

    /// <summary>`sv_skyname`'s default — <c>movevars_shared.cpp:105</c>.</summary>
    public const string DefaultSkyName = "sky_urb01";

    /// <summary>Classname of the entity by which a map overrides the detail prop distances.</summary>
    public const string DetailControllerClass = "env_detail_controller";

    /// <summary>The one material every detail sprite is cut from, when a map names none.</summary>
    /// <remarks>
    /// <c>#define DETAIL_SPRITE_MATERIAL "detail/detailsprites"</c> — `detailobjectsystem.cpp:44`.
    /// </remarks>
    public const string DefaultDetailSpriteMaterial = "detail/detailsprites";

    /// <summary>The sheet this map's detail sprites are cut from.</summary>
    /// <param name="entities">Entities from <see cref="Parse"/>.</param>
    /// <returns>
    /// <c>worldspawn</c>'s <c>detailmaterial</c>, or <see cref="DefaultDetailSpriteMaterial"/> when
    /// the map states none.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> is null.</exception>
    /// <remarks>
    /// **`CDetailObjectSystem::LevelInitPostEntity` opens with this**
    /// (<c>detailobjectsystem.cpp:1516</c>):
    ///
    /// <code>
    ///   const char *pDetailSpriteMaterial = DETAIL_SPRITE_MATERIAL;
    ///   C_World *pWorld = GetClientWorldEntity();
    ///   if ( pWorld &amp;&amp; pWorld->GetDetailSpriteMaterial() &amp;&amp; *(pWorld->GetDetailSpriteMaterial()) )
    ///   {
    ///       pDetailSpriteMaterial = pWorld->GetDetailSpriteMaterial();
    ///   }
    /// </code>
    ///
    /// and the string is `worldspawn`'s `detailmaterial` key — `DEFINE_KEYFIELD(
    /// m_iszDetailSpriteMaterial, FIELD_STRING, "detailmaterial" )`, <c>world.cpp:392</c>, sent to
    /// the client as `m_iszDetailSpriteMaterial`.
    ///
    /// **The struct comment saying every sprite lies in `detail/detailsprites` is out of date, and
    /// measurably so.** All 234 installed maps set the key; only 49 of them set it to that. TF2
    /// ships at least sixteen sheets — `_trainyard` on 42 maps, `_2fort` on 38, `_sawmill` on 32 —
    /// and their mean colours are nothing like each other, so a viewer that hardcoded the default
    /// drew desert grass on `koth_harvest_final` (`_harvest`) and on `cp_granary` (`_granary`).
    ///
    /// **The empty check is not redundant.** The engine tests the pointer AND its first character,
    /// so a map setting `detailmaterial` to an empty string gets the default rather than a material
    /// with no name — and `worldspawn` always carries the field once it is networked.
    ///
    /// **Truncated to 255 characters**, because the field it travels through is
    /// <c>char m_iszDetailSpriteMaterial[MAX_DETAIL_SPRITE_MATERIAL_NAME_LENGTH]</c> with that
    /// length being 256 (`c_world.h:47`). A longer key in an untrusted map reaches the client cut
    /// off, so cutting it here is what the client would have seen.
    /// </remarks>
    public static string DetailSpriteMaterial(IReadOnlyList<BspEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (BspEntity entity in entities)
        {
            if (entity.TryGetValue("classname", out string? classname) &&
                string.Equals(classname, "worldspawn", StringComparison.OrdinalIgnoreCase) &&
                entity.TryGetValue("detailmaterial", out string? material) &&
                material.Length > 0)
            {
                return material.Length > DetailSpriteMaterialLength - 1
                    ? material[..(DetailSpriteMaterialLength - 1)]
                    : material;
            }
        }

        return DefaultDetailSpriteMaterial;
    }

    /// <summary>`MAX_DETAIL_SPRITE_MATERIAL_NAME_LENGTH` — `c_world.h:47`.</summary>
    private const int DetailSpriteMaterialLength = 256;

    /// <summary>The map's detail prop fade override, if it states one.</summary>
    /// <param name="entities">Entities from <see cref="Parse"/>.</param>
    /// <returns>
    /// The controller's <c>fademindist</c> and <c>fademaxdist</c>, or null when the map has no
    /// <c>env_detail_controller</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> is null.</exception>
    /// <remarks>
    /// **A map can lower `cl_detaildist` and `cl_detailfade`, and only lower them.**
    /// `CDetailObjectSystem::LevelInitPostEntity` (<c>detailobjectsystem.cpp:1524</c>) writes the
    /// two cvars itself:
    ///
    /// <code>
    ///   if ( GetDetailController() )
    ///   {
    ///       cl_detailfade.SetValue( MIN( m_flDefaultFadeStart, GetDetailController()->m_flFadeStartDist ) );
    ///       cl_detaildist.SetValue( MIN( m_flDefaultFadeEnd, GetDetailController()->m_flFadeEndDist ) );
    ///   }
    ///   else
    ///   {
    ///       // revert to default values if the map doesn't specify
    ///       cl_detailfade.SetValue( m_flDefaultFadeStart );
    ///       cl_detaildist.SetValue( m_flDefaultFadeEnd );
    ///   }
    /// </code>
    ///
    /// **`MIN` is the whole mechanism and it is one-directional.** A map may cut a player's detail
    /// distance and can never raise it, so a mapper cannot force grass onto a machine whose owner
    /// turned it off. The pair the `MIN` is taken against is captured once in `Init()`
    /// (<c>detailobjectsystem.cpp:370</c>) — before any map loads — which is what makes a config
    /// value survive a map that overrides it.
    ///
    /// **The keys go in crossed, and that is Valve's, not a transcription slip.** `fademindist` —
    /// which a mapper reads as "the distance where fading begins" — is assigned to `cl_detailfade`,
    /// whose own help text is "Distance across which detail props fade in": a WIDTH. So a
    /// controller saying <c>fademindist 700, fademaxdist 1000</c> does not fade from 700 to 1000;
    /// it produces a 1000-unit maximum with a 700-unit fade band, which begins at 300.
    ///
    /// **A key the map omits is zero, not a default**, because Valve's entity allocator zeroes the
    /// object — <c>baseentity.cpp:3814</c>, *"All fields in the object are all initialized to 0."*
    /// `CEnvDetailController` has a `KeyValue` override and no constructor assignment, so a
    /// controller with neither key drives both cvars to zero and draws no detail props at all.
    ///
    /// **The LAST controller in the lump wins.** The class registers itself from its constructor —
    /// <c>s_detailController = this</c> (<c>env_detail_controller.cpp:36</c>) — with no guard
    /// against there already being one, so a map carrying two is decided by spawn order.
    /// </remarks>
    public static (float FadeStart, float FadeEnd)? DetailController(IReadOnlyList<BspEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        (float, float)? found = null;

        foreach (BspEntity entity in entities)
        {
            if (!entity.TryGetValue("classname", out string? classname) ||
                !string.Equals(classname, DetailControllerClass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            found = (ReadDistance(entity, "fademindist"), ReadDistance(entity, "fademaxdist"));
        }

        return found;
    }

    private static float ReadDistance(BspEntity entity, string key) =>
        entity.TryGetValue(key, out string? stated) &&
        float.TryParse(stated, NumberStyles.Float, CultureInfo.InvariantCulture, out float distance)
            ? distance
            : 0f;

    /// <summary>The six sky faces, in CUBE FACE order.</summary>
    /// <returns>The material names, relative to <c>materials/</c>, without extension.</returns>
    /// <param name="skyName">The map's <c>skyname</c>.</param>
    /// <remarks>
    /// **Two arrays of these six strings exist in the SDK, in DIFFERENT orders, and only one of
    /// them means a direction.** `skyboxswapper.cpp:60` and `vscript_server.cpp:2190` both use
    /// <c>{ "rt", "bk", "lf", "ft", "up", "dn" }</c> — but only to precache, where order is
    /// irrelevant. The one that assigns directions is vbsp's, because its output is a CUBEMAP and
    /// the index IS the cube face (<c>cubemap.cpp:195</c>):
    ///
    /// <code>
    ///   const char *facingName[6] = { "rt", "lf", "bk", "ft", "up", "dn" };
    /// </code>
    ///
    /// Cube faces run +X, −X, +Y, −Y, +Z, −Z, so in Source's axes — X forward, Y left, Z up —
    /// `rt` is +X, `lf` is −X, `bk` is +Y, `ft` is −Y, `up` is +Z and `dn` is −Z. **Taking the
    /// precache array instead swaps `bk` and `lf`**, which puts two walls of the sky on each
    /// other's side: a plausible picture with the horizon in the wrong place.
    /// </remarks>
    public static string[] SkyFaces(string skyName)
    {
        ArgumentNullException.ThrowIfNull(skyName);

        string[] facing = ["rt", "lf", "bk", "ft", "up", "dn"];
        string[] materials = new string[facing.Length];

        for (int face = 0; face < facing.Length; face++)
        {
            materials[face] = $"skybox/{skyName}{facing[face]}";
        }

        return materials;
    }

    /// <summary>Index of the entity lump in the directory.</summary>
    private const int EntityLump = BspLumpIndex.Entities;

    private static bool TryReadQuoted(ReadOnlySpan<byte> span, ref int position, out string token)
    {
        int start = position + 1;
        int end = start;

        while (end < span.Length && span[end] != (byte)'"')
        {
            end++;
        }

        if (end >= span.Length)
        {
            // The lump ends inside a quoted string: a truncated file, and there is no token here.
            token = string.Empty;
            return false;
        }

        // UTF-8 rather than ASCII. Community maps carry non-English targetnames and messages, and
        // an ASCII read turns those into a plausible wrong string rather than failing.
        token = System.Text.Encoding.UTF8.GetString(span[start..end]);
        position = end + 1;
        return true;
    }

    private static bool TryReadVector(string text, out (float X, float Y, float Z) vector)
    {
        vector = default;

        Span<Range> parts = stackalloc Range[4];
        ReadOnlySpan<char> span = text;
        int count = span.Split(parts, ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (count != 3)
        {
            return false;
        }

        if (!float.TryParse(span[parts[0]], CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(span[parts[1]], CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(span[parts[2]], CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        vector = (x, y, z);
        return true;
    }
}
