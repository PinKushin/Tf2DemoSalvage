using System;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CExLabel` (game/client/econ/econ_controls.cpp:531): a label whose `fgcolor` outlasts the scheme.</summary>
/// <param name="parent">The parent, or null.</param>
/// <param name="name">The panel's name, or null.</param>
public sealed class TfExLabel(VguiPanel? parent, string? name) : VguiLabel(parent, name)
{
    private string _color = string.Empty;

    /// <inheritdoc/>
    public override string ClassName => "CExLabel";

    /// <inheritdoc/>
    /// <remarks>`fgcolor`, else `Label.TextColor` — green where the scheme has neither.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySettings(block, context);
        SetColorStr(block.Find("fgcolor")?.Value ?? "Label.TextColor", context);
    }

    /// <inheritdoc/>
    /// <remarks>"Reapply our custom color, so we stomp the base scheme's" — kept as `"%d %d %d %d"` and looked up again.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        SetColorStr(_color, context);
    }

    private void SetColorStr(string color, VguiContext context)
    {
        (byte red, byte green, byte blue, byte alpha) = context.Scheme.GetColor(color, (0, 255, 0, 255));

        _color = string.Create(CultureInfo.InvariantCulture, $"{red} {green} {blue} {alpha}");
        FgColor = (red, green, blue, alpha);
    }
}

/// <summary>`CTFImagePanel` (game/client/tf/vgui/tf_imagepanel.cpp): a scalable image whose image follows the local team.</summary>
/// <remarks>
/// `teambg_0` to `teambg_3` (`TF_TEAM_COUNT`) name an image per team; `UpdateBGImage` sets the local player's when it has
/// one and leaves `image` otherwise. `localplayer_changeteam` runs it again — here, setting <see cref="LocalTeam"/>.
/// </remarks>
/// <param name="parent">The parent, or null.</param>
/// <param name="name">The panel's name, or null.</param>
public sealed class TfImagePanel(VguiPanel? parent, string? name) : VguiScalableImagePanel(parent, name)
{
    private readonly string[] _teamBg = ["", "", "", ""];
    private int _localTeam;

    /// <inheritdoc/>
    public override string ClassName => "CTFImagePanel";

    /// <summary>`m_iBGTeam`: the local player's team, `TEAM_UNASSIGNED` without one.</summary>
    public int LocalTeam
    {
        get => _localTeam;
        set
        {
            _localTeam = value;
            UpdateBGImage();
        }
    }

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        for (int team = 0; team < _teamBg.Length; team++)
        {
            _teamBg[team] = block.Find(string.Create(CultureInfo.InvariantCulture, $"teambg_{team}"))?.Value ?? string.Empty;
        }

        base.ApplySettings(block, context);
        UpdateBGImage();
    }

    private void UpdateBGImage()
    {
        if (_localTeam >= 0 && _localTeam < _teamBg.Length && _teamBg[_localTeam].Length > 0)
        {
            SetImage(_teamBg[_localTeam]);
        }
    }
}
