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
/// never collated; a box with no extent is unplaced, which the main view collates without a cull and the sky and water views
/// never do. The leaf sets are rebuilt only when the view changes, so a frame's many boxes reuse one.
///
/// **Each view draws what it collates** (B711): <see cref="Sees"/> answers for the main view, <see cref="SeesInSky"/> for
/// the 3D skybox view and <see cref="SeesInWater"/> for one water view, each of which draws its own
/// (`viewrender.cpp:4869-5030`, `:5411-5440`).
/// </remarks>
public sealed class BoxCollation
{
    private readonly HashSet<int> _main = [];
    private readonly HashSet<int> _sky = [];
    private readonly List<int> _touched = [];
    private readonly List<HashSet<int>> _water = [];
    private int _waterViews;
    private CollationView? _view;

    /// <summary>Whether the main view collates the box.</summary>
    /// <param name="view">The frame's views.</param>
    /// <param name="min">The box's lower corner, in world space.</param>
    /// <param name="max">The box's upper corner, in world space.</param>
    /// <returns>True when the main view would draw it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public bool Sees(CollationView view, Vector3 min, Vector3 max)
    {
        ArgumentNullException.ThrowIfNull(view);
        Prepare(view);

        // **A box with no extent is unplaced**, as the leaf system holds it (`ClientLeafSystem`, `WorldSpaceBounds.IsPlaced`):
        // linked to no leaf, collated by the main view at place 0 without a cull, never by the sky.
        if (!WorldSpaceBounds.IsPlaced((min.X, min.Y, min.Z, max.X, max.Y, max.Z)))
        {
            return true;
        }

        Touch(view, min, max);

        bool listed = view.Views.MainLeaves is null || TouchesAny(_main);

        return listed && !view.Frustum.Cull(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    /// <summary>Whether the 3D skybox view collates the box (<c>CSkyboxView::DrawInternal</c>, B711).</summary>
    /// <param name="view">The frame's views.</param>
    /// <param name="min">The box's lower corner, in world space.</param>
    /// <param name="max">The box's upper corner, in world space.</param>
    /// <returns>True when the sky view lists a leaf the box is in and its frustum holds the box.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public bool SeesInSky(CollationView view, Vector3 min, Vector3 max)
    {
        ArgumentNullException.ThrowIfNull(view);
        Prepare(view);

        if (!WorldSpaceBounds.IsPlaced((min.X, min.Y, min.Z, max.X, max.Y, max.Z)))
        {
            return false;
        }

        Touch(view, min, max);

        return TouchesAny(_sky) && !view.Views.SkyFrustum.Cull(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    /// <summary>Whether one water view collates the box (B711).</summary>
    /// <param name="view">The frame's views.</param>
    /// <param name="water">The water view's place in <see cref="RenderableViews.Water"/>.</param>
    /// <param name="min">The box's lower corner, in world space.</param>
    /// <param name="max">The box's upper corner, in world space.</param>
    /// <returns>True when that view lists a leaf the box is in and its frustum holds the box.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public bool SeesInWater(CollationView view, int water, Vector3 min, Vector3 max)
    {
        ArgumentNullException.ThrowIfNull(view);
        Prepare(view);

        if (water < 0 || water >= _waterViews || !WorldSpaceBounds.IsPlaced((min.X, min.Y, min.Z, max.X, max.Y, max.Z)))
        {
            return false;
        }

        Touch(view, min, max);

        return TouchesAny(_water[water]) && !view.Views.Water![water].Frustum.Cull(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    /// <summary>The views' leaf sets, rebuilt when the view changes.</summary>
    private void Prepare(CollationView view)
    {
        if (ReferenceEquals(view, _view))
        {
            return;
        }

        _view = view;
        _main.Clear();
        _sky.Clear();

        AddAll(view.Views.MainLeaves, _main);
        AddAll(view.Views.SkyLeaves, _sky);

        IReadOnlyList<RenderableView> water = view.Views.Water ?? [];

        for (int at = 0; at < water.Count; at++)
        {
            if (_water.Count == at)
            {
                _water.Add([]);
            }

            _water[at].Clear();
            AddAll(water[at].Leaves, _water[at]);
        }

        _waterViews = water.Count;
    }

    /// <summary>The leaves the box is in, or leaf 0 on a map with no tree.</summary>
    private void Touch(CollationView view, Vector3 min, Vector3 max)
    {
        _touched.Clear();

        if (view.Tree is { IsEmpty: false } tree)
        {
            tree.EnumerateLeavesInBox((min.X, min.Y, min.Z), (max.X, max.Y, max.Z), _touched);
        }
        else
        {
            _touched.Add(0);
        }
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
