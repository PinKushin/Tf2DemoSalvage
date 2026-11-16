using System;
using System.Collections.ObjectModel;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CItemMaterialCustomizationIconPanel` (game/client/econ/item_model_panel.cpp:52): a painted item's splat, one slanted
/// column per paint color (B751).
/// </summary>
/// <remarks>*Not built:* a custom decal's texture (`m_hUGCId`, :74), downloaded from the workshop by its UGC id.</remarks>
/// <param name="parent">The parent.</param>
/// <param name="name">The name.</param>
public sealed class TfPaintIconPanel(VguiPanel? parent, string? name) : VguiPanel(parent, name)
{
    /// <summary>The splat texture `DrawSetTextureFile` loads (:94).</summary>
    public const string SplatTexture = "vgui/backpack_jewel_paint_splatter";

    /// <inheritdoc/>
    public override string ClassName => "CItemMaterialCustomizationIconPanel";

    /// <summary>`m_colPaintColors`: one per paint, red team first.</summary>
    public Collection<(byte Red, byte Green, byte Blue, byte Alpha)> PaintColors { get; } = [];

    /// <inheritdoc/>
    /// <remarks>`PaintBackground` (:71): each color's column of the splat, at the panel's alpha.</remarks>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        for (int index = 0; index < PaintColors.Count; index++)
        {
            (byte red, byte green, byte blue, _) = PaintColors[index];

            surface.DrawSetTexture(SplatTexture);
            surface.DrawSetColor((red, green, blue, (byte)GetFloat("alpha")));
            DrawQuad(surface, index, PaintColors.Count);
            surface.DrawSetColor((255, 255, 255, 255));
        }

        surface.DrawSetTexture(null);
    }

    /// <summary>`DrawQuad` (:107): column <paramref name="index"/> of <paramref name="count"/>, its inner edges slanted.</summary>
    private void DrawQuad(IVguiSurface surface, int index, int count)
    {
        float scale = 1.0f / count;
        float offsetL = index * scale;
        float offsetR = (index + 1) * scale;
        float slant = scale * 0.65f;

        float upperLeft = index == 0 ? offsetL : offsetL + slant;
        float upperRight = index == count - 1 ? offsetR : offsetR + slant;
        float lowerLeft = index == 0 ? offsetL : offsetL - slant;
        float lowerRight = index == count - 1 ? offsetR : offsetR - slant;

        surface.DrawTexturedPolygon(
        [
            new VguiVertex(Wide * upperLeft, 0f, upperLeft, 0f),
            new VguiVertex(Wide * upperRight, 0f, upperRight, 0f),
            new VguiVertex(Wide * lowerRight, Tall, lowerRight, 1f),
            new VguiVertex(Wide * lowerLeft, Tall, lowerLeft, 1f),
        ]);
    }
}
