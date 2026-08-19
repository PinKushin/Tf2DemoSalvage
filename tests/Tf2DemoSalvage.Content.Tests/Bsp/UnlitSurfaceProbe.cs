using System;
using System.IO;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// How much of a map is surface that light never reaches.
/// </summary>
/// <remarks>
/// **The question behind the black boxes over cp_process's last points.** Those rooms are covered
/// by a ceiling, and a ceiling brush has two faces: the underside that the room sees, and the top
/// that faces the void above it. The viewer culls down-facing brush faces so a ceiling does not
/// hide the room it covers — which leaves the *top* of that same brush pointing straight at an
/// overhead camera, lit by nothing, drawn black, exactly over the room.
///
/// A probe rather than a test: it measures and prints, and what to do about the result is a
/// separate decision. Run by hand.
/// </remarks>
public sealed class UnlitSurfaceProbe
{
    [Test]
    [Explicit("Diagnostic. Measures how many surfaces carry no light at all.")]
    public void UnlitSurfaces_TheirShareOfTheMap_IsReported()
    {
        string map = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage",
            "maps",
            "cp_process_f12.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore($"No map at {map}; open a demo in the viewer first.");
            return;
        }

        byte[] file = File.ReadAllBytes(map);

        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(file);

        int total = 0;
        int black = 0;
        int blackFacingUp = 0;

        foreach (BspSurface surface in surfaces)
        {
            if (!surface.IsVisible || surface.Lightmap.IsEmpty)
            {
                continue;
            }

            total++;

            // Every luxel dark. Not "dim" - a genuinely dark room still carries a few counts of
            // bounced light, while a face the compiler never traced a ray to is flat zero.
            if (!surface.Lightmap.Pixels.Span.TrimStart((byte)0).IsEmpty)
            {
                continue;
            }

            black++;

            if (surface.Normal.Z > 0.7f)
            {
                blackFacingUp++;
            }
        }

        TestContext.Out.WriteLine(
            $"UNLIT {total} visible surfaces, {black} with no light at all, " +
            $"{blackFacingUp} of those facing up");

        total.ShouldBeGreaterThan(0);
    }
}
