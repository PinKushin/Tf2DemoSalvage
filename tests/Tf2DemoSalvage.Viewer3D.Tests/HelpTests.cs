using System;
using System.Linq;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>The <c>--help</c> page against the parsers it documents (B517).</summary>
/// <remarks>
/// `--help` said `"width height"` for <c>TF2VIEW_WINDOW_SIZE</c> while <see cref="WindowGeometry.Size"/>
/// splits on `x`, so a script that followed the help was silently given the default window. The
/// check is that the quoted example on each line is something the parser accepts.
/// </remarks>
public sealed class HelpTests
{
    private static string Example(string variable)
    {
        string line = Help.Text.Split('\n').Single(l => l.TrimStart().StartsWith(variable + " ", StringComparison.Ordinal));
        int open = line.IndexOf('"', StringComparison.Ordinal);
        int close = line.IndexOf('"', open + 1);
        return line[(open + 1)..close];
    }

    [Test]
    public void Text_WindowSizeExample_ParsesAsASize()
    {
        WindowGeometry.Size(Example(MainForm.WindowSizeVariable)).ShouldBe((1576, 889));
    }

    [Test]
    public void Text_WindowPosExample_ParsesAsAPosition()
    {
        WindowGeometry.Position(Example("TF2VIEW_WINDOW_POS")).ShouldBe((85, 78));
    }
}
