using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Narrowing a playlist to the demo someone is looking for.
/// </summary>
/// <remarks>
/// **A real archive is the condition this has to work under.** The ESEA folders opened during
/// development hold 370 demos named <c>esea_match_13977649.dem</c> — machine identifiers with no
/// human meaning — so scrolling is not a way to find anything. The folder is usually the part a
/// person remembers, which is why it is searched alongside the file name.
/// </remarks>
public sealed class PlaylistFilterTests
{
    private static readonly DemoEntry[] Library =
    [
        Entry(@"D:\demos\ESEA Season 29\esea_match_13977649.dem"),
        Entry(@"D:\demos\ESEA Season 29\esea_match_13977650.dem"),
        Entry(@"D:\demos\ETF2L 2014\gullywash_final.dem"),
        Entry(@"D:\demos\pugs\process_ace.dem"),
    ];

    [Test]
    public void Filter_EmptyQuery_KeepsEverything()
    {
        // The default state of the box. An empty filter is not a filter.
        PlaylistFilter.Apply(Library, string.Empty).Count.ShouldBe(Library.Length);
    }

    [Test]
    public void Filter_WhitespaceQuery_KeepsEverything()
    {
        PlaylistFilter.Apply(Library, "   ").Count.ShouldBe(Library.Length);
    }

    [Test]
    public void Filter_MatchesTheFileName()
    {
        PlaylistFilter.Apply(Library, "gullywash")
            .Select(entry => entry.Name).ShouldBe(["gullywash_final.dem"]);
    }

    [Test]
    public void Filter_MatchesTheFolder()
    {
        // The part a person actually remembers. Nobody recalls which eight-digit id they wanted,
        // but they do recall it was the ETF2L set.
        PlaylistFilter.Apply(Library, "etf2l")
            .Select(entry => entry.Name).ShouldBe(["gullywash_final.dem"]);
    }

    [Test]
    public void Filter_IsCaseInsensitive()
    {
        // The condition has to differ between correct and broken: "ETF2L" is stored capitalised,
        // so searching for it in capitals would pass under a case-SENSITIVE comparison too and
        // prove nothing. Lower case is the input that separates them.
        PlaylistFilter.Apply(Library, "etf2l").Count.ShouldBe(1);
        PlaylistFilter.Apply(Library, "GULLYWASH").Count.ShouldBe(1);
    }

    [Test]
    public void Filter_AllTermsMustMatch()
    {
        // Space-separated terms narrow rather than widen, which is how a file browser behaves. The
        // control is the second entry: "esea 13977650" must exclude it, and an OR would not.
        IReadOnlyList<DemoEntry> found = PlaylistFilter.Apply(Library, "esea 13977650");

        found.Select(entry => entry.Name).ShouldBe(["esea_match_13977650.dem"]);
    }

    [Test]
    public void Filter_TermsMayMatchDifferentFields()
    {
        // One term from the folder, one from the name. Requiring both to hit the same field would
        // make the most natural query - where you were and what you want - return nothing.
        PlaylistFilter.Apply(Library, "season 13977649")
            .Select(entry => entry.Name).ShouldBe(["esea_match_13977649.dem"]);
    }

    [Test]
    public void Filter_NoMatch_ReturnsNothing()
    {
        PlaylistFilter.Apply(Library, "badlands").ShouldBeEmpty();
    }

    [Test]
    public void Filter_KeepsTheOriginalOrder()
    {
        // Grouping in the playlist depends on it: reordering would scatter a folder's demos across
        // several groups with the same name.
        PlaylistFilter.Apply(Library, "esea").Select(entry => entry.Name)
            .ShouldBe(["esea_match_13977649.dem", "esea_match_13977650.dem"]);
    }

    private static DemoEntry Entry(string path)
    {
        string folder = System.IO.Path.GetDirectoryName(path)!;

        return new DemoEntry(
            path,
            System.IO.Path.GetFileName(path),
            System.IO.Path.GetFileName(folder),
            @"D:\demos");
    }
}
