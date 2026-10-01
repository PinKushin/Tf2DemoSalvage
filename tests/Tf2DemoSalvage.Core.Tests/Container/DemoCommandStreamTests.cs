using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// The command stream read off a <see cref="Stream"/>, one command at a time (B449).
/// </summary>
/// <remarks>
/// **The engine's own shape.** <c>CDemoFile::ReadCmdHeader</c> and <c>ReadRawData</c> read one
/// command off a file handle and nothing more, so the engine's memory is bounded by a command, not
/// by the file. The array reader holds the whole file; a 2 GB idle-server recording needed more
/// than a 6 GiB heap. Every test here compares the stream reader against
/// <see cref="DemoCommandReader.ReadWhole"/>, which is the reference: same commands, same tail,
/// same truncation report.
/// </remarks>
public sealed class DemoCommandStreamTests
{
    private const int CommandHeaderBytes = 5;
    private const int StopBytes = 4;
    private const int PacketPrologueBytes = 76 + 8;

    /// <summary>Command bodies with a cut in every place a file can end.</summary>
    private static IEnumerable<TestCaseData> Streams()
    {
        byte[] whole = WholeDemo();
        byte[] body = whole[DemoHeader.SizeBytes..];
        int beforeStop = body.Length - StopBytes;

        yield return new TestCaseData((object)body).SetName("Read_WholeDemo_MatchesReadWhole");
        yield return new TestCaseData((object)body[..beforeStop]).SetName("Read_NoStop_MatchesReadWhole");
        yield return new TestCaseData((object)body[..(beforeStop + 1)]).SetName("Read_StopWithNoTick_MatchesReadWhole");
        yield return new TestCaseData((object)body[..^1]).SetName("Read_ShortStop_MatchesReadWhole");
        yield return new TestCaseData((object)Array.Empty<byte>()).SetName("Read_Empty_MatchesReadWhole");

        byte[] two = TwoPackets();
        int firstEnd = two.Length - LastPacketBytes(two);

        foreach (int tail in new[] { 2, CommandHeaderBytes - 1, CommandHeaderBytes + 40, CommandHeaderBytes + PacketPrologueBytes + 2, CommandHeaderBytes + PacketPrologueBytes + 4 + 3 })
        {
            yield return new TestCaseData((object)two[DemoHeader.SizeBytes..(firstEnd + tail)])
                .SetName($"Read_CutWith{tail}TailBytes_MatchesReadWhole");
        }

        List<byte> user = [(byte)DemoCommandType.UserCmd, .. BitConverter.GetBytes(7), 0x01, 0x02];
        yield return new TestCaseData((object)user.ToArray()).SetName("Read_CutInsideAUserCmdSequence_MatchesReadWhole");

        List<byte> tables = [(byte)DemoCommandType.DataTables, .. BitConverter.GetBytes(1), .. BitConverter.GetBytes(9999), 0x01, 0x02];
        yield return new TestCaseData((object)tables.ToArray()).SetName("Read_DeclaresMoreThanRemain_MatchesReadWhole");

        List<byte> mixed =
        [
            (byte)DemoCommandType.SyncTick, .. BitConverter.GetBytes(3),
            (byte)DemoCommandType.ConsoleCmd, .. BitConverter.GetBytes(4), .. BitConverter.GetBytes(4), 0x41, 0x42, 0x43, 0x00,
            (byte)DemoCommandType.UserCmd, .. BitConverter.GetBytes(5), .. BitConverter.GetBytes(77), .. BitConverter.GetBytes(2), 0x09, 0x08,
            (byte)DemoCommandType.Stop, 0x06, 0x00, 0x00,
        ];
        yield return new TestCaseData((object)mixed.ToArray()).SetName("Read_EveryContainerShape_MatchesReadWhole");
    }

    [TestCaseSource(nameof(Streams))]
    public void Read_Stream_MatchesReadWhole(byte[] body)
    {
        string? memoryReason = null;
        (IReadOnlyList<DemoCommand> expected, ReadOnlyMemory<byte> expectedTail) =
            DemoCommandReader.ReadWhole(body, reason => memoryReason = reason);

        string? streamReason = null;
        ReadOnlyMemory<byte> tail = ReadOnlyMemory<byte>.Empty;
        using MemoryStream stream = new(body, writable: false);
        List<DemoCommand> actual = [.. DemoCommandReader.Read(stream, (bytes, reason) =>
        {
            tail = bytes;
            streamReason = reason;
        })];

        ShouldMatch(actual, expected);
        tail.ToArray().ShouldBe(expectedTail.ToArray());
        streamReason.ShouldBe(memoryReason);
    }

    [Test]
    public void Read_UnrecognisedCommand_ThrowsLikeTheArrayReader()
    {
        using MemoryStream stream = new([0x63]);

        Should.Throw<InvalidDataException>(() => DemoCommandReader.Read(stream).ToList())
            .Message.ShouldBe(Should.Throw<InvalidDataException>(() => DemoCommandReader.Read(new byte[] { 0x63 }).ToList()).Message);
    }

    [Test]
    public void Read_NegativeLength_ThrowsLikeTheArrayReader()
    {
        byte[] body = [(byte)DemoCommandType.DataTables, .. BitConverter.GetBytes(1), .. BitConverter.GetBytes(-8), 1, 2, 3, 4];
        using MemoryStream stream = new(body);

        Should.Throw<InvalidDataException>(() => DemoCommandReader.Read(stream, (_, _) => { }).ToList())
            .Message.ShouldBe(Should.Throw<InvalidDataException>(() => DemoCommandReader.Read(body, _ => { }).ToList()).Message);
    }

    [Test]
    public void Read_DeclaresMoreThanAnUnseekableStreamHolds_ReportsWithoutAllocatingTheClaim()
    {
        // A corrupt length near int.MaxValue must not become a 2 GB allocation before the short
        // read is noticed: the tail is what is actually there.
        byte[] body = [(byte)DemoCommandType.DataTables, .. BitConverter.GetBytes(1), .. BitConverter.GetBytes(int.MaxValue), 1, 2];
        using CountingStream stream = new(body, seekable: false);

        string? reason = null;
        ReadOnlyMemory<byte> tail = default;
        DemoCommandReader.Read(stream, (bytes, why) => (tail, reason) = (bytes, why)).ShouldBeEmpty();

        tail.ToArray().ShouldBe(body);
        reason.ShouldNotBeNull().ShouldContain(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public void Read_EachCommand_IsYieldedBeforeTheNextIsRead()
    {
        // The property B449 needs: when a command is handed out, the stream has been read to that
        // command's last byte and not one byte further. Nothing past the current command is held.
        byte[] body = ManyPackets(200)[DemoHeader.SizeBytes..];
        using CountingStream stream = new(body, seekable: true);
        long end = 0;
        int seen = 0;

        foreach (DemoCommand command in DemoCommandReader.Read(stream))
        {
            end += Size(command, body.Length - end);
            stream.Position.ShouldBe(end, $"command {seen}");
            seen++;
        }

        seen.ShouldBe(201);
        stream.LargestRead.ShouldBeLessThanOrEqualTo(LargestCommand(body));
    }

    [Test]
    public void File_CutDemo_CountsAndCarriesTheTailLikeReadWhole()
    {
        byte[] two = TwoPackets();
        byte[] cut = two[..(two.Length - 7)];
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, cut);
            (IReadOnlyList<DemoCommand> expected, ReadOnlyMemory<byte> tail) =
                DemoCommandReader.ReadWhole(cut.AsMemory(DemoHeader.SizeBytes));

            DemoCommandCollection file = DemoCommandCollection.Open(path);

            file.Header.NetworkProtocol.ShouldBe(SyntheticDemo.DefaultProtocol);
            file.Count.ShouldBe(expected.Count);
            file.Tail.ToArray().ShouldBe(tail.ToArray());
            file.Truncated.ShouldNotBeNull();
            ShouldMatch([.. file], expected);
            ShouldMatch([.. file], expected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void File_WholeDemo_HasNoTailAndNoTruncation()
    {
        byte[] whole = WholeDemo();
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, whole);

            DemoCommandCollection file = DemoCommandCollection.Open(path);

            file.Tail.IsEmpty.ShouldBeTrue();
            file.Truncated.ShouldBeNull();
            file.Count.ShouldBe(DemoCommandReader.ReadWhole(whole.AsMemory(DemoHeader.SizeBytes)).Commands.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void ShouldMatch(List<DemoCommand> actual, IReadOnlyList<DemoCommand> expected)
    {
        actual.Count.ShouldBe(expected.Count);

        for (int i = 0; i < expected.Count; i++)
        {
            actual[i].Type.ShouldBe(expected[i].Type, $"command {i}");
            actual[i].Tick.ShouldBe(expected[i].Tick, $"command {i}");
            actual[i].Payload.ToArray().ShouldBe(expected[i].Payload.ToArray(), $"command {i}");
            actual[i].Prologue.ToArray().ShouldBe(expected[i].Prologue.ToArray(), $"command {i}");
            actual[i].View.ShouldBe(expected[i].View, $"command {i}");
        }
    }

    /// <summary>The bytes a command took on disk; dem_stop takes whatever remained.</summary>
    private static long Size(DemoCommand command, long remaining) => command.Type switch
    {
        DemoCommandType.Stop => remaining,
        DemoCommandType.SyncTick => CommandHeaderBytes,
        _ => CommandHeaderBytes + command.Prologue.Length + 4 + command.Payload.Length,
    };

    private static long LargestCommand(byte[] body) =>
        DemoCommandReader.Read(body).Max(command => Size(command, StopBytes));

    private static byte[] WholeDemo() => SyntheticDemo.From(
        SyntheticDemo.DefaultProtocol,
        new DemoCommand(DemoCommandType.SyncTick, 0, ReadOnlyMemory<byte>.Empty),
        Packet(1),
        new DemoCommand(DemoCommandType.ConsoleCmd, 2, "say hi\0"u8.ToArray()),
        Packet(3));

    private static byte[] TwoPackets() => SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));

    private static byte[] ManyPackets(int count) => SyntheticDemo.From(
        SyntheticDemo.DefaultProtocol, [.. Enumerable.Range(1, count).Select(Packet)]);

    /// <summary>The second packet plus the stop, so cutting at <c>Length - this</c> leaves one whole packet.</summary>
    private static int LastPacketBytes(byte[] two)
    {
        byte[] one = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1));
        return two.Length - (one.Length - StopBytes);
    }

    private static DemoCommand Packet(int tick) => SyntheticDemo.Packet(
        SyntheticDemo.DefaultProtocol, tick, new PrintMessage("streamed, not held"));

    /// <summary>A read-only stream that records where it is and the largest single read asked of it.</summary>
    private sealed class CountingStream(byte[] bytes, bool seekable) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public long LargestRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => seekable;

        public override bool CanWrite => false;

        public override long Length => seekable ? _inner.Length : throw new NotSupportedException();

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            LargestRead = Math.Max(LargestRead, buffer.Length);
            return _inner.Read(buffer);
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
