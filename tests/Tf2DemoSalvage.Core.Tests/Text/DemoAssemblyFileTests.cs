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
    public void Compile_TwoPacketsAndAStop_ReportsThreeCommandsAndTheBytesWritten()
    {
        byte[] demo = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));
        string demoPath = Path.Combine(_folder, "in.dem");
        string text = Path.Combine(_folder, "in.txt");
        File.WriteAllBytes(demoPath, demo);
        DemoAssembly.Export(demoPath, text);

        DemoAssembly.Compile(text, Path.Combine(_folder, "out.dem")).ShouldBe((3, demo.Length));
    }

    [Test]
    public void Compile_MalformedText_LeavesNoOutputAndNoTemp()
    {
        // Text with a valid command but no 'demo' header block: the parse throws only at the end, after
        // the streaming compile has written commands, so a partial file would exist without the guard.
        string text = Path.Combine(_folder, "bad.txt");
        File.WriteAllText(text, "consolecmd 5 data 6869\n");

        Should.Throw<InvalidDataException>(() => DemoAssembly.Compile(text, Path.Combine(_folder, "out.dem")));

        Directory.GetFiles(_folder).ShouldBe([text]);
    }

    [Test]
    public void Compile_IntoAStreamAfterThreeBytes_WritesTheDemoAfterThemAndReportsItsLength()
    {
        // The header is written last, over a placeholder, so it must land where the stream started (B449).
        byte[] demo = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));

        (int commands, long bytes, byte[] written) = CompileToStream(demo);

        (commands, bytes).ShouldBe((3, (long)demo.Length));
        written.ShouldBe([9, 9, 9, .. demo]);
    }

    [Test]
    public void Compile_IntoAStreamFromACutDemo_WritesTheTail()
    {
        byte[] one = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1));
        byte[] two = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));
        byte[] cut = two[..(one.Length - 4 + 3)];

        CompileToStream(cut).Written.ShouldBe([9, 9, 9, .. cut]);
    }

    [Test]
    public void Compile_IntoAStreamWithACommandAfterTheStop_CountsItAndWritesNothingAfterTheStop()
    {
        // Parse then DemoWriter.Write counted it and wrote nothing past dem_stop; the stream does the same.
        byte[] demo = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));

        (int commands, long bytes, byte[] written) = CompileToStream(demo, "consolecmd 5 data 6869\n");

        (commands, bytes).ShouldBe((4, (long)demo.Length));
        written.ShouldBe([9, 9, 9, .. demo]);
    }

    private (int Commands, long Bytes, byte[] Written) CompileToStream(byte[] demo, string appended = "")
    {
        string demoPath = Path.Combine(_folder, "in.dem");
        string text = Path.Combine(_folder, "in.txt");
        File.WriteAllBytes(demoPath, demo);
        DemoAssembly.Export(demoPath, text);
        File.AppendAllText(text, appended);

        using StreamReader reader = new(text);
        using MemoryStream output = new();
        output.Write([9, 9, 9]);
        (int commands, long bytes) = DemoAssembly.Compile(reader, output);

        return (commands, bytes, output.ToArray());
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
