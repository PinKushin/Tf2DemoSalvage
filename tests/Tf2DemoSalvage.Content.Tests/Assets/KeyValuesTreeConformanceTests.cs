using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A KeyValues tree built as `KeyValues::LoadFromBuffer` builds it — the loader every `.res` file goes through.</summary>
/// <remarks>
/// `src/tier1/KeyValues.cpp`: `ReadToken` (:538), `LoadFromBuffer` (:2260), `RecursiveLoadFromBuffer`, `EvaluateConditional`
/// (:2218), `ParseIncludedKeys` (:2068), `AppendIncludedKeys` (:2049) and `RecursiveMergeKeyValues` (:2149). A HUD is a pile
/// of these files layered by `#base`, and a customised one overrides stock by exactly these merge rules.
/// </remarks>
public sealed class KeyValuesTreeConformanceTests
{
    [Test]
    public void Load_NestedBlocksAndDuplicateKeys_KeepsEveryKeyInOrder()
    {
        KeyValuesTree root = Load("""
            "Root"
            {
                "a"     "1"
                "a"     "2"
                Sub { x 5 }
            }
            """);

        root.Name.ShouldBe("Root");
        root.Children.Select(child => child.Name).ShouldBe(["a", "a", "Sub"]);
        root.Children[1].Value.ShouldBe("2");
        root.Find("sub")!.Find("X")!.Value.ShouldBe("5");
    }

    [Test]
    public void Load_ACommentAndAQuotedBrace_AreNotStructure()
    {
        KeyValuesTree root = Load("""
            Root // a comment { not a block
            {
                "text"  "{braces}" // trailing
            }
            """);

        root.Find("text")!.Value.ShouldBe("{braces}");
    }

    [TestCase("[$WIN32]", true)]
    [TestCase("[$WINDOWS]", true)]
    [TestCase("[!$X360]", true)]
    [TestCase("[$X360]", false)]
    [TestCase("[$OSX]", false)]
    [TestCase("[$LINUX]", false)]
    [TestCase("[$POSIX]", false)]
    [TestCase("[$DECK]", false)]
    [TestCase("[!$WIN32]", false)]
    public void Load_AValueConditional_KeepsTheKeyOnlyWhenItHoldsOnAPc(string condition, bool kept)
    {
        KeyValuesTree root = Load($$"""Root { "k" "v" {{condition}} "after" "1" }""");

        (root.Find("k") is not null).ShouldBe(kept);
        root.Find("after").ShouldNotBeNull("the key after a dropped one still reads");
    }

    [Test]
    public void Load_ABlockConditional_DropsTheBlockWhenFalse()
    {
        KeyValuesTree root = Load("""Root { "Pc" [$WIN32] { a 1 } "Console" [$X360] { a 2 } }""");

        root.Find("Pc").ShouldNotBeNull();
        root.Find("Console").ShouldBeNull();
    }

    [Test]
    public void Load_ABaseFile_MergesUnderTheFileWhichWins()
    {
        Dictionary<string, string> files = new()
        {
            ["resource/ui/base.res"] = """
                "Base"
                {
                    "Panel" { "xpos" "10" "ypos" "20" }
                    "Only" { "wide" "5" }
                }
                """,
        };

        KeyValuesTree root = Load(
            """
            #base "base.res"
            "Hud"
            {
                "Panel" { "xpos" "99" }
            }
            """,
            "resource/ui/hud.res",
            files);

        KeyValuesTree panel = root.Find("Panel")!;

        panel.Find("xpos")!.Value.ShouldBe("99", "the file's own value wins");
        panel.Find("ypos")!.Value.ShouldBe("20", "a key only the base has is merged in");
        root.Find("Only")!.Find("wide")!.Value.ShouldBe("5");
    }

    [Test]
    public void Load_ABaseMatch_IsCaseSensitive()
    {
        // `RecursiveMergeKeyValues` matches with `Q_strcmp`, so "panel" in the base is a different key from "Panel".
        Dictionary<string, string> files = new() { ["base.res"] = """B { "panel" { x 1 } }""" };

        KeyValuesTree root = Load("""#base "base.res" R { "Panel" { y 2 } }""", "hud.res", files);

        root.Children.Select(child => child.Name).ShouldBe(["Panel", "panel"]);
    }

    [Test]
    public void Load_AnInclude_AppendsItsRootAfterThisFilesRoots()
    {
        Dictionary<string, string> files = new() { ["dir/extra.res"] = """Extra { e 1 }""" };

        IReadOnlyList<KeyValuesTree> roots = KeyValuesTree.LoadAll(
            Encoding.UTF8.GetBytes("""#include "extra.res" First { f 1 } Second { s 2 }"""),
            "dir/main.res",
            path => files.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null);

        roots.Select(root => root.Name).ShouldBe(["First", "Second", "Extra"]);
    }

    private static KeyValuesTree Load(string text, string resource = "test.res", Dictionary<string, string>? files = null) =>
        KeyValuesTree.Load(
            Encoding.UTF8.GetBytes(text),
            resource,
            path => files is not null && files.TryGetValue(path, out string? found) ? Encoding.UTF8.GetBytes(found) : null);
}
