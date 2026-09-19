using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>What a demo load is doing, shown over the viewport while it does it.</summary>
/// <remarks>
/// **The owner asked for it**: *"is there a way to add like a splash screen that pops up immediately on boot and gives the user a
/// progress bar?"* A 26-minute match decodes for 80 seconds and reads its map for another 14, and until this the window did not
/// even appear for that long — the demo named on the command line was loaded inside the constructor. The decode reports a
/// fraction; the map, model and sound reads do not, so they show a marquee under their own name.
/// </remarks>
internal sealed class LoadingOverlay : Panel
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "In Controls, which base.Dispose walks.")]
    private readonly Label _stage;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "In Controls, which base.Dispose walks.")]
    private readonly ProgressBar _bar;

    /// <summary>Builds the overlay, hidden.</summary>
    public LoadingOverlay()
    {
        Name = MainForm.LoadingOverlayId;
        AccessibleName = "Loading";
        Size = new Size(420, 72);
        BackColor = SystemColors.Control;
        BorderStyle = BorderStyle.FixedSingle;
        Visible = false;

        _stage = new Label
        {
            Name = MainForm.LoadingStageId,
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        // A live readout, so its name is its text — the status label's lesson: a fixed name hides every message from UIA.
        _stage.TextChanged += (_, _) => _stage.AccessibleName = _stage.Text;

        _bar = new ProgressBar
        {
            Name = MainForm.LoadingProgressId,
            AccessibleName = "Loading progress",
            Dock = DockStyle.Bottom,
            Height = 24,
            Maximum = 1000,
        };

        Controls.Add(_bar);
        Controls.Add(_stage);
    }

    /// <summary>Shows a stage.</summary>
    /// <param name="stage">What the load is doing, in words.</param>
    /// <param name="fraction">How far through it, or null when the stage cannot say.</param>
    public void Report(string stage, double? fraction)
    {
        _stage.Text = fraction is { } done
            ? $"{stage}… {done * 100d:0}%"
            : $"{stage}…";

        _bar.Style = fraction is null ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _bar.Value = (int)Math.Round(Math.Clamp(fraction ?? 0d, 0d, 1d) * _bar.Maximum);

        Center();
        Visible = true;
        BringToFront();
    }

    /// <inheritdoc/>
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);

        if (Parent is not null)
        {
            Parent.Resize += (_, _) => Center();
        }
    }

    private void Center()
    {
        if (Parent is not null)
        {
            Location = new Point(
                Math.Max(0, (Parent.ClientSize.Width - Width) / 2),
                Math.Max(0, (Parent.ClientSize.Height - Height) / 2));
        }
    }
}
