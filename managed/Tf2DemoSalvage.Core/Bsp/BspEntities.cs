using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Bsp;

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

    /// <summary>Index of the entity lump in the directory.</summary>
    private const int EntityLump = 0;

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
