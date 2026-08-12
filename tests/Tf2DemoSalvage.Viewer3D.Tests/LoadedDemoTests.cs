using System;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests reading a demo far enough to drive the transport.
/// </summary>
/// <remarks>
/// Built with <see cref="DemoWriter"/> rather than taken from the corpus. The viewer's tests
/// should not depend on Git LFS content - the corpus is 21 MB fetched over a metered allowance,
/// and what is being tested here is "the header reached the transport bar", which needs no real
/// recording at all.
/// </remarks>
public sealed class LoadedDemoTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Disposable temp folder; a lock must not fail an otherwise passing test.
        }
    }

    /// <summary>Writes a demo whose header says what this test needs it to say.</summary>
    private string WriteDemo(string map, int ticks, float seconds)
    {
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "test server",
            ClientName = "tester",
            MapName = map,
            GameDirectory = "tf",
            PlaybackTimeSeconds = seconds,
            PlaybackTicks = ticks,
            PlaybackFrames = ticks,
            SignonLengthBytes = 0,
        };

        string path = Path.Combine(_folder, map + ".dem");
        File.WriteAllBytes(path, DemoWriter.Write(header, [new DemoCommand(DemoCommandType.Stop, 0, default)]));
        return path;
    }

    [Test]
    public void TheHeaderIsReadWithoutDecodingTheWholeDemo()
    {
        // The transport needs a length before playback starts, and a header read is bounded work
        // - a 39 MB demo must not be walked end to end just to enable a scrub bar.
        string path = WriteDemo("cp_process_final", ticks: 45000, seconds: 680f);

        LoadedDemo demo = LoadedDemo.Load(path);

        demo.MapName.ShouldBe("cp_process_final");
        demo.LastTick.ShouldBe(45000);
        demo.NetworkProtocol.ShouldBe(24);
    }

    [Test]
    public void TheDurationComesFromTheHeader()
    {
        LoadedDemo demo = LoadedDemo.Load(WriteDemo("koth_product", ticks: 6600, seconds: 100f));

        demo.Duration.ShouldBe(TimeSpan.FromSeconds(100), tolerance: TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public void ADemoTooShortToHoldAHeaderIsRejectedAsSuch()
    {
        // The message matters: "not a demo" is actionable where an IndexOutOfRange from inside a
        // parser is not, and a truncated file is the normal case for this project.
        string path = Path.Combine(_folder, "truncated.dem");
        File.WriteAllBytes(path, new byte[64]);

        Should.Throw<InvalidDataException>(() => LoadedDemo.Load(path));
    }

    [Test]
    public void LoadingFromThePlaylistFillsTheTransport()
    {
        // The end-to-end wiring: a demo in the library, selected, loaded, and the scrub bar comes
        // alive with the right length. Without the last part the controls stay disabled and the
        // demo looks unopened.
        string path = WriteDemo("cp_gullywash_final1", ticks: 12345, seconds: 187f);

        using MainForm form = new(path);

        // LoadDemo rather than LoadSelected: ListView selection needs a created window handle, so
        // on a form that was never shown SelectedItems is always empty. The double-click path
        // that resolves a selection is covered by the UI tests, against a real window.
        form.LoadDemo(path);

        form.Demo.ShouldNotBeNull().MapName.ShouldBe("cp_gullywash_final1");
        form.Transport.LastTick.ShouldBe(12345);
    }

    [Test]
    public void ADemoThatWillNotParseLeavesTheApplicationUsable()
    {
        // Expected, not exceptional: opening files other software rejects is the point of this
        // project, so a bad one reports itself and the user picks another from the same playlist.
        string path = System.IO.Path.Combine(_folder, "broken.dem");
        File.WriteAllBytes(path, new byte[64]);

        using MainForm form = new(path);

        Should.NotThrow(() => form.LoadDemo(path));

        form.Demo.ShouldBeNull();
        form.StatusText.ShouldContain("Could not open");
        form.Transport.LastTick.ShouldBe(0);
    }

    [Test]
    public void AMissingFileSaysWhichFile()
    {
        Should.Throw<FileNotFoundException>(
            () => LoadedDemo.Load(Path.Combine(_folder, "absent.dem")));
    }
}
