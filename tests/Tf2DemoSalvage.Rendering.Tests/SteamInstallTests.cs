using System;
using System.Collections.Generic;
using System.IO;


namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Tests finding Steam, and through it TF2, the way Steam records its own location.</summary>
/// <remarks>
/// **The gap these close (2026-10-02):** the viewer only ever looked under Program Files (x86), so a
/// tester with Steam on <c>D:\Steam</c> was told TF2 was not installed. Steam writes its own path to
/// the registry; these feed that value through the seam rather than reading the real registry,
/// whose contents are a property of the machine and not of the code.
/// </remarks>
[Platform("Win", Reason = "Steam's registry keys and backslash paths exist only on Windows")]
public sealed class SteamInstallTests
{
    private const string UserKey = @"HKEY_CURRENT_USER\Software\Valve\Steam";
    private const string MachineKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam";

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
        catch (IOException failure)
        {
            // Disposable temp tree; a lock must not fail a passing test, but it is reported.
            TestContext.Out.WriteLine("temp tree not removed: " + failure.Message);
        }
    }

    private static Func<string, string, string?> Registry(Dictionary<(string, string), string> values) =>
        (key, name) => values.TryGetValue((key, name), out string? value) ? value : null;

    [Test]
    public void Root_WithUserSteamPathOnAnotherDrive_IsThatPathWithBackslashes()
    {
        SteamInstall steam = new(
            Registry(new() { [(UserKey, "SteamPath")] = "d:/steam" }), @"C:\Program Files (x86)", null, _ => true);

        steam.Root.ShouldBe(@"d:\steam");
        steam.LibraryFile.ShouldBe(@"d:\steam\steamapps\libraryfolders.vdf");
    }

    [Test]
    public void Root_WithOnlyMachineInstallPath_IsThatPath()
    {
        SteamInstall steam = new(
            Registry(new() { [(MachineKey, "InstallPath")] = @"E:\Steam" }), @"C:\Program Files (x86)", null, _ => true);

        steam.Root.ShouldBe(@"E:\Steam");
    }

    [Test]
    public void Root_WithNoRegistryValues_IsProgramFilesSteam()
    {
        SteamInstall steam = new(Registry([]), @"C:\Program Files (x86)", null);

        steam.Root.ShouldBe(@"C:\Program Files (x86)\Steam");
    }

    /// <remarks>
    /// **Stale registry (owner, 2026-10-02, D203):** a SteamPath left behind by an uninstalled or
    /// moved Steam must not win over a source that has a library list. The seam answers which
    /// <c>libraryfolders.vdf</c> exists, so nothing touches the disk.
    /// </remarks>
    [Test]
    public void Root_WithUserSteamPathLackingLibraryFile_IsMachineInstallPath()
    {
        SteamInstall steam = new(
            Registry(new()
            {
                [(UserKey, "SteamPath")] = "d:/gone",
                [(MachineKey, "InstallPath")] = @"E:\Steam",
            }),
            @"C:\Program Files (x86)",
            null,
            path => path == @"E:\Steam\steamapps\libraryfolders.vdf");

        steam.Root.ShouldBe(@"E:\Steam");
    }

    [Test]
    public void Root_WithBothRegistryPathsLackingLibraryFile_IsProgramFilesSteam()
    {
        SteamInstall steam = new(
            Registry(new()
            {
                [(UserKey, "SteamPath")] = "d:/gone",
                [(MachineKey, "InstallPath")] = @"E:\Gone",
            }),
            @"C:\Program Files (x86)",
            null,
            path => path == @"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf");

        steam.Root.ShouldBe(@"C:\Program Files (x86)\Steam");
    }

    [Test]
    public void Root_WithNoSourceHavingLibraryFile_IsTheFirstNamedSource()
    {
        // Nothing valid anywhere: the answer is still the first source named, so a message about a
        // missing install points at the folder Steam itself recorded.
        SteamInstall steam = new(
            Registry(new() { [(UserKey, "SteamPath")] = "d:/gone" }), @"C:\Program Files (x86)", null, _ => false);

        steam.Root.ShouldBe(@"d:\gone");
    }

    [Test]
    public void GameFolder_WithOverrideSet_IsTheOverrideEvenWhenSteamHasTf2()
    {
        string steamRoot = SteamWithTf2InSecondLibrary(out _);
        string chosen = Directory.CreateDirectory(Path.Combine(_root, "chosen", "tf")).FullName;
        SteamInstall steam = new(
            Registry(new() { [(UserKey, "SteamPath")] = steamRoot }), null, chosen);

        steam.GameFolder().ShouldBe(chosen);
    }

    [Test]
    public void GameFolder_WithTf2InSecondLibrary_IsThatLibrarysTfFolder()
    {
        string steamRoot = SteamWithTf2InSecondLibrary(out string expected);
        SteamInstall steam = new(
            Registry(new() { [(UserKey, "SteamPath")] = steamRoot.Replace('\\', '/') }), null, null);

        steam.GameFolder().ShouldBe(expected);
    }

    // **D210: a folder the user picked is validated before it is saved.** The recogniser is the one
    // `GameInstall` uses — `tf2_textures_dir.vpk` — because Steam keeps a library directory for a game
    // that has been uninstalled, so the folder merely existing proves nothing.
    [Test]
    public void AsGameFolder_TfFolderHoldingTheTexturesVpk_IsThatFolder()
    {
        SteamInstall.AsGameFolder(@"G:\TF2\tf", path => path == @"G:\TF2\tf\tf2_textures_dir.vpk")
            .ShouldBe(@"G:\TF2\tf");
    }

    [Test]
    public void AsGameFolder_TheTeamFortress2FolderAboveIt_IsItsTfFolder()
    {
        // The folder a person sees in Steam's "Browse local files" is the one above tf/; taking it
        // rather than refusing it is the picker meeting the user where the obvious click lands.
        SteamInstall.AsGameFolder(@"G:\TF2", path => path == @"G:\TF2\tf\tf2_textures_dir.vpk")
            .ShouldBe(@"G:\TF2\tf");
    }

    [Test]
    public void AsGameFolder_AFolderWithoutTheVpk_IsNull()
    {
        SteamInstall.AsGameFolder(@"G:\Pictures", _ => false).ShouldBeNull();
    }

    [Test]
    public void ChosenFolder_EnvironmentAndConfigBothSet_IsTheEnvironment()
    {
        // TF2_FOLDER stays the override for scripts and CI (D210); the cfg is the user's path.
        SteamInstall.ChosenFolder(@"E:\ci\tf", @"G:\TF2\tf").ShouldBe(@"E:\ci\tf");
    }

    [Test]
    public void ChosenFolder_OnlyConfigSet_IsTheConfig()
    {
        SteamInstall.ChosenFolder(null, @"G:\TF2\tf").ShouldBe(@"G:\TF2\tf");
        SteamInstall.ChosenFolder(string.Empty, @"G:\TF2\tf").ShouldBe(@"G:\TF2\tf");
    }

    /// <summary>A Steam root whose first library is empty and whose second holds TF2.</summary>
    private string SteamWithTf2InSecondLibrary(out string tfFolder)
    {
        string steamRoot = Path.Combine(_root, "Steam");
        string empty = Path.Combine(_root, "LibA");
        string second = Path.Combine(_root, "LibB");
        tfFolder = Directory.CreateDirectory(
            Path.Combine(second, "steamapps", "common", "Team Fortress 2", "tf")).FullName;
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        File.WriteAllText(
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n" +
            "\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + empty.Replace(@"\", @"\\", StringComparison.Ordinal) + "\"\n" +
            "\t\t\"apps\"\n\t\t{\n\t\t\t\"228980\"\t\t\"1\"\n\t\t}\n\t}\n" +
            "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + second.Replace(@"\", @"\\", StringComparison.Ordinal) + "\"\n" +
            "\t\t\"apps\"\n\t\t{\n\t\t\t\"440\"\t\t\"1\"\n\t\t}\n\t}\n}\n");
        return steamRoot;
    }
}
