using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Probe;
using Tf2DemoSalvage.Probe.Probes;
using Tf2DemoSalvage.Scene;

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

    [Test]
    public void Run_BytesAfterTheStop_FailsTheAssemblyAsATailAfterDemStop()
    {
        // The reader ends at dem_stop, so these bytes are in neither a command nor the tail; the shape is named
        // from the last command the container walk saw, carried, where the array census read the list's last.
        string path = WriteDemo();
        // Zeros: the first is read as dem_stop's absent fourth tick byte, which a non-zero one would push past 2^24.
        File.AppendAllBytes(path, new byte[5]);
        DemoCensus census = Census(path, out CensusRow row);

        census.Run();

        (row["assembly_shape"], row["rebuilt_bytes"], row["assembly_where"])
            .ShouldBe((CensusRow.Shape("tail not carried: bytes after dem_stop"), "1188", "byte 1188"));
    }

    /// <summary>
    /// **A failure opening the install is a recorded row failure, not the end of the census** (review 5): the interp
    /// reading throws as an unreadable config would, and the row says so under the timeline, the one stage that needs
    /// the install, while every other stage still runs. Control: the container passes after it, and no build ran.
    /// </summary>
    [Test]
    public void Run_WhenOpeningTheInstallThrows_FailsTheTimelineAndRunsTheRest()
    {
        string path = WriteDemo();
        int builds = 0;
        DemoCensus census = Census(path, out CensusRow row, "timeline", Megabyte(64));
        census.ReadInterp = () => throw new InvalidDataException("an unreadable config");
        census.Build = (_, _, _) =>
        {
            builds++;
            throw new InvalidOperationException("no build runs when the install did not open");
        };

        census.Run();

        (row.Status("timeline"), row["timeline_detail"], row.Status("container"), builds)
            .ShouldBe(("fail", "InvalidDataException: an unreadable config", "pass", 0));
    }

    /// <summary>
    /// **A warming build that throws leaves none of its time in `timeline_seconds`** (review 5). The reachable case: an
    /// <see cref="OutOfMemoryException"/> under the census heap limit, which the stage records as a budget skip and still
    /// writes its seconds. The warming build spins a full second first, so a column that kept it reads at least 1.0; one
    /// that left it out reads the file read and nothing else. Control: the stage did take the budget skip.
    /// </summary>
    [Test]
    public void Run_WhenTheWarmingBuildRunsOutOfMemory_LeavesItsTimeOutOfTheSeconds()
    {
        string install = Directory.CreateDirectory(Path.Combine(_folder, "tf")).FullName;
        File.WriteAllText(Path.Combine(install, "census-marker.txt"), "here");
        GameContent game = ProbeInstall.Open(install, output: null);

        DemoCensus census = Census(WriteDemo(), out CensusRow row, "timeline", Megabyte(64));
        census.OpenGame = _ => game;
        census.ReadInterp = () => new ClientInterp();
        census.Build = (_, _, _) =>
        {
            long started = Stopwatch.GetTimestamp();

            while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1))
            {
                // Spinning: the time a warming build spends before it runs out.
            }

            // The runtime's own OutOfMemoryException: an array past Array.MaxLength is refused before anything is allocated.
            _ = new byte[int.MaxValue];
            throw new InvalidOperationException("unreachable: the allocation above throws");
        };

        census.Run();

        (row.Status("timeline"), row["timeline_skip"]).ShouldBe(("skip", "budget"), "the control: the warming build threw");
        double.Parse(row["timeline_seconds"], System.Globalization.CultureInfo.InvariantCulture).ShouldBeLessThan(0.5);
    }

    private static long Megabyte(int count) => count * 1048576L;

    private DemoCensus Census(string path, out CensusRow row, string stage = "assembly", long budget = 1)
    {
        row = new CensusRow();
        row["path"] = path;
        row["sha256"] = "0123456789abcdef";

        CensusOptions options = new()
        {
            Roots = [path],
            CsvPath = Path.Combine(_folder, "census.csv"),
            SummaryPath = Path.Combine(_folder, "census.md"),
            Stages = new HashSet<string>([stage], StringComparer.Ordinal),
            TempDirectory = _folder,
            BudgetBytes = budget,
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
