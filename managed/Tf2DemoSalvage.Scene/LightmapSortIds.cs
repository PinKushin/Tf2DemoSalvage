using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Each face's surface sort ID — its material AND its lightmap page — as the engine and the material system assign
/// them at level load (B457).
/// </summary>
/// <remarks>
/// **Read in disassembly; the full account is B457.**
///
/// - engine.dll <c>0x1800d2b10</c> puts every face in a tree ordered by <c>0x1800d2650</c>: lit before unlit
///   (SURFDRAW_NOLIGHT, from texinfo SURF_NOLIGHT), then the material's enumeration ID, then faces without light
///   styles before those with, then the larger luxel area (<c>size[0] · size[1]</c>) first; equal faces in face
///   order. In that order a lit face asks for <c>AllocateLightmap(size[0] + 3 (×4 bumped), size[1] + 3)</c> and an
///   unlit one for <c>AllocateWhiteLightmap</c>.
/// - materialsystem.dll <c>AllocateLightmap</c> (<c>0x180023900</c>): on a material change every page but the
///   last is closed and, if a material was current, the last page takes a new sort ID; the block goes in the first
///   open page that takes it, else a new page with a new sort ID. Pages are <c>min(1024, max texture width)</c> by
///   <c>min(512, max texture height)</c> (<c>0x1800480c0</c>), and BeginLightmapAllocation (<c>0x180023f90</c>)
///   opens one, sort ID 0.
/// - <c>AllocateWhiteLightmap</c> (<c>0x180023ef0</c>): a new sort ID whenever its material changes.
///
/// *Interpolated, named in B457:* the enumeration ID orders materials by their name's CUtlSymbol, i.e. by when the
/// process first interned the name (<c>0x18000b780</c>); a map's materials are first loaded by
/// <c>CMod_LoadTextures</c> (engine <c>0x18016eb00</c>) in TEXDATA order, so a fresh process orders them by texdata
/// index, which is <see cref="BspSurface.MaterialIndex"/>. A material the process met before the map sorts earlier
/// in the engine and is not modelled. "Needs bumped lightmaps" is texinfo SURF_BUMPLIGHT, which vbsp sets from the
/// same material property.
/// </remarks>
public static class LightmapSortIds
{
    /// <summary>The page size on any GPU whose texture limit is at least 1024 × 512.</summary>
    public const int PageWidth = 1024;

    /// <summary>The page height.</summary>
    public const int PageHeight = 512;

    /// <summary>Assigns every face its sort ID.</summary>
    /// <param name="surfaces">Every face of the map, as read.</param>
    /// <returns>Sort ID by face index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surfaces"/> is null.</exception>
    public static IReadOnlyDictionary<int, int> Assign(IReadOnlyList<BspSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        Dictionary<int, int> ids = [];

        // 0x1800d2650, the tree order; OrderBy is stable, so equal faces keep face order as the tree's
        // right-going insert does.
        IEnumerable<BspSurface> ordered = surfaces
            .OrderBy(face => Unlit(face))
            .ThenBy(face => face.MaterialIndex)
            .ThenBy(face => HasStyles(face))
            .ThenByDescending(face => (face.LuxelWidth - 1) * (face.LuxelHeight - 1));

        List<Packer> pages = [new Packer(0)];
        int counter = 0;
        int? current = null;
        int? white = null;

        foreach (BspSurface face in ordered)
        {
            if (Unlit(face))
            {
                // 0x180023ef0.
                if (white != face.MaterialIndex)
                {
                    if (current is not null || white is not null)
                    {
                        counter++;
                    }

                    white = face.MaterialIndex;
                }

                ids[face.FaceIndex] = counter;
                continue;
            }

            // 0x180023900.
            if (current != face.MaterialIndex)
            {
                pages.RemoveRange(0, pages.Count - 1);

                if (current is not null)
                {
                    pages[^1].SortId++;
                    counter++;
                }

                current = face.MaterialIndex;
            }

            int width = face.LuxelWidth + 2;

            if ((face.Flags & SurfaceProperties.BumpLight) != 0)
            {
                width *= 4;
            }

            int height = face.LuxelHeight + 2;
            Packer? into = pages.FirstOrDefault(page => page.Add(width, height));

            if (into is null)
            {
                counter++;
                into = new Packer(counter);
                pages.Add(into);

                // The engine stops with "too big to fit in page"; nothing here may stop a map loading.
                into.Add(width, height);
            }

            ids[face.FaceIndex] = into.SortId;
        }

        return ids;
    }

    private static bool Unlit(BspSurface face) => (face.Flags & SurfaceProperties.NoLight) != 0;

    /// <summary>Mod_LoadFaces' 0x400: unless style 0 is 0 or 255 and style 1 is 255.</summary>
    private static bool HasStyles(BspSurface face) =>
        ((face.Style0 + 1) & 0xfe) != 0 || face.Style1 != 255;

    /// <summary><c>CImagePacker</c> — a wavefront of the highest used row per column (<c>0x180047ea0</c>).</summary>
    private sealed class Packer(int sortId)
    {
        private readonly int[] _wavefront = Enumerable.Repeat(-1, PageWidth).ToArray();
        private int _minimumFailedWidth = PageWidth + 1;
        private int _minimumFailedHeight = PageHeight + 1;

        public int SortId { get; set; } = sortId;

        public bool Add(int width, int height)
        {
            if (width >= _minimumFailedWidth && height >= _minimumFailedHeight)
            {
                return false;
            }

            int bestX = -1;
            int outerMinY = PageHeight;
            int lastMaxY = -2;

            for (int outerX = 0; outerX <= PageWidth - width;)
            {
                if (_wavefront[outerX] == lastMaxY)
                {
                    outerX++;
                    continue;
                }

                int maxIndex = outerX;
                int maxY = -1;

                for (int x = outerX; x < outerX + width; x++)
                {
                    if (_wavefront[x] >= maxY)
                    {
                        maxY = _wavefront[x];
                        maxIndex = x;
                    }
                }

                lastMaxY = _wavefront[maxIndex];

                if (outerMinY > lastMaxY)
                {
                    outerMinY = lastMaxY;
                    bestX = outerX;
                }

                outerX = maxIndex + 1;
            }

            if (bestX == -1 || outerMinY + 1 + height >= PageHeight - 1)
            {
                if (width <= _minimumFailedWidth && height <= _minimumFailedHeight)
                {
                    _minimumFailedWidth = width;
                    _minimumFailedHeight = height;
                }

                return false;
            }

            for (int x = bestX; x < bestX + width; x++)
            {
                _wavefront[x] = outerMinY + height;
            }

            return true;
        }
    }
}
