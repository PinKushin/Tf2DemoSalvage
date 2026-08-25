using System;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Reading the lumps a map is kept from, and what happens when one will not read.
/// </summary>
/// <remarks>
/// **The failure behaviour differs per lump on purpose, and each difference was paid for** — so it
/// is the part worth pinning. Terrain costs itself; decals cost themselves and are reported; the
/// surfaces and lighting are NOT guarded, because a map whose faces will not read is not a map and
/// continuing past that produces a black world rather than an error.
///
/// **What is NOT covered here, said rather than left to look covered:** reading a real map. That
/// needs a BSP, which comes from the TF2 install rather than the corpus, so it belongs with the
/// conformance suites that already skip without one. What these cover is the shape of the failure,
/// which no install is needed for and which nothing tested while this lived in `MainForm.ReadMap`
/// (B188, B184).
/// </remarks>
public sealed class MapLevelTests
{
    [Test]
    public void Read_WithBytesThatAreNotAMap_ThrowsRatherThanAnsweringAnEmptyLevel()
    {
        // **The unguarded half, and the distinction that matters.** An empty level would draw a
        // black world and report nothing — the caller cannot tell it from a map that is genuinely
        // dark. Throwing says which.
        Should.Throw<Exception>(() =>
            MapLevel.Read(new byte[64], NullLogger.Instance));
    }

    [Test]
    public void Read_WithNoLogger_Refuses()
    {
        // Every lump failure here is reported rather than swallowed, so a null sink is a caller
        // mistake rather than a quiet mode.
        Should.Throw<ArgumentNullException>(() =>
            MapLevel.Read(new byte[64], assets: null!));
    }

    [Test]
    public void Read_WithAnEmptyFile_DoesNotHang()
    {
        // The degenerate input a truncated download produces. It must fail rather than loop or
        // read past the end — the header is the first thing every lump reader consults.
        Should.Throw<Exception>(() => MapLevel.Read(ReadOnlyMemory<byte>.Empty, NullLogger.Instance));
    }
}
