using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Orders translucent model instances the way the engine draws them: back to front.
/// </summary>
/// <remarks>
/// **<c>CClientLeafSystem::SortEntities</c>** (`clientleafsystem.cpp:1758`): each entity's distance
/// is the dot of its render-bounds CENTER against the view forward axis — *"Compute the center of
/// the object (needed for translucent brush models)"*, because a door's origin is nowhere near its
/// middle — and the entries are sorted ascending. The draw then walks the list BACKWARDS
/// (`viewrender.cpp:4577`, *"traversing the leaf list backwards to get the appropriate sort
/// ordering (back to front)"*), so the farthest entity blends first and the nearest last, which is
/// the only order alpha blending composes correctly in.
///
/// **This replaced prop input order, which a comment defended with the sort's own argument** (the
/// outside audit's finding 2): "blending is order-dependent" is exactly why the order must come
/// from the camera rather than from whatever order the scene happened to emit entities in.
///
/// **Per leaf**, as the engine does: <c>BuildRenderablesList</c> collates the visible leaves front to
/// back and sorts only each leaf's new translucent entries (<c>clientleafsystem.cpp:1822-1834</c>), so
/// the list is ordered by leaf place first and by distance within a leaf (B426).
/// </remarks>
public static class TranslucentOrder
{
    /// <summary>Sorts entries by leaf place, then ascending along the view axis; draw them by walking in reverse.</summary>
    /// <param name="entries">The translucent survivors, each carrying its leaf place and its distance along the view.</param>
    /// <remarks>
    /// Internal, and typed on the concrete list on purpose: this is the draw loop's own reusable
    /// buffer, sorted in place every frame, and CA1002's abstraction advice is for public
    /// surfaces — a wrapper here would be an allocation in the render loop for nobody.
    /// </remarks>
    internal static void Sort<T>(List<(int Leaf, float Along, T Entry)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // **Leaves in list order, each leaf's entries in the order they were collated** (B734): an insertion sort on the
        // leaf alone, which keeps the entry order within a leaf, as `BuildRenderablesList` hands each leaf's new entries
        // to `SortEntities` in collation order (`clientleafsystem.cpp:1822-1834`).
        for (int at = 1; at < entries.Count; at++)
        {
            (int Leaf, float Along, T Entry) moving = entries[at];
            int to = at - 1;

            while (to >= 0 && entries[to].Leaf > moving.Leaf)
            {
                entries[to + 1] = entries[to];
                to--;
            }

            entries[to + 1] = moving;
        }

        // Then each leaf's run by `SortEntities`' own H-sort.
        for (int start = 0; start < entries.Count;)
        {
            int end = start + 1;

            while (end < entries.Count && entries[end].Leaf == entries[start].Leaf)
            {
                end++;
            }

            HSort(entries, start, end - start);
            start = end;
        }
    }

    /// <summary>
    /// <c>SortEntities</c>' "H-sort" over one leaf's run (`clientleafsystem.cpp:1785-1809`): a gnome sort at strides 4, 2 and
    /// 1, swapping only on a strictly greater distance, so ties come out in its own order rather than a stable one.
    /// </summary>
    private static void HSort<T>(List<(int Leaf, float Along, T Entry)> entries, int start, int count)
    {
        if (count <= 1)
        {
            return;
        }

        int stepSize = 4;

        while (stepSize != 0)
        {
            int end = count - stepSize;
            int i = 0;

            // The engine's `for` steps back on a swap by rewriting its own counter; a `while` is the same walk.
            while (i < end)
            {
                if (entries[start + i].Along > entries[start + i + stepSize].Along)
                {
                    (entries[start + i], entries[start + i + stepSize]) = (entries[start + i + stepSize], entries[start + i]);

                    i = i == 0 ? -stepSize : i - (stepSize << 1);
                }

                i += stepSize;
            }

            stepSize >>= 1;
        }
    }

    /// <summary>
    /// The order <c>DrawNoZBufferTranslucentRenderables</c> draws the depth-ignoring renderables in (B734): the same collated,
    /// sorted list the walk reads, walked from its last entry to its first (`viewrender.cpp:4427-4458`).
    /// </summary>
    /// <param name="entries">Each renderable's leaf place, its distance along the view, and its index; sorted here.</param>
    /// <param name="order">Receives the indices in draw order; cleared first.</param>
    internal static void BackToFront(List<(int Leaf, float Along, int Entry)> entries, List<int> order)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(order);

        Sort(entries);
        order.Clear();

        for (int at = entries.Count; --at >= 0;)
        {
            order.Add(entries[at].Entry);
        }
    }

    /// <summary>A box centre's distance along the view forward axis — a particle effect's sort key (B734).</summary>
    /// <param name="centre">The world box centre: <c>GetRenderOrigin() + (mins + maxs) / 2</c>, render bounds being relative.</param>
    /// <param name="eye">The view origin.</param>
    /// <param name="forward">The view forward axis, unit length.</param>
    /// <returns>The dot product <c>SortEntities</c> sorts by (<c>clientleafsystem.cpp:1771-1777</c>).</returns>
    public static float Along(
        (float X, float Y, float Z) centre,
        (float X, float Y, float Z) eye,
        (float X, float Y, float Z) forward) =>
        ((centre.X - eye.X) * forward.X) + ((centre.Y - eye.Y) * forward.Y) + ((centre.Z - eye.Z) * forward.Z);

    /// <summary>An instance's distance along the view forward axis, measured at its box center.</summary>
    /// <param name="instance">The placed instance, whose <c>WorldBounds</c> is already world-space.</param>
    /// <param name="eye">The view origin.</param>
    /// <param name="forward">The view forward axis, unit length.</param>
    /// <remarks>
    /// The engine's arithmetic verbatim: box center minus render origin, dotted with forward. An
    /// instance with no bounds (the default zero box) falls back to its origin, and one with
    /// neither sits at the eye — drawn last, which is the safe side for something whose place is
    /// unknown.
    /// </remarks>
    public static float Along(
        in ModelInstance instance,
        (float X, float Y, float Z) eye,
        (float X, float Y, float Z) forward)
    {
        (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) =
            instance.WorldBounds;

        float x;
        float y;
        float z;

        if (minX == 0f && minY == 0f && minZ == 0f && maxX == 0f && maxY == 0f && maxZ == 0f)
        {
            (x, y, z) = instance.Origin ?? eye;
        }
        else
        {
            x = (minX + maxX) * 0.5f;
            y = (minY + maxY) * 0.5f;
            z = (minZ + maxZ) * 0.5f;
        }

        return ((x - eye.X) * forward.X) + ((y - eye.Y) * forward.Y) + ((z - eye.Z) * forward.Z);
    }
}
