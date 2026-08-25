using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Operations on the list of props a moment will draw.</summary>
public static class DrawList
{
    /// <summary>Narrows the draw list to the props a visibility rule kept.</summary>
    /// <param name="drawn">The draw list, replaced in place.</param>
    /// <param name="visible">What the rule kept, which may be a view over <paramref name="drawn"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **One place, because this was written twice** — once for
    /// <see cref="FirstPersonVisibility"/> and once for <see cref="WeaponVisibility"/>, identically,
    /// including the part that is not obvious.
    ///
    /// **The intermediate copy is load-bearing and looks like waste.** <paramref name="visible"/>
    /// may be a view over <paramref name="drawn"/> rather than a separate list, in which case it is
    /// still reading the draw list when the swap begins — so clearing first would hand
    /// <c>AddRange</c> an empty sequence and delete the whole scene. Both callers answer with a
    /// materialised list today, so nothing is currently broken by removing it; the next visibility
    /// rule has no reason to know that, and a pinned test says so.
    ///
    /// **The count comparison is an early-out, not the rule.** Equal counts mean the filter kept
    /// everything, which is the ordinary frame — no camera filter and every weapon drawn — and it
    /// should not pay for a copy to discover that.
    /// </remarks>
    public static void KeepOnly(IList<SceneProp> drawn, IReadOnlyList<SceneProp> visible)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(visible);

        if (visible.Count == drawn.Count)
        {
            return;
        }

        List<SceneProp> kept = [.. visible];

        drawn.Clear();

        foreach (SceneProp prop in kept)
        {
            drawn.Add(prop);
        }
    }
}
