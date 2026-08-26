using Microsoft.Extensions.Logging.Abstractions;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Warning once per map that the leaf outline is empty.</summary>
/// <remarks>
/// **The latch was a bool in `MainForm`** (B188, D90), which meant the behaviour it guards could
/// only be exercised by constructing a window. It guards a real hazard: the outline is rebuilt every
/// frame the overlay is on, and B191 was one log line per frame taking a machine-wide lock and a
/// disk flush — 120 ms of a 133 ms frame.
/// </remarks>
public sealed class LeafBoxesTests
{
    [Test]
    public void Lines_WithNoMap_WarnsOnceHoweverManyFrames()
    {
        // **The count is the assertion, not merely "it warned".** A latch that never latched would
        // pass a "did it warn" test perfectly and reintroduce the per-frame flush this exists to
        // prevent.
        RecordingLogger log = new();
        LeafBoxes boxes = new(log);

        for (int frame = 0; frame < 5; frame++)
        {
            boxes.Lines(map: null, (0f, 0f, 0f));
        }

        log.Count("mat_leafvis").ShouldBe(1);
    }

    [Test]
    public void Lines_WithNoMap_SaysWhichSilenceItIs()
    {
        // **"no leaf box" is true of all three causes and useful for none** (D83): a map that never
        // loaded, a map with no BSP tree, and a camera in a leaf the lump gives no bounds for are
        // three problems with three fixes.
        RecordingLogger log = new();

        new LeafBoxes(log).Lines(map: null, (0f, 0f, 0f));

        log.Count("no map loaded").ShouldBe(1);
    }

    [Test]
    public void Lines_AfterForget_WarnsAgainForTheNextMap()
    {
        // **The reset is half the behaviour.** Latching for the life of the process would mean the
        // second map's silence went unreported — and switching demos is the common case here, not
        // the rare one.
        RecordingLogger log = new();
        LeafBoxes boxes = new(log);

        boxes.Lines(map: null, (0f, 0f, 0f));
        boxes.Forget();
        boxes.Lines(map: null, (0f, 0f, 0f));

        log.Count("mat_leafvis").ShouldBe(2);
    }

    [Test]
    public void Lines_WithNoMap_ReturnsNothingToDraw()
    {
        new LeafBoxes(NullLogger.Instance).Lines(map: null, (0f, 0f, 0f)).ShouldBeEmpty();
    }
}
