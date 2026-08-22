using System;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Headers no engine would write — every one must error or be read, never crash.
/// </summary>
/// <remarks>
/// **Owner's direction, 2026-08-21:**
///
/// > *"since we can use synthetic fixtures we should have tests with 'bad' fixtures we should never
/// > see in a demo, and we should make sure those dont crash or bug out our parser by erroring or
/// > rejecting."*
///
/// > *"id also think that the vast majority of great software negative tests extensively and checks
/// > all their error codes."*
///
/// **This is the class of input no corpus can supply.** All 53 demos here were written by the
/// engine, so every one is well-formed by construction; a suite built only on them has never seen a
/// negative length, an unterminated string or a stamp one byte wrong. Those are exactly the inputs
/// that decide whether a parser fails cleanly or corrupts something downstream — and this project's
/// whole subject is files that are damaged, truncated or produced by tools other than the engine.
///
/// **The standard each case is held to, and the first draft of this file got it wrong.** Seven
/// tests were written asserting that `Parse` REJECTS a negative length, a negative count, a
/// negative time and a NaN. They failed, the guards went in, and two existing tests went red:
///
/// > "Not rejected: a malformed value is the parser's caller's problem to judge, and **a salvage
/// > tool should surface what the file claims rather than refuse to open it**."
/// > — `Parse_NonPositiveSignonLength_IsAcceptedAndReported`
///
/// That decision is right and it is the whole premise of the project. A file whose header claims
/// −4 ticks may still hold a perfectly good match, and refusing to open it loses the match to
/// protect nobody. `DemoSurvey.Measure` already demonstrates the alternative: it treats any
/// non-positive tick count as "the header states nothing" and recovers the real extent by walking
/// the command stream. **That is salvage, and it is strictly better than an exception.**
///
/// So the standard is:
///
/// - **The value survives parsing**, verbatim, whatever it is.
/// - **Every consumer of it copes** — with a recovered value where one can be derived, and an
///   honest "not known" where it cannot.
/// - **Never** a crash the caller cannot anticipate, a hang, or a plausible-looking number derived
///   from nonsense. The last is the one to hunt: `ArgumentOutOfRangeException` from thirty frames
///   deep gives a caller nothing to catch, and a NaN duration gives a reader something worse than
///   nothing.
///
/// The tests below therefore assert against the CONSUMERS as well as the parser, which is the
/// stronger question and the one the guards would have hidden.
/// </remarks>
public sealed class DemoHeaderHostileInputTests
{
    /// <summary>A well-formed header, as the ENGINE lays one out — the base every case mutates.</summary>
    /// <remarks>
    /// Field offsets come from the format, not from <c>DemoHeader.Parse</c>: stamp at 0, the two
    /// protocols at 8 and 12, four 260-byte text fields at 16/276/536/796, then time, ticks, frames
    /// and signon length at 1056/1060/1064/1068. <c>DemoWriter</c> writes the same offsets and its
    /// output is played by the 2007 client, which is what makes them the engine's rather than ours.
    /// </remarks>
    private static byte[] Wellformed()
    {
        byte[] header = new byte[DemoHeader.SizeBytes];

        Encoding.ASCII.GetBytes("HL2DEMO\0").CopyTo(header, 0);
        BitConverter.GetBytes(3).CopyTo(header, 8);
        BitConverter.GetBytes(24).CopyTo(header, 12);
        Encoding.UTF8.GetBytes("a server\0").CopyTo(header, 16);
        Encoding.UTF8.GetBytes("a player\0").CopyTo(header, 276);
        Encoding.UTF8.GetBytes("cp_process_final\0").CopyTo(header, 536);
        Encoding.UTF8.GetBytes("tf\0").CopyTo(header, 796);
        BitConverter.GetBytes(1594.695f).CopyTo(header, 1056);
        BitConverter.GetBytes(106_313).CopyTo(header, 1060);
        BitConverter.GetBytes(106_219).CopyTo(header, 1064);
        BitConverter.GetBytes(4_096).CopyTo(header, 1068);

        return header;
    }

    [Test]
    public void Parse_TheWellformedBase_IsReadCorrectly()
    {
        // **The control, and without it every rejection below could be a broken fixture builder.**
        // If this base did not parse, "the mutated one threw" would say nothing about the mutation.
        DemoHeader header = DemoHeader.Parse(Wellformed());

        header.MapName.ShouldBe("cp_process_final");
        header.NetworkProtocol.ShouldBe(24);
        header.PlaybackTicks.ShouldBe(106_313);
        header.SignonLengthBytes.ShouldBe(4_096);
    }

    [Test]
    public void Parse_AStampOneByteWrong_ThrowsInvalidData()
    {
        byte[] header = Wellformed();
        header[6] = (byte)'X';   // HL2DEMX

        InvalidDataException error =
            Should.Throw<InvalidDataException>(() => DemoHeader.Parse(header));

        // The message has to name what was found, or a caller reporting it tells the user nothing.
        error.Message.ShouldContain("HL2DEMO");
    }

    [Test]
    public void Parse_AnEmptyStamp_ThrowsInvalidDataRatherThanReadingOn()
    {
        // All zeroes is what a zero-filled or newly created file looks like, and it is the most
        // likely malformed input a user will actually hand this tool.
        Should.Throw<InvalidDataException>(() => DemoHeader.Parse(new byte[DemoHeader.SizeBytes]));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(7)]
    [TestCase(1071)]
    public void Parse_ShorterThanAHeader_ThrowsEndOfStream(int length)
    {
        // Every boundary that could be read one byte early: empty, one byte, the stamp's own
        // width, and one short of the whole header.
        EndOfStreamException error =
            Should.Throw<EndOfStreamException>(() => DemoHeader.Parse(new byte[length]));

        error.Message.ShouldContain("1072");
    }

    [Test]
    public void Parse_AnUnterminatedTextField_StopsAtTheFieldRatherThanRunningOn()
    {
        // **260 bytes with no NUL anywhere.** The engine always terminates, so no real demo does
        // this — and a reader that scans for a terminator without a bound would run into the next
        // field and return a map name with the game directory glued to it.
        byte[] header = Wellformed();

        for (int index = 0; index < 260; index++)
        {
            header[536 + index] = (byte)'A';
        }

        DemoHeader parsed = DemoHeader.Parse(header);

        parsed.MapName.Length.ShouldBe(260, "the field's own width bounds the read");
        parsed.MapName.ShouldBe(new string('A', 260));

        // The control: the NEXT field is untouched, which is what "did not run on" means. Without
        // this the assertion above is satisfied by a reader that consumed 260 bytes from anywhere.
        parsed.GameDirectory.ShouldBe("tf");
    }

    [Test]
    public void Parse_InvalidUtf8InAName_ProducesReplacementCharactersRatherThanThrowing()
    {
        // **A lone continuation byte is not valid UTF-8**, and player names are attacker-controlled
        // in the sense that matters: they come from a stranger's client. Encoding.UTF8.GetString
        // substitutes U+FFFD rather than throwing, which is the behaviour to WANT here — a demo
        // must still open. Pinned so a later switch to a throwing decoder is a deliberate choice.
        byte[] header = Wellformed();

        header[276] = 0x80;
        header[277] = 0x00;

        DemoHeader parsed = DemoHeader.Parse(header);

        parsed.ClientName.ShouldBe("�");

        // And the rest of the header still reads, which is the point of not throwing.
        parsed.MapName.ShouldBe("cp_process_final");
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void Parse_ANegativeSignonLength_IsSurfacedVerbatim(int length)
    {
        // Surfaced, not rejected — the caller judges it. What matters is that it arrives unchanged,
        // so a caller that DOES want to judge it has the real value rather than a clamped one.
        byte[] header = Wellformed();
        BitConverter.GetBytes(length).CopyTo(header, 1068);

        DemoHeader.Parse(header).SignonLengthBytes.ShouldBe(length);
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void Parse_NegativeTicksOrFrames_AreSurfacedVerbatim(int value)
    {
        byte[] ticks = Wellformed();
        BitConverter.GetBytes(value).CopyTo(ticks, 1060);

        DemoHeader.Parse(ticks).PlaybackTicks.ShouldBe(value);

        byte[] frames = Wellformed();
        BitConverter.GetBytes(value).CopyTo(frames, 1064);

        DemoHeader.Parse(frames).PlaybackFrames.ShouldBe(value);
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void Measure_ANegativeTickCount_IsRecoveredByWalkingTheStream(int declared)
    {
        // **The consumer half, and it is the assertion that matters.** A negative count is not a
        // length; taking it literally would leave the transport disabled exactly as zero does.
        // DemoSurvey treats anything non-positive as "the header states nothing" and derives the
        // real extent from the commands — salvage rather than rejection, which is why the parser
        // does not need to throw.
        byte[] header = Wellformed();
        BitConverter.GetBytes(declared).CopyTo(header, 1060);

        DemoSurvey survey = DemoSurvey.Measure(DemoWriter.Write(DemoHeader.Parse(header), []));

        survey.HeaderStatedLength.ShouldBeFalse(
            "a negative count is not a statement of length");

        survey.LastTick.ShouldBe(0, "and with no commands there is nothing to recover");
    }

    [Test]
    public void Measure_APositiveTickCount_IsTakenFromTheHeader()
    {
        // The control for the pair above: a header that DOES state a length is believed, and not
        // re-derived. Without this, "HeaderStatedLength is false" would be satisfied by a survey
        // that never trusts a header at all.
        DemoSurvey survey =
            DemoSurvey.Measure(DemoWriter.Write(DemoHeader.Parse(Wellformed()), []));

        survey.HeaderStatedLength.ShouldBeTrue();
        survey.LastTick.ShouldBe(106_313);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void Parse_ANonFiniteOrNegativePlaybackTime_IsSurfacedVerbatim(float seconds)
    {
        // **NaN compares false against everything**, so it passes any `> 0` guard and then makes
        // every duration computed from it NaN without one of them failing
        // (docs/memory/numeric-decoding-traps.md).
        //
        // Surfaced rather than rejected, for the same reason as the counts — but unlike the tick
        // count there is currently NO consumer that recovers from it, because nothing derives a
        // duration from this field. If one is ever added it needs its own guard, and this test is
        // where to find out that the input exists.
        byte[] header = Wellformed();
        BitConverter.GetBytes(seconds).CopyTo(header, 1056);

        float parsed = DemoHeader.Parse(header).PlaybackTimeSeconds;

        if (float.IsNaN(seconds))
        {
            float.IsNaN(parsed).ShouldBeTrue("NaN must arrive as NaN, not as zero");
        }
        else
        {
            parsed.ShouldBe(seconds);
        }
    }

    [Test]
    public void Parse_AZeroLengthRecording_IsReadRatherThanRejected()
    {
        // **The line between impossible and merely unusual, and it is the important one here.**
        // Zeroed playback fields are what an unfinalised header looks like, and 152 of 152
        // ESEA-sourced demos are reported to carry them. Rejecting those would refuse a large
        // fraction of the files this project exists to read
        // (docs/memory/a-header-written-last-is-absent.md, decode-must-be-total.md).
        byte[] header = Wellformed();

        BitConverter.GetBytes(0f).CopyTo(header, 1056);
        BitConverter.GetBytes(0).CopyTo(header, 1060);
        BitConverter.GetBytes(0).CopyTo(header, 1064);
        BitConverter.GetBytes(0).CopyTo(header, 1068);

        DemoHeader parsed = DemoHeader.Parse(header);

        parsed.PlaybackTicks.ShouldBe(0);
        parsed.PlaybackFrames.ShouldBe(0);
        parsed.SignonLengthBytes.ShouldBe(0);
    }

    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(int.MaxValue)]
    public void Parse_AnyProtocolNumber_IsReadWithoutJudgement(int protocol)
    {
        // **Deliberately permissive, and the reason is this project's entire premise.** An unknown
        // protocol is the case it was built for; refusing one here would reject a demo before
        // anything had a chance to look at its embedded schema. Whether a protocol can be DECODED
        // is a separate question answered further in, with a better message.
        byte[] header = Wellformed();
        BitConverter.GetBytes(protocol).CopyTo(header, 12);

        DemoHeader.Parse(header).NetworkProtocol.ShouldBe(protocol);
    }
}
