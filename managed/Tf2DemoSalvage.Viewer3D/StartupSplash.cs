using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>A small window shown the moment the program starts, until the main window is up (D182).</summary>
/// <remarks>
/// **The owner's request, and the first attempt answered a different one**: *"I asked for a splash screen for when the
/// APPLICATION IS LOADING, literally on the boot of the app itself … I want to see something happening basically immediately
/// after double clicking the program, Like how maui and MPF do just naturally."* The demo-loading overlay stays; this is the
/// part before any window of ours existed.
///
/// **On its own UI thread**, so its bar keeps moving while the main thread builds <see cref="MainForm"/> — a window on the main
/// thread would be frozen by exactly the work it is there to cover. It is closed from the main window's first <c>Shown</c>.
/// </remarks>
internal sealed class StartupSplash : Form
{
    private StartupSplash()
    {
        Name = MainForm.StartupSplashId;
        AccessibleName = "TF2 Demo Salvage is starting";
        Text = "TF2 Demo Salvage - starting";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ControlBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(360, 90);

        Controls.Add(new ProgressBar
        {
            Name = "StartupProgress",
            AccessibleName = "Starting",
            Style = ProgressBarStyle.Marquee,
            Dock = DockStyle.Bottom,
            Height = 22,
        });

        Controls.Add(new Label
        {
            Name = "StartupText",
            Text = "TF2 Demo Salvage is starting…",
            AccessibleName = "TF2 Demo Salvage is starting",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        });
    }

    /// <summary>Shows the splash on its own thread and returns at once.</summary>
    /// <returns>Call it to close the splash; safe from any thread, and more than once.</returns>
    public static Action Open()
    {
        // **Never waited on**: the main thread goes straight on building the real window. Either side may finish first, so a
        // close asked for before the splash exists is remembered, and the splash checks it once it is up.
        int closing = 0;
        StartupSplash? splash = null;

        Thread thread = new(() =>
        {
            StartupSplash made = new();
            made.Shown += (_, _) =>
            {
                if (Volatile.Read(ref closing) == 1)
                {
                    made.Close();
                }
            };

            Volatile.Write(ref splash, made);

            if (Volatile.Read(ref closing) == 0)
            {
                Application.Run(made);
            }

            made.Dispose();
        })
        {
            IsBackground = true,
            Name = "startup splash",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return () =>
        {
            Interlocked.Exchange(ref closing, 1);

            if (Volatile.Read(ref splash) is { IsHandleCreated: true, IsDisposed: false } open)
            {
                open.BeginInvoke(open.Close);
            }
        };
    }
}
