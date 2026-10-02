using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Probe.Probes;

namespace Tf2DemoSalvage.Corpus.Tests.Container;

/// <summary>
/// The decode census reads a demo off its file a command at a time, so its memory is the state and not the file (B449).
/// </summary>
/// <remarks>
/// Synthetic, with offsets laid out by hand (D38): the census lives in the probe, which only this assembly sees. Each
/// command's file offset is the stream's position, carried out of the walk — the array census found it by the
/// command's slice of the whole file, which a streamed command no longer has.
/// </remarks>
public sealed class DemoCensusStreamTests
{
    private const int Prologue = 76 + 8;

    private static readonly byte[] Console = "hi\0"u8.ToArray();

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2ds-census-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void Walk_ASyntheticDemo_CarriesEachCommandsStartAndPayloadOffset()
    {
        // header 1072 | consolecmd: 5 + 4 + 3 = 12 | synctick: 5 | packet: 5 + 84 + 4 + 2 | stop: 1 + 3, the file's end
        string path = WriteDemo();

        List<DemoCensus.Located> walked = [.. DemoCensus.Walk(path)];

        walked.Select(entry => (entry.Command.Type, entry.Start, entry.PayloadAt)).ShouldBe(
        [
            (DemoCommandType.ConsoleCmd, 1072L, 1081L),
            (DemoCommandType.SyncTick, 1084L, 1089L),
            (DemoCommandType.Packet, 1089L, 1182L),
            (DemoCommandType.Stop, 1184L, 1188L),
        ]);
    }

    [Test]
    public void Locate_AByteInsideThePacket_NamesThePacketItsTickAndIndex()
    {
        DemoCensus census = Census(WriteDemo(), out _);

        census.Locate(1100, withOffset: true).ShouldBe("a Packet command at tick 7, command 2, byte 1100");
    }

    [Test]
    public void Locate_AByteInsideTheStop_NamesThePacketBeforeIt()
    {
        // dem_stop names no offset of its own (the array census's rule), so a byte in it falls to the packet.
        DemoCensus census = Census(WriteDemo(), out _);

        census.Locate(1185, withOffset: false).ShouldBe("a Packet command");
    }

    [TestCase(0, 0)]
    [TestCase(70_000, 70_000)]
    [TestCase(131_071, 131_071)]
    public void CommonPrefixLength_StreamsDifferingAtOneByte_IsThatByte(int differs, long expected)
    {
        byte[] first = Pattern(150_000);
        byte[] second = Pattern(150_000);
        second[differs] ^= 0xFF;

        DemoCensus.CommonPrefixLength(new MemoryStream(first), new MemoryStream(second)).ShouldBe(expected);
    }

    [Test]
    public void CommonPrefixLength_OneStreamAPrefixOfTheOther_IsTheShorterLength()
    {
        byte[] longer = Pattern(100_000);

        DemoCensus.CommonPrefixLength(new MemoryStream(longer), new MemoryStream(longer[..70_001])).ShouldBe(70_001);
        DemoCensus.CommonPrefixLength(new MemoryStream(longer[..70_001]), new MemoryStream(longer)).ShouldBe(70_001);
    }

    [Test]
    public void Run_UnderABudgetBelowTheOldFiveTimesTheDemo_RebuildsEveryByte()
    {
        // The array census skipped the assembly above 5x the demo in its budget; streaming it has no such multiple.
        string path = WriteDemo();
        DemoCensus census = Census(path, out CensusRow row);

        census.Run();

        (row.Status("container"), row.Status("assembly"), row["rebuilt_bytes"], row["first_difference"])
            .ShouldBe(("pass", "pass", new FileInfo(path).Length.ToString(System.Globalization.CultureInfo.InvariantCulture), "-1"));
    }

    /// <summary>Bytes that differ from their neighbours, so a comparison off by one position cannot match.</summary>
    private static byte[] Pattern(int length)
    {
        byte[] bytes = new byte[length];

        for (int index = 0; index < length; index++)
        {
            bytes[index] = (byte)((index * 31) + (index >> 8) + 7);
        }

        return bytes;
    }

    private DemoCensus Census(string path, out CensusRow row)
    {
        row = new CensusRow();
        row["path"] = path;
        row["sha256"] = "0123456789abcdef";

        CensusOptions options = new()
        {
            Roots = [path],
            CsvPath = Path.Combine(_folder, "census.csv"),
            SummaryPath = Path.Combine(_folder, "census.md"),
            Stages = new HashSet<string>(["assembly"], StringComparer.Ordinal),
            TempDirectory = _folder,
            BudgetBytes = 1,
        };

        byte[] header = new byte[DemoHeader.SizeBytes];
        using (FileStream stream = File.OpenRead(path))
        {
            stream.ReadExactly(header);
        }

        return new DemoCensus(row, path, new FileInfo(path).Length, DemoHeader.Parse(header), options, TextWriter.Null);
    }

    private string WriteDemo()
    {
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "synthetic",
            ClientName = "synthetic",
            MapName = "cp_process_final",
            GameDirectory = "tf",
            PlaybackTimeSeconds = 1f,
            PlaybackTicks = 66,
            PlaybackFrames = 1,
            SignonLengthBytes = 0,
        };

        DemoCommand[] commands =
        [
            new(DemoCommandType.ConsoleCmd, 3, Console, ReadOnlyMemory<byte>.Empty),
            new(DemoCommandType.SyncTick, 5, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty),
            new(DemoCommandType.Packet, 7, new byte[2], new byte[Prologue]),
            new(DemoCommandType.Stop, 9, ReadOnlyMemory<byte>.Empty),
        ];

        string path = Path.Combine(_folder, "synthetic.dem");
        File.WriteAllBytes(path, DemoWriter.Write(header, commands));
        return path;
    }
}
