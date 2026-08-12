using System;
using System.IO;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests finding a map's BSP on disk.
/// </summary>
/// <remarks>
/// **The library folder cannot be assumed.** Steam supports several, and on this developer's
/// machine TF2 lives on a second drive while Steam itself is under Program Files — so a locator
/// that guessed the default path would find nothing while 233 maps sat one drive away.
///
/// Everything here runs against synthetic directory trees. Reading a real Steam install would
/// make the tests depend on which games happen to be installed, which is a property of the
/// machine and not of the code.
/// </remarks>
public sealed class MapLocatorTests
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
            // Disposable temp tree; a lock must not fail a passing test.
        }
    }

    /// <summary>Writes a libraryfolders.vdf listing the given library paths.</summary>
    /// <param name="entries">Each library path, and whether TF2 is installed in it.</param>
    private string WriteLibraryFile(params (string Path, bool HasTf2)[] entries)
    {
        string steamApps = Path.Combine(_root, "Steam", "steamapps");
        Directory.CreateDirectory(steamApps);

        using StringWriter text = new();
        text.WriteLine("\"libraryfolders\"");
        text.WriteLine("{");

        for (int i = 0; i < entries.Length; i++)
        {
            text.WriteLine($"\t\"{i}\"");
            text.WriteLine("\t{");
            text.WriteLine($"\t\t\"path\"\t\t\"{entries[i].Path.Replace("\\", "\\\\", StringComparison.Ordinal)}\"");
            text.WriteLine("\t\t\"apps\"");
            text.WriteLine("\t\t{");
            text.WriteLine("\t\t\t\"570\"\t\t\"123\"");

            if (entries[i].HasTf2)
            {
                text.WriteLine("\t\t\t\"440\"\t\t\"456\"");
            }

            text.WriteLine("\t\t}");
            text.WriteLine("\t}");
        }

        text.WriteLine("}");

        string file = Path.Combine(steamApps, "libraryfolders.vdf");
        File.WriteAllText(file, text.ToString());
        return file;
    }

    /// <summary>Creates a library containing a TF2 install with the given maps.</summary>
    private string CreateLibrary(string name, params string[] maps)
    {
        string library = Path.Combine(_root, name);
        string mapsFolder = Path.Combine(
            library, "steamapps", "common", "Team Fortress 2", "tf", "maps");
        Directory.CreateDirectory(mapsFolder);

        foreach (string map in maps)
        {
            File.WriteAllBytes(Path.Combine(mapsFolder, map + ".bsp"), new byte[32]);
        }

        return library;
    }

    [Test]
    public void AMapIsFoundInTheLibraryThatHasTf2()
    {
        string library = CreateLibrary("GameDrive", "cp_process_final");
        string vdf = WriteLibraryFile((library, HasTf2: true));

        MapLocator locator = new(vdf, Path.Combine(_root, "own-maps"));

        locator.Find("cp_process_final").ShouldNotBeNull()
            .ShouldEndWith("cp_process_final.bsp");
    }

    [Test]
    public void ASecondLibraryIsSearchedWhenTheFirstDoesNotHaveTheGame()
    {
        // The case this exists for: Steam under Program Files, the game on another drive. A
        // locator that stopped at the first library would find nothing.
        string empty = CreateLibrary("SystemDrive");
        Directory.Delete(Path.Combine(empty, "steamapps", "common"), recursive: true);

        string gameDrive = CreateLibrary("GameDrive", "koth_product_final");
        string vdf = WriteLibraryFile((empty, HasTf2: false), (gameDrive, HasTf2: true));

        MapLocator locator = new(vdf, Path.Combine(_root, "own-maps"));

        locator.Find("koth_product_final").ShouldNotBeNull().ShouldContain("GameDrive");
    }

    [Test]
    public void OurOwnFolderIsUsedWhenTheGameDoesNotHaveTheMap()
    {
        // Community maps are not in the install. This is where a downloaded one lands, and it is
        // deliberately not inside the user's game folder - see DECISIONS.md D32.
        string library = CreateLibrary("GameDrive", "cp_badlands");
        string ours = Path.Combine(_root, "own-maps");
        Directory.CreateDirectory(ours);
        File.WriteAllBytes(Path.Combine(ours, "cp_gullywash_final1.bsp"), new byte[32]);

        MapLocator locator = new(WriteLibraryFile((library, HasTf2: true)), ours);

        locator.Find("cp_gullywash_final1").ShouldNotBeNull().ShouldContain("own-maps");
    }

    [Test]
    public void TheGameInstallWinsWhenBothHaveTheMap()
    {
        // The user's own copy is the one the game would load, so it is the one a viewer should
        // show. It is also the copy nobody downloaded from a stranger.
        string library = CreateLibrary("GameDrive", "cp_dustbowl");
        string ours = Path.Combine(_root, "own-maps");
        Directory.CreateDirectory(ours);
        File.WriteAllBytes(Path.Combine(ours, "cp_dustbowl.bsp"), new byte[32]);

        MapLocator locator = new(WriteLibraryFile((library, HasTf2: true)), ours);

        locator.Find("cp_dustbowl").ShouldNotBeNull().ShouldContain("GameDrive");
    }

    [Test]
    public void AMissingMapIsNullRatherThanAnException()
    {
        // A demo can name a map nobody has. The viewer still plays the demo without one.
        string library = CreateLibrary("GameDrive", "cp_badlands");

        MapLocator locator = new(
            WriteLibraryFile((library, HasTf2: true)), Path.Combine(_root, "own-maps"));

        locator.Find("ctf_turbine_pro_rc4").ShouldBeNull();
    }

    [Test]
    public void AMissingLibraryFileIsNotFatal()
    {
        // Steam may not be installed at all; the viewer is not a game launcher and must still run.
        MapLocator locator = new(
            Path.Combine(_root, "nope", "libraryfolders.vdf"), Path.Combine(_root, "own-maps"));

        Should.NotThrow(() => locator.Find("cp_process_final"));
        locator.Find("cp_process_final").ShouldBeNull();
    }

    [Test]
    public void AMapNameWithAPathInItIsRefused()
    {
        // The map name comes from a demo header, which is untrusted input. Without this, a header
        // naming "..\\..\\Windows\\System32\\config\\SAM" would send the locator wherever it liked.
        string library = CreateLibrary("GameDrive", "cp_badlands");
        MapLocator locator = new(
            WriteLibraryFile((library, HasTf2: true)), Path.Combine(_root, "own-maps"));

        Should.Throw<ArgumentException>(() => locator.Find(@"..\..\windows\system32\config\SAM"));
        Should.Throw<ArgumentException>(() => locator.Find("maps/cp_badlands"));
    }

    [Test]
    public void AUserConfiguredFolderBeatsTheDetectedInstall()
    {
        // Someone only sets this when detection got it wrong, or when their maps live somewhere
        // the scheme does not cover - a portable install, a network share, a folder of community
        // maps outside Steam. Searching it after the automatic result would make the setting look
        // broken in precisely the case it exists for.
        string library = CreateLibrary("GameDrive", "cp_snakewater_final1");

        string configured = Path.Combine(_root, "my-maps");
        Directory.CreateDirectory(configured);
        File.WriteAllBytes(Path.Combine(configured, "cp_snakewater_final1.bsp"), new byte[64]);

        MapLocator locator = new(
            WriteLibraryFile((library, HasTf2: true)), Path.Combine(_root, "own-maps"), configured);

        locator.Find("cp_snakewater_final1").ShouldNotBeNull().ShouldContain("my-maps");
    }

    [Test]
    public void AConfiguredFolderThatDoesNotHaveTheMapFallsThrough()
    {
        // The override adds a place to look; it does not replace the others.
        string library = CreateLibrary("GameDrive", "koth_lakeside_final");

        MapLocator locator = new(
            WriteLibraryFile((library, HasTf2: true)),
            Path.Combine(_root, "own-maps"),
            Path.Combine(_root, "empty-folder"));

        locator.Find("koth_lakeside_final").ShouldNotBeNull().ShouldContain("GameDrive");
    }

    [Test]
    public void TheLibraryPathsEscapedBackslashesAreUnescaped()
    {
        // A VDF stores Windows paths with doubled backslashes. Reading them literally produces a
        // path that exists nowhere and a locator that silently finds nothing.
        string library = CreateLibrary("GameDrive", "pl_upward");
        string vdf = WriteLibraryFile((library, HasTf2: true));

        File.ReadAllText(vdf).ShouldContain(@"\\");

        new MapLocator(vdf, Path.Combine(_root, "own-maps"))
            .Find("pl_upward").ShouldNotBeNull();
    }
}
