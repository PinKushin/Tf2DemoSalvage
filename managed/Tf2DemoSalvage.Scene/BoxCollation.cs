using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Whether a frame's views would collate one world box — the leaf system's answer for a single renderable, for a
/// collection small enough not to need its own <see cref="ClientLeafSystem"/>.
/// </summary>
/// <remarks>
/// **The same two questions <see cref="ClientLeafSystem.BuildRenderablesList"/> asks** (`clientleafsystem.cpp:1574-1716`,
/// `:1813`): is the box filed in a leaf the view lists, and is it inside that view's frustum. A placed box in no leaf is
/// never collated; a box with no extent is unplaced, which the main view collates without a cull and the sky never does. The leaf sets are rebuilt only
/// when the view changes, so a frame's many boxes reuse one.
/// </remarks>
public sealed class BoxCollation
{
    private readonly HashSet<int> _main = [];
    private readonly HashSet<int> _sky = [];
    private readonly List<int> _touched = [];
    private CollationView? _view;

    /// <summary>Whether <paramref name="view"/> collates the box.</summary>
    /// <param name="view">The frame's views.</param>
    /// <param name="min">The box's lower corner, in world space.</param>
    /// <param name="max">The box's upper corner, in world space.</param>
    /// <returns>True when some view would draw it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public bool Sees(CollationView view, Vector3 min, Vector3 max)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (!ReferenceEquals(view, _view))
        {
            _view = view;
            _main.Clear();
            _sky.Clear();

            AddAll(view.Views.MainLeaves, _main);
            AddAll(view.Views.SkyLeaves, _sky);
        }

        // **A box with no extent is unplaced**, as the leaf system holds it (`ClientLeafSystem`, `WorldSpaceBounds.IsPlaced`):
        // linked to no leaf, collated by the main view at place 0 without a cull, never by the sky.
        if (!WorldSpaceBounds.IsPlaced((min.X, min.Y, min.Z, max.X, max.Y, max.Z)))
        {
            return true;
        }

        _touched.Clear();

        if (view.Tree is { IsEmpty: false } tree)
        {
            tree.EnumerateLeavesInBox((min.X, min.Y, min.Z), (max.X, max.Y, max.Z), _touched);
        }
        else
        {
            _touched.Add(0);
        }

        bool listed = view.Views.MainLeaves is null || TouchesAny(_main);

        if (listed && !view.Frustum.Cull(min.X, min.Y, min.Z, max.X, max.Y, max.Z))
        {
            return true;
        }

        return TouchesAny(_sky) && !view.Views.SkyFrustum.Cull(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    /// <summary>Indexed, so a frame's new view boxes no enumerator (B599).</summary>
    private static void AddAll(IReadOnlyList<int>? leaves, HashSet<int> into)
    {
        for (int at = 0; leaves is not null && at < leaves.Count; at++)
        {
            into.Add(leaves[at]);
        }
    }

    private bool TouchesAny(HashSet<int> leaves)
    {
        for (int at = 0; at < _touched.Count; at++)
        {
            if (leaves.Contains(_touched[at]))
            {
                return true;
            }
        }

        return false;
    }
}
