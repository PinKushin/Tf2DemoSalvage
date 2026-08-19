using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// <c>MessageAssembly</c> both ways — render a message as text, assemble that text back to bits.
/// </summary>
/// <remarks>
/// **The text form is the project's decompiler output, and until now nothing tested it without a
/// demo.** Measured 2026-08-18: `MessageAssembly` carried 122 mutants no `Core.Tests` test reached,
/// the largest single block in the codebase — 40 of them String mutators, which is precisely the
/// class a corpus test is worst at catching. See `docs/MEASUREMENT-PLAN.md`.
///
/// **The property is message → text → bits → message.** Both directions are exercised in one
/// assertion, and neither side is hand-written: `Write` renders, `Assemble` parses that rendering
/// back into bits, and the reader decodes those bits. A field the text form drops, mis-orders or
/// mis-quotes cannot survive the trip. Asserting against hand-written expected text instead would
/// pin today's formatting rather than its meaning, and would break on every cosmetic change.
///
/// **A few tests DO pin the exact text, deliberately and only where the text is the contract** —
/// the keyword a line starts with, which is what a person reads and what `Assemble` dispatches on.
/// Those are the cases where "it round-trips" is not enough, because a message rendered under the
/// wrong keyword still round-trips if the parser makes the same mistake in reverse.
///
/// Strings get the most attention here for a reason recorded in
/// `docs/memory/international-names-are-required.md` and visible in the mutator mix: quoting,
/// escaping and UTF-8 are where a text format actually breaks, and an English corpus of ordinary
/// cvar names exercises none of it.
/// </remarks>
public sealed class MessageAssemblyTests
{
    private const ushort Protocol = 24;

    [Test]
    public void MessageAssembly_NetNop_RendersItsKeywordAndComesBack()
    {
        // The keyword is pinned because Assemble dispatches on it: a message rendered under the
        // wrong name still round-trips if the parser is wrong the same way.
        MessageAssembly.Write(NetEmptyMessage.Instance, Protocol, null).ShouldBe(["net_nop"]);

        TextRoundTrip(NetEmptyMessage.Instance).ShouldBeOfType<NetEmptyMessage>();
    }

    [Test]
    public void MessageAssembly_NetTick_CarriesRawCountersNotSeconds()
    {
        // "The fields are wire values, not display values" - seconds are a division, and a
        // division does not compile back to the same bits.
        NetTickMessage tick = new(120935, 1500, 42);

        MessageAssembly.Write(tick, Protocol, null)![0].ShouldBe("net_tick 120935 1500 42");

        NetTickMessage read = TextRoundTrip(tick).ShouldBeOfType<NetTickMessage>();

        read.Tick.ShouldBe(120935);
        read.HostFrameTimeRaw.ShouldBe((ushort)1500);

        // Three fields of different widths: the third differs from the second so a swap shows.
        read.HostFrameTimeStdDevRaw.ShouldBe((ushort)42);
    }

    [Test]
    public void MessageAssembly_APrintedString_SurvivesSpacesQuotesAndBackslashes()
    {
        // **The case the corpus cannot supply.** Every string in the text form is quoted, so a
        // quote or a backslash inside one has to be escaped and unescaped exactly. Real demos
        // carry ordinary console text; nothing in the corpus contains a quote character, so the
        // escaping path had no test at all before this one.
        const string Awkward = "say \"hello\" \\ end";

        TextRoundTrip(new PrintMessage(Awkward)).ShouldBeOfType<PrintMessage>()
            .Text.ShouldBe(Awkward);
    }

    [Test]
    public void MessageAssembly_AStringWithACarriageReturn_SurvivesTheTextForm()
    {
        // **Player index 13 broke the round trip, and this is not a contrived case.** TF2's
        // `teamplay_point_captured` carries its `cappers` field as a string of raw player-index
        // BYTES, so a capture by the player in slot 13 puts 0x0D inside a string.
        //
        // The writer escaped `\n` and not `\r`, so that byte was emitted literally — and
        // TextReader.ReadLine treats a bare carriage return as a line break. The line split in
        // two, leaving a dangling `"` that assembled to a single empty token and threw
        // "Unknown message ''".
        //
        // Found by round-tripping whole demos: of ten in the corpus, nine are byte-identical and
        // z1800 is the only one where somebody in slot 13 capped a point.
        const string Cappers = "\r";

        TextRoundTrip(new PrintMessage(Cappers)).ShouldBeOfType<PrintMessage>()
            .Text.ShouldBe(Cappers);
    }

    [Test]
    public void MessageAssembly_ANewlineAndACarriageReturn_AreNotConfused()
    {
        // **The control on the fix.** Escaping `\r` by mapping it onto the same escape as `\n`
        // would satisfy the test above and silently corrupt every string containing either — the
        // repair looking like it worked, which is the failure mode this project keeps finding.
        //
        // Asserted in both directions and in one string, so a swap cannot pass.
        const string Both = "line\nreturn\rend";

        PrintMessage read = TextRoundTrip(new PrintMessage(Both)).ShouldBeOfType<PrintMessage>();

        read.Text.ShouldBe(Both);
        read.Text.IndexOf('\n', System.StringComparison.Ordinal).ShouldBe(4);
        read.Text.IndexOf('\r', System.StringComparison.Ordinal).ShouldBe(11);
    }

    [Test]
    public void MessageAssembly_AnEmptyString_DiffersFromAMissingOne()
    {
        // An empty quoted string is a real value and a token the tokenizer has to keep. Dropping
        // it shifts every later token, so this fails loudly rather than subtly if it regresses.
        TextRoundTrip(new PrintMessage(string.Empty)).ShouldBeOfType<PrintMessage>()
            .Text.ShouldBe(string.Empty);
    }

    [Test]
    public void MessageAssembly_AStringCommand_SurvivesNonAscii()
    {
        const string International = "name \"Ω_переменная_名前\"";

        TextRoundTrip(new StringCmdMessage(International)).ShouldBeOfType<StringCmdMessage>()
            .Command.ShouldBe(International);
    }

    [Test]
    public void MessageAssembly_ConVarPairs_KeepTheirOrderAndValues()
    {
        // **A LIST of pairs, not a dictionary, and the test is written to that shape on purpose.**
        // The wire carries an ordered sequence and the decode preserves it, so order is part of
        // what has to round-trip - a dictionary here would quietly make that untestable
        // (docs/memory/round-trip-needs-the-encoding-shape.md). Three pairs rather than one,
        // because a single pair round-trips even when the count field is wrong.
        KeyValuePair<string, string>[] convars =
        [
            new("sv_cheats", "0"),
            new("mp_timelimit", "30"),
            new("tf_bot_count", "12"),
        ];

        SetConVarMessage read =
            TextRoundTrip(new SetConVarMessage(convars)).ShouldBeOfType<SetConVarMessage>();

        read.Variables.Count.ShouldBe(3);
        read.Variables.ShouldBe(convars);
    }

    [Test]
    public void MessageAssembly_ASingleConVar_StillCarriesItsCount()
    {
        SetConVarMessage read = TextRoundTrip(
            new SetConVarMessage([new KeyValuePair<string, string>("sv_gravity", "800")]))
            .ShouldBeOfType<SetConVarMessage>();

        read.Variables.Count.ShouldBe(1);
        read.Variables[0].Key.ShouldBe("sv_gravity");
        read.Variables[0].Value.ShouldBe("800");
    }

    [Test]
    public void MessageAssembly_SignOnState_RendersBothFieldsInOrder()
    {
        MessageAssembly.Write(new SignOnStateMessage(6, 12345), Protocol, null)![0]
            .ShouldBe("net_signonstate 6 12345");

        SignOnStateMessage read =
            TextRoundTrip(new SignOnStateMessage(6, 12345)).ShouldBeOfType<SignOnStateMessage>();

        read.State.ShouldBe(6);
        read.SpawnCount.ShouldBe(12345);
    }

    [Test]
    public void MessageAssembly_SetViewAndPrefetch_RoundTripAtTheirWidestValues()
    {
        TextRoundTrip(new SetViewMessage(2047)).ShouldBeOfType<SetViewMessage>()
            .EntityIndex.ShouldBe(2047);

        TextRoundTrip(new PrefetchMessage(8191)).ShouldBeOfType<PrefetchMessage>()
            .SoundIndex.ShouldBe(8191);
    }

    [Test]
    public void MessageAssembly_FixAngle_RoundTripsThroughTextWithItsFlag()
    {
        const float AngleStep = 360f / 65536f;

        FixAngleMessage read = TextRoundTrip(new FixAngleMessage(true, 45f, 90f, 180f))
            .ShouldBeOfType<FixAngleMessage>();

        read.IsRelative.ShouldBeTrue();
        read.Pitch.ShouldBe(45f, AngleStep);
        read.Yaw.ShouldBe(90f, AngleStep);
        read.Roll.ShouldBe(180f, AngleStep);

        // The control for the flag, which is one bit and therefore invisible in a single case.
        TextRoundTrip(new FixAngleMessage(false, 0f, 0f, 0f)).ShouldBeOfType<FixAngleMessage>()
            .IsRelative.ShouldBeFalse();
    }

    [Test]
    public void MessageAssembly_AFileNameWithSpaces_SurvivesTheTextForm()
    {
        // A path with a space is the ordinary case that breaks a whitespace tokenizer, and map
        // and download paths genuinely contain them.
        FileMessage read = TextRoundTrip(new FileMessage(7u, "maps/my map v2.bsp", true))
            .ShouldBeOfType<FileMessage>();

        read.TransferId.ShouldBe(7u);
        read.FileName.ShouldBe("maps/my map v2.bsp");
        read.IsRequested.ShouldBeTrue();
    }

    [Test]
    public void MessageAssembly_GetCvarValue_RoundTripsItsCookieAtFullWidth()
    {
        // A 32-bit cookie with the top bit set: a signed/unsigned slip survives small values.
        GetCvarValueMessage read = TextRoundTrip(new GetCvarValueMessage(0xDEADBEEF, "sv_cheats"))
            .ShouldBeOfType<GetCvarValueMessage>();

        read.Cookie.ShouldBe(0xDEADBEEF);
        read.CvarName.ShouldBe("sv_cheats");
    }

    [Test]
    public void MessageAssembly_RawBits_GoBackExactlyIncludingThePartialByte()
    {
        // `raw` is the fallback every un-promoted message uses, so it carries most of a demo
        // before the text forms exist. The bit count is what makes it exact: 12 bits is a byte
        // and a half, and writing 16 would corrupt everything after it.
        byte[] bits = [0xAB, 0xC0];

        string line = MessageAssembly.WriteRaw(bits, 12, "svc_something");

        line.ShouldStartWith("raw 12 ABC0");
        line.ShouldContain("svc_something");

        BitWriter writer = new();
        MessageAssembly.Assemble(line, static () => null, writer, State());

        writer.BitCount.ShouldBe(12);
    }

    [Test]
    public void MessageAssembly_CanWrite_AgreesWithWhatWriteProduces()
    {
        // The two are separate switches over the same type list, which is exactly the shape that
        // drifts. A message CanWrite claims but Write returns null for would silently fall back to
        // raw for ever, costing coverage with nothing to show it.
        INetMessage[] messages =
        [
            NetEmptyMessage.Instance,
            new NetTickMessage(1, 2, 3),
            new PrintMessage("x"),
            new StringCmdMessage("x"),
            new SignOnStateMessage(1, 2),
            new SetViewMessage(1),
            new PrefetchMessage(1),
            new FixAngleMessage(false, 0f, 0f, 0f),
            new FileMessage(1u, "x", false),
            new GetCvarValueMessage(1u, "x"),
        ];

        foreach (INetMessage message in messages)
        {
            MessageAssembly.CanWrite(message).ShouldBeTrue(message.GetType().Name);
            MessageAssembly.Write(message, Protocol, null).ShouldNotBeNull(message.GetType().Name);
        }
    }

    /// <summary>Renders a message to text, assembles it back to bits, and decodes it.</summary>
    [Test]
    public void MessageAssembly_VoiceDataWithAnEmptyBody_SurvivesTheTextForm()
    {
        // **A real crash, found by round-tripping a WHOLE demo rather than a prefix.** The writer
        // emits `svc_voicedata {client} {proximity} {bodyBits} {hex}`, and an empty body makes
        // Convert.ToHexString return "" — so the line splits into four tokens and the reader's
        // unguarded `Convert.FromHexString(tokens[4])` threw ArgumentOutOfRangeException.
        //
        // **The corpus round trip could not have caught it**, and the reason is structural rather
        // than bad luck: that suite compares the first 600 commands of each demo, a limit chosen so
        // mutation runs finish overnight, and voice data does not appear until players start
        // talking — which is thousands of commands in. A cap on stream position systematically
        // hides whatever only happens late.
        VoiceDataMessage empty = new(Client: 3, Proximity: 1, BodyBits: 0, Body: default);

        VoiceDataMessage read = TextRoundTrip(empty).ShouldBeOfType<VoiceDataMessage>();

        read.Client.ShouldBe(3);
        read.Proximity.ShouldBe(1);
        read.BodyBits.ShouldBe(0);
        read.Body.Length.ShouldBe(0);
    }

    [Test]
    public void MessageAssembly_VoiceDataWithABody_SurvivesTheTextForm()
    {
        // **The control.** A fix that returned an empty body unconditionally would satisfy the test
        // above and silently drop every real voice packet — which is the failure this project keeps
        // finding, where the repair is worse than the fault because it looks like it worked.
        VoiceDataMessage spoken = new(
            Client: 7, Proximity: 0, BodyBits: 24, Body: new byte[] { 0xDE, 0xAD, 0xBE });

        VoiceDataMessage read = TextRoundTrip(spoken).ShouldBeOfType<VoiceDataMessage>();

        read.Client.ShouldBe(7);
        read.BodyBits.ShouldBe(24);
        read.Body.ToArray().ShouldBe(new byte[] { 0xDE, 0xAD, 0xBE });
    }

    private static INetMessage TextRoundTrip(INetMessage message)
    {
        IReadOnlyList<string> lines = MessageAssembly.Write(message, Protocol, null)
            .ShouldNotBeNull($"{message.GetType().Name} has no text form");

        BitWriter writer = new();
        int next = 1;

        MessageAssembly.Assemble(
            lines[0],
            () => next < lines.Count ? lines[next++] : null,
            writer,
            State());

        IReadOnlyList<INetMessage> read = NetMessageReader.Read(writer.Build(), State()).Messages;

        read.Count.ShouldBeGreaterThan(0, "the message did not survive the text form");

        // Trailing bit padding decodes as net_NOP - see NetMessageWriterTests for why that is the
        // reader being right rather than an extra message.
        foreach (INetMessage trailing in read.Skip(1))
        {
            trailing.ShouldBeOfType<NetEmptyMessage>();
        }

        return read[0];
    }

    private static NetDecodeState State() => new() { NetworkProtocol = Protocol };
}
