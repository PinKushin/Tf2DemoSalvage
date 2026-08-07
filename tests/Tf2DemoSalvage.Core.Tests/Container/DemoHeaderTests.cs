using System;
using System.IO;
using System.Text;
using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Tests for the fixed 1072-byte demo header, written before the implementation (D6).
/// Layout is CONFIRMED against three corpus demos - see docs/SPEC.md.
/// </summary>
public sealed class DemoHeaderTests
{
    private const int HeaderBytes = 1072;

    /// <summary>
    /// Builds a header with the documented field offsets. Text fields are fixed-width and
    /// NUL-padded, deliberately with trailing garbage after the NUL so tests prove the reader
    /// truncates at the terminator rather than trusting the rest of the field to be zero.
    /// </summary>
    private static byte[] BuildHeader(
        string stamp = "HL2DEMO",
        int demoProtocol = 3,
        int networkProtocol = 24,
        string server = "serveme.tf (#1055422)",
        string client = "SourceTV Demo",
        string map = "cp_process_final",
        string gameDirectory = "tf",
        float playbackTime = 1814.0249f,
        int ticks = 120935,
        int frames = 120913,
        int signonLength = 850953,
        bool padWithGarbage = true)
    {
        byte[] buffer = new byte[HeaderBytes];
        if (padWithGarbage)
        {
            Array.Fill(buffer, (byte)0xCC);
        }

        void WriteText(int offset, int width, string value)
        {
            Span<byte> field = buffer.AsSpan(offset, width);
            field.Clear();
            int written = Encoding.ASCII.GetBytes(value, field);
            if (padWithGarbage && written + 1 < width)
            {
                // Garbage *after* the NUL terminator, which a correct reader must ignore.
                field[(written + 1)..].Fill(0xCC);
            }
        }

        WriteText(0, 8, stamp);
        BitConverter.TryWriteBytes(buffer.AsSpan(8), demoProtocol);
        BitConverter.TryWriteBytes(buffer.AsSpan(12), networkProtocol);
        WriteText(16, 260, server);
        WriteText(276, 260, client);
        WriteText(536, 260, map);
        WriteText(796, 260, gameDirectory);
        BitConverter.TryWriteBytes(buffer.AsSpan(1056), playbackTime);
        BitConverter.TryWriteBytes(buffer.AsSpan(1060), ticks);
        BitConverter.TryWriteBytes(buffer.AsSpan(1064), frames);
        BitConverter.TryWriteBytes(buffer.AsSpan(1068), signonLength);
        return buffer;
    }

    [Fact]
    public void Parse_WellFormedHeader_ReadsEveryField()
    {
        DemoHeader header = DemoHeader.Parse(BuildHeader());

        header.DemoProtocol.ShouldBe(3);
        header.NetworkProtocol.ShouldBe(24);
        header.ServerName.ShouldBe("serveme.tf (#1055422)");
        header.ClientName.ShouldBe("SourceTV Demo");
        header.MapName.ShouldBe("cp_process_final");
        header.GameDirectory.ShouldBe("tf");
        header.PlaybackTimeSeconds.ShouldBe(1814.0249f, 0.001f);
        header.PlaybackTicks.ShouldBe(120935);
        header.PlaybackFrames.ShouldBe(120913);
        header.SignonLengthBytes.ShouldBe(850953);
    }

    [Fact]
    public void Parse_TextFieldsWithGarbageAfterTerminator_TruncatesAtTheNul()
    {
        // The fixture writes 0xCC after every NUL. A reader that decodes the whole 260 bytes
        // would produce trailing junk; one that stops at the terminator will not.
        DemoHeader header = DemoHeader.Parse(BuildHeader(map: "koth_harvest_final"));

        header.MapName.ShouldBe("koth_harvest_final");
        header.GameDirectory.ShouldBe("tf");
    }

    [Fact]
    public void Parse_TextFieldFillingItsEntireWidth_DoesNotOverrun()
    {
        string longMap = new('m', 259);

        DemoHeader header = DemoHeader.Parse(BuildHeader(map: longMap));

        header.MapName.ShouldBe(longMap);
    }

    [Fact]
    public void Parse_EmptyTextField_ReturnsEmptyNotTrailingGarbage()
    {
        // A NUL at index 0 means an empty field. Found by a surviving mutant: with the
        // terminator search written as `<= 0`, this returns 260 bytes of padding instead of "".
        DemoHeader header = DemoHeader.Parse(BuildHeader(server: "", gameDirectory: ""));

        header.ServerName.ShouldBe(string.Empty);
        header.GameDirectory.ShouldBe(string.Empty);
    }

    [Fact]
    public void Parse_WrongStamp_ThrowsInvalidData()
    {
        InvalidDataException exception = Should.Throw<InvalidDataException>(
            () => DemoHeader.Parse(BuildHeader(stamp: "NOTADEMO")));

        exception.Message.ShouldContain("HL2DEMO");
    }

    [Fact]
    public void Parse_BufferShorterThanTheHeader_ThrowsEndOfStream()
    {
        byte[] truncated = BuildHeader();

        Should.Throw<EndOfStreamException>(() => DemoHeader.Parse(truncated.AsSpan(0, 1071)));
    }

    [Fact]
    public void HeaderSizeBytes_MatchesTheDocumentedLayout()
    {
        DemoHeader.SizeBytes.ShouldBe(1072);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Parse_NonPositiveSignonLength_IsAcceptedAndReported(int signonLength)
    {
        // Not rejected: a malformed value is the parser's caller's problem to judge, and a
        // salvage tool should surface what the file claims rather than refuse to open it.
        DemoHeader header = DemoHeader.Parse(BuildHeader(signonLength: signonLength));

        header.SignonLengthBytes.ShouldBe(signonLength);
    }
}
