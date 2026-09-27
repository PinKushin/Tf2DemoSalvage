using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`ProgressBar::ProgressDir_e` (ProgressBar.h).</summary>
public enum VguiProgressDirection
{
    /// <summary>`PROGRESS_EAST`: fills left to right, the default.</summary>
    East,

    /// <summary>`PROGRESS_WEST`.</summary>
    West,

    /// <summary>`PROGRESS_NORTH`.</summary>
    North,

    /// <summary>`PROGRESS_SOUTH`.</summary>
    South,
}

/// <summary>`vgui::ProgressBar` (vgui2/vgui_controls/ProgressBar.cpp): a bar of segments filled to a fraction.</summary>
/// <remarks>
/// The constructor's `SetSegmentInfo( QuickPropScale( 4 ), QuickPropScale( 8 ) )` and `SetBarInset( QuickPropScale( 4 ) )`
/// scale only a panel already proportional when made; a control a `.res` makes has no parent yet, so it keeps 4 and 8.
/// </remarks>
public class VguiProgressBar : VguiPanel
{
    private string? _dialogVariable;

    /// <summary>`ProgressBar( parent, panelName )`.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiProgressBar(VguiPanel? parent, string? name)
        : base(parent, name)
    {
    }

    /// <inheritdoc/>
    public override string ClassName => "ProgressBar";

    /// <summary>`_progress`, 0 to 1.</summary>
    public float Progress { get; private set; }

    /// <summary>`m_iProgressDirection`.</summary>
    public VguiProgressDirection Direction { get; set; }

    /// <summary>`_segmentGap`.</summary>
    public int SegmentGap { get; set; } = 4;

    /// <summary>`_segmentWide`.</summary>
    public int SegmentWide { get; set; } = 8;

    /// <summary>`m_iBarInset`.</summary>
    public int BarInset { get; set; } = 4;

    /// <summary>`m_iBarMargin`.</summary>
    public int BarMargin { get; set; }

    /// <summary>`SetProgress` (:153): clamped to 0..1.</summary>
    /// <param name="progress">The fraction.</param>
    public void SetProgress(float progress)
    {
        // `if (progress != _progress)` guards only a repaint; the value set is the same either way.
        Progress = Math.Clamp(progress, 0f, 1f);
    }

    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:340): `progress` and `variable`, then the panel's own.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        // `GetFloat( "progress", 0.0f )` assigns `_progress` directly, unclamped.
        Progress = float.TryParse(block.Find("progress")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float progress)
            ? progress
            : 0f;

        if (block.Find("variable")?.Value is { Length: > 0 } variable)
        {
            _dialogVariable = variable;
        }

        base.ApplySettings(block, context);
    }

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:179): `ProgressBar.FgColor`, `ProgressBar.BgColor`, the `ButtonDepressedBorder`.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        FgColor = context.Scheme.GetColor("ProgressBar.FgColor", default);
        BgColor = context.Scheme.GetColor("ProgressBar.BgColor", default);
        Border = context.Borders.Get("ButtonDepressedBorder");
    }

    /// <inheritdoc/>
    /// <remarks>`OnDialogVariablesChanged` (:381): the variable as an int percent, when it is not negative.</remarks>
    public override void OnDialogVariablesChanged(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        if (_dialogVariable is null)
        {
            return;
        }

        // `KeyValues::GetInt( name, -1 )` of a string: `atoi`, 0 for text that is no number.
        int value = variables.TryGetValue(_dialogVariable, out string? text) ? LeadingInt(text) : -1;

        if (value >= 0)
        {
            SetProgress(value / 100.0f);
        }
    }

    /// <inheritdoc/>
    /// <remarks>`PaintBackground` (:81): a flat fill in the background colour.</remarks>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        surface.DrawSetColor(BgColor);
        surface.DrawFilledRect(0, 0, Wide, Tall);
    }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:117): as many whole segments as the fraction of those that fit.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        int wide = Wide;
        int tall = Tall;
        int x = 0;
        int y = 0;
        int segmentTotal;

        switch (Direction)
        {
            case VguiProgressDirection.West:
                wide -= 2 * BarMargin;
                x = wide - BarMargin;
                y = BarInset;
                segmentTotal = wide / (SegmentGap + SegmentWide);
                break;
            case VguiProgressDirection.North:
                tall -= 2 * BarMargin;
                x = BarInset;
                y = tall - BarMargin;
                segmentTotal = tall / (SegmentGap + SegmentWide);
                break;
            case VguiProgressDirection.South:
                tall -= 2 * BarMargin;
                x = BarInset;
                y = BarMargin;
                segmentTotal = tall / (SegmentGap + SegmentWide);
                break;
            default:
                wide -= 2 * BarMargin;
                x = BarMargin;
                y = BarInset;
                segmentTotal = wide / (SegmentGap + SegmentWide);
                break;
        }

        int segmentsDrawn = (int)(segmentTotal * Progress);

        surface.DrawSetColor(FgColor);

        for (int segment = 0; segment < segmentsDrawn; segment++)
        {
            PaintSegment(surface, ref x, ref y, tall, wide);
        }
    }

    /// <summary>`PaintSegment` (:91).</summary>
    private void PaintSegment(IVguiSurface surface, ref int x, ref int y, int tall, int wide)
    {
        switch (Direction)
        {
            case VguiProgressDirection.West:
                x -= SegmentGap + SegmentWide;
                surface.DrawFilledRect(x, y, x + SegmentWide, y + tall - (y * 2));
                break;
            case VguiProgressDirection.North:
                y -= SegmentGap + SegmentWide;
                surface.DrawFilledRect(x, y, x + wide - (x * 2), y + SegmentWide);
                break;
            case VguiProgressDirection.South:
                y += SegmentGap;
                surface.DrawFilledRect(x, y, x + wide - (x * 2), y + SegmentWide);
                y += SegmentWide;
                break;
            default:
                x += SegmentGap;
                surface.DrawFilledRect(x, y, x + SegmentWide, y + tall - (y * 2));
                x += SegmentWide;
                break;
        }
    }

    /// <summary>`atoi`: the leading optionally-signed digits, 0 when there are none.</summary>
    private static int LeadingInt(string text)
    {
        int end = 0;

        if (end < text.Length && text[end] is '-' or '+')
        {
            end++;
        }

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return int.TryParse(text.AsSpan(0, end), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
}

/// <summary>`vgui::ContinuousProgressBar` (ProgressBar.cpp:394): one solid bar, with the change since a previous value marked.</summary>
public sealed class VguiContinuousProgressBar : VguiProgressBar
{
    // `m_colorGain` and `m_colorLoss` (:397-398).
    private static readonly (byte, byte, byte, byte) ColorGain = (100, 255, 100, 255);
    private static readonly (byte, byte, byte, byte) ColorLoss = (200, 45, 45, 255);

    /// <summary>`ContinuousProgressBar( parent, panelName )`.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiContinuousProgressBar(VguiPanel? parent, string? name)
        : base(parent, name)
    {
    }

    /// <inheritdoc/>
    public override string ClassName => "ContinuousProgressBar";

    /// <summary>`_prevProgress`: -1 for none.</summary>
    public float PrevProgress { get; private set; } = -1f;

    /// <summary>`SetPrevProgress` (:404): -1 kept, anything else clamped to 0..1.</summary>
    /// <param name="progress">The previous fraction, or -1.</param>
    public void SetPrevProgress(float progress) =>
        // `progress == -1.f`, by bits: -1 has one representation.
        PrevProgress = BitConverter.SingleToInt32Bits(progress) == BitConverter.SingleToInt32Bits(-1f) ? progress : Math.Clamp(progress, 0f, 1f);

    /// <inheritdoc/>
    /// <remarks>`Paint` (:416): the bar in the foreground colour, the gain after it or the loss over it.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        int wide = Wide;
        int tall = Tall;
        float progress = Progress;
        float previous = PrevProgress;
        bool usePrevious = previous >= 0f;
        bool gain = progress > previous;

        surface.DrawSetColor(FgColor);

        switch (Direction)
        {
            case VguiProgressDirection.West:
                if (usePrevious)
                {
                    if (gain)
                    {
                        surface.DrawFilledRect((int)(wide * (1.0f - previous)), 0, wide, tall);
                        surface.DrawSetColor(ColorGain);
                        surface.DrawFilledRect((int)(wide * (1.0f - progress)), 0, (int)(wide * (1.0f - previous)), tall);
                        return;
                    }

                    surface.DrawSetColor(ColorLoss);
                    surface.DrawFilledRect((int)(wide * (1.0f - previous)), 0, (int)(wide * (1.0f - progress)), tall);
                }

                surface.DrawSetColor(FgColor);
                surface.DrawFilledRect((int)(wide * (1.0f - progress)), 0, wide, tall);
                return;

            case VguiProgressDirection.North:
                if (usePrevious)
                {
                    if (gain)
                    {
                        surface.DrawFilledRect(0, (int)(tall * (1.0f - previous)), wide, tall);
                        surface.DrawSetColor(ColorGain);
                        surface.DrawFilledRect(0, (int)(tall * (1.0f - progress)), wide, (int)(tall * (1.0f - previous)));
                        return;
                    }

                    surface.DrawSetColor(ColorLoss);
                    surface.DrawFilledRect(0, (int)(tall * (1.0f - previous)), wide, (int)(tall * (1.0f - progress)));
                }

                surface.DrawSetColor(FgColor);
                surface.DrawFilledRect(0, (int)(tall * (1.0f - progress)), wide, tall);
                return;

            case VguiProgressDirection.South:
                PaintSouth(surface, wide, tall, progress, previous, usePrevious, gain);
                return;

            default:
                if (usePrevious)
                {
                    if (gain)
                    {
                        surface.DrawFilledRect(0, 0, (int)(wide * previous), tall);
                        surface.DrawSetColor(ColorGain);
                        surface.DrawFilledRect((int)(wide * previous), 0, (int)(wide * progress), tall);
                        return;
                    }

                    surface.DrawSetColor(ColorLoss);
                    surface.DrawFilledRect((int)(wide * progress), 0, (int)(wide * previous), tall);
                }

                surface.DrawSetColor(FgColor);
                surface.DrawFilledRect(0, 0, (int)(wide * progress), tall);
                return;
        }
    }

    /// <summary>`PROGRESS_SOUTH` (:498).</summary>
    private void PaintSouth(IVguiSurface surface, int wide, int tall, float progress, float previous, bool usePrevious, bool gain)
    {
        if (usePrevious)
        {
            if (gain)
            {
                surface.DrawFilledRect(0, 0, wide, (int)(tall * (1.0f - progress)));
                surface.DrawSetColor(ColorGain);
                surface.DrawFilledRect(0, (int)(tall * (1.0f - progress)), wide, (int)(tall * (1.0f - previous)));
                return;
            }

            surface.DrawSetColor(ColorLoss);
            surface.DrawFilledRect(0, (int)(tall * (1.0f - previous)), wide, (int)(tall * (1.0f - progress)));
        }

        surface.DrawSetColor(FgColor);
        surface.DrawFilledRect(0, 0, wide, (int)(tall * progress));
    }
}
