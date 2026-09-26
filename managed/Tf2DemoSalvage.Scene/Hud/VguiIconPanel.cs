using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CIconPanel` (game/client/game_controls/IconPanel.cpp): one of `gHUD`'s icons, at its own size or scaled to the panel.</summary>
/// <remarks>`icon` names it and `scaleImage` stretches it; it draws in `iconColor` (IconPanel.h:39, white by default).</remarks>
public sealed class VguiIconPanel : VguiPanel
{
    private string _iconName = string.Empty;
    private bool _scaleImage;

    /// <summary>`CIconPanel( parent, name )`.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The name, or null.</param>
    public VguiIconPanel(VguiPanel? parent, string? name)
        : base(parent, name) =>
        DeclareAnimationVar("iconColor", VguiPanelVarType.Color, "255 255 255 255");

    /// <inheritdoc/>
    public override string ClassName => "CIconPanel";

    /// <summary>`m_icon`.</summary>
    public HudTexture? Icon { get; private set; }

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        _iconName = block.Find("icon")?.Value ?? string.Empty;
        Icon = HudViewport.Of(this)?.Icons?.GetIcon(_iconName);
        _scaleImage = PanelLayout.Atoi(block.Find("scaleImage")?.Value ?? string.Empty) != 0;
        base.ApplySettings(block, context);
    }

    /// <summary>`SetIcon`.</summary>
    /// <param name="name">The icon's name.</param>
    public void SetIcon(string name)
    {
        _iconName = name ?? throw new ArgumentNullException(nameof(name));
        Icon = HudViewport.Of(this)?.Icons?.GetIcon(_iconName);
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);

        if (_iconName.Length > 0)
        {
            Icon = HudViewport.Of(this)?.Icons?.GetIcon(_iconName);
        }

        FgColor = context.Scheme.GetColor("FgColor", (255, 255, 255, 255));
    }

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        base.Paint(surface, context);

        if (Icon is not { } icon)
        {
            return;
        }

        (byte, byte, byte, byte) color = GetColor("iconColor");

        if (_scaleImage)
        {
            icon.DrawSelf(surface, 0, 0, Wide, Tall, color);
        }
        else
        {
            icon.DrawSelf(surface, 0, 0, color);
        }
    }
}

/// <summary>`CAvatarImagePanel`: a Steam avatar, which a demo cannot fetch — laid out like the game's, and never drawn.</summary>
/// <remarks>
/// The target ID shows one only for a Steam friend (`tf_hud_target_id_show_avatars` 2) or with that cvar at 1, and hides it
/// for everyone else; it keeps the panel for its layout, which reads whether the avatar is visible.
/// **Not modelled:** the avatar itself — fetching one needs Steam, and the default shows only friends of the viewer.
/// </remarks>
/// <param name="parent">The parent, or null.</param>
/// <param name="name">The name, or null.</param>
public sealed class VguiAvatarImagePanel(VguiPanel? parent, string? name) : VguiPanel(parent, name)
{
    /// <inheritdoc/>
    public override string ClassName => "CAvatarImagePanel";
}
