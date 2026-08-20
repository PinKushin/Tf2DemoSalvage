using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// What the assembler does with text it cannot compile.
/// </summary>
/// <remarks>
/// **The assembly form is an input, not just an output**, and every input a person can type is one
/// a person can mistype. The compiler exists so a demo can be edited by hand — that is the whole
/// reason for a text form that round-trips to bytes — so a mistyped line has to come back with a
/// message naming what was wrong, rather than producing a demo that differs from what was meant.
///
/// These paths cannot be reached by a written demo, which is what the rest of this project's
/// fixtures build: a demo compiled from an assembly this project itself produced is always
/// well-formed by construction. The failing input has to be authored deliberately, and the message
/// is the observable — a refusal that threw a bare exception would be a correct refusal and a
/// useless one.
/// </remarks>
public sealed class DemoAssemblyRefusalTests
{
    [Test]
    public void Parse_TextWithNoHeaderBlock_SaysTheHeaderIsMissing()
    {
        // Every field of the container header comes from this block, so there is nothing to guess
        // from: a demo with no header is not a demo with default values.
        Refuse("stop 100\n").ShouldContain("no 'demo' header block");
    }

    [Test]
    public void Parse_AHeaderLineWithNoValue_QuotesTheLine()
    {
        // The line is quoted back because a header block is a dozen similar lines and "a header
        // line has no value" would leave the reader to find which.
        Refuse("demo\n  networkprotocol\nend\n").ShouldContain("networkprotocol");
    }

    [Test]
    public void Parse_AHeaderMissingAField_NamesTheFieldRatherThanDefaultingIt()
    {
        // **Absent and zero are different**, and the second is a demo that claims protocol 0.
        // Every field is required for that reason: nothing here has a defensible default.
        Refuse(HeaderWithout("networkprotocol")).ShouldContain("networkprotocol");
    }

    [Test]
    public void Parse_AnUnknownCommandKeyword_NamesTheKeyword()
    {
        // A misspelled command is the likeliest hand-editing mistake, and guessing the nearest
        // match would silently compile a different demo from the one that was written.
        Refuse(Header() + "stopp 100\n").ShouldContain("stopp");
    }

    [Test]
    public void Parse_ACommandWithNoTick_SaysSo()
    {
        // Every command is stamped with the tick it belongs to, and a command with no tick has no
        // position in the stream — there is nowhere to put it.
        Refuse(Header() + "stop\n").ShouldContain("no tick");
    }

    [Test]
    public void Parse_ACommandWhoseTickIsNotANumber_QuotesWhatWasThere()
    {
        // Quoted rather than described, because the usual cause is a typo one character long.
        Refuse(Header() + "stop later\n").ShouldContain("later");
    }

    [Test]
    public void Parse_ACommandWithAnUnknownSection_NamesTheSectionAndTheCommand()
    {
        // A command's payload arrives in named sections — `view`, `bytes`, and so on — so an
        // unrecognised one means the writer and the reader disagree about the format, which is
        // worth failing loudly rather than skipping.
        string message = Refuse(Header() + "stop 100 sideways 00\n");

        message.ShouldContain("sideways");
        message.ShouldContain("stop");
    }

    [Test]
    public void Parse_APacketBlockThatIsNeverClosed_SaysItWasNotClosed()
    {
        // **A truncated file ends mid-block**, so this is the message a half-written edit
        // produces. Treating the end of input as an implicit `end` would compile a packet missing
        // whatever the author had not typed yet.
        Refuse(Header() + "packet 100 view " + new string('0', 128) + " {\n")
            .ShouldContain("not closed");
    }

    [Test]
    public void Parse_WellFormedText_StillCompiles()
    {
        // **The sensitivity control.** Every assertion above is that parsing failed, and a Parse
        // that threw unconditionally would satisfy all of them.
        using StringReader reader = new(Header() + "stop 100\n");

        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = DemoAssembly.Parse(reader);

        header.NetworkProtocol.ShouldBe(24);
        commands.ShouldHaveSingleItem().Type.ShouldBe(DemoCommandType.Stop);
    }

    /// <summary>Parses text expected to fail, returning the message.</summary>
    private static string Refuse(string text)
    {
        using StringReader reader = new(text);

        return Should.Throw<InvalidDataException>(() => DemoAssembly.Parse(reader)).Message;
    }

    /// <summary>The header block's lines, one field each.</summary>
    private static readonly string[] HeaderLines =
    [
        "demo",
        "  demoprotocol 3",
        "  networkprotocol 24",
        "  server \"synthetic\"",
        "  client \"synthetic\"",
        "  map \"cp_process_final\"",
        "  gamedir \"tf\"",
        "  playbacktime 1.0",
        "  playbackticks 66",
        "  playbackframes 66",
        "  signonlength 0",
        "end",
    ];

    /// <summary>A complete, well-formed header block.</summary>
    private static string Header() => string.Join('\n', HeaderLines) + "\n";

    /// <summary>The same block with one field's line removed.</summary>
    /// <remarks>
    /// Built by filtering the lines rather than by a string replacement on the whole block. A
    /// replacement that matches nothing removes nothing, and the test then passes or fails on a
    /// header that is still complete — which is the failure mode that cost a run here: the search
    /// text carried a bare newline and the file had been rewritten with CRLF.
    /// </remarks>
    private static string HeaderWithout(string field)
    {
        List<string> kept = [];
        bool removed = false;

        foreach (string line in HeaderLines)
        {
            if (line.TrimStart().StartsWith(field + " ", StringComparison.Ordinal))
            {
                removed = true;
                continue;
            }

            kept.Add(line);
        }

        removed.ShouldBeTrue($"the header has no '{field}' line to remove");

        return string.Join('\n', kept) + "\n";
    }
}
