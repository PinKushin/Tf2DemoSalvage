using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>`--measure`'s sun line: the lighting's own counts, differenced across whole frames (B594 follow-up).</summary>
public sealed class SunMeterTests
{
    private static LevelLighting Sunlit() => new(
        leaves: null,
        ambient: [],
        worldLights: [],
        sun: new BspWorldLight((0f, 0f, 8192f), (0.9f, 0.8f, 0.7f), (0f, 0f, -1f), WorldLightKind.SkyLight),
        new RecordingLogger(),
        strikesSky: static (_, _) => true);

    [Test]
    public void Report_TwoFramesOfThreeAndOneCalls_IsTheirMeanPerFrame()
    {
        LevelLighting lighting = Sunlit();
        SunMeter meter = new();
        FrameParts parts = new();

        meter.Frame(lighting, parts);
        lighting.SunAt(0f, 0f, 0f);
        lighting.SunAt(0f, 0f, 0f);
        lighting.SunAt(0f, 0f, 0f);
        meter.Frame(lighting, parts);
        lighting.SunAt(0f, 0f, 0f);
        meter.Frame(lighting, parts);

        meter.Report().ShouldStartWith("sun per frame 2 calls (light cache 0, static props 0, direct 2) in 0 ms over 2 frames");
        meter.Report().ShouldBeEmpty("a report starts a new window");
    }

    /// <remarks>A new map's counter starts at zero: the meter re-reads it rather than subtracting across two maps.</remarks>
    [Test]
    public void Frame_AfterTheLightingChanges_RestartsTheReadingInsteadOfGoingNegative()
    {
        LevelLighting first = Sunlit();
        SunMeter meter = new();

        meter.Frame(first, null);

        for (int call = 0; call < 5; call++)
        {
            first.SunAt(0f, 0f, 0f);
        }

        meter.Frame(first, null);
        meter.Report();

        LevelLighting second = Sunlit();

        meter.Frame(second, null);
        second.SunAt(0f, 0f, 0f);
        meter.Frame(second, null);

        meter.Report().ShouldStartWith("sun per frame 1 calls");
    }
}
