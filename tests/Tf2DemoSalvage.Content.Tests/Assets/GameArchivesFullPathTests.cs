using System;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary><see cref="GameArchives.FullPathOnDisk"/>: the loose file an API that needs a real path is handed.</summary>
public sealed class GameArchivesFullPathTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "gamearchives-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "resource"));
        File.WriteAllBytes(Path.Combine(_folder, "resource", "face.ttf"), [1]);
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void FullPathOnDisk_ALooseFile_IsItsFullPath() =>
        GameArchives.Open(_folder).FullPathOnDisk("resource/face.ttf").ShouldBe(Path.Combine(_folder, "resource", "face.ttf"));

    [Test]
    public void FullPathOnDisk_AbsentOrOutsideTheFolder_IsNull()
    {
        GameArchives archives = GameArchives.Open(_folder);

        archives.FullPathOnDisk("resource/missing.ttf").ShouldBeNull();
        archives.FullPathOnDisk("../" + Path.GetFileName(_folder) + "/resource/face.ttf").ShouldNotBeNull("the control: the same file reached back in");
        archives.FullPathOnDisk("../escape.ttf").ShouldBeNull();
    }
}
