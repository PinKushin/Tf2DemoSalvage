using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Assets;

namespace Tf2DemoSalvage.Core.Tests.Assets;

/// <summary>
/// Reading a game's declared content search path.
/// </summary>
/// <remarks>
/// **Built fixtures here, unusually for this project, and on purpose.** Everything else in this
/// area is measured against shipped files because the risk is misreading a binary layout this
/// project invented an understanding of. <c>gameinfo.txt</c> is text whose meaning is stated in
/// Valve's own comments inside it, so the risk is the opposite one: mishandling a shape that a
/// stock install does not contain. A quoted path, an absent gameinfo, a VPK already named with
/// <c>_dir</c> — none appear in Team Fortress 2's file, and all are things another game or a mod
/// will hand this.
///
/// The stock file gets its own test at the end, so both are covered.
/// </remarks>
public sealed class GameSearchPathTests
{
    private const string Minimal = """
        "GameInfo"
        {
            FileSystem
            {
                SearchPaths
                {
                    game+mod+custom_mod  tf/custom/*
                    game+mod             tf/tf2_textures.vpk
                    game                 |all_source_engine_paths|hl2/hl2_textures.vpk
                    mod+mod_write        |gameinfo_path|.
                }
            }
        }
        """;

    private string _root = string.Empty;
    private string _game = string.Empty;

    [SetUp]
    public void MakeAnInstall()
    {
        _root = Path.Combine(Path.GetTempPath(), "tf2salvage-search-" + Guid.NewGuid().ToString("N"));
        _game = Path.Combine(_root, "tf");

        Directory.CreateDirectory(Path.Combine(_root, "hl2"));
        Directory.CreateDirectory(Path.Combine(_game, "custom"));
    }

    [TearDown]
    public void RemoveTheInstall()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not a test failure.
        }
    }

    [Test]
    public void Parse_AnArchive_GetsTheDirSuffixTheEngineAppends()
    {
        // **The entry says tf2_textures.vpk and the file on disk is tf2_textures_dir.vpk.** Opening
        // the name as written finds nothing, and finding nothing here means every material in that
        // archive silently fails to resolve.
        IReadOnlyList<SearchPathEntry> entries = GameSearchPath.Parse(Minimal, _game);

        entries.ShouldContain(entry =>
            entry.IsArchive && entry.Path.EndsWith("tf2_textures_dir.vpk", StringComparison.Ordinal));
    }

    [Test]
    public void Parse_TheEnginePathsToken_ReachesTheInstallRoot()
    {
        // |all_source_engine_paths| is the folder ABOVE the game, which is how hl2's content is
        // reached. Resolving it against the game folder instead lands on tf/hl2, which does not
        // exist, and the entry is silently dropped.
        IReadOnlyList<SearchPathEntry> entries = GameSearchPath.Parse(Minimal, _game);

        string expected = Path.Combine(_root, "hl2", "hl2_textures_dir.vpk");

        entries.ShouldContain(entry => string.Equals(entry.Path, expected, StringComparison.Ordinal));
    }

    [Test]
    public void Parse_TheGameInfoToken_ResolvesToTheGameFolderItself()
    {
        // "|gameinfo_path|." is how the file names the mod folder, trailing dot and all.
        IReadOnlyList<SearchPathEntry> entries = GameSearchPath.Parse(Minimal, _game);

        entries.ShouldContain(entry =>
            !entry.IsArchive &&
            string.Equals(
                Path.TrimEndingDirectorySeparator(entry.Path), _game, StringComparison.Ordinal));
    }

    [Test]
    public void Parse_TheCustomWildcard_MountsWhatIsActuallyThere()
    {
        // tf/custom/* is a real wildcard, and it mounts both folders and dropped-in VPKs - which is
        // how a mod is distributed. An entry left with the asterisk in it resolves to nothing.
        Directory.CreateDirectory(Path.Combine(_game, "custom", "myhud"));
        File.WriteAllBytes(Path.Combine(_game, "custom", "amod_dir.vpk"), [1, 2, 3]);

        IReadOnlyList<SearchPathEntry> entries = GameSearchPath.Parse(Minimal, _game);

        entries.ShouldContain(entry => !entry.IsArchive && entry.Path.EndsWith("myhud", StringComparison.Ordinal));
        entries.ShouldContain(entry => entry.IsArchive && entry.Path.EndsWith("amod_dir.vpk", StringComparison.Ordinal));
        entries.ShouldAllBe(entry => !entry.Path.Contains('*'));
    }

    [Test]
    public void Parse_AVpkAlreadyNamedWithDir_IsNotSuffixedTwice()
    {
        // A custom VPK dropped into tf/custom is already named in full, and appending _dir again
        // gives amod_dir_dir.vpk - a file that does not exist, failing silently.
        Directory.CreateDirectory(Path.Combine(_game, "custom"));
        File.WriteAllBytes(Path.Combine(_game, "custom", "amod_dir.vpk"), [1]);

        IReadOnlyList<SearchPathEntry> entries = GameSearchPath.Parse(Minimal, _game);

        entries.ShouldAllBe(entry => !entry.Path.Contains("_dir_dir", StringComparison.Ordinal));
    }

    [Test]
    public void Parse_CommentsAndTheClosingBrace_AreNotPaths()
    {
        // Valve's own file is full of comments, and the block ends at a brace. A parser that took
        // every line would mount "//" and "}" as folders.
        const string Commented = """
            SearchPaths
            {
                // game+mod  tf/should_not_appear.vpk
                game+mod     tf/real.vpk   // trailing note
            }
            game             tf/after_the_block.vpk
            """;

        IReadOnlyList<SearchPathEntry> entries = GameSearchPath.Parse(Commented, _game);

        entries.Count.ShouldBe(1);
        entries[0].Path.ShouldEndWith("real_dir.vpk");
    }

    [Test]
    public void Read_AFolderWithNoGameInfo_SaysNothingRatherThanFailing()
    {
        // Being handed a folder that is not a Source game is normal, and the caller has its own
        // fallback. Throwing here would make an ordinary case an error.
        GameSearchPath.Read(_game).ShouldBeEmpty();
    }

    [Test]
    public void Read_APreVpkInstall_YieldsFoldersAndNoArchives()
    {
        // **The second generation of layout, which is the whole reason for reading the file.**
        // TF2's 2008 build declares three folders and no VPKs at all:
        //
        //     Game  |gameinfo_path|.
        //     Game  tf
        //     Game  |all_source_engine_paths|hl2
        //
        // A reader that hardcodes tf2_textures_dir.vpk finds nothing here, and a reader that
        // assumes every entry is an archive suffixes folders into paths that do not exist. The
        // claim being tested is that one reader covers both eras without a special case, and that
        // claim is worth measuring rather than asserting.
        string? legacy = new[]
        {
            Environment.GetEnvironmentVariable("TF2_LEGACY_FOLDER"),
            "F:/tf2-builds/tf2-2008/tf",
            "F:/tf2-builds/tf2-2007/tf",
        }.FirstOrDefault(folder =>
            !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "gameinfo.txt")));

        if (legacy is null)
        {
            Assert.Ignore("No pre-VPK build available; set TF2_LEGACY_FOLDER to run this.");
            return;
        }

        IReadOnlyList<SearchPathEntry> entries = GameSearchPath.Read(legacy);

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(entry => !entry.IsArchive, "a pre-VPK install declares no archives");

        // And the folders it names must be real, since a token resolved wrongly still produces a
        // plausible-looking path.
        entries.Count(entry => Directory.Exists(entry.Path))
            .ShouldBeGreaterThan(1, "its declared folders should exist on disk");
    }

    [Test]
    public void Read_TheRealGameInfo_FindsTheArchivesTheGameActuallyShips()
    {
        // **The stock file, because the fixtures above are this project's idea of the format.**
        // Only the real one proves the parser survives Valve's actual spacing, comments and
        // ordering.
        string? installed = new[]
        {
            Environment.GetEnvironmentVariable("TF2_FOLDER"),
            @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
            @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
        }.FirstOrDefault(folder =>
            !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "gameinfo.txt")));

        if (installed is null)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        IReadOnlyList<SearchPathEntry> entries = GameSearchPath.Read(installed);

        entries.ShouldNotBeEmpty();

        // The two archives this project actually reads content from, plus hl2's - which is the
        // whole reason for reading the file rather than hardcoding tf.
        foreach (string expected in new[]
        {
            "tf2_textures_dir.vpk", "tf2_misc_dir.vpk", "hl2_textures_dir.vpk", "hl2_misc_dir.vpk",
        })
        {
            entries.ShouldContain(
                entry => entry.Path.EndsWith(expected, StringComparison.OrdinalIgnoreCase), expected);
        }

        // And every archive it names must exist, since the point of the _dir suffix is that the
        // written name is not the file name.
        foreach (SearchPathEntry entry in entries.Where(entry => entry.IsArchive))
        {
            File.Exists(entry.Path).ShouldBeTrue(entry.Path);
        }
    }
}
