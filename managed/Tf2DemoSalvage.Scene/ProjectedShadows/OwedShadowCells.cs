using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>
/// The cells a draw still owes: <c>SHADOW_FLAGS_TEXTURE_DIRTY</c>, which the engine clears only once
/// <c>DrawRenderToTextureShadow</c> has drawn the cell (`clientshadowmgr.cpp:3774-3796`) — where here the casters decide a
/// redraw when they hand a frame over, so a frame no draw took leaves its new and dirty cells owed (B702, finding 107).
/// </summary>
/// <remarks>
/// **An owed cell stays owed until the shadow is drawn into it.** A frame that does not enumerate the shadow keeps the debt,
/// as the engine's flag outlives a frame its view did not list the shadow in; a frame that draws it, or that gives its cell
/// to another shadow (<c>UseTexture</c>'s new cell, which that shadow redraws) or gives it a cell of its own, settles it.
/// A blob frame keeps it: <c>DrawRenderToTextureShadowLOD</c> leaves the dirty flag set (`:3816-3826`).
/// </remarks>
public sealed class OwedShadowCells
{
    private readonly Dictionary<int, ShadowCell> _owed = [];
    private readonly HashSet<ShadowCell> _redrawn = [];
    private readonly HashSet<int> _present = [];
    private readonly List<int> _settled = [];

    /// <summary>How many cells are owed.</summary>
    public int Count => _owed.Count;

    /// <summary>A frame no draw took: every cell it was to redraw is owed.</summary>
    /// <param name="undrawn">The frame.</param>
    /// <exception cref="ArgumentNullException"><paramref name="undrawn"/> is null.</exception>
    public void Owe(ShadowFrame undrawn)
    {
        ArgumentNullException.ThrowIfNull(undrawn);

        foreach (CastShadow shadow in undrawn.Casters)
        {
            if (shadow.Redraw && !shadow.Blob)
            {
                _owed[shadow.EntityIndex] = shadow.Cell;
            }
        }
    }

    /// <summary>The next frame with the owed redraws it can settle marked, and those settled forgotten.</summary>
    /// <param name="next">The frame.</param>
    /// <returns><paramref name="next"/> itself when nothing is owed or nothing changes; a copy with the redraws otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> is null.</exception>
    public ShadowFrame Apply(ShadowFrame next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (_owed.Count == 0)
        {
            return next;
        }

        _redrawn.Clear();
        _present.Clear();

        foreach (CastShadow shadow in next.Casters)
        {
            _present.Add(shadow.EntityIndex);

            if (shadow.Redraw && !shadow.Blob)
            {
                _redrawn.Add(shadow.Cell);
            }
        }

        List<CastShadow>? casters = null;

        for (int at = 0; at < next.Casters.Count; at++)
        {
            CastShadow shadow = next.Casters[at];

            // An enumerated shadow lists at least its caster (`DrawShadowHierarchy`); one handed back unenumerated, none.
            if (shadow.Blob || shadow.Redraw || shadow.Hierarchy.Count == 0 || !_owed.TryGetValue(shadow.EntityIndex, out ShadowCell cell) ||
                cell != shadow.Cell || _redrawn.Contains(cell))
            {
                continue;
            }

            casters ??= [.. next.Casters];
            casters[at] = shadow with { Redraw = true };
            _redrawn.Add(cell);
            _owed.Remove(shadow.EntityIndex);
        }

        Settle(next);

        return casters is null ? next : next with { Casters = casters };
    }

    /// <summary>Forgets each debt the frame settles: the shadow gone, drawn, on another cell, or its cell redrawn by another.</summary>
    private void Settle(ShadowFrame next)
    {
        _settled.Clear();

        foreach ((int entity, ShadowCell cell) in _owed)
        {
            if (!_present.Contains(entity) || _redrawn.Contains(cell))
            {
                _settled.Add(entity);
            }
        }

        foreach (CastShadow shadow in next.Casters)
        {
            if (!shadow.Blob && _owed.TryGetValue(shadow.EntityIndex, out ShadowCell cell) && cell != shadow.Cell)
            {
                _settled.Add(shadow.EntityIndex);
            }
        }

        foreach (int entity in _settled)
        {
            _owed.Remove(entity);
        }
    }
}
