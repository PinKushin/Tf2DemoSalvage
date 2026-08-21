using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Diagnostics;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One cubemap the map compiler baked, and where it stands.</summary>
/// <param name="X">Position east-west, snapped to a whole unit by vbsp.</param>
/// <param name="Y">Position north-south.</param>
/// <param name="Z">Position vertically.</param>
/// <param name="Size">
/// The edge of one cube face in pixels, already resolved — <c>dcubemapsample_t.size</c> is a CODE
/// rather than a length, and this is the length.
/// </param>
/// <remarks>
/// **A cubemap has no name of its own; the position is the name.** <c>bspfile.h:996</c> says so
/// beside the field: <c>"the filename for the vtf file is derived from the position"</c>. See
/// <see cref="BspCubemaps.TextureName"/> for the derivation, which is vbsp's.
/// </remarks>
public readonly record struct BspCubemap(int X, int Y, int Z, int Size);

/// <summary>
/// The reflections a map bakes into itself, <c>LUMP_CUBEMAPS</c> 42.
/// </summary>
/// <remarks>
/// **This is half of <c>$envmap</c>, which 79 of cp_process_final's 410 materials ask for** — very
/// nearly one surface in five, including every pane of glass and the metalwork around both second
/// points. Without it they draw matte, which is half of why a capture point disc looks flat. B55.
///
/// The lump is an array of <c>dcubemapsample_t</c> (<c>bspfile.h:992</c>):
///
/// <code>
/// struct dcubemapsample_t
/// {
///     int           origin[3];   // position of light snapped to the nearest integer
///                                // the filename for the vtf file is derived from the position
///     unsigned char size;        // 0 - default
///                                // otherwise, 1&lt;&lt;(size-1)
/// };
/// </code>
///
/// **Sixteen bytes, not thirteen, and the difference is padding the declaration does not mention.**
/// Three ints and a byte is thirteen bytes of content, and C++ pads a struct to its own alignment —
/// four, from the ints — so <c>sizeof(dcubemapsample_t)</c> is 16 with three unnamed bytes at the
/// end. The lump is written with <c>SwapLumpToDisk&lt;dcubemapsample_t&gt;( LUMP_CUBEMAPS )</c>
/// (<c>bsplib.cpp:4891</c>), which writes <c>sizeof</c> per element, so the padding is on disk.
///
/// <c>DECLARE_BYTESWAP_DATADESC()</c> inside the struct does not change this: it expands to
/// <c>static</c> members and friend templates only (<c>datamap.h:318</c>), so it adds no instance
/// data and no vtable.
///
/// **This reader was written with a stride of 13 and it was wrong**, which is worth keeping because
/// of how it read: the FIRST cubemap came out correct and every one after it was composed from the
/// tail of one record and the head of the next. On cp_process_final that gave a first entry at
/// <c>(0, 0, 608)</c> — entirely plausible — followed by <c>(-2147483648, -2147483642, 1879048200)</c>.
/// The synthetic tests all passed, because their fixtures were built 13 bytes wide to match the
/// belief being tested. See <c>docs/findings/27-cubemap-placement.md</c>.
///
/// The arithmetic settles it without appeal to any of that: the lump is 688 bytes, which is
/// 43 × 16 exactly and is not divisible by 13.
///
/// **A size of 0 means the DEFAULT and not a degenerate cube**, and getting that wrong is
/// spectacular rather than subtle: <c>1 &lt;&lt; (0 - 1)</c> in C# is <c>1 &lt;&lt; 31</c>, because the
/// shift count is masked to five bits. The default is 32 (<c>vbsp/cubemap.cpp:280</c>).
///
/// Resolved here rather than by the caller, so the escape value cannot leak into arithmetic
/// somewhere downstream — the same reasoning as
/// <c>docs/memory/sentinels-conflate-unknown-with-answer.md</c>.
/// </remarks>
public static class BspCubemaps
{
    /// <summary>
    /// Bytes per <c>dcubemapsample_t</c>: three ints, one unsigned char, and three bytes of
    /// alignment padding the declaration does not mention but <c>sizeof</c> supplies.
    /// </summary>
    public const int Stride = 16;

    /// <summary><c>DEFAULT_CUBEMAP_SIZE</c>, <c>vbsp/cubemap.cpp:280</c>.</summary>
    public const int DefaultSize = 32;

    /// <summary>Reads every cubemap placement a map declares.</summary>
    /// <param name="file">The whole BSP.</param>
    /// <returns>The placements in lump order, empty when the map declares none.</returns>
    public static IReadOnlyList<BspCubemap> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> lump = BspLumpData.Read(file, header.Lump(BspLumpIndex.Cubemaps)).Span;

        if (lump.IsEmpty)
        {
            // **Not a defect.** A map compiled without env_cubemap entities carries none, and the
            // correct picture for it is one where nothing reflects. Logged rather than warned.
            DecodeLog.Note("assets", "the map bakes no cubemaps, so nothing will reflect");

            return [];
        }

        List<BspCubemap> cubemaps = new(lump.Length / Stride);

        for (int at = 0; at + Stride <= lump.Length; at += Stride)
        {
            ReadOnlySpan<byte> entry = lump[at..];

            cubemaps.Add(new BspCubemap(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                Size(entry[12])));
        }

        if (lump.Length % Stride != 0)
        {
            // The stride is not in question — it is what the structure declares — so a lump that
            // is not a whole number of records is corruption, and saying so is worth more than
            // silently keeping the whole ones.
            DecodeLog.Lost(
                "assets",
                $"the cubemap lump is {lump.Length} bytes, which is not a whole number of " +
                $"{Stride}-byte records; the final one was dropped");
        }

        DecodeLog.Note("assets", $"{cubemaps.Count} baked cubemaps");

        return cubemaps;
    }

    /// <summary>The name of the VTF a placement's reflection was compiled into.</summary>
    /// <param name="mapName">The map's base name, without a folder or an extension.</param>
    /// <param name="cubemap">The placement.</param>
    /// <returns>A path relative to <c>materials/</c>, without an extension.</returns>
    /// <remarks>
    /// **vbsp's own format string, not a convention inferred from filenames**
    /// (<c>vbsp/cubemap.cpp:511</c>, reached as <c>GeneratePatchedName( "c", info, false, … )</c>):
    ///
    /// <code>
    /// const char *pSeparator = bMaterialName ? "_" : "";
    /// Q_snprintf( pBuffer, nMaxLen, "maps/%s/%s%s%d_%d_%d", info.m_pMapName,
    ///     pMaterialName, pSeparator, info.m_pOrigin[0], info.m_pOrigin[1], info.m_pOrigin[2] );
    /// ...
    /// BackSlashToForwardSlash( pBuffer );
    /// Q_strlower( pBuffer );
    /// </code>
    ///
    /// Two things follow that are easy to get wrong from a filename alone:
    ///
    /// - **The separator is empty for a texture and an underscore for a material.** The material
    ///   form is what this project has already seen — <c>MapAssetsTests</c> records
    ///   <c>maps/cp_process_final/icarus/glasschrome001_544_1952_929.vmt</c> in the map's pakfile —
    ///   and copying that shape for the texture gives <c>c_544_…</c>, which exists nowhere.
    /// - **The whole name is lowercased.** Whatever case the map was compiled under, the name in
    ///   the archive is lower — and these archives are matched by name rather than by a filesystem,
    ///   so the case is ours to get right.
    ///
    /// <c>%d</c> keeps a negative coordinate's sign, which does not collide with the underscore
    /// separator.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification =
            "This is not normalisation for comparison, which is what the rule is about. vbsp " +
            "ends GeneratePatchedName with Q_strlower and writes the LOWERCASE name into the " +
            "archive, so lowercase is the value being reproduced. Uppercasing would name a file " +
            "that does not exist.")]
    public static string TextureName(string mapName, BspCubemap cubemap)
    {
        ArgumentNullException.ThrowIfNull(mapName);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"maps/{mapName}/c{cubemap.X}_{cubemap.Y}_{cubemap.Z}")
            .ToLowerInvariant();
    }

    /// <summary>Which baked cubemap a point reflects.</summary>
    /// <param name="cubemaps">Every placement the map declares, in lump order.</param>
    /// <param name="x">Position east-west, in world units.</param>
    /// <param name="y">Position north-south.</param>
    /// <param name="z">Height.</param>
    /// <returns>Its index in <paramref name="cubemaps"/>, or -1 when the map declares none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cubemaps"/> is null.</exception>
    /// <remarks>
    /// **A brush face needs no search and a model does, and the two shaders are where that splits.**
    /// <c>LightmappedGeneric</c> refuses the literal <c>env_cubemap</c> outright — <c>"env_cubemap
    /// used on world geometry without rebuilding map. . ignoring"</c>,
    /// <c>lightmappedgeneric_dx9_helper.cpp:83</c> — so brushwork reflects only what vbsp patched
    /// into it at compile time. <c>VertexLitGeneric</c> carries no such rejection and loads whatever
    /// <c>$envmap</c> names, so on a model the literal survives to runtime and means "the cubemap
    /// bound as local" (<c>BindLocalCubemap</c>, <c>imaterialsystem.h:1200</c>).
    ///
    /// **This is Valve's rule, from <c>Cubemap_FindClosestCubemap</c>
    /// (<c>vbsp/cubemap.cpp:835</c>), reduced to the half a model can use.** That function runs two
    /// passes: first the nearest placement lying IN FRONT of the surface, tested as
    /// <c>DotProduct( vecDelta, pPlane->normal ) >= 0</c>, and if none is in front, the nearest
    /// overall. The first pass needs <c>pPlane->normal</c> — the plane of one brush side — and the
    /// function returns -1 immediately when handed no side at all. A model has no such plane, so
    /// the second pass is the whole of the applicable rule.
    ///
    /// **Evidence class, flagged because they are not equal.** That the rule is nearest-by-distance
    /// is READ FROM PUBLISHED SOURCE. That the engine picks a model's local cubemap by this same
    /// rule at runtime is INTERPOLATED: the routine that does it is inside the closed engine, the
    /// client tree binds a local cubemap only in <c>basemodelpanel.cpp</c> and only a fixed default,
    /// and nothing published states the runtime rule. See <c>docs/DECISIONS.md</c> D44.
    ///
    /// **Ties go to the earlier placement**, because Valve compares with a strict <c>&lt;</c> against
    /// a running minimum. Squared distance is compared rather than distance: the ordering is
    /// identical, and skipping the square root removes the one operation in here that could round
    /// two genuinely different placements into a tie.
    /// </remarks>
    public static int Closest(IReadOnlyList<BspCubemap> cubemaps, float x, float y, float z)
    {
        ArgumentNullException.ThrowIfNull(cubemaps);

        int closest = -1;
        double nearest = double.MaxValue;

        for (int index = 0; index < cubemaps.Count; index++)
        {
            BspCubemap cubemap = cubemaps[index];

            // Accumulated in double rather than float. A map runs to ±16384 units, so a squared
            // separation reaches 8×10⁸ — where a float's step is about 64 square units, enough to
            // round two placements a few units apart into a tie and resolve it by lump order.
            double dx = cubemap.X - (double)x;
            double dy = cubemap.Y - (double)y;
            double dz = cubemap.Z - (double)z;

            double distance = (dx * dx) + (dy * dy) + (dz * dz);

            if (distance < nearest)
            {
                nearest = distance;
                closest = index;
            }
        }

        return closest;
    }

    /// <summary>Turns the stored size CODE into an edge length in pixels.</summary>
    private static int Size(byte code) => code == 0 ? DefaultSize : 1 << (code - 1);
}
