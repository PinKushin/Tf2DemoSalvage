using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`Label::Alignment`.</summary>
public enum VguiAlignment
{
    /// <summary>`a_northwest`.</summary>
    NorthWest,

    /// <summary>`a_north`.</summary>
    North,

    /// <summary>`a_northeast`.</summary>
    NorthEast,

    /// <summary>`a_west`.</summary>
    West,

    /// <summary>`a_center`.</summary>
    Center,

    /// <summary>`a_east`.</summary>
    East,

    /// <summary>`a_southwest`.</summary>
    SouthWest,

    /// <summary>`a_south`.</summary>
    South,

    /// <summary>`a_southeast`.</summary>
    SouthEast,
}

/// <summary>`vgui::Label` (vgui2/vgui_controls/Label.cpp): a panel drawing one <see cref="VguiTextImage"/>.</summary>
/// <remarks>
/// Only the text image is modelled — `AddImage` is a code path no `.res` reaches, and a derived control that needs it
/// adds it. Not modelled: hotkeys and `associate`, which answer keyboard focus a demo viewer never has, and `%var%` label
/// text, whose `#var_` string localises to itself and so draws the same until dialog variables exist.
/// </remarks>
public class VguiLabel : VguiPanel
{
    // `g_AlignmentStrings` order, compared with `stricmp` (Label.cpp:1178).
    private static readonly string[] AlignmentStrings =
    [
        "north-west", "north", "north-east", "west", "center", "east", "south-west", "south", "south-east",
    ];

    private readonly VguiTextImage _textImage;
    private VguiContext? _context;
    private (int X, int Y) _textInset;
    private string? _fontOverrideName;
    private TextColorState _textColorState;
    private bool _wrap;
    private bool _centerWrap;
    private bool _autoWideToContents;
    private bool _autoTallToContents;
    private bool _autoWideDirty;
    private bool _autoTallDirty;
    private (byte, byte, byte, byte) _disabledFgColor1;
    private (byte, byte, byte, byte) _disabledFgColor2;

    /// <summary>`Label( parent, panelName, text )`: a text image coloured (0, 0, 0, 0) as image 0.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiLabel(VguiPanel? parent, string? name)
        : base(parent, name) =>
        _textImage = new VguiTextImage(null) { Color = (0, 0, 0, 0) };

    private enum TextColorState
    {
        Normal,
        Dull,
        Bright,
    }

    /// <inheritdoc/>
    public override string ClassName => "Label";

    /// <summary>`_contentAlignment`: west until set.</summary>
    public VguiAlignment ContentAlignment { get; set; } = VguiAlignment.West;

    /// <summary>The text as the image holds it — localised.</summary>
    public string Text => _textImage.Text;

    /// <summary>`SetText`.</summary>
    /// <param name="text">The text, or a `#` token.</param>
    /// <param name="localize">The localisation lookup, or null.</param>
    public void SetText(string? text, Func<string, string?>? localize)
    {
        _textImage.SetText(text ?? string.Empty, localize);
        _autoWideDirty = _autoWideToContents;
        _autoTallDirty = _autoTallToContents;
        InvalidateLayout();
    }

    /// <summary>`SetTextInset`: the draw width is the label less the x inset.</summary>
    /// <param name="x">The x inset.</param>
    /// <param name="y">The y inset.</param>
    public void SetTextInset(int x, int y)
    {
        _textInset = (x, y);
        _textImage.SetDrawWidth(Wide - x);
    }

    /// <summary>`GetContentSize`: the aligned span, the inset, and the image's untruncated size in place of its size.</summary>
    /// <returns>The size the label's content wants.</returns>
    public (int Wide, int Tall) GetContentSize()
    {
        IVguiSurface surface = Surface();

        if (_textImage.Font is null && _context is not null)
        {
            _textImage.Font = _context.GetFont("Default", Proportional);
        }

        (int tx0, int ty0, int tx1, int ty1) = ComputeAlignment();
        (int contentWide, int contentTall) = _textImage.GetContentSize(surface);
        int wide = (tx1 - tx0) + _textInset.X - _textImage.Wide + contentWide;

        return (wide, Math.Max((ty1 - ty0) + _textInset.Y, contentTall));
    }

    /// <summary>`SizeToContents`.</summary>
    public void SizeToContents() => (Wide, Tall) = GetContentSize();

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        base.ApplySettings(block, context);

        if (block.Find("labelText")?.Value is { } labelText)
        {
            SetText(labelText, context.Localize);
        }

        int align = Array.FindIndex(AlignmentStrings, name => string.Equals(name, block.Find("textAlignment")?.Value, StringComparison.OrdinalIgnoreCase));

        if (align >= 0)
        {
            ContentAlignment = (VguiAlignment)align;
        }

        _textColorState = TextColorState.Normal;

        if (Int(block, "dulltext", 0) == 1)
        {
            _textColorState = TextColorState.Dull;
        }
        else if (Int(block, "brighttext", 0) == 1)
        {
            _textColorState = TextColorState.Bright;
        }

        if (block.Find("font")?.Value is { Length: > 0 } overrideFont)
        {
            _fontOverrideName = overrideFont;
            _textImage.Font = context.GetFont(overrideFont, Proportional);
        }
        else if (_fontOverrideName is not null)
        {
            _fontOverrideName = null;
            _textImage.Font = context.GetFont("Default", Proportional);
        }

        _centerWrap = Int(block, "centerwrap", 0) > 0;
        _textImage.CenterWrap = _centerWrap;
        _autoWideToContents = Int(block, "auto_wide_tocontents", 0) > 0;
        _autoTallToContents = Int(block, "auto_tall_tocontents", 0) > 0;
        _wrap = Int(block, "wrap", 0) > 0;
        _textImage.Wrap = _wrap;

        int insetX = Int(block, "textinsetx", _textInset.X);
        int insetY = Int(block, "textinsety", _textInset.Y);
        bool proportionalInsets = Int(block, "use_proportional_insets", 0) > 0;

        // `IsEmpty` is true for a missing key, so only a written inset scales — else a set one would grow each call.
        if (proportionalInsets && block.Find("textinsetx") is not null)
        {
            insetX = context.Scale(insetX);
        }

        if (proportionalInsets && block.Find("textinsety")?.Value is { } flInsetY)
        {
            insetY = (int)MathF.Ceiling(PanelLayout.Atof(flInsetY) * context.Scale(1000) / 1000f);
        }

        SetTextInset(insetX, insetY);
        _textImage.AllCaps = Int(block, "allcaps", 0) > 0;
        InvalidateLayout(reloadScheme: true);
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        base.ApplySchemeSettings(context);

        if (_fontOverrideName is not null)
        {
            _textImage.Font = context.GetFont(_fontOverrideName, Proportional);
        }

        _textImage.Font ??= context.GetFont("Default", Proportional);

        if (_wrap || _centerWrap)
        {
            _textImage.SetSize(Wide - _textInset.X, Tall);
        }
        else
        {
            (int wide, int tall) = _textImage.GetContentSize(Surface());

            _textImage.SetSize(wide, tall);
        }

        _autoWideDirty |= _autoWideToContents;
        _autoTallDirty |= _autoTallToContents;

        if (_autoWideToContents || _autoTallToContents)
        {
            HandleAutoSizing();
        }

        (byte, byte, byte, byte) white = (255, 255, 255, 255);

        _disabledFgColor1 = context.Scheme.GetColor("Label.DisabledFgColor1", white);
        _disabledFgColor2 = context.Scheme.GetColor("Label.DisabledFgColor2", white);
        BgColor = context.Scheme.GetColor("Label.BgColor", white);
        FgColor = context.Scheme.GetColor(
            _textColorState switch
            {
                TextColorState.Dull => "Label.TextDullColor",
                TextColorState.Bright => "Label.TextBrightColor",
                _ => "Label.TextColor",
            },
            white);
    }

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        (int tx0, int ty0, _, int ty1) = ComputeAlignment();
        int x = tx0;
        int y = _textInset.Y + ty0;

        x += ContentAlignment switch
        {
            VguiAlignment.NorthWest or VguiAlignment.West or VguiAlignment.SouthWest => _textInset.X,
            VguiAlignment.NorthEast or VguiAlignment.East or VguiAlignment.SouthEast => -_textInset.X,
            _ => 0,
        };

        int imageY = y;

        if (ContentAlignment is VguiAlignment.West or VguiAlignment.Center or VguiAlignment.East && _textImage.Tall < ty1 - ty0)
        {
            imageY = ((ty1 - ty0 - _textImage.Tall) / 2) + y;
        }

        if (Enabled)
        {
            _textImage.SetPos(x, imageY);
            _textImage.Color = FgColor;
            _textImage.Paint(surface);
            return;
        }

        // The embossed disabled look: offset in the first colour, then in place in the second.
        _textImage.SetPos(x + 1, imageY + 1);
        _textImage.Color = _disabledFgColor1;
        _textImage.Paint(surface);
        _textImage.SetPos(x, imageY);
        _textImage.Color = _disabledFgColor2;
        _textImage.Paint(surface);
    }

    /// <inheritdoc/>
    protected override void PerformLayout()
    {
        int wide = Wide - _textInset.X;
        (int contentWide, int contentTall) = _textImage.GetContentSize(Surface());

        _textImage.SetSize(_wrap || _centerWrap ? wide : Math.Min(wide, contentWide), contentTall);

        HandleAutoSizing();
        HandleAutoSizing();
    }

    /// <summary>`ComputeAlignment`: the image's box in the paint size, west when the image is wider than it.</summary>
    private (int X0, int Y0, int X1, int Y1) ComputeAlignment()
    {
        (int left, int top, int right, int bottom) = Border?.GetInset() ?? (0, 0, 0, 0);
        int wide = Wide - (left + right);
        int tall = Tall - (top + bottom);
        (int imageWide, int imageTall) = (_textImage.Wide, _textImage.Tall);
        VguiAlignment xAlignment = imageWide > wide ? VguiAlignment.West : ContentAlignment;

        int tx0 = xAlignment switch
        {
            VguiAlignment.North or VguiAlignment.Center or VguiAlignment.South => (wide - imageWide) / 2,
            VguiAlignment.NorthEast or VguiAlignment.East or VguiAlignment.SouthEast => wide - imageWide,
            _ => 0,
        };

        int ty0 = ContentAlignment switch
        {
            VguiAlignment.West or VguiAlignment.Center or VguiAlignment.East => (tall - imageTall) / 2,
            VguiAlignment.SouthWest or VguiAlignment.South or VguiAlignment.SouthEast => tall - imageTall,
            _ => 0,
        };

        return (tx0, ty0, tx0 + imageWide, ty0 + imageTall);
    }

    /// <summary>`HandleAutoSizing`: a dirty axis takes the content size, once.</summary>
    private void HandleAutoSizing()
    {
        if (!_autoWideDirty && !_autoTallDirty)
        {
            return;
        }

        (int wide, int tall) = GetContentSize();

        (Wide, Tall) = (_autoWideDirty ? wide : Wide, _autoTallDirty ? tall : Tall);
        _autoWideDirty = false;
        _autoTallDirty = false;
    }

    private IVguiSurface Surface() =>
        _context?.Surface ?? throw new InvalidOperationException($"Label '{Name}' measures text before a context with a surface reached it.");
}
