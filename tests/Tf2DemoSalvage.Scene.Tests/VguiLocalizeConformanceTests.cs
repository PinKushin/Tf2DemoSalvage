using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CLocalizedStringTable::AddFile` (vgui2.dll 0x180008f80) and `AddString` (0x180009ed0).</summary>
/// <remarks>
/// A file is UTF-16 with its byte order mark or it is ignored. Tokens are read one at a time — quoted with only
/// <c>\n</c> and <c>\"</c> escaped, or up to whitespace — and a key starting <c>//</c> skips the line. Under
/// <c>Tokens</c> each pair is added, later ones replacing; a non-English file skips <c>[english]</c> keys; a following
/// <c>[$…]</c> token decides whether the pair counts. A <c>%language%</c> path loads English, then the language over it.
/// </remarks>
public sealed class VguiLocalizeConformanceTests
{
    [Test]
    public void AddFile_Tokens_AreFoundWithoutCase()
    {
        VguiLocalize table = Load("\"lang\" { \"Language\" \"English\" \"Tokens\" { \"TF_Hello\" \"Hi\" } }");

        table.Find("tf_hello").ShouldBe("Hi");
        table.Find("TF_Missing").ShouldBeNull();
    }

    [Test]
    public void AddFile_Escapes_OnlyNewlineAndQuote()
    {
        VguiLocalize table = Load("\"lang\" { \"Tokens\" { \"A\" \"one\\ntwo \\\"q\\\" back\\\\slash\" } }");

        table.Find("A").ShouldBe("one\ntwo \"q\" back\\\\slash");
    }

    [Test]
    public void AddFile_ACommentKey_SkipsTheRestOfTheLine()
    {
        VguiLocalize table = Load("\"lang\" { \"Tokens\" {\r\n// \"A\" \"no\"\r\n\"B\" \"yes\"\r\n} }");

        table.Find("A").ShouldBeNull();
        table.Find("B").ShouldBe("yes");
    }

    // `AddFile` only takes a token starting "[$" as a conditional, and the platform test reads a '!' only before the
    // '$' — so "[$!X360]" names no platform it knows and is false, where "[$!FRENCH]" goes to the language list, which
    // reads the '!' where it is.
    [TestCase("[$WIN32]", "Win")]
    [TestCase("[$WINDOWS]", "Win")]
    [TestCase("[$X360]", "Plain")]
    [TestCase("[$!X360]", "Plain")]
    [TestCase("[$ENGLISH]", "Win")]
    [TestCase("[$english]", "Win")]
    [TestCase("[$FRENCH]", "Plain")]
    [TestCase("[$!FRENCH]", "Win")]
    [TestCase("[$!ENGLISH]", "Plain")]
    [TestCase("[$SOMETHING]", "Plain")]
    public void AddFile_AConditional_DecidesWhetherThePairCounts(string condition, string expected)
    {
        VguiLocalize table = Load($"\"lang\" {{ \"Tokens\" {{ \"A\" \"Plain\" \"A\" \"Win\" {condition} \"B\" \"b\" }} }}");

        table.Find("A").ShouldBe(expected);
        table.Find("B").ShouldBe("b", "the conditional is consumed, not read as the next key");
    }

    [Test]
    public void AddFile_NotUnicode_IsIgnored()
    {
        VguiLocalize table = new("english");

        table.AddFile("resource/a.txt", _ => Encoding.ASCII.GetBytes("\"lang\" { \"Tokens\" { \"A\" \"x\" } }")).ShouldBeFalse();
        table.Find("A").ShouldBeNull();
    }

    [Test]
    public void AddFile_PercentLanguage_LoadsEnglishThenTheLanguageOverIt()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/tf_english.txt"] = Utf16("\"lang\" { \"Tokens\" { \"A\" \"a\" \"B\" \"b\" } }"),
            ["resource/tf_french.txt"] = Utf16("\"lang\" { \"Tokens\" { \"A\" \"à\" \"[english]A\" \"a\" } }"),
        };
        VguiLocalize table = new("french");

        table.AddFile("resource/tf_%language%.txt", files.GetValueOrDefault).ShouldBeTrue();

        table.Find("A").ShouldBe("à");
        table.Find("B").ShouldBe("b", "English fills what the language lacks");
        table.Find("[english]A").ShouldBeNull("a language file skips its [english] reference keys");
    }

    [Test]
    public void AddFile_AnEnglishFile_KeepsItsEnglishKeys() =>
        Load("\"lang\" { \"Tokens\" { \"[english]A\" \"a\" } }").Find("[english]A").ShouldBe("a");

    private static VguiLocalize Load(string text)
    {
        VguiLocalize table = new("english");

        table.AddFile("resource/test_english.txt", _ => Utf16(text)).ShouldBeTrue();

        return table;
    }

    private static byte[] Utf16(string text) => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)];
}
