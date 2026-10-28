namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which of two halves a frame's pose writes, against the engine's two queued contexts (B581 rung 3).</summary>
/// <remarks>
/// **What the engine does** (finding 99 §2-3, read from TF2's x64 `materialsystem.dll`): `CMaterialSystem::EndFrame`
/// (`0x180014bc0`) waits for the previous frame's job, then flips the queued-context index (`0x2f00`, 0↔1) and hands the
/// context just recorded to the `MatQueue` thread; the game thread records the next frame into the OTHER context while
/// that one plays. Each context owns its own `CMemoryStack`, and per-frame render data lives there: *"Allocates temp
/// render data. Renderdata goes out of scope at frame end in multicore"* (`public/materialsystem/imaterialsystem.h:1623-1626`,
/// `LockRenderData`). The client keeps its own data the same way: `CMemoryStack m_QueuedRopeMemory[2]`, swapped by
/// `SwitchStack` (`game/client/c_rope.cpp:178-206`).
///
/// **Ours:** the pose's outputs (instance lists, their matrices, skinning matrices, face streams) are kept in two halves,
/// and a pose writes the half that was NOT handed with the frame in flight. The index moves only when a hand-off happened
/// since the last pose — the engine flips once per `EndFrame`, and a frame handed without a pose (a paused viewer) must
/// not let the next pose land on the half being drawn.
/// </remarks>
public sealed class PoseHalvesConformanceTests
{
    [Test]
    public void Next_AfterEachHandOff_AlternatesLikeTheEnginesTwoQueuedContexts()
    {
        PoseHalves halves = new();

        int[] written = [halves.Next(0), halves.Next(1), halves.Next(2), halves.Next(3)];

        written.ShouldBe([0, 1, 0, 1]);
    }

    [Test]
    public void Next_TwiceWithNoHandOffBetween_KeepsTheHalfThatWasNotHanded()
    {
        // A seek's replays pose more than once before the next hand-off: none of those poses was handed, so the half in
        // flight is still the other one, and toggling would land on it.
        PoseHalves halves = new();
        int first = halves.Next(4);

        halves.Next(4).ShouldBe(first);
        halves.Next(4).ShouldBe(first);
    }

    [Test]
    public void Next_AfterTwoHandOffsWithNoPoseBetween_WritesTheOtherHalfThanTheOneLastPosed()
    {
        // **The case a hand-off parity gets wrong** (control for the design): a paused viewer hands the SAME posed list
        // twice; the count's parity would say "half 0" again, which is the list in flight.
        PoseHalves halves = new();
        int posed = halves.Next(0);

        halves.Next(2).ShouldBe(1 - posed);
    }

    [Test]
    public void Current_BeforeAnyPose_IsHalfZero()
    {
        new PoseHalves().Current.ShouldBe(0);
    }
}
