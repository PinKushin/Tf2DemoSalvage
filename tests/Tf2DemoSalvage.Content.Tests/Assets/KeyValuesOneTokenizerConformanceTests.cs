using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Every reader of Valve's KeyValues text reads it as `KeyValues::ReadToken` and `RecursiveLoadFromBuffer` do (B524).
/// </summary>
/// <remarks>
/// *Read from published source*, `tier1/KeyValues.cpp`: `ReadToken` (:538-618) eats white space and `//` comments
/// before each token, reads a quoted token to the next quote, a brace alone, and a bare token up to white space, a
/// quote or a brace, flagging it conditional when it held `[` then `]`. `RecursiveLoadFromBuffer` keeps a key whose
/// conditional tag fails only to throw it away; `EvaluateConditional` (:2218) answers for a Windows PC here.
///
/// **Four readers had four tokenizers.** The cases below are the shipped shapes each got wrong, counted by
/// `kv-census` before they were changed: a tag in `scripts/game_sounds.txt` read as a key, an empty quoted value
/// that ended a material's parse (200 keys in 191 materials… see RISKS B524), a `360?` key overriding the PC's.
/// </remarks>
public sealed class KeyValuesOneTokenizerConformanceTests
{
    private static List<KeyValuesToken> Tokens(string text)
    {
        KeyValuesTokens tokens = new(text);
        List<KeyValuesToken> all = [];

        while (tokens.Next() is { } token)
        {
            all.Add(token);
        }

        return all;
    }

    [Test]
    public void Next_ACommentWhereATokenWouldStart_IsEatenToTheEndOfTheLine() =>
        Tokens("a // \"b\" \"c\"\n d").Select(token => token.Text).ShouldBe(["a", "d"]);

    /// <remarks>Only `EatCPPComment` before a token looks for `//`; inside a bare token the slashes are text.</remarks>
    [Test]
    public void Next_TwoSlashesInsideABareToken_AreItsText() =>
        Tokens("models//x.mdl next").Select(token => token.Text).ShouldBe(["models//x.mdl", "next"]);

    [Test]
    public void Next_ABareToken_EndsAtAQuoteOrABrace() =>
        Tokens("key\"value\"{x}").ShouldBe(
        [
            new KeyValuesToken("key", false, false),
            new KeyValuesToken("value", true, false),
            new KeyValuesToken("{", false, false),
            new KeyValuesToken("x", false, false),
            new KeyValuesToken("}", false, false),
        ]);

    [Test]
    public void Next_AQuotedBrace_IsTextAndABareOneIsStructure()
    {
        List<KeyValuesToken> tokens = Tokens("\"{\" { \"\"");

        tokens[0].Opens.ShouldBeFalse();
        tokens[1].Opens.ShouldBeTrue();
        tokens[2].ShouldBe(new KeyValuesToken(string.Empty, true, false), "an empty quoted token is a token");
    }

    [TestCase("[$WIN32]", true)]
    [TestCase("[!$X360]", true)]
    [TestCase("a[b]", true)]
    [TestCase("]x[", false)]
    [TestCase("\"[$WIN32]\"", false)]
    public void Next_ABareTokenHoldingABracketPair_IsConditional(string text, bool conditional) =>
        Tokens(text).ShouldHaveSingleItem().Conditional.ShouldBe(conditional);

    private static List<string> Events(string text)
    {
        List<string> events = [];

        KeyValuesReader.Read(Encoding.UTF8.GetBytes(text), (key, value, depth) =>
        {
            events.Add($"{depth}:{key}={value ?? "{}"}");
            return true;
        });

        return events;
    }

    /// <remarks>The shipped `Game.Stalemate`: the tags were reported as the keys `[$WIN32]` and the `.wav`.</remarks>
    [Test]
    public void Read_ATagAfterAValue_KeepsTheKeyThatHoldsHereAndDropsTheOther() =>
        Events("\"Game.Stalemate\" { \"wave\" \"a.mp3\" [$WIN32] \"wave\" \"a.wav\" [$X360] \"pitch\" \"100\" }")
            .ShouldBe(["0:Game.Stalemate={}", "1:wave=a.mp3", "1:pitch=100"]);

    [Test]
    public void Read_ATagBetweenAKeyAndItsBlock_DropsTheWholeBlockWhenItFails() =>
        Events("\"a\" [$X360] { \"x\" \"1\" \"deep\" { \"y\" \"2\" } } \"b\" [$WIN32] { \"z\" \"3\" }")
            .ShouldBe(["0:b={}", "1:z=3"]);

    [Test]
    public void Read_AQuotedClosingBraceAsAValue_DoesNotCloseTheBlock() =>
        Events("\"a\" { \"close\" \"}\" \"after\" \"1\" }").ShouldBe(["0:a={}", "1:close=}", "1:after=1"]);

    /// <remarks>`LoadFromBuffer` (:2260) consumes `#base` and `#include` with the file they name; neither is a key.</remarks>
    [Test]
    public void Read_ABaseOrAnIncludeAtTheTop_IsNotReportedAsAKey() =>
        Events("#base \"other.txt\"\n#include \"more.txt\"\n\"a\" \"1\"").ShouldBe(["0:a=1"]);

    private static byte[] Script(string body) => Encoding.UTF8.GetBytes("PlayerClass\n{\n" + body + "\n}\n");

    /// <remarks>
    /// `pKeyValuesData->GetString( "model" )` asks the file's root for a DIRECT sub-key (`tf_classdata.cpp`); a key of
    /// that name inside a nested block is somebody else's.
    /// </remarks>
    [Test]
    public void Model_OnlyInsideANestedBlock_IsNotTheClasss() =>
        ClassScript.Model(Script("\"AmmoMax\" { \"model\" \"models/wrong.mdl\" }")).ShouldBeNull();

    [Test]
    public void Model_TaggedForAnotherPlatform_IsNotRead() =>
        ClassScript.Model(Script("\"model\" \"models/console.mdl\" [$X360]\n\"model\" \"models/pc.mdl\" [$WIN32]"))
            .ShouldBe("models/pc.mdl");

    private static VmtMaterial Material(string body) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"VertexLitGeneric\"\n{\n" + body + "\n}\n"));

    /// <remarks>
    /// The shipped `debug/env_cubemap_model_translucent` shape: `"$basetexture" ""` ended the parse, and every key
    /// after it was lost.
    /// </remarks>
    [Test]
    public void Parse_AnEmptyQuotedValue_IsAValueAndTheParseGoesOn()
    {
        VmtMaterial material = Material("\"$detail\" \"\"\n\"$envmap\" \"env_cubemap\"");

        material.Value("$detail").ShouldBe(string.Empty);
        material.Value("$envmap").ShouldBe("env_cubemap");
    }

    /// <remarks>
    /// *Read in disassembly*, `CMaterial`'s material-var parse (`materialsystem.dll 0x180008d20`): the text before a
    /// `?` in a key is a test — `lowfill`, `hdr`, `srgb`, `ldr`, `360`, an optional leading `!` — and the key is
    /// skipped when the test fails. `360` never holds on a PC. The shipped `brutal_eddiehair` states `$color2` and then
    /// `360?$color2`: the console's grey overwrote the PC's colour here.
    /// </remarks>
    [Test]
    public void Parse_AKeyForTheConsoleAfterThePlainOne_LeavesThePcsValue() =>
        Material("\"$color2\" \"{26 26 26}\"\n\"360?$color2\" \"[ 0.7 0.7 0.7 ]\"").Value("$color2").ShouldBe("{26 26 26}");

    [TestCase("!360?$x", "1")]
    [TestCase("360?$x", null)]
    [TestCase("srgb?$x", "1")]
    [TestCase("!srgb?$x", null)]
    [TestCase("lowfill?$x", null)]
    [TestCase("!lowfill?$x", "1")]
    [TestCase("hdr?$x", "1")]
    [TestCase("ldr?$x", null)]
    [TestCase("nonsense?$x", null)]
    [TestCase("!nonsense?$x", "1")]
    public void Parse_AConditionedKey_IsKeptOnlyWhenItsTestHoldsOnAnHdrPc(string key, string? expected) =>
        Material($"\"{key}\" \"1\"").Value("$x").ShouldBe(expected);

    /// <remarks>A material is loaded by `KeyValues` first, so its tags are evaluated before any material rule.</remarks>
    [Test]
    public void Parse_AKeyTaggedForTheConsole_IsDropped() =>
        Material("\"$x\" \"console\" [$X360]\n\"$y\" \"1\"").Value("$x").ShouldBeNull();

    [Test]
    public void Parse_ABareKeyRunIntoAQuote_EndsAtTheQuote() =>
        Material("$basetexture\"brick/wall\"").Value("$basetexture").ShouldBe("brick/wall");

    private const string GameInfo = """
        "GameInfo"
        {
        	game	"Team Fortress 2"
        	// SearchPaths { game wrong/first }
        	FileSystem
        	{
        		SteamAppId	440
        		SearchPaths
        		{
        			game+mod			tf/tf2_misc.vpk
        			// game			hl2/commented.vpk
        			game				"hl2/hl2_misc.vpk" [$WIN32]
        			game				hl2/console.vpk [$X360]
        			game_lv				tf/tf2_lv.vpk
        		}
        	}
        }
        """;

    /// <remarks>
    /// `FileSystem_LoadSearchPaths` reads `GameInfo`'s `FileSystem` / `SearchPaths` block through `KeyValues`: a
    /// commented line is nothing, a tag chooses, and a `SearchPaths` named in a comment is not the block.
    /// </remarks>
    [Test]
    public void Parse_AGameInfo_ListsThePathsTheBlockStatesForThisPlatform()
    {
        string folder = OperatingSystem.IsWindows() ? @"C:\game\tf" : "/game/tf";

        GameSearchPath.Parse(GameInfo, folder)
            .Select(entry => System.IO.Path.GetFileName(entry.Path))
            .ShouldBe(["tf2_misc_dir.vpk", "hl2_misc_dir.vpk"]);
    }
}
