using System;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>Where each panel lands on screen — `vgui2.dll`'s `VPanel_Solve` (0x18001c8e0), closed code.</summary>
/// <remarks>
/// Read from the disassembly; `VPanel` keeps positions as `short`s, which the casts below keep.
/// <list type="bullet">
/// <item>With a pin sibling (solved first), X is <c>(int)((float)(int)(siblingWide · fx[siblingCorner] + siblingX) −
/// wide · fx[ownCorner]) + sign · x</c>, where the sibling's X is its absolute position less the parent's; the sign is −1
/// on a sibling's left edge (corners 0, 2 and 7), +1 otherwise. Y likewise, −1 on a top edge (0, 1 and 4).</item>
/// <item>Absolute position is that plus the parent's absolute position and its left and top inset.</item>
/// <item>The clip rectangle is the panel's own, cut to the parent's; a far edge beyond the parent's is set to the parent's
/// less the parent's right or bottom inset, and never left before the near edge.</item>
/// </list>
/// Popups, which solve against the surface's embedded panel, are not modelled: a HUD has none.
/// </remarks>
public static class VguiLayout
{
    /// <summary>The corner fractions at 0x18007c530, in `PinCorner_e` order.</summary>
    private static readonly (float X, float Y)[] Corners =
    [
        (0f, 0f), (1f, 0f), (0f, 1f), (1f, 1f), (0.5f, 0f), (1f, 0.5f), (0.5f, 1f), (0f, 0.5f),
    ];

    /// <summary>`CMatSystemSurface::SolveTraverse` (vguimatsurface.dll 0x1800134c0): scheme settings, think, solve.</summary>
    /// <param name="panel">The root of the tree.</param>
    /// <param name="context">The scheme and screen.</param>
    /// <param name="forceApplySchemeSettings">Whether invisible children get their scheme too.</param>
    public static void SolveTraverse(VguiPanel panel, VguiContext context, bool forceApplySchemeSettings = false)
    {
        SchemeSettingsTraverse(panel, context, forceApplySchemeSettings);
        ThinkTraverse(panel);
        InternalSolveTraverse(panel);
    }

    /// <summary>`InternalSolveTraverse` (0x180010c10): solves a panel, then each visible child in paint order.</summary>
    /// <param name="panel">The root of the tree to solve.</param>
    public static void InternalSolveTraverse(VguiPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        Solve(panel);

        foreach (VguiPanel child in panel.Children)
        {
            if (child.Visible)
            {
                InternalSolveTraverse(child);
            }
        }
    }

    /// <summary>`InternalSchemeSettingsTraverse` (0x1800109e0): visible children (all, when forced) first, then the panel.</summary>
    private static void SchemeSettingsTraverse(VguiPanel panel, VguiContext context, bool force)
    {
        ArgumentNullException.ThrowIfNull(panel);

        foreach (VguiPanel child in panel.Children)
        {
            if (force || child.Visible)
            {
                SchemeSettingsTraverse(child, context, force);
            }
        }

        panel.PerformApplySchemeSettings(context);
    }

    /// <summary>`InternalThinkTraverse` (0x180010d90): the panel thinks, then each visible child.</summary>
    private static void ThinkTraverse(VguiPanel panel)
    {
        panel.Think();

        foreach (VguiPanel child in panel.Children)
        {
            if (child.Visible)
            {
                ThinkTraverse(child);
            }
        }
    }

    /// <summary>`VPanel_Solve`: one panel's absolute position and clip rectangle.</summary>
    /// <param name="panel">The panel.</param>
    public static void Solve(VguiPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        VguiPanel? parent = panel.Parent;
        int x = panel.X;
        int y = panel.Y;

        if (panel.PinSibling is { } sibling)
        {
            Solve(sibling);

            int siblingX = sibling.AbsX - (parent?.AbsX ?? 0);
            int siblingY = sibling.AbsY - (parent?.AbsY ?? 0);
            (float siblingFx, float siblingFy) = Corners[(byte)panel.PinToSiblingCorner];
            (float ownFx, float ownFy) = Corners[(byte)panel.PinCornerToSibling];
            int signX = panel.PinToSiblingCorner is VguiPinCorner.TopLeft or VguiPinCorner.BottomLeft or VguiPinCorner.CenterLeft ? -1 : 1;
            int signY = panel.PinToSiblingCorner is VguiPinCorner.TopLeft or VguiPinCorner.TopRight or VguiPinCorner.CenterTop ? -1 : 1;

            x = (int)((float)(int)((sibling.Wide * siblingFx) + siblingX) - (panel.Wide * ownFx)) + (x * signX);
            y = (int)((float)(int)((sibling.Tall * siblingFy) + siblingY) - (panel.Tall * ownFy)) + (y * signY);
        }

        short absX = (short)x;
        short absY = (short)y;

        if (parent is not null)
        {
            absX = (short)(absX + parent.AbsX + parent.Inset.Left);
            absY = (short)(absY + parent.AbsY + parent.Inset.Top);
        }

        panel.AbsX = absX;
        panel.AbsY = absY;

        short x0 = absX;
        short y0 = absY;
        short x1 = (short)(absX + panel.Wide);
        short y1 = (short)(absY + panel.Tall);

        if (parent is not null)
        {
            (int parentX0, int parentY0, int parentX1, int parentY1) = parent.ClipRect;

            x0 = x0 < parentX0 ? (short)parentX0 : x0;
            y0 = y0 < parentY0 ? (short)parentY0 : y0;
            x1 = parentX1 < x1 ? (short)(parentX1 - parent.Inset.Right) : x1;
            y1 = parentY1 < y1 ? (short)(parentY1 - parent.Inset.Bottom) : y1;
            x1 = x1 < x0 ? x0 : x1;
            y1 = y1 < y0 ? y0 : y1;
        }

        panel.ClipRect = (x0, y0, x1, y1);
    }
}
