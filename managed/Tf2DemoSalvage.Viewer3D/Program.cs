using System;
using Silk.NET.Maths;
using Silk.NET.Windowing;

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
/// At this stage the window exists and the device works; nothing is drawn into it yet.
/// </remarks>
internal static class Program
{
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 720;

    /// <summary>Opens the viewer window.</summary>
    private static void Main()
    {
        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(DefaultWidth, DefaultHeight),
            Title = "tf2demoview",

            // Silk must not create an OpenGL context: the device is Direct3D and binds to the
            // window's Win32 handle directly.
            API = GraphicsAPI.None,
        };

        using IWindow window = Window.Create(options);
        window.Initialize();

        using Device3D device = Device3D.Create(window);

        window.Resize += size => device.Resize(size.X, size.Y);
        window.Render += _ => device.ClearAndPresent(0.06f, 0.07f, 0.09f);

        window.Run();
    }
}
