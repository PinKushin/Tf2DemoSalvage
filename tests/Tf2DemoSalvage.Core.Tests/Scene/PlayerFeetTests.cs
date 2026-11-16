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
