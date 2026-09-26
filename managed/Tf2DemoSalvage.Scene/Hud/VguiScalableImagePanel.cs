using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`vgui::ScalableImagePanel` (vgui2/vgui_controls/ScalableImagePanel.cpp): one texture as a nine-slice.</summary>
/// <remarks>
/// `ApplySettings` (:183): `drawcolor` (sscanf of three or more, else the scheme's, else nothing), `src_corner_*` in
/// texels, `draw_corner_*` in pixels — scaled on a proportional panel — and `image` under `vgui/`. `PerformLayout` (:228)
/// makes the source corners a fraction of the texture. `PaintBackground` (:78) draws each of the nine as a
/// `DrawTexturedPolygon` quad.
/// </remarks>
public class VguiScalableImagePanel : VguiPanel
{
    private VguiContext? _context;
    private string? _imageName;
    private (byte Red, byte Green, byte Blue, byte Alpha) _drawColor = (255, 255, 255, 255);
    private int _srcCornerWidth;
    private int _srcCornerHeight;
    private int _cornerWidth;
    private int _cornerHeight;
    private float _cornerWidthPercent;
    private float _cornerHeightPercent;

    /// <summary>`ScalableImagePanel( parent, name )`.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiScalableImagePanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
    }

    /// <inheritdoc/>
    public override string ClassName => "ScalableImagePanel";

    /// <summary>`SetImage`: `vgui/` and the name, or none for an empty one.</summary>
    /// <param name="imageName">The image.</param>
    public void SetImage(string imageName)
    {
        ArgumentNullException.ThrowIfNull(imageName);

        _imageName = imageName.Length > 0 ? "vgui/" + imageName : null;
        InvalidateLayout();
    }

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        base.ApplySettings(block, context);

        if (block.Find("drawcolor")?.Value is { Length: > 0 } draw)
        {
            Span<int> channels = [0, 0, 0, 255];

            _drawColor = PanelLayout.ScanInts(draw, channels) >= 3
                ? ((byte)channels[0], (byte)channels[1], (byte)channels[2], (byte)channels[3])
                : context.Scheme.GetColor(draw, (0, 0, 0, 0));
        }

        _srcCornerHeight = Int(block, "src_corner_height", 0);
        _srcCornerWidth = Int(block, "src_corner_width", 0);
        _cornerHeight = Int(block, "draw_corner_height", 0);
        _cornerWidth = Int(block, "draw_corner_width", 0);

        if (Proportional)
        {
            _cornerHeight = context.Scale(_cornerHeight);
            _cornerWidth = context.Scale(_cornerWidth);
        }

        SetImage(block.Find("image")?.Value ?? string.Empty);
    }

    /// <inheritdoc/>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        surface.DrawSetColor(_drawColor with { Alpha = (byte)(int)GetFloat("alpha") });
        surface.DrawSetTexture(_imageName ?? string.Empty);

        float y = 0f;
        float uvy = 0f;

        for (int row = 0; row < 3; row++)
        {
            bool edgeRow = row != 1;
            float uvh = edgeRow ? _cornerHeightPercent : Math.Max(1f - (2f * _cornerHeightPercent), 0f);
            float drawH = edgeRow ? _cornerHeight : Math.Max(0, Tall - (2 * _cornerHeight));
            float x = 0f;
            float uvx = 0f;

            for (int col = 0; col < 3; col++)
            {
                bool edgeColumn = col != 1;
                float uvw = edgeColumn ? _cornerWidthPercent : Math.Max(1f - (2f * _cornerWidthPercent), 0f);
                float drawW = edgeColumn ? _cornerWidth : Math.Max(0, Wide - (2 * _cornerWidth));

                surface.DrawTexturedQuad(x, y, x + drawW, y + drawH, uvx, uvy, uvx + uvw, uvy + uvh);
                x += drawW;
                uvx += uvw;
            }

            y += drawH;
            uvy += uvh;
        }
    }

    /// <inheritdoc/>
    protected override void PerformLayout()
    {
        // With no image the texture id is still the fresh one, which measures 0 by 0.
        (int wide, int tall) = _imageName is not null && _context?.Surface is { } surface ? surface.DrawGetTextureSize(_imageName) : (0, 0);

        _cornerWidthPercent = wide > 0 ? (float)_srcCornerWidth / wide : 0f;
        _cornerHeightPercent = tall > 0 ? (float)_srcCornerHeight / tall : 0f;
    }
}
