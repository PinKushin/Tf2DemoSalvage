using System;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>A config's bytes as text and back, every byte kept (B823).</summary>
/// <remarks>
/// TF2 reads a config's BYTES (`CUtlBuffer::ParseToken`, tier1/utlbuffer.cpp:1242-1326), so a byte that is no UTF-8
/// character must survive the trip through a .NET string: as the lone surrogate U+DC00 + byte, Python's PEP 383
/// `surrogateescape`. Replacement decoding would turn every such byte into U+FFFD and lose which byte it was.
/// </remarks>
public sealed class Utf8EscapedTests
{
    [Test]
    public void GetString_ALatin1Config_SpellsEachHighByteAlone()
    {
        byte[] latin1 = Encoding.Latin1.GetBytes("say café");

        Utf8Escaped.GetString(latin1).ShouldBe("say caf\uDCE9");
    }

    [Test]
    public void GetString_ValidUtf8_IsItsText() =>
        Utf8Escaped.GetString(Encoding.UTF8.GetBytes("café \U0001F600")).ShouldBe("café \U0001F600");

    /// <remarks>Every byte sequence comes back as itself: all 256 bytes, alone and after a lead byte that wants more.</remarks>
    [Test]
    public void GetBytes_OfGetString_IsTheOriginalBytes()
    {
        byte[] bytes = [.. Enumerable.Range(0, 256).Select(b => (byte)b), 0xC3, 0xE2, 0x82, 0xF0, 0x9F, 0x98, 0xED, 0xB2, 0x80];

        Utf8Escaped.GetBytes(Utf8Escaped.GetString(bytes)).ShouldBe(bytes);
    }

    /// <remarks>
    /// **A count by character agrees with the bytes** (B823 review 1, Sonnet): a spelled lone byte is 1, a pair 4 then 0
    /// even where its second half lies in the spelled range (U+10080 is D800 DC80), and an unpaired surrogate is U+FFFD's 3.
    /// The control is the encoder's own total.
    /// </remarks>
    [Test]
    public void ByteCount_EveryKindOfCharacter_IsItsShareOfTheBytes()
    {
        const string Text = "aé€𐂀\uDCE9\uD800x\uDC00";

        int[] counts = [.. Enumerable.Range(0, Text.Length).Select(at => Utf8Escaped.ByteCount(Text, at))];

        counts.ShouldBe([1, 2, 3, 4, 0, 1, 3, 1, 3]);
        counts.Sum().ShouldBe(Utf8Escaped.GetBytes(Text).Length);
    }

    /// <remarks>A surrogate outside the spelled range, or unpaired high, is what any UTF-8 encoder writes: U+FFFD.</remarks>
    [Test]
    public void GetBytes_ASurrogateNoByteSpells_IsTheReplacementCharacter()
    {
        Utf8Escaped.GetBytes("\uDC41").ShouldBe(new byte[] { 0xEF, 0xBF, 0xBD });
        Utf8Escaped.GetBytes("\uD800x").ShouldBe(new byte[] { 0xEF, 0xBF, 0xBD, 0x78 });
    }
}
