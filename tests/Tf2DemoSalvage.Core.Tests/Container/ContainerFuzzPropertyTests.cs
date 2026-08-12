using System;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Fuzz;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Deterministic layer of the container and Snappy fuzz targets.
/// </summary>
/// <remarks>
/// The cheap half of D8: the same properties the coverage-guided runner drives, exercised by
/// seeded input so they run on every build and in CI without libFuzzer or a Linux toolchain.
///
/// **Random bytes alone would prove very little here.** A demo is refused at the header unless
/// its magic matches, so uniformly random buffers never reach the command reader at all — they
/// test the first eight bytes, repeatedly. So the interesting cases start from a *valid* header
/// and corrupt what follows, which is where lengths and offsets get read from the data and used
/// to index it.
/// </remarks>
public sealed class ContainerFuzzPropertyTests
{
    private const int Seed = 20260811;
    private const int RandomCaseCount = 600;
    private const int MaxTailLength = 256;

    [Test]
    public void Container_SeededRandomBytes_NeverViolatesTheProperty()
    {
        Random random = new(Seed);

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] data = new byte[random.Next(0, 600)];
            random.NextBytes(data);

            Should.NotThrow(() => ContainerFuzzTarget.Consume(data));
        }
    }

    [Test]
    public void Container_ValidHeaderWithCorruptCommands_NeverViolatesTheProperty()
    {
        // The condition that matters. Random bytes die at the magic; these get past it and reach
        // the code that reads a length from the file and then uses it as an offset into the file.
        Random random = new(Seed);
        int parsedAtLeastOnce = 0;

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] tail = new byte[random.Next(0, MaxTailLength + 1)];
            random.NextBytes(tail);

            byte[] demo = [.. ValidHeader(), .. tail];

            int commands = 0;
            Should.NotThrow(() => commands = ContainerFuzzTarget.ConsumeAndCountCommands(demo));
            parsedAtLeastOnce += commands > 0 ? 1 : 0;
        }

        // The harness must be doing work. A target that refused everything at the header would
        // pass this file vacuously - the same failure as a libFuzzer run that executes nothing
        // and still reports green.
        parsedAtLeastOnce.ShouldBeGreaterThan(
            0, "no corrupted demo ever reached the command reader");
    }

    [Test]
    public void Snappy_SeededRandomBytes_NeverViolatesTheProperty()
    {
        Random random = new(Seed);

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] data = new byte[random.Next(1, 256)];
            random.NextBytes(data);

            Should.NotThrow(() => SnappyFuzzTarget.Consume(data));
        }
    }

    [Test]
    public void Snappy_AHugeDeclaredOutputIsRefusedRatherThanAllocated()
    {
        // A Snappy stream opens with a varint of the decompressed size. Believing it is how a
        // few bytes of input turn into a multi-gigabyte allocation, and it is reachable from a
        // demo because string tables arrive compressed off the network.
        byte[] enormous = [0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0x00];

        Should.NotThrow(() => SnappyFuzzTarget.Consume(enormous));
    }

    /// <summary>A header the reader accepts, so corruption lands in the command stream.</summary>
    private static byte[] ValidHeader()
    {
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "fuzz",
            ClientName = "fuzz",
            MapName = "fuzz",
            GameDirectory = "tf",
            PlaybackTimeSeconds = 1f,
            PlaybackTicks = 1,
            PlaybackFrames = 1,
            SignonLengthBytes = 0,
        };

        return DemoWriter.Write(header, [new DemoCommand(DemoCommandType.Stop, 0, default)])
            .Take(DemoHeader.SizeBytes)
            .ToArray();
    }
}
