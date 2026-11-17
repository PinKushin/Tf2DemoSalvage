using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The viewer's per-frame feet: one frame time a frame, one turn a frame, a restart on a move back (B750 review).</summary>
/// <remarks>
/// `UpdateClientSideAnimations` runs once a frame (`cdll_client_int.cpp:2189`), converging each drawn player's feet by
/// `gpGlobals->frametime` (`multiplayer_animstate.cpp:1759`). The oracle is <see cref="FeetYaw"/> driven by hand, whose
/// own conformance tests hold the arithmetic. Eyes at 100 with feet planted at 0: past the 45 degree allowance, so the
/// goal steps 45 each time the feet are advanced, and the feet turn by the time given.
/// </remarks>
public sealed class PlayerFeetTests
{
    private const float Interval = 0.015f;

    private const float Eyes = 100f;

    [Test]
    public void Advance_APlayerNotAskedForAFrame_TurnsByTheFramesTimeNotHisGap()
    {
        // Player 2 is asked every frame; player 1 skips the frame at 100.25, as a dead or dormant player does.
        PlayerFeet feet = new();
        feet.Advance(1, 100d, Eyes, 0f, 0f, Interval);
        feet.Advance(2, 100d, Eyes, 0f, 0f, Interval);
        feet.Advance(2, 100.25d, Eyes, 0f, 0f, Interval);
        feet.Advance(2, 100.5d, Eyes, 0f, 0f, Interval);

        float turned = feet.Advance(1, 100.5d, Eyes, 0f, 0f, Interval);

        turned.ShouldBe(Once(0.25f * Interval), 0.0001f);
    }

    /// <remarks>
    /// **A held custom-model yaw is the last drawn, and only for the same object** (B765): serial 1 is drawn at 170; the
    /// slot's next occupant, serial 2, holds what the timeline drew him at, 0, as a new anim state does. Serial 1 again is
    /// the control: he holds 170.
    /// </remarks>
    [TestCase(1, 170f, TestName = "Render_TheSamePlayerHolding_KeepsTheYawLastDrawn")]
    [TestCase(2, 0f, TestName = "Render_ANewPlayerInTheSlotHolding_StartsFromTheTimeline")]
    public void Render_AHeldYaw_BelongsToTheObjectThatDrewIt(int secondSerial, float held)
    {
        PlayerFeet feet = new();
        feet.Render(1, 100d, written: 170f, timelineYaw: 170f, Interval, serial: 1);

        feet.Render(1, 101d, written: null, timelineYaw: 0f, Interval, serial: secondSerial).ShouldBe(held);
    }

    /// <remarks>
    /// **A custom model's branch never turns the feet** (`tf_playeranimstate.cpp:340-366`), so a player drawn by it first
    /// and by the class's path after is planted where the timeline's feet are, 30, not at the custom yaw, 170.
    /// </remarks>
    [Test]
    public void Advance_AfterOnlyCustomFrames_PlantsTheTimelinesFeet()
    {
        PlayerFeet feet = new();
        feet.Render(1, 100d, written: 170f, timelineYaw: 170f, Interval);

        feet.Advance(1, 101d, Eyes, 0f, timelineFeet: 30f, Interval).ShouldBe(30f);
    }

    [Test]
    public void Advance_AskedTwiceInOneFrame_TurnsOnce()
    {
        // The moment, the sounds and the bullets all ask in one frame; a second ask must not step the goal again.
        PlayerFeet feet = new();
        feet.Advance(1, 100d, Eyes, 0f, 0f, Interval);
        float first = feet.Advance(1, 100.25d, Eyes, 0f, 0f, Interval);

        float again = feet.Advance(1, 100.25d, Eyes, 0f, 0f, Interval);
        float next = feet.Advance(1, 100.5d, Eyes, 0f, 0f, Interval);

        again.ShouldBe(first);
        next.ShouldBe(Twice(0.25f * Interval), 0.0001f);
    }

    [Test]
    public void Advance_AfterAMoveBack_StartsFromTheTimelinesFeet()
    {
        PlayerFeet feet = new();
        feet.Advance(1, 100d, Eyes, 0f, 0f, Interval);
        feet.Advance(1, 100.5d, Eyes, 0f, 0f, Interval).ShouldNotBe(0f, "the control: the feet had turned");

        feet.Advance(1, 99d, Eyes, 0f, 30f, Interval).ShouldBe(30f);
    }

    /// <summary>Feet planted at 0, advanced once toward eyes at 100.</summary>
    private static float Once(float seconds)
    {
        FeetYaw expected = FeetYaw.Planted(0f);
        expected.Advance(Eyes, 0f, seconds);

        return expected.Current;
    }

    /// <summary>Feet planted at 0, advanced twice toward eyes at 100.</summary>
    private static float Twice(float seconds)
    {
        FeetYaw expected = FeetYaw.Planted(0f);
        expected.Advance(Eyes, 0f, seconds);
        expected.Advance(Eyes, 0f, seconds);

        return expected.Current;
    }
}
