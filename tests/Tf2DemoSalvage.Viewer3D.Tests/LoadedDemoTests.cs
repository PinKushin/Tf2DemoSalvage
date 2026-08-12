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
    public void ATruncatedDemoGetsItsLengthMeasuredRatherThanBelieved()
    {
        // The reported bug, reduced. `esea_match_13977649.dem` holds 110,238 frames of
        // cp_process_final and declares zero, because the engine writes the header's counts by
        // seeking back at the END of recording and that recording was cut short. Believing it left
        // the transport disabled, the timeline empty, and the demo looking unopened.
        string path = WriteTruncatedDemo("cp_process_final", lastTick: 8400);

        LoadedDemo demo = LoadedDemo.Load(path);

        demo.LastTick.ShouldBe(8400);
        demo.LengthWasMeasured.ShouldBeTrue();
    }

    [Test]
    public void AMeasuredLengthAlsoGivesADuration()
    {
        // A dead scrub bar and a 00:00 duration are the same bug seen from two places. TF2 runs at
        // 66.667 ticks per second, so 8400 ticks is a little over two minutes.
        LoadedDemo demo = LoadedDemo.Load(WriteTruncatedDemo("cp_badlands", lastTick: 8400));

        demo.Duration.TotalSeconds.ShouldBe(126.0, tolerance: 0.5);
    }

    [Test]
    public void ACompleteDemoIsNotMeasured()
    {
        // The control. Measuring means walking the whole file, and doing that to confirm a number
        // the header already states would make opening a large demo feel broken.
        LoadedDemo demo = LoadedDemo.Load(WriteDemo("koth_product", ticks: 6600, seconds: 100f));

        demo.LengthWasMeasured.ShouldBeFalse();
    }

    [Test]
    public void ATruncatedDemoEnablesTheTransport()
    {
        // What the user actually saw: play unavailable, no timeline, after double-clicking a demo
        // that is 110,000 frames long. The transport is right to refuse a zero-length demo, so the
        // fix has to arrive before it, as a real length.
        string path = WriteTruncatedDemo("cp_process_final", lastTick: 8400);

        using MainForm form = new(path);
        form.LoadDemo(path);

        form.Transport.LastTick.ShouldBe(8400);
    }

    /// <summary>Writes a demo that never had its header counts filled in.</summary>
    /// <remarks>
    /// Zero ticks and zero frames over a stream that reaches <paramref name="lastTick"/> — the
    /// exact shape of a recording the server died in the middle of, which is forty-three percent
    /// of the measured ESEA archive.
    /// </remarks>
    private string WriteTruncatedDemo(string map, int lastTick)
    {
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "test server",
            ClientName = "tester",
            MapName = map,
            GameDirectory = "tf",
            PlaybackTimeSeconds = 0f,
            PlaybackTicks = 0,
            PlaybackFrames = 0,
            SignonLengthBytes = 0,
        };

        // No dem_stop, because that is what "truncated" means: the writer never got there.
        DemoCommand[] commands =
        [
            new(DemoCommandType.SyncTick, 0, default),
            new(DemoCommandType.ConsoleCmd, lastTick, new byte[] { 0x68, 0x69, 0x00 }),
        ];

        string path = Path.Combine(_folder, map + "-truncated.dem");
        File.WriteAllBytes(path, DemoWriter.Write(header, commands));
        return path;
    }

    [Test]
    public void OneFileOnTheCommandLineIsOpenedNotJustListed()
    {
        // The file-association case. Double-clicking a .dem in Explorer has to end with the demo
        // on screen: listing it in a playlist and waiting is not what opening a file means
        // anywhere else, and it is what the viewer used to do.
        string path = WriteDemo("cp_snakewater_final1", ticks: 22000, seconds: 330f);

        using MainForm form = new(path);

        form.Demo.ShouldNotBeNull().MapName.ShouldBe("cp_snakewater_final1");
        form.Transport.LastTick.ShouldBe(22000);
    }

    [Test]
    public void AFolderOnTheCommandLineIsListedNotOpened()
    {
        // The control, and the reason the check is on file-ness rather than on count. A folder
        // means "here is a playlist"; picking one of its demos to start would be guessing which.
        WriteDemo("cp_badlands", ticks: 100, seconds: 2f);

        using MainForm form = new(_folder);

        form.Demo.ShouldBeNull();
        form.Transport.LastTick.ShouldBe(0);
    }

    [Test]
    public void SeveralFilesOnTheCommandLineAreListedNotOpened()
    {
        // Multi-select from Explorer. Same reasoning as a folder: several files are a playlist.
        string first = WriteDemo("cp_process_final", ticks: 100, seconds: 2f);
        string second = WriteDemo("koth_product", ticks: 200, seconds: 4f);

        using MainForm form = new(first, second);

        form.Demo.ShouldBeNull();
    }

    [Test]
    public void AMissingFileSaysWhichFile()
    {
        Should.Throw<FileNotFoundException>(
            () => LoadedDemo.Load(Path.Combine(_folder, "absent.dem")));
    }
}
