using System.Text;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The `lightstyles` string table: each entry's user data is a style's pattern, the text its number. `R_AnimateLight`
/// (`engine.dll` `0x1800d3ec0`) reads it through `GetStringUserData`, so a pattern is whatever the table held last.
/// </summary>
public sealed class LightStyleFeedConformanceTests
{
    [Test]
    public void PatternAt_TheTablesCreation_HoldsFromTheStart()
    {
        LightStyleFeed feed = new();

        feed.Apply([Entry(32, "m")], tick: null);

        feed.PatternAt(32, 0).ShouldBe("m");
    }

    /// <remarks>A switchable light turned off at tick 1560 and on at 1828, as `koth_harvest_event`'s style 37 is.</remarks>
    [Test]
    public void PatternAt_AnUpdate_HoldsFromItsTickAndNotBefore()
    {
        LightStyleFeed feed = new();

        feed.Apply([Entry(37, "m")], tick: null);
        feed.Apply([Entry(37, "a")], tick: 1560);
        feed.Apply([Entry(37, "m")], tick: 1828);

        (feed.PatternAt(37, 1559), feed.PatternAt(37, 1560), feed.PatternAt(37, 1828)).ShouldBe(("m", "a", "m"));
    }

    [Test]
    public void PatternAt_AStyleNeverSet_IsEmpty()
    {
        new LightStyleFeed().PatternAt(5, 100).ShouldBe(string.Empty);
    }

    /// <remarks>The pattern is sent with its terminating null, which `R_AnimateLight` takes off the length.</remarks>
    private static StringTableEntry Entry(int style, string pattern) =>
        new(style, null, Encoding.ASCII.GetBytes(pattern + "\0"));
}
