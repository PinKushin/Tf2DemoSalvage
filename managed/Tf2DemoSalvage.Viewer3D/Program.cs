using System;
using System.Windows.Forms;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Diagnostics;
using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Entry point for the demo viewer.
/// </summary>
/// <remarks>
/// **One renderer, two camera modes, not two applications.** The intended progression is a
/// top-down labelled overview first — which needs nothing but entity origins, and those are
/// already decoded — and a free camera over real map geometry later, once BSP and VPK reading
/// exist. Those differ by a projection matrix and a camera controller, so a separate 2D viewer
/// would be a codebase thrown away at the point it started being interesting. The empty
/// `Viewer2D` project was removed for that reason.
///
/// **The shell is WinForms and the viewport is Direct3D**, which is the split the owner asked
/// for: menus, a timeline and an entity list are ordinary controls, and drawing them in D3D would
/// mean writing a UI toolkit to avoid using one. It also gives the UI tests a surface to address,
/// since WinForms controls expose AutomationId and accessible names to UIA.
///
/// At this stage the window and menu exist and the device is created against the viewport panel;
/// nothing is drawn into it yet.
/// </remarks>
internal static class Program
{
    /// <summary>Opens the viewer window.</summary>
    /// <param name="args">
    /// Files or folders to open, as a file association or a shortcut supplies them.
    /// </param>
    [STAThread]
    private static void Main(string[] args)
    {
        // STAThread and this initialisation order are both required by WinForms itself: COM
        // apartment first, then visual styles, before any control exists.
        //
        // **This is the composition root, and it is the only place that builds a logger (D83).**
        // Everything below takes an ILoggerFactory or an ILogger it was handed; nothing reaches for
        // a static. That is the whole of the conversion off ViewerLog, and it is what lets the same
        // code log to a file here, to nothing in a test, and to a console in the CLI.
        using FileLoggerProvider logs = new(
            FileLogWriter.DefaultFolder,
            "viewer",
            $"TF2 Demo Salvage viewer, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // Concrete type on purpose: CA1859 prefers it where the local never needs the interface,
        // and this is the composition root — nothing here substitutes a different factory.
        using LoggerFactory loggers = new([logs]);

        // **Core and Content can now say what they lost.** Both are libraries and neither can see
        // this log, so until they had a sink their only honest options were to throw or return
        // nothing - and the second is the silent fallback this project bans everywhere it can
        // reach. Attaching it here is the whole wiring.
        //
        // Both take an area and a finished message, so they need no change for this conversion —
        // they were already inverted, and better than they looked.
        DecodeLog.Sink = (area, message) =>
            loggers.CreateLogger(area).LogWarning("{Message}", message);

        // Ordinary observations go to the ordinary channel. A count arriving as a warning teaches
        // the reader to skip warnings, which is the opposite of what the log is for.
        DecodeLog.Notes = loggers.LogTo();

        ApplicationConfiguration.Initialize();
        // Passed straight through: double-clicking a .dem, selecting several and pressing enter,
        // or dropping a folder on the executable all arrive here as paths, and all go through the
        // same library code the Open buttons use.
        using MainForm shell = new(loggers, args);

        // **The `developer` cvar's other end.** The sink has always filtered on a settable Minimum
        // and nothing could set it, so Debug was unreachable and demoting a noisy line to Debug was
        // deletion wearing a comment. The form knows the setting — from the config or `+developer 1`
        // — and this is the only place that holds the provider, so the switch is handed over rather
        // than the provider being reached for from inside.
        shell.SetLogVerbosity = level => logs.Minimum = level;
        shell.ApplyLogVerbosity();

        Application.Run(shell);
    }
}
