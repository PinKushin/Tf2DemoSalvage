using System;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// The file-to-file export and compile that the CLI's <c>-a</c>/<c>-c</c> and the viewer's
/// Export/Compile buttons both call, so neither can drift from the other.
/// </summary>
public sealed class DemoAssemblyFileTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2ds-asmfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void ExportThenCompile_WholeDemo_ReproducesBytes()
    {
        byte[] demo = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));

        RoundTrip(demo).ShouldBe(demo);
    }

    [Test]
    public void ExportThenCompile_CutDemoWithATail_ReproducesBytes()
    {
        // Two packets, no dem_stop, the second cut three bytes in: the tail must come back (B448).
        byte[] one = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1));
        byte[] two = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));
        byte[] cut = two[..(one.Length - 4 + 3)];

        RoundTrip(cut).ShouldBe(cut);
    }

    [Test]
    public void Export_TwoPacketsAndAStop_ReportsThreeCommands()
    {
        string demoPath = Path.Combine(_folder, "in.dem");
        File.WriteAllBytes(demoPath, SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2)));

        DemoAssembly.Export(demoPath, Path.Combine(_folder, "out.txt")).ShouldBe(3);
    }

    [Test]
    public void Compile_TwoPacketsAndAStop_ReportsTheBytesWritten()
    {
        byte[] demo = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));
        string demoPath = Path.Combine(_folder, "in.dem");
        string text = Path.Combine(_folder, "in.txt");
        File.WriteAllBytes(demoPath, demo);
        DemoAssembly.Export(demoPath, text);

        DemoAssembly.Compile(text, Path.Combine(_folder, "out.dem")).ShouldBe(demo.Length);
    }

    private byte[] RoundTrip(byte[] demo)
    {
        string demoPath = Path.Combine(_folder, "in.dem");
        string text = Path.Combine(_folder, "in.txt");
        string rebuilt = Path.Combine(_folder, "out.dem");
        File.WriteAllBytes(demoPath, demo);

        DemoAssembly.Export(demoPath, text);
        DemoAssembly.Compile(text, rebuilt);

        return File.ReadAllBytes(rebuilt);
    }

    private static DemoCommand Packet(int tick) => SyntheticDemo.Packet(
        SyntheticDemo.DefaultProtocol, tick, new PrintMessage("exported and compiled"));
}
