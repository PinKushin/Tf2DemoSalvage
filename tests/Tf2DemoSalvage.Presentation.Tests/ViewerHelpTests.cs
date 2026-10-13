using System;
using System.Linq;
namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The <c>--help</c> page against the parsers it documents (B517).</summary>
/// <remarks>
/// `--help` said `"width height"` for <c>TF2VIEW_WINDOW_SIZE</c> while <see cref="WindowGeometry.Size"/>
/// splits on `x`, so a script that followed the help was silently given the default window. The
/// check is that the quoted example on each line is something the parser accepts.
/// </remarks>
public sealed class ViewerHelpTests
{
    private static string Example(string variable)
    {
        string line = ViewerHelp.Text.Split('\n').Single(l => l.TrimStart().StartsWith(variable + " ", StringComparison.Ordinal));
        int open = line.IndexOf('"', StringComparison.Ordinal);
        int close = line.IndexOf('"', open + 1);
        return line[(open + 1)..close];
    }

    [Test]
    public void Text_WindowSizeExample_ParsesAsASize()
    {
        WindowGeometry.Size(Example("TF2VIEW_WINDOW_SIZE")).ShouldBe((1576, 889));
    }

    [Test]
    public void Text_WindowPosExample_ParsesAsAPosition()
    {
        WindowGeometry.Position(Example("TF2VIEW_WINDOW_POS")).ShouldBe((85, 78));
    }

    [Test]
    public void Wanted_AnyOfTheFourSpellingsAnywhere_IsTrue()
    {
        foreach (string spelling in (string[])["--help", "-h", "-?", "/?"])
        {
            ViewerHelp.Wanted(["a.dem", spelling]).ShouldBeTrue(spelling);
        }
    }

    [Test]
    public void Wanted_WithoutOne_IsFalse()
    {
        ViewerHelp.Wanted([]).ShouldBeFalse();
        ViewerHelp.Wanted(["a.dem", "--shot", "help.png", "-help"]).ShouldBeFalse();
    }
}
