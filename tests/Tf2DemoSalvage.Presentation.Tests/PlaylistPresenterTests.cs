using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The owner of the playlist (D214) [no engine counterpart: the game's demo browser lists one folder and filters
/// nothing], over a folder of empty files a test wrote — a row is a name, and no demo is read to list one.
/// </summary>
public sealed class PlaylistPresenterTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(_folder, "later"));
        File.WriteAllBytes(Path.Combine(_folder, "b_second.dem"), []);
        File.WriteAllBytes(Path.Combine(_folder, "a_first.dem"), []);
        File.WriteAllBytes(Path.Combine(_folder, "later", "c_third.dem"), []);
        File.WriteAllBytes(Path.Combine(_folder, "not-a-demo.txt"), []);
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException held)
        {
            // A disposable temp folder: a file still held open is reported and left, and fails no test.
            TestContext.Out.WriteLine($"left {_folder} behind: {held.Message}");
        }
    }

    [Test]
    public void Add_AFolderWithASubfolder_ListsItsDemosByFolderThenNameAndCountsThem()
    {
        PlaylistPresenter playlist = new();

        playlist.Add(string.Empty, _folder).ShouldBe("3 demos.");

        playlist.Count.ShouldBe(3);
        Enumerable.Range(0, 3).Select(index => playlist.Row(index).ShouldNotBeNull().Name)
            .ShouldBe(["a_first.dem", "b_second.dem", "c_third.dem"]);
        playlist.Row(0).ShouldNotBeNull().Path.ShouldBe(Path.Combine(_folder, "a_first.dem"));
        playlist.Row(2).ShouldNotBeNull().Folder.ShouldNotBe(playlist.Row(0).ShouldNotBeNull().Folder, "the subfolder is its own folder");
    }

    [Test]
    public void Add_OneFileThenNothing_SaysOneDemoAndNoDemosFound()
    {
        new PlaylistPresenter().Add(string.Empty, Path.Combine(_folder, "a_first.dem")).ShouldBe("1 demo.");
        new PlaylistPresenter().Add(string.Empty, Path.Combine(_folder, "later", "nothing-here")).ShouldBe("No demos found.");
    }

    [Test]
    public void Add_WithTextInTheSearchBox_ShowsOnlyWhatMatchesButCountsTheLibrary()
    {
        PlaylistPresenter playlist = new();

        playlist.Add("second", _folder).ShouldBe("3 demos.", "the status is the library, not the filter");

        playlist.Count.ShouldBe(1);
        playlist.Row(0).ShouldNotBeNull().Name.ShouldBe("b_second.dem");
    }

    [Test]
    public void Filter_ByNameThenCleared_NarrowsTheRowsAndBringsThemBack()
    {
        PlaylistPresenter playlist = new();
        playlist.Add(string.Empty, _folder);

        playlist.Filter("third");

        playlist.Count.ShouldBe(1);
        playlist.Row(0).ShouldNotBeNull().Name.ShouldBe("c_third.dem");

        playlist.Filter(string.Empty);

        playlist.Count.ShouldBe(3);
    }

    [Test]
    public void Row_PastEitherEnd_IsNothingRatherThanAThrow()
    {
        PlaylistPresenter playlist = new();
        playlist.Add(string.Empty, _folder);

        playlist.Row(-1).ShouldBeNull();
        playlist.Row(3).ShouldBeNull("a paint handler asks for rows a shorter list no longer has");
    }
}
