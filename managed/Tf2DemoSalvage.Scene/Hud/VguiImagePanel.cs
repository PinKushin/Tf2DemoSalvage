using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`vgui::Bitmap`, closed in vgui2.dll: one material drawn as a textured rectangle.</summary>
/// <remarks>
/// vgui2.dll (`tf2vgui2`): `CScheme::GetImage` (0x18000d2f0) names the material `vgui/%s`, or the name as given when it
/// contains `.pic`; the constructor (0x180001f30) and vtable 0x18005cad8. `GetSize` (0x180002220) is the texture's size
/// until one is set; `Paint` (0x180002290) sets the colour and texture, measures when the width is still 0, then
/// `DrawTexturedRect( x, y, x + wide, y + tall )`. **Not modelled:** `.pic` procedural images, and a rotated bitmap, which
/// draws a polygon at the panel's origin rather than its position — no stock HUD file sets `rotation`.
/// </remarks>
/// <param name="material">The material path, `vgui/` included.</param>
public sealed class VguiBitmap(string material) : VguiImage
{
    /// <summary>The material path.</summary>
    public string Material { get; } = material;

    /// <summary>`GetSize`: the texture's size until a size is set.</summary>
    /// <param name="surface">The surface, which knows the texture.</param>
    /// <returns>The size.</returns>
    public (int Wide, int Tall) GetSize(IVguiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Wide == 0 && Tall == 0)
        {
            (int wide, int tall) = surface.DrawGetTextureSize(Material);

            SetSize(wide, tall);
        }

        return (Wide, Tall);
    }

    /// <inheritdoc/>
    public override (int Wide, int Tall) GetContentSize(IVguiSurface surface) => GetSize(surface);

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        surface.DrawSetColor(Color);
        surface.DrawSetTexture(Material);

        if (Wide == 0)
        {
            GetSize(surface);
        }

        surface.DrawTexturedRect(X, Y, X + Wide, Y + Tall);
    }
}

/// <summary>`vgui::ImagePanel` (vgui2/vgui_controls/ImagePanel.cpp): a fill colour and one image, scaled, tiled or placed.</summary>
/// <remarks>
/// `ApplySettings` (:283) reads its own keys and then the panel's, so `fillcolor_override` and `drawcolor_override` win.
/// `PaintBackground` (:118) replaces the panel's: the fill when its alpha is above 0, then the image at the origin —
/// scaled to `scaleAmount` or else to the panel, or tiled across it, or at its own size. `centerImage` has no `.res` key.
/// </remarks>
public class VguiImagePanel : VguiPanel
{
    private string? _imageName;
    private bool _positionImage = true;
    private bool _scaleImage;
    private float _scaleAmount;
    private bool _tileHorizontally;
    private bool _tileVertically;

    /// <summary>`ImagePanel( parent, name )`.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiImagePanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        RegisterColorAsOverridable("fillcolor_override", color => FillColor = color);
        RegisterColorAsOverridable("drawcolor_override", color => DrawColor = color);
    }

    /// <inheritdoc/>
    public override string ClassName => "ImagePanel";

    /// <summary>`m_pImage`: set by `ApplySchemeSettings` from the image name.</summary>
    public VguiBitmap? Image { get; set; }

    /// <summary>`m_FillColor`: none until set.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) FillColor { get; set; }

    /// <summary>`m_DrawColor`: white until set.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) DrawColor { get; set; } = (255, 255, 255, 255);

    /// <summary>`SetImage( const char * )`: the scheme loads it at the next scheme pass.</summary>
    /// <param name="imageName">The image, under `vgui/`.</param>
    public void SetImage(string imageName)
    {
        if (string.Equals(imageName, _imageName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _imageName = imageName;
        InvalidateLayout(reloadScheme: true);
    }

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(context);

        _imageName = null;
        _positionImage = Int(block, "positionImage", 1) != 0;
        _scaleImage = Int(block, "scaleImage", 0) != 0;
        _scaleAmount = block.Find("scaleAmount")?.Value is { } amount ? PanelLayout.Atof(amount) : 0f;

        // `QuickPropScale( 1000 )` scales only on a proportional panel.
        if (Int(block, "scaleProportional", 0) == 1 && _scaleImage && Proportional)
        {
            _scaleAmount *= .001f * context.Scale(1000);
        }

        int tile = Int(block, "tileImage", 0);

        _tileHorizontally = Int(block, "tileHorizontally", tile) != 0;
        _tileVertically = Int(block, "tileVertically", tile) != 0;

        if (block.Find("image")?.Value is { Length: > 0 } imageName)
        {
            SetImage(imageName);
        }

        if (block.Find("fillcolor")?.Value is { Length: > 0 } fill)
        {
            FillColor = ScriptColor(fill, context, (0, 0, 0, 0));
        }

        if (block.Find("drawcolor")?.Value is { Length: > 0 } draw)
        {
            DrawColor = ScriptColor(draw, context, (255, 255, 255, 255));
        }

        base.ApplySettings(block, context);
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        if (_imageName is { Length: > 0 })
        {
            // `CScheme::GetImage`: `vgui/%s`, unless the name is a `.pic`.
            Image = new VguiBitmap(_imageName.Contains(".pic", StringComparison.Ordinal) ? _imageName : "vgui/" + _imageName);
        }
    }

    /// <inheritdoc/>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (FillColor.Alpha > 0)
        {
            surface.DrawSetColor(FillColor);
            surface.DrawFilledRect(0, 0, Wide, Tall);
        }

        if (Image is not { } image)
        {
            return;
        }

        image.Color = DrawColor;

        if (_positionImage)
        {
            image.SetPos(0, 0);
        }

        (int imageWide, int imageTall) = image.GetSize(surface);

        if (_scaleImage)
        {
            // The size lives in the bitmap, so it is set to the drawn size and put back after.
            if (_scaleAmount > 0f)
            {
                image.SetSize((int)(imageWide * _scaleAmount), (int)(imageTall * _scaleAmount));
            }
            else
            {
                image.SetSize(Wide, Tall);
            }

            image.Paint(surface);
            image.SetSize(imageWide, imageTall);
            return;
        }

        if (!_tileHorizontally && !_tileVertically)
        {
            image.Paint(surface);
            return;
        }

        // Valve steps by the image's size and would never finish on an empty one, whose rectangles draw nothing anyway.
        if (imageWide <= 0 || imageTall <= 0)
        {
            return;
        }

        for (int y = 0; y < Tall; y += imageTall)
        {
            for (int x = 0; x < Wide; x += imageWide)
            {
                image.SetPos(x, y);
                image.Paint(surface);

                if (!_tileHorizontally)
                {
                    break;
                }
            }

            if (!_tileVertically)
            {
                break;
            }
        }

        if (_positionImage)
        {
            image.SetPos(0, 0);
        }
    }

    /// <summary>`sscanf( "%d %d %d %d" )` over a default alpha of 255 when three or more read, else the scheme's colour.</summary>
    private static (byte, byte, byte, byte) ScriptColor(string text, VguiContext context, (byte, byte, byte, byte) fallback)
    {
        Span<int> channels = [0, 0, 0, 255];

        return PanelLayout.ScanInts(text, channels) >= 3
            ? ((byte)channels[0], (byte)channels[1], (byte)channels[2], (byte)channels[3])
            : context.Scheme.GetColor(text, fallback);
    }
}
