using System;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>`CFPSPanel` (`game/client/vgui_fpspanel.cpp`): the frame-rate and position readout, a VGUI panel on the tools root.</summary>
/// <remarks>
/// <list type="bullet">
/// <item>Made hidden, with no background (:69); its font is the scheme's `DefaultFixedOutline` (:137).</item>
/// <item>`ComputeSize` (:110): at <c>parent wide − FPS_PANEL_WIDTH</c>, 0, and 300 by <c>4 × tall + 8</c> — on a screen
/// narrower than 300 that is off the left edge, as it is in TF2.</item>
/// <item>`OnTick`, every 250 ms (:81): shown while either readout is on (`ShouldDraw`, :157).</item>
/// <item>`Paint` (:230): the frame-rate line at (2, 2) in its colour, then the position lines, white, each
/// <c>2 + i × ( tall + 2 )</c> down, one counter across both. Drawn with `DrawColoredText`.</item>
/// </list>
/// The meter's own rules — averaging, watermarks, colour, the first frame after showing drawing nothing — are
/// <see cref="FpsMeter"/>; the CPU-frequency line is not drawn, as it is not while TF2 is not monitoring.
/// </remarks>
public sealed class FpsPanel : VguiPanel
{
    /// <summary>`FPS_PANEL_WIDTH`.</summary>
    public const int PanelWidth = 300;

    private VguiFontAmalgam? _font;

    /// <summary>`CFPSPanel( parent )`.</summary>
    /// <param name="parent">The tools root.</param>
    public FpsPanel(VguiPanel parent)
        : base(parent, "CFPSPanel")
    {
        Visible = false;
        FgColor = (0, 0, 0, 255);
        PaintBackgroundEnabled = false;
        ComputeSize();
    }

    /// <summary>`cl_showfps`.</summary>
    public int Mode { get; set; }

    /// <summary>This frame's meter reading, or null when the meter draws nothing this frame.</summary>
    public FpsReading? Reading { get; set; }

    /// <summary>`V_GetFileName( engine->GetLevelName() )`.</summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>`cl_showpos` and what it reads.</summary>
    public PositionReadout Position { get; set; }

    /// <summary>`gpGlobals->absoluteframetime`, which `ShouldDraw` requires positive for the meter.</summary>
    public double FrameSeconds { get; set; }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        _font = context.GetFont("DefaultFixedOutline", proportional: false);
        ComputeSize();
    }

    /// <summary>`ComputeSize`.</summary>
    public void ComputeSize()
    {
        X = (Parent?.Wide ?? 0) - PanelWidth;
        Y = 0;
        Wide = PanelWidth;
        Tall = (4 * FontTall) + 8;
    }

    /// <summary>`OnTick`: shown while `ShouldDraw`.</summary>
    public void OnTick() => Visible = (Mode != 0 && FrameSeconds > 0) || Position.Visible;

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (_font is not { } font)
        {
            return;
        }

        const int x = 2;
        int line = 0;

        if (Reading is { } reading)
        {
            line++;
            (byte red, byte green, byte blue) = reading.Colour;
            surface.DrawColoredText(font, x, 2, (red, green, blue, 255), reading.Text(MapName));
        }

        foreach (string text in Position.Lines)
        {
            surface.DrawColoredText(font, x, 2 + (line * (FontTall + 2)), (255, 255, 255, 255), text);
            line++;
        }
    }

    /// <summary>`surface()->GetFontTall( m_hFont )`: 0 for handle 0.</summary>
    private int FontTall => _font?.Height ?? 0;
}
