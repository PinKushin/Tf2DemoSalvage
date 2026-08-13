using System;
using System.Windows.Forms;

using Tf2DemoSalvage.Core.Diagnostics;

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
        ViewerLog.Begin("viewer");

        // **Core and Content can now say what they lost.** Both are libraries and neither can see
        // this log, so until they had a sink their only honest options were to throw or return
        // nothing - and the second is the silent fallback this project bans everywhere it can
        // reach. Attaching it here is the whole wiring.
        DecodeLog.Sink = ViewerLog.Warn;

        ApplicationConfiguration.Initialize();
        // Passed straight through: double-clicking a .dem, selecting several and pressing enter,
        // or dropping a folder on the executable all arrive here as paths, and all go through the
        // same library code the Open buttons use.
        using MainForm shell = new(args);
        Application.Run(shell);
    }
}
