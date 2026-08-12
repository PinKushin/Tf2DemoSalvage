using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests how a chosen file or folder becomes a playlist of demos.
/// </summary>
/// <remarks>
/// Real directories in the temp folder rather than an abstraction over the file system. The
/// behaviour under test IS file-system behaviour - recursion, extension matching, ordering - and
/// an interface faked in the test would be asserting that the fake does what the fake does.
/// </remarks>
public sealed class DemoLibraryTests
{
    private string _root = string.Empty;

    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void RemoveRoot()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked file must not fail an otherwise passing test; the temp folder is disposable.
        }
    }

    private string Demo(string relativePath)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[16]);
        return full;
    }

    [Test]
    public void ASingleFileBecomesAPlaylistOfOne()
    {
        string demo = Demo("solo.dem");

        DemoLibrary library = new();
        library.Open(demo);

        library.Entries.ShouldHaveSingleItem().Path.ShouldBe(demo);
    }

    [Test]
    public void AFolderBecomesAPlaylistOfItsDemos()
    {
        Demo("a.dem");
        Demo("b.dem");

        DemoLibrary library = new();
        library.Open(_root);

        library.Entries.Select(e => e.Name).ShouldBe(["a.dem", "b.dem"]);
    }

    [Test]
    public void AnOrdinaryFolderIncludesItsSubfolders()
    {
        // The case the owner asked for: a folder of demos organised into subfolders is one
        // playlist, not several.
        Demo("top.dem");
        Demo("season12/week3/match.dem");

        DemoLibrary library = new();
        library.Open(_root);

        // Ordered by FOLDER then name, which is what the side panel groups by - so the root's
        // own demo comes before one nested under it, whatever the file names are. The first
        // version of this expectation assumed plain name order and was simply wrong about the
        // contract.
        library.Entries.Select(e => e.Name).ShouldBe(["top.dem", "match.dem"]);
    }

    [Test]
    public void TheGamesAssetFoldersAreSkipped()
    {
        // Pointing at the game's `tf` folder and walking everything would trawl the whole
        // install - materials, models and sound are gigabytes - to find the handful of demos the
        // game writes into the top of it.
        //
        // Skipping asset folders by NAME rather than detecting the game directory, because the
        // name test is right in more cases: it also works for a copied or archived install, and
        // TF2's asset folder names have not changed in the game's lifetime. Anyone who points
        // this at something genuinely unusual pays only in scan time.
        Demo("recorded.dem");
        Demo("materials/deep/notademo.dem");
        Demo("models/player/notademo.dem");
        Demo("sound/vo/notademo.dem");
        Demo("maps/notademo.dem");

        DemoLibrary library = new();
        library.Open(_root);

        library.Entries.ShouldHaveSingleItem().Name.ShouldBe("recorded.dem");
    }

    [Test]
    public void AFolderNamedLikeAnAssetFolderElsewhereIsStillSkipped()
    {
        // The rule is by name at any depth, which is the trade being made deliberately: a demo
        // genuinely stored in a folder called "sound" is missed. That is worth it against
        // scanning a full game install, and it is the case nobody has.
        Demo("season12/sound/hidden.dem");
        Demo("season12/match.dem");

        DemoLibrary library = new();
        library.Open(_root);

        library.Entries.ShouldHaveSingleItem().Name.ShouldBe("match.dem");
    }

    [Test]
    public void NonDemoFilesAreIgnored()
    {
        Demo("keep.dem");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not a demo");

        DemoLibrary library = new();
        library.Open(_root);

        library.Entries.ShouldHaveSingleItem().Name.ShouldBe("keep.dem");
    }

    [Test]
    public void SeveralFoldersCanBeOpenAtOnce()
    {
        // Explicitly wanted: open an arbitrary number of folders and choose what to play from
        // across all of them.
        Demo("one/first.dem");
        Demo("two/second.dem");

        DemoLibrary library = new();
        library.Open(Path.Combine(_root, "one"));
        library.Open(Path.Combine(_root, "two"));

        library.Entries.Select(e => e.Name).ShouldBe(["first.dem", "second.dem"]);
        library.Roots.Count.ShouldBe(2);
    }

    [Test]
    public void OpeningTheSameFolderTwiceDoesNotDuplicateIt()
    {
        Demo("only.dem");

        DemoLibrary library = new();
        library.Open(_root);
        library.Open(_root);

        library.Entries.ShouldHaveSingleItem();
        library.Roots.ShouldHaveSingleItem();
    }

    [Test]
    public void AMissingPathAddsNothingRatherThanThrowing()
    {
        // A folder can vanish between being chosen and being read - a network share, a removed
        // drive. The viewer reports an empty playlist; it does not fall over.
        DemoLibrary library = new();

        Should.NotThrow(() => library.Open(Path.Combine(_root, "gone")));
        library.Entries.ShouldBeEmpty();
    }

    [Test]
    public void EntriesKnowWhichFolderTheyCameFrom()
    {
        // The side panel groups by folder, so every entry has to carry its own.
        Demo("season12/match.dem");

        DemoLibrary library = new();
        library.Open(_root);

        library.Entries.ShouldHaveSingleItem()
            .Folder.ShouldBe(Path.Combine(_root, "season12"));
    }
}
