using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// A borderless window that floats playback controls over the viewport.
/// </summary>
/// <remarks>
/// **A separate window rather than a child control on the viewport panel, and that is forced.**
/// The swap chain presents with <c>FLIP_DISCARD</c>, and a child HWND overlapping a flip-model
/// swap chain does not composite reliably — it flickers, or disappears behind the presented frame.
/// The alternatives were drawing the controls inside Direct3D, which puts them beyond the reach of
/// UIA and therefore beyond the reach of any UI test, or dropping back to the older BitBlt swap
/// effect and paying for it on every frame.
///
/// An OWNED window, so it follows its owner: it stays above the main form without being
/// <c>TopMost</c> — which would leave it hovering over other applications when the viewer loses
/// focus. It also never takes activation, so clicking Play does not steal focus from the form
/// underneath and interrupt keyboard shortcuts.
/// </remarks>
internal sealed class OverlayWindow : Form
{
    /// <summary>Do not activate this window when it is shown or clicked.</summary>
    private const int WsExNoActivate = 0x08000000;

    /// <summary>A tool window: no taskbar entry, no alt-tab entry.</summary>
    private const int WsExToolWindow = 0x00000080;

    /// <summary>Builds the overlay around a set of controls.</summary>
    /// <param name="content">The control to host, typically the transport bar.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public OverlayWindow(Control content)
    {
        ArgumentNullException.ThrowIfNull(content);

        Name = "PlaybackOverlay";
        AccessibleName = "Playback overlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;

        // Matches the viewport's clear colour rather than a translucent black, so the controls sit
        // on something legible without the cost of layered-window compositing over a swap chain.
        BackColor = Color.FromArgb(16, 18, 23);

        // **Mostly transparent, because in full screen this sits ON the map.** The controls are
        // the point of the overlay and the map is the point of full screen, so the bar has to be
        // readable without taking a strip of the world away. Form.Opacity applies to the whole
        // layered window, controls included, which is what is wanted here - a transparency key
        // would punch the background through to the desktop rather than to the viewport beneath.
        Opacity = 0.55;

        // **Measured BEFORE docking, and that ordering is the whole point.** Docking the content
        // Fill makes it adopt this form's size, so anything that reads the content's height
        // afterwards reads the form's default 300 pixels instead of the bar's 44. The overlay was
        // built ~308 pixels tall that way, with the transport stretched down it and its controls
        // sitting a quarter of the way up the map.
        ClientSize = new Size(ClientSize.Width, content.Height + (Padding.Vertical / 2) + 8);

        content.Dock = DockStyle.Fill;
        Controls.Add(content);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Both flags matter. Without NOACTIVATE, clicking the overlay activates it and the main form
    /// loses focus, which breaks keyboard shortcuts and makes the title bar flicker on every
    /// click. Without TOOLWINDOW it appears in the taskbar and alt-tab as a second application.
    /// </remarks>
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Shown without activation for the same reason the style bit is set: activation on show would
    /// pull focus away from the viewer the moment the overlay appears.
    /// </remarks>
    protected override bool ShowWithoutActivation => true;

    /// <summary>Positions the overlay along the bottom of a viewport, in screen coordinates.</summary>
    /// <param name="viewport">The control being overlaid.</param>
    /// <param name="margin">Gap left around the overlay, in pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewport"/> is null.</exception>
    public void PositionOver(Control viewport, int margin = 16)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        Rectangle area = viewport.RectangleToScreen(viewport.ClientRectangle);

        Bounds = new Rectangle(
            area.Left + margin,
            area.Bottom - Height - margin,
            Math.Max(0, area.Width - (margin * 2)),
            Height);
    }
}
