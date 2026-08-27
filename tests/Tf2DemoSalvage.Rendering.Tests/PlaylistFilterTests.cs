using System.Collections.Generic;
using System.Linq;


namespace Tf2DemoSalvage.Rendering.Tests;

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

    [Test]
    public void Order_ALibraryInAnyOrder_GroupsByFolderThenName()
    {
        // **`Apply`'s own contract depends on this and could not enforce it.** Its documentation
        // says order is preserved *"because the playlist groups by folder afterwards, and reordering
        // would scatter one folder across several groups of the same name"* — so the grouping is a
        // PRECONDITION of the filter, and it lived three lines inline in `MainForm` while the filter
        // lived in Scene. Two halves of one policy, in two projects, with nothing making them agree.
        DemoEntry[] shuffled =
        [
            Entry(@"D:\demos\season31\b.dem"),
            Entry(@"D:\demos\pugs\z.dem"),
            Entry(@"D:\demos\season31\a.dem"),
            Entry(@"D:\demos\pugs\a.dem"),
        ];

        PlaylistFilter.Order(shuffled).Select(entry => entry.Path)
            .ShouldBe([
                @"D:\demos\pugs\a.dem",
                @"D:\demos\pugs\z.dem",
                @"D:\demos\season31\a.dem",
                @"D:\demos\season31\b.dem",
            ]);
    }

    [Test]
    public void Order_FoldersWhoseCaseDisagreesWithOrdinal_SortsAlphabetically()
    {
        // **Case-insensitive, because the library is read off a Windows disk** where folder names
        // are spelled however whoever made them felt like. An ordinal sort puts every capital before
        // every lowercase, so `Ultiduo` lands before `esea` and the list stops being alphabetical to
        // the person reading it.
        //
        // **The first version of this test used `esea`, `ESEA` and `pugs` and could not fail.**
        // Under `Ordinal` the folders differ, so `ESEA` sorts first; under `OrdinalIgnoreCase` they
        // compare equal and `ThenBy` on the name puts `ESEA`'s `a.dem` first — the same output for
        // opposite reasons. That is the "wrong condition" case from the testing standards: an input
        // for which correct and broken predict the same observation, and the instinct to strengthen
        // the assertion would not have helped.
        //
        // The input below is chosen so the two orders genuinely disagree: ordinal puts `Ultiduo`
        // first because `U` is 85 and `e` is 101.
        DemoEntry[] mixed =
        [
            Entry(@"D:\demos\Ultiduo\a.dem"),
            Entry(@"D:\demos\esea\a.dem"),
        ];

        PlaylistFilter.Order(mixed).Select(entry => entry.Folder)
            .ShouldBe(["esea", "Ultiduo"]);
    }

    [Test]
    public void Order_ThenApply_KeepsTheGroupingTheFilterPromises()
    {
        // The two halves together, which is the claim neither can make alone: after ordering, a
        // filter that preserves order still hands back one contiguous run per folder.
        DemoEntry[] shuffled =
        [
            Entry(@"D:\demos\season31\esea_b.dem"),
            Entry(@"D:\demos\pugs\esea_z.dem"),
            Entry(@"D:\demos\season31\esea_a.dem"),
        ];

        PlaylistFilter.Apply(PlaylistFilter.Order(shuffled), "esea")
            .Select(entry => entry.Folder)
            .ShouldBe(["pugs", "season31", "season31"]);
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
