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
/// The engine sorts per leaf inside a back-to-front leaf walk; this viewer's draw list has no
/// per-leaf grouping, so the sort runs over the whole translucent set against the same axis —
/// coarser bookkeeping, same comparison, same resulting rule.
/// </remarks>
public static class TranslucentOrder
{
    /// <summary>Sorts entries ascending along the view axis; draw them back to front by walking in reverse.</summary>
    /// <param name="entries">The translucent survivors, each carrying its distance along the view.</param>
    /// <remarks>
    /// Internal, and typed on the concrete list on purpose: this is the draw loop's own reusable
    /// buffer, sorted in place every frame, and CA1002's abstraction advice is for public
    /// surfaces — a wrapper here would be an allocation in the render loop for nobody.
    /// </remarks>
    internal static void Sort<T>(List<(float Along, T Entry)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        entries.Sort(static (a, b) => a.Along.CompareTo(b.Along));
    }

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
