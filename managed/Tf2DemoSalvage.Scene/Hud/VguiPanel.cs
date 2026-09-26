using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`IPanelAnimationPropertyConverter`'s type names (Panel.cpp:6243, `InitPropertyConverters`).</summary>
public enum VguiPanelVarType
{
    /// <summary>`float`: `atof`.</summary>
    Real,

    /// <summary>`int`: `atoi`.</summary>
    Whole,

    /// <summary>`Color`: a scheme colour name, else transparent black.</summary>
    Color,

    /// <summary>`bool`: `atoi` non-zero; a default of `true` too.</summary>
    Bool,

    /// <summary>`string` or `char`.</summary>
    Text,

    /// <summary>`HFont`: `IScheme::GetFont` at the panel's proportionality.</summary>
    Font,

    /// <summary>`proportional_float`: scaled through an int, **whether or not the panel is proportional**.</summary>
    ProportionalFloat,

    /// <summary>`proportional_int`: scaled, whether or not the panel is proportional.</summary>
    ProportionalInt,

    /// <summary>`proportional_xpos`: `ComputePos` across the parent (proportional) or screen.</summary>
    ProportionalXPos,

    /// <summary>`proportional_ypos`: `ComputePos` down the parent (proportional) or screen.</summary>
    ProportionalYPos,

    /// <summary>`proportional_width`: `ComputeWide`, `o` answering 0.</summary>
    ProportionalWidth,

    /// <summary>`proportional_height`: `ComputeTall`, `o` answering 0.</summary>
    ProportionalHeight,

    /// <summary>`textureid`: a texture name, or null for none.</summary>
    TextureId,
}

/// <summary>`vgui::Panel`, the part a `.res` file and a scheme set — ported from `vgui_controls/Panel.cpp`.</summary>
/// <remarks>
/// **Published source**: `vgui_controls` is statically linked into TF2's `client.dll`, and the SDK's copy is TF2's. What
/// lives behind `ipanel()` and `scheme()` is `vgui2.dll`, closed, and comes in through <see cref="VguiContext"/>.
/// Input, navigation, tooltips, drag and drop and build mode are not modelled: a demo's HUD takes no input.
/// </remarks>
public class VguiPanel
{
    private readonly List<VguiPanel> _children = [];
    private readonly List<AnimationVar> _animationVars = [];
    private readonly Dictionary<string, (byte, byte, byte, byte)> _colourOverrides = new(StringComparer.Ordinal);
    private bool _needsDefaultSettings = true;

    // `Init` sets NEEDS_LAYOUT and NEEDS_SCHEME_UPDATE.
    private bool _needsLayout = true;
    private bool _needsSchemeUpdate = true;
    private short _zPos;
    private VguiBorder? _border;
    private VguiPanel? _pinSibling;
    private bool _proportional;

    /// <summary>`Panel( parent, panelName )`: `Init( 0, 0, 64, 24 )`, then the name, then the parent.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiPanel(VguiPanel? parent, string? name)
    {
        Name = name ?? string.Empty;

        // Panel.h:937 — `Init` sets alpha 255 and the rest zero; the texture ids stay -1 until the defaults pass.
        DeclareAnimationVar("alpha", VguiPanelVarType.Real, "255", 255f);
        DeclareAnimationVar("PaintBackgroundType", VguiPanelVarType.Whole, "0");
        DeclareAnimationVar("Texture1", VguiPanelVarType.TextureId, "vgui/hud/8x800corner1");
        DeclareAnimationVar("Texture2", VguiPanelVarType.TextureId, "vgui/hud/8x800corner2");
        DeclareAnimationVar("Texture3", VguiPanelVarType.TextureId, "vgui/hud/8x800corner3");
        DeclareAnimationVar("Texture4", VguiPanelVarType.TextureId, "vgui/hud/8x800corner4");
        SetParent(parent);
    }

    /// <summary>`GetClassName`: what a `.res` names in `ControlName`.</summary>
    public virtual string ClassName => "Panel";

    /// <summary>`GetName`.</summary>
    public string Name { get; set; }

    /// <summary>`GetParent`.</summary>
    public VguiPanel? Parent { get; private set; }

    /// <summary>The children, in the order they were added.</summary>
    public IReadOnlyList<VguiPanel> Children => _children;

    /// <summary>X relative to the parent.</summary>
    public int X { get; set; }

    /// <summary>Y relative to the parent.</summary>
    public int Y { get; set; }

    /// <summary>Wide; 64 until set.</summary>
    public int Wide { get; set; } = 64;

    /// <summary>Tall; 24 until set.</summary>
    public int Tall { get; set; } = 24;

    /// <summary>`zpos`: paint order among siblings, kept as a `short`.</summary>
    /// <remarks>
    /// `VPanel_SetZPos` (vgui2.dll 0x18001c610) stores it and bubbles the panel through the parent's children, past a
    /// neighbour only while strictly out of order.
    /// </remarks>
    public int ZPos
    {
        get => _zPos;
        set
        {
            _zPos = (short)value;

            if (Parent is null)
            {
                return;
            }

            List<VguiPanel> siblings = Parent._children;
            int index = siblings.IndexOf(this);

            while (index > 0 && siblings[index - 1]._zPos > _zPos)
            {
                (siblings[index - 1], siblings[index]) = (siblings[index], siblings[index - 1]);
                index--;
            }

            while (index < siblings.Count - 1 && siblings[index + 1]._zPos < _zPos)
            {
                (siblings[index + 1], siblings[index]) = (siblings[index], siblings[index + 1]);
                index++;
            }
        }
    }

    /// <summary>`GetInset`: left, top, right, bottom.</summary>
    public (int Left, int Top, int Right, int Bottom) Inset { get; set; }

    /// <summary>`GetAbsPos` X, as the last solve left it.</summary>
    public int AbsX { get; internal set; }

    /// <summary>`GetAbsPos` Y, as the last solve left it.</summary>
    public int AbsY { get; internal set; }

    /// <summary>`GetClipRect`: left, top, right, bottom in screen pixels, as the last solve left it.</summary>
    public (int X0, int Y0, int X1, int Y1) ClipRect { get; internal set; }

    /// <summary>The sibling `pin_to_sibling` resolved to — cached once found, as `m_pinSibling` is.</summary>
    internal VguiPanel? PinSibling
    {
        get
        {
            if (_pinSibling is null && PinToSibling is { } name)
            {
                _pinSibling = FindSiblingByName(name);
            }

            return _pinSibling;
        }
    }

    /// <summary>`PinToSibling`: a new name drops the cached sibling; the same name keeps it.</summary>
    /// <param name="sibling">The sibling's name, or null to unpin.</param>
    /// <param name="ownCorner">`pin_corner_to_sibling`, as `GetPinCornerFromString` reads it.</param>
    /// <param name="siblingCorner">`pin_to_sibling_corner`, likewise.</param>
    public void PinTo(string? sibling, string? ownCorner, string? siblingCorner)
    {
        PinCornerToSibling = PinCornerFromString(ownCorner);
        PinToSiblingCorner = PinCornerFromString(siblingCorner);

        if (_pinSibling is not null && sibling is not null && string.Equals(PinToSibling, sibling, StringComparison.Ordinal))
        {
            return;
        }

        PinToSibling = sibling;
        _pinSibling = null;
    }

    /// <summary>`FindSiblingByName`: the parent's children in paint order, case-insensitive — this panel included.</summary>
    private VguiPanel? FindSiblingByName(string name) =>
        Parent?._children.Find(sibling => string.Equals(sibling.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>`IsVisible`.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>`IsEnabled`.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>`IsProportional`: inherited from the parent when parented, and pushed to the children when set.</summary>
    public bool Proportional
    {
        get => _proportional;
        set
        {
            _proportional = value;

            foreach (VguiPanel child in _children)
            {
                child.Proportional = value;
            }
        }
    }

    /// <summary>`PAINT_BACKGROUND_ENABLED`, on by default.</summary>
    public bool PaintBackgroundEnabled { get; set; } = true;

    /// <summary>`PAINT_BORDER_ENABLED`, on by default.</summary>
    public bool PaintBorderEnabled { get; set; } = true;

    /// <summary>`PAINT_ENABLED`, on by default.</summary>
    public bool PaintEnabled { get; set; } = true;

    /// <summary>`RoundedCorners`: `PANEL_ROUND_CORNER_*` bits, all four by default.</summary>
    public int RoundedCorners { get; set; } = 0xF;

    /// <summary>`SetBorder` / `GetBorder`: setting one takes its inset and its background type, clamped to 0..2.</summary>
    public VguiBorder? Border
    {
        get => _border;
        set
        {
            _border = value;
            Inset = value?.GetInset() ?? default;

            if (value is not null)
            {
                SetAnimationValue("PaintBackgroundType", Math.Clamp(value.BackgroundType, 0, 2));
            }
        }
    }

    /// <summary>`GetFgColor`.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) FgColor { get; set; }

    /// <summary>`GetBgColor`.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) BgColor { get; set; }

    /// <summary>`pin_to_sibling`: the sibling's name, or null.</summary>
    public string? PinToSibling { get; private set; }

    /// <summary>`pin_corner_to_sibling`: this panel's corner.</summary>
    public VguiPinCorner PinCornerToSibling { get; private set; }

    /// <summary>`pin_to_sibling_corner`: the sibling's corner.</summary>
    public VguiPinCorner PinToSiblingCorner { get; private set; }

    /// <summary>`AutoResize`.</summary>
    public int AutoResize { get; private set; }

    /// <summary>`PinCorner`.</summary>
    public VguiPinCorner PinCorner { get; private set; }

    /// <summary>`SetAutoResize`'s offsets: pinned X and Y, unpinned X and Y.</summary>
    public (int PinnedX, int PinnedY, int UnpinnedX, int UnpinnedY) AutoResizeOffsets { get; private set; }

    /// <summary>`Panel::_buildGroup`: the group this panel is registered in, which `EditablePanel::OnChildAdded` sets.</summary>
    internal VguiBuildGroup? BuildGroup { get; set; }

    /// <summary>`SetParent`: moves the panel, inherits the parent's proportionality, and joins its build group.</summary>
    /// <param name="parent">The new parent, or null.</param>
    public void SetParent(VguiPanel? parent)
    {
        Parent?._children.Remove(this);
        Parent = parent;

        if (parent is null)
        {
            return;
        }

        parent._children.Add(this);
        Proportional = parent.Proportional;
        parent.OnChildAdded(this);
    }

    /// <summary>`OnChildAdded`.</summary>
    /// <param name="child">The child just parented here.</param>
    protected virtual void OnChildAdded(VguiPanel child)
    {
    }

    /// <summary>`ApplySettings` (Panel.cpp:4340): the `.res` block for this panel.</summary>
    /// <param name="block">The block.</param>
    /// <param name="context">The scheme and screen.</param>
    public virtual void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(context);

        if (_needsDefaultSettings)
        {
            InitDefaultValues(context);
        }

        // `InternalApplySettings`: each key that names an animation variable sets it.
        foreach (KeyValuesTree key in block.Children)
        {
            if (FindAnimationVar(key.Name) is { } variable)
            {
                variable.Value = Convert(variable, block.Find(variable.Name)?.Value ?? string.Empty, context);
            }
        }

        int parentWide = context.ScreenWide;
        int parentTall = context.ScreenTall;

        if (Int(block, "proportionalToParent", 0) == 1 && Parent is not null)
        {
            (parentWide, parentTall) = (Parent.Wide, Parent.Tall);
        }

        (int wide, int tall) = PanelLayout.Sizes(
            block.Find("wide")?.Value, block.Find("tall")?.Value, Wide, Tall, parentWide, parentTall, Proportional, context.Scale, context.Normalize);

        X = PanelLayout.Position(block.Find("xpos")?.Value, X, wide, parentWide, Proportional, context.Scale);
        Y = PanelLayout.Position(block.Find("ypos")?.Value, Y, tall, parentTall, Proportional, context.Scale);

        if (block.Find("zpos") is not null)
        {
            ZPos = Int(block, "zpos", 0);
        }

        (Wide, Tall) = (wide, tall);
        ApplyAutoResizeSettings(block, context);

        if (Int(block, "IgnoreScheme", 0) != 0)
        {
            ApplySchemeSettings(context);
        }

        int state = Int(block, "visible", 1);

        if (state is 0 or 1)
        {
            Visible = state == 1;
        }

        Enabled = Int(block, "enabled", 1) != 0;

        if (Int(block, "paintbackground", -1) is var background and >= 0)
        {
            PaintBackgroundEnabled = background != 0;
        }

        if (Int(block, "paintborder", -1) is var border and >= 0)
        {
            PaintBorderEnabled = border != 0;
        }

        if (block.Find("border")?.Value is { Length: > 0 } borderName)
        {
            Border = context.Borders.Get(borderName);
        }

        if (Int(block, "RoundedCorners", -1) is var rounded and >= 0)
        {
            RoundedCorners = rounded;
        }

        if (block.Find("fieldName")?.Value is { } fieldName)
        {
            Name = fieldName;
        }

        PinTo(block.Find("pin_to_sibling")?.Value, block.Find("pin_corner_to_sibling")?.Value, block.Find("pin_to_sibling_corner")?.Value);

        ApplyColourOverride(block, "fgcolor_override", context);
        ApplyColourOverride(block, "bgcolor_override", context);
        ApplyOverridableColors();
    }

    /// <summary>`ApplySchemeSettings`: `Panel.FgColor` and `Panel.BgColor`, then any `.res` override put back.</summary>
    /// <param name="context">The scheme.</param>
    public virtual void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        (byte, byte, byte, byte) white = (255, 255, 255, 255);

        FgColor = context.Scheme.GetColor("Panel.FgColor", white);
        BgColor = context.Scheme.GetColor("Panel.BgColor", white);
        _needsSchemeUpdate = false;
        ApplyOverridableColors();
    }

    /// <summary>`PerformApplySchemeSettings` (Panel.cpp): the defaults if still pending, then the scheme once.</summary>
    /// <param name="context">The scheme.</param>
    public void PerformApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_needsDefaultSettings)
        {
            InitDefaultValues(context);
        }

        if (!_needsSchemeUpdate)
        {
            return;
        }

        ApplySchemeSettings(context);
        ApplyOverridableColors();
    }

    /// <summary>`Think`: a visible panel lays out when it needs to, then `OnThink`.</summary>
    public void Think()
    {
        if (Visible && _needsLayout && !_needsSchemeUpdate)
        {
            // `InternalPerformLayout`: never before the scheme is applied.
            _needsLayout = false;
            PerformLayout();
        }

        OnThink();
    }

    /// <summary>`InvalidateLayout`: lay out again at the next think.</summary>
    public void InvalidateLayout() => _needsLayout = true;

    /// <summary>`PerformLayout`: empty — a control places its parts here.</summary>
    protected virtual void PerformLayout()
    {
    }

    /// <summary>`OnThink`: empty — a control updates itself each frame here.</summary>
    protected virtual void OnThink()
    {
    }

    /// <summary>Declares a `CPanelAnimationVar`: a value a `.res` key and the animation controller both set by name.</summary>
    /// <param name="scriptName">The key, matched case-insensitively.</param>
    /// <param name="type">The converter.</param>
    /// <param name="defaultValue">The default, converted on the first `ApplySettings`.</param>
    /// <param name="initial">The value before the defaults pass — what the constructor leaves; zero when not given.</param>
    protected void DeclareAnimationVar(string scriptName, VguiPanelVarType type, string defaultValue, object? initial = null) =>
        _animationVars.Add(new AnimationVar(scriptName, type, defaultValue) { Value = initial ?? Zero(type) });

    /// <summary>Sets an animation variable by name — what a `.res` key, the animation controller or code does.</summary>
    /// <param name="name">The script name.</param>
    /// <param name="value">The value, of the variable's type.</param>
    public void SetAnimationValue(string name, object? value) =>
        (FindAnimationVar(name) ?? throw new ArgumentException($"{ClassName} declares no animation variable {name}.", nameof(name))).Value = value;

    private static object? Zero(VguiPanelVarType type) => type switch
    {
        VguiPanelVarType.Real or VguiPanelVarType.ProportionalFloat => 0f,
        VguiPanelVarType.Bool => false,
        VguiPanelVarType.Color => ((byte)0, (byte)0, (byte)0, (byte)0),
        VguiPanelVarType.Text => string.Empty,
        VguiPanelVarType.Font or VguiPanelVarType.TextureId => null,
        _ => 0,
    };

    /// <summary>`PaintTraverse` (Panel.cpp:1128).</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="context">The scheme manager's scale.</param>
    /// <param name="repaint">Whether this panel draws; its children are traversed either way.</param>
    public void PaintTraverse(IVguiSurface surface, VguiContext context, bool repaint = true)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!Visible)
        {
            return;
        }

        float oldAlpha = surface.AlphaMultiplier;
        float newAlpha = oldAlpha * GetFloat("alpha") * 1.0f / 255.0f;
        (int x0, int y0, int x1, int y1) = ClipRect;

        if (x1 <= x0 || y1 <= y0)
        {
            repaint = false;
        }

        surface.AlphaMultiplier = newAlpha;

        VguiBorder? border = PaintBorderEnabled ? Border : null;
        bool borderFirst = Border?.PaintFirst ?? false;

        if (borderFirst && repaint && border is not null)
        {
            surface.PushMakeCurrent(this, false);
            border.Paint(surface, this, context);
            surface.PopMakeCurrent(this);
        }

        if (repaint)
        {
            if (PaintBackgroundEnabled)
            {
                surface.PushMakeCurrent(this, false);
                PaintBackground(surface, context);
                surface.PopMakeCurrent(this);
            }

            if (PaintEnabled)
            {
                surface.PushMakeCurrent(this, true);
                Paint(surface, context);
                surface.PopMakeCurrent(this);
            }
        }

        foreach (VguiPanel child in _children)
        {
            if (child.Visible)
            {
                child.PaintTraverse(surface, context, repaint);
            }
        }

        if (repaint && !borderFirst && border is not null)
        {
            surface.PushMakeCurrent(this, false);
            border.Paint(surface, this, context);
            surface.PopMakeCurrent(this);
        }

        surface.AlphaMultiplier = oldAlpha;
    }

    /// <summary>`Paint`: empty on purpose — a control draws its content here.</summary>
    /// <param name="surface">The surface, made current with the inset.</param>
    /// <param name="context">The scheme manager's scale.</param>
    public virtual void Paint(IVguiSurface surface, VguiContext context)
    {
    }

    /// <summary>`PaintBackground` (Panel.cpp:1301), by `PaintBackgroundType`.</summary>
    /// <param name="surface">The surface, made current without inset.</param>
    /// <param name="context">The scheme manager's scale.</param>
    public virtual void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        switch (GetInt("PaintBackgroundType"))
        {
            case 1:
                DrawTexturedBox(surface, 0, 0, Wide, Tall, BgColor);
                break;
            case 2:
                DrawBox(surface, context, 0, 0, Wide, Tall, BgColor);
                break;
            case 3:
                DrawBoxFade(surface, context, 0, 0, Wide, Tall, BgColor, 255, 0, horizontal: true);
                break;
            default:
                surface.DrawSetColor(BgColor);
                surface.DrawFilledRect(0, 0, Wide, Tall);
                break;
        }
    }

    /// <summary>`GetCornerTextureSize` (Panel.cpp:6412).</summary>
    private (int Wide, int Tall) CornerTextureSize(VguiContext context)
    {
        if (GetString("Texture1") is null)
        {
            return (0, 0);
        }

        // "Do not use the scheme for this, so it's screen space that affects the rounding always."
        int size = Proportional ? Math.Max(context.Scale(8) / 2, 8) : 8;

        return (size, size);
    }

    /// <summary>`DrawTexturedBox` (Panel.cpp:6674).</summary>
    private void DrawTexturedBox(IVguiSurface surface, int x, int y, int wide, int tall, (byte, byte, byte, byte) color)
    {
        if (GetString("Texture1") is not { } texture)
        {
            return;
        }

        surface.DrawSetColor(color);
        surface.DrawSetTexture(texture);
        surface.DrawTexturedRect(x, y, x + wide, y + tall);
    }

    /// <summary>`DrawBox` (Panel.cpp:6438): three strips, then each corner rounded by its texture or filled square.</summary>
    private void DrawBox(IVguiSurface surface, VguiContext context, int x, int y, int wide, int tall, (byte, byte, byte, byte) color)
    {
        if (CornerTextures() is not { } textures)
        {
            return;
        }

        (int cornerWide, int cornerTall) = CornerTextureSize(context);

        surface.DrawSetColor(color);
        surface.DrawFilledRect(x + cornerWide, y, x + wide - cornerWide, y + cornerTall);
        surface.DrawFilledRect(x, y + cornerTall, x + wide, y + tall - cornerTall);
        surface.DrawFilledRect(x + cornerWide, y + tall - cornerTall, x + wide - cornerWide, y + tall);

        Corner(surface, 0x1, textures[0], x, y, x + cornerWide, y + cornerTall);
        Corner(surface, 0x2, textures[1], x + wide - cornerWide, y, x + wide, y + cornerTall);
        Corner(surface, 0x4, textures[3], x, y + tall - cornerTall, x + cornerWide, y + tall);
        Corner(surface, 0x8, textures[2], x + wide - cornerWide, y + tall - cornerTall, x + wide, y + tall);
    }

    private void Corner(IVguiSurface surface, int bit, string texture, int x0, int y0, int x1, int y1)
    {
        if ((RoundedCorners & bit) != 0)
        {
            surface.DrawSetTexture(texture);
            surface.DrawTexturedRect(x0, y0, x1, y1);
        }
        else
        {
            surface.DrawFilledRect(x0, y0, x1, y1);
        }
    }

    /// <summary>`DrawBoxFade` (Panel.cpp:6525), not hollow.</summary>
    private void DrawBoxFade(
        IVguiSurface surface, VguiContext context, int x, int y, int wide, int tall, (byte, byte, byte, byte Alpha) color, int alpha0, int alpha1, bool horizontal)
    {
        if (CornerTextures() is not { } textures || surface.AlphaMultiplier == 0f)
        {
            return;
        }

        (int cornerWide, int cornerTall) = CornerTextureSize(context);

        surface.DrawSetColor(color);

        if (!horizontal)
        {
            surface.DrawFilledRectFade(x + cornerWide, y, x + wide - cornerWide, y + cornerTall, alpha0, alpha0, horizontal);
            surface.DrawFilledRectFade(x, y + cornerTall, x + wide, y + tall - cornerTall, alpha0, alpha1, horizontal);
            surface.DrawFilledRectFade(x + cornerWide, y + tall - cornerTall, x + wide - cornerWide, y + tall, alpha1, alpha1, horizontal);
        }
        else
        {
            surface.DrawFilledRectFade(x, y + cornerTall, x + cornerWide, y + tall - cornerTall, alpha0, alpha0, horizontal);
            surface.DrawFilledRectFade(x + cornerWide, y, x + wide - cornerWide, y + tall, alpha0, alpha1, horizontal);
            surface.DrawFilledRectFade(x + wide - cornerWide, y + cornerTall, x + wide, y + tall - cornerTall, alpha1, alpha1, horizontal);
        }

        float oldAlpha = color.Alpha;
        (byte, byte, byte, byte) first = color with { Alpha = (byte)(int)(oldAlpha * (alpha0 / 255.0f)) };
        (byte, byte, byte, byte) second = color with { Alpha = (byte)(int)(oldAlpha * (alpha1 / 255.0f)) };

        surface.DrawSetColor(first);
        surface.DrawSetTexture(textures[0]);
        surface.DrawTexturedRect(x, y, x + cornerWide, y + cornerTall);

        if (!horizontal)
        {
            surface.DrawSetTexture(textures[1]);
            surface.DrawTexturedRect(x + wide - cornerWide, y, x + wide, y + cornerTall);
            surface.DrawSetColor(second);
            surface.DrawSetTexture(textures[2]);
            surface.DrawTexturedRect(x + wide - cornerWide, y + tall - cornerTall, x + wide, y + tall);
            surface.DrawSetTexture(textures[3]);
            surface.DrawTexturedRect(x, y + tall - cornerTall, x + cornerWide, y + tall);
        }
        else
        {
            surface.DrawSetTexture(textures[3]);
            surface.DrawTexturedRect(x, y + tall - cornerTall, x + cornerWide, y + tall);
            surface.DrawSetColor(second);
            surface.DrawSetTexture(textures[1]);
            surface.DrawTexturedRect(x + wide - cornerWide, y, x + wide, y + cornerTall);
            surface.DrawSetTexture(textures[2]);
            surface.DrawTexturedRect(x + wide - cornerWide, y + tall - cornerTall, x + wide, y + tall);
        }
    }

    /// <summary>`Texture1`..`Texture4`, or null when any is unset — every box draw checks all four.</summary>
    private string[]? CornerTextures() =>
        GetString("Texture1") is { } one && GetString("Texture2") is { } two && GetString("Texture3") is { } three && GetString("Texture4") is { } four
            ? [one, two, three, four]
            : null;

    /// <summary>An animation variable's value as a float.</summary>
    /// <param name="name">The script name.</param>
    /// <returns>The value.</returns>
    public float GetFloat(string name) => (float)Var(name)!;

    /// <summary>An animation variable's value as an int.</summary>
    /// <param name="name">The script name.</param>
    /// <returns>The value.</returns>
    public int GetInt(string name) => (int)Var(name)!;

    /// <summary>An animation variable's value as a bool.</summary>
    /// <param name="name">The script name.</param>
    /// <returns>The value.</returns>
    public bool GetBool(string name) => (bool)Var(name)!;

    /// <summary>An animation variable's value as a colour.</summary>
    /// <param name="name">The script name.</param>
    /// <returns>The value.</returns>
    public (byte Red, byte Green, byte Blue, byte Alpha) GetColor(string name) => ((byte, byte, byte, byte))Var(name)!;

    /// <summary>An animation variable's value as a string, texture name or null.</summary>
    /// <param name="name">The script name.</param>
    /// <returns>The value.</returns>
    public string? GetString(string name) => (string?)Var(name);

    /// <summary>An animation variable's value as a font handle.</summary>
    /// <param name="name">The script name.</param>
    /// <returns>The handle, or null for handle 0.</returns>
    public VguiFontAmalgam? GetFont(string name) => (VguiFontAmalgam?)Var(name);

    private object? Var(string name) =>
        (FindAnimationVar(name) ?? throw new ArgumentException($"{ClassName} declares no animation variable {name}.", nameof(name))).Value;

    /// <summary>`FindPanelAnimationEntry`: the most derived declaration first.</summary>
    private AnimationVar? FindAnimationVar(string name)
    {
        for (int index = _animationVars.Count - 1; index >= 0; index--)
        {
            if (string.Equals(_animationVars[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return _animationVars[index];
            }
        }

        return null;
    }

    /// <summary>`InternalInitDefaultValues`: every variable from its default, once.</summary>
    private void InitDefaultValues(VguiContext context)
    {
        _needsDefaultSettings = false;

        for (int index = _animationVars.Count - 1; index >= 0; index--)
        {
            AnimationVar variable = _animationVars[index];

            variable.Value = variable.Type switch
            {
                VguiPanelVarType.Bool => string.Equals(variable.Default, "true", StringComparison.OrdinalIgnoreCase)
                    || PanelLayout.Atoi(variable.Default) != 0,

                // `InitFromDefault` resolves the default as a colour name, never through the `kv->GetColor` branch.
                VguiPanelVarType.Color => context.Scheme.GetColor(variable.Default, default),
                _ => Convert(variable, variable.Default, context),
            };
        }
    }

    /// <summary>A converter's `SetData`, from the key's string.</summary>
    private object? Convert(AnimationVar variable, string text, VguiContext context)
    {
        (int parentWide, int parentTall) = Proportional && Parent is not null
            ? (Parent.Wide, Parent.Tall)
            : (context.ScreenWide, context.ScreenTall);

        return variable.Type switch
        {
            VguiPanelVarType.Real => PanelLayout.Atof(text),
            VguiPanelVarType.Whole => PanelLayout.Atoi(text),
            VguiPanelVarType.Bool => PanelLayout.Atoi(text) != 0,
            VguiPanelVarType.Text => text,
            VguiPanelVarType.Font => context.GetFont(text, Proportional),
            VguiPanelVarType.Color => context.Scheme.GetColor(text, default),
            VguiPanelVarType.ProportionalFloat => (float)context.Scale((int)PanelLayout.Atof(text)),
            VguiPanelVarType.ProportionalInt => context.Scale(PanelLayout.Atoi(text)),
            VguiPanelVarType.ProportionalXPos => PanelLayout.Position(text, 0, Wide, parentWide, Proportional, context.Scale),
            VguiPanelVarType.ProportionalYPos => PanelLayout.Position(text, 0, Tall, parentTall, Proportional, context.Scale),
            VguiPanelVarType.ProportionalWidth => IsOtherAxis(text)
                ? 0
                : PanelLayout.Sizes(text, null, Wide, Tall, parentWide, parentTall, Proportional, context.Scale, context.Normalize).Wide,
            VguiPanelVarType.ProportionalHeight => IsOtherAxis(text)
                ? 0
                : PanelLayout.Sizes(null, text, Wide, Tall, parentWide, parentTall, Proportional, context.Scale, context.Normalize).Tall,
            VguiPanelVarType.TextureId => text.Length > 0 ? text : null,
            _ => throw new ArgumentOutOfRangeException(nameof(variable), variable.Type, null),
        };
    }

    /// <summary>`CProportionalWidthProperty` asserts on `o` and answers 0.</summary>
    private static bool IsOtherAxis(string text) => text.Length > 0 && text[0] is 'o' or 'O';

    /// <summary>`ApplyAutoResizeSettings` (Panel.cpp): the pinned and unpinned corner offsets against the parent.</summary>
    private void ApplyAutoResizeSettings(KeyValuesTree block, VguiContext context)
    {
        int autoResize = Int(block, "AutoResize", 0);
        VguiPinCorner corner = (VguiPinCorner)Int(block, "PinCorner", 0);
        (int parentWide, int parentTall) = Parent is null ? (Wide, Tall) : (Parent.Wide, Parent.Tall);
        int right = X + Wide - parentWide;
        int bottom = Y + Tall - parentTall;

        (int pinnedX, int pinnedY, int unpinnedX, int unpinnedY) = corner switch
        {
            VguiPinCorner.TopLeft => (X, Y, right, bottom),
            VguiPinCorner.TopRight => (right, Y, X, bottom),
            VguiPinCorner.BottomLeft => (X, bottom, right, Y),
            VguiPinCorner.BottomRight => (right, bottom, X, Y),
            _ => (0, 0, 0, 0),
        };

        pinnedX = Offset(block, "PinnedCornerOffsetX", pinnedX, context);
        pinnedY = Offset(block, "PinnedCornerOffsetY", pinnedY, context);
        unpinnedX = Offset(block, "UnpinnedCornerOffsetX", unpinnedX, context);
        unpinnedY = Offset(block, "UnpinnedCornerOffsetY", unpinnedY, context);

        if (autoResize == 0)
        {
            (unpinnedX, unpinnedY) = (0, 0);
        }

        AutoResize = autoResize;
        PinCorner = corner;
        AutoResizeOffsets = (pinnedX, pinnedY, unpinnedX, unpinnedY);
    }

    /// <summary>An offset override: scaled on a proportional panel, as written otherwise.</summary>
    private int Offset(KeyValuesTree block, string key, int fallback, VguiContext context)
    {
        if (block.Find(key) is null)
        {
            return fallback;
        }

        int value = Int(block, key, 0);

        return Proportional ? context.Scale(value) : value;
    }

    /// <summary>The overridable-colour loop at the end of `ApplySettings`.</summary>
    private void ApplyColourOverride(KeyValuesTree block, string key, VguiContext context)
    {
        if (block.Find(key)?.Value is not { } text)
        {
            return;
        }

        if (text.Length > 0 && (text[0] == '.' || char.IsAsciiDigit(text[0])))
        {
            // `sscanf( "%f %f %f %f" )` into zeroed floats, each cast to `unsigned char`.
            float[] channels = new float[4];
            string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            for (int index = 0; index < Math.Min(parts.Length, 4); index++)
            {
                // `%f` fails on a token with no number at its start, and the scan stops there.
                if (parts[index][0] is not ('.' or '-' or '+') && !char.IsAsciiDigit(parts[index][0]))
                {
                    break;
                }

                channels[index] = PanelLayout.Atof(parts[index]);
            }

            _colourOverrides[key] = ((byte)channels[0], (byte)channels[1], (byte)channels[2], (byte)channels[3]);
        }
        else
        {
            _colourOverrides[key] = context.Scheme.GetColor(text, (255, 255, 255, 255));
        }
    }

    /// <summary>`ApplyOverridableColors`.</summary>
    private void ApplyOverridableColors()
    {
        if (_colourOverrides.TryGetValue("fgcolor_override", out (byte, byte, byte, byte) fg))
        {
            FgColor = fg;
        }

        if (_colourOverrides.TryGetValue("bgcolor_override", out (byte, byte, byte, byte) bg))
        {
            BgColor = bg;
        }
    }

    /// <summary>`GetPinCornerFromString`: one character is `atoi`, else one of the eight names, else top left.</summary>
    private static VguiPinCorner PinCornerFromString(string? text)
    {
        if (text is null)
        {
            return VguiPinCorner.TopLeft;
        }

        if (text.Length == 1)
        {
            return (VguiPinCorner)PanelLayout.Atoi(text);
        }

        int index = Array.FindIndex(PinCornerStrings, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? VguiPinCorner.TopLeft : (VguiPinCorner)index;
    }

    /// <summary>`g_PinCornerStrings` (Panel.cpp:54).</summary>
    private static readonly string[] PinCornerStrings =
    [
        "PIN_TOPLEFT", "PIN_TOPRIGHT", "PIN_BOTTOMLEFT", "PIN_BOTTOMRIGHT",
        "PIN_CENTER_TOP", "PIN_CENTER_RIGHT", "PIN_CENTER_BOTTOM", "PIN_CENTER_LEFT",
    ];

    /// <summary>`KeyValues::GetInt` on a string value: `atoi`.</summary>
    private protected static int Int(KeyValuesTree block, string key, int fallback) =>
        block.Find(key)?.Value is { } value ? PanelLayout.Atoi(value) : fallback;

    private sealed class AnimationVar(string name, VguiPanelVarType type, string defaultValue)
    {
        public string Name { get; } = name;

        public VguiPanelVarType Type { get; } = type;

        public string Default { get; } = defaultValue;

        public object? Value { get; set; }

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} = {Value}");
    }
}

/// <summary>`Panel::PinCorner_e`, in `g_PinCornerStrings` order.</summary>
public enum VguiPinCorner
{
    /// <summary>`PIN_TOPLEFT`.</summary>
    TopLeft,

    /// <summary>`PIN_TOPRIGHT`.</summary>
    TopRight,

    /// <summary>`PIN_BOTTOMLEFT`.</summary>
    BottomLeft,

    /// <summary>`PIN_BOTTOMRIGHT`.</summary>
    BottomRight,

    /// <summary>`PIN_CENTER_TOP`.</summary>
    CenterTop,

    /// <summary>`PIN_CENTER_RIGHT`.</summary>
    CenterRight,

    /// <summary>`PIN_CENTER_BOTTOM`.</summary>
    CenterBottom,

    /// <summary>`PIN_CENTER_LEFT`.</summary>
    CenterLeft,
}
