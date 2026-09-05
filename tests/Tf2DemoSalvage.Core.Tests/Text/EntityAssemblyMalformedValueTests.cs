using System;
using System.IO;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A trace whose values were mistyped is refused in the file's OWN voice (B344).
/// </summary>
/// <remarks>
/// **The other half of <see cref="EntityAssemblyRefusalTests"/>, which cuts real text rather than
/// mistyping it.** A truncated file and a mistyped one fail in different places: truncation runs the
/// reader off the end and is caught by the four block guards, while a mistyped value is read and
/// rejected — or, before this, read and thrown on by .NET.
///
/// **The exception TYPE is what carries the context, which is why these assert on the message.**
/// `DemoAssembly.cs:533` catches `InvalidDataException` and nothing else, for one purpose: to rethrow
/// it with the offending text attached — `$"{failure.Message} (assembling: {line})"`. So a bare
/// `Enum.Parse` (`ArgumentException`), `int.Parse` (`FormatException`), raw indexer
/// (`ArgumentOutOfRangeException`), raw dictionary lookup (`KeyNotFoundException`) or
/// `Convert.FromHexString` (`FormatException`) all walked straight past the one handler that would
/// have said which line was wrong.
///
/// The asymmetry was visible: a typo in a field NAME reported the file, the line and the field, while
/// a typo in that same line's update type — three tokens earlier — reported
/// `Requested value 'entre' was not found` and named nothing at all.
///
/// **A corpus can never reach any of this.** Every demo is a valid recording, so the round-trip
/// suites only ever hand well-formed text; these refusals exist for the hand-edited trace the
/// readable form was built to allow.
/// </remarks>
public sealed class EntityAssemblyMalformedValueTests
{
    /// <summary>A well-formed header, opening a block.</summary>
    private const string Header =
        "svc_packetentities delta=0 from=- max=64 baseline=0 updatebaseline=0 updated=1 bits=0 {";

    /// <remarks>
    /// **The header's one raw indexer.** `from` was read as `header["from"]` rather than through
    /// `Field`, because it carries `-` for "no delta source" and so cannot be parsed as a number —
    /// and that exemption took the refusal with it.
    /// </remarks>
    [Test]
    public void Build_AHeaderWithNoFromField_NamesTheField()
    {
        Build("svc_packetentities delta=0 max=64 baseline=0 updatebaseline=0 updated=1 bits=0 {", "}")
            .ShouldContain("from");
    }

    /// <remarks>
    /// **An entity line cut short indexes past its own tokens.** `parts[2]` was read with no length
    /// check, so `entity 1` threw `ArgumentOutOfRangeException` — an exception that says nothing
    /// about traces at all.
    /// </remarks>
    [Test]
    public void Build_AnEntityLineWithNoUpdateType_IsRefusedRatherThanIndexingPastTheLine()
    {
        Build(Header, "entity 1", "}").ShouldContain("entity");
    }

    /// <remarks>
    /// **A misspelt update type LISTS the four that exist**, because a person who typed `entre`
    /// cannot recover `ENTER` from `Requested value 'entre' was not found`. The set is small and
    /// closed, so printing it costs a line and ends the search.
    /// </remarks>
    [Test]
    public void Build_AnUnknownUpdateType_NamesTheOnesThatExist()
    {
        string failure = Build(Header, "entity 1 entre class=0 serial=7 ibits=0 {", "}", "}");

        failure.ShouldContain("entre", Case.Sensitive);
        failure.ShouldContain("Enter", Case.Insensitive);
        failure.ShouldContain("Delta", Case.Insensitive);
    }

    /// <remarks>
    /// **A numeric update type outside the enum was not merely a wrong exception type.**
    /// `Enum.Parse` accepts `99` and yields an undefined value, which then decodes against a switch
    /// matching no branch — a wrong answer rather than a refusal. `Enum.IsDefined` makes it total.
    /// </remarks>
    [Test]
    public void Build_AnUpdateTypeOutsideTheEnum_IsRefusedRatherThanDecodedAsUndefined()
    {
        Build(Header, "entity 1 99 class=0 serial=7 ibits=0 {", "}", "}")
            .ShouldContain("99", Case.Sensitive);
    }

    /// <remarks>The entity index, which is the other bare parse on the same line.</remarks>
    [Test]
    public void Build_AnEntityIndexThatIsNotANumber_IsRefused()
    {
        Build(Header, "entity x ENTER class=0 serial=7 ibits=0 {", "}", "}")
            .ShouldContain("x", Case.Sensitive);
    }

    /// <remarks>
    /// **`Field` refused a MISSING field and not an unparseable one** — the `TryGetValue` failed
    /// loudly and the `int.Parse` beneath it did not, so `class=` with its digits deleted took the
    /// other path.
    /// </remarks>
    [Test]
    public void Build_AFieldThatIsNotANumber_NamesTheFieldAndTheValue()
    {
        string failure = Build(Header, "entity 1 ENTER class=x serial=7 ibits=0 {", "}", "}");

        failure.ShouldContain("class");
        failure.ShouldContain("x", Case.Sensitive);
    }

    /// <remarks>
    /// **A slack payload is hexadecimal**, and `Convert.FromHexString` raises its own
    /// `FormatException` — a sixth type past the handler that attaches the line.
    /// </remarks>
    [Test]
    public void Build_ASlackPayloadThatIsNotHexadecimal_IsRefused()
    {
        Build(Header, "slack 8 zz", "}").ShouldContain("zz", Case.Sensitive);
    }

    /// <remarks>
    /// **An effect's delay is the file's only REAL number**, so it took a different parse from every
    /// other field and had its own escape.
    /// </remarks>
    [Test]
    public void BuildEffects_ADelayThatIsNotANumber_IsRefused()
    {
        Should.Throw<InvalidDataException>(
            () => EntityAssembly.BuildEffects(
                Tokens("svc_tempentities count=1 bits=0 {"),
                Lines("effect class=0 delay=soon {", "}", "}"),
                SyntheticPlayer.Decoder()))
            .Message.ShouldContain("delay");
    }

    /// <remarks>
    /// **The refusal that already worked, kept as the CONTROL for the seven above.** It proves the
    /// file's own voice is what the others should have spoken in, rather than a wording these tests
    /// invented — and that `Build` is not simply refusing everything handed to it, since the same
    /// well-formed header and block shape are what every case here differs from by one token.
    /// </remarks>
    [Test]
    public void Build_AnEntityLineMissingAField_NamesTheField()
    {
        Build(Header, "entity 1 ENTER serial=7 ibits=0 {", "}", "}").ShouldContain("class");
    }

    /// <summary>Assembles a block and returns the refusal it produces.</summary>
    private static string Build(string header, params string[] lines) =>
        Should.Throw<InvalidDataException>(
            () => EntityAssembly.Build(
                Tokens(header), Lines(lines), SyntheticPlayer.Decoder()))
            .Message;

    /// <summary>A header line as <c>Assemble</c> would have tokenised it.</summary>
    private static string[] Tokens(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A reader over the given lines, answering null past the end.</summary>
    private static Func<string?> Lines(params string[] lines)
    {
        int next = 0;

        return () => next < lines.Length ? lines[next++] : null;
    }
}
