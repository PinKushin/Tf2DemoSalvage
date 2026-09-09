using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A compiled scene's clock, its loop, and which gesture it stages (B351).
/// </summary>
/// <remarks>
/// **Closed form against the engine's stepping, which is the whole claim.** `C_SceneEntity::DoThink`
/// advances `m_flCurrentTime += gpGlobals->frametime` and `DispatchProcessLoop` sets it back to the
/// `LOOP` event's parameter (<c>c_sceneentity.cpp:992</c>, <c>:594</c>); a viewer that seeks cannot
/// step, so it folds instead. These assert that folding lands where stepping would.
///
/// Synthetic, because the loop windows here are ones this file chose — a corpus scene can only say
/// what happens to be in it (D38).
/// </remarks>
[TestFixture]
public sealed class SceneTauntTests
{
    /// <summary>A high-five pose: one gesture, then a loop over [0.5, 2.5].</summary>
    private static SceneTaunt Held =>
        new([new SceneTauntGesture(0f, "taunt_highFiveStart")], LoopsFrom: 0.5f, LoopsAt: 2.5f);

    /// <summary>A one-shot taunt, which never folds its clock back.</summary>
    private static SceneTaunt Once =>
        new([new SceneTauntGesture(0f, "taunt_laugh")], LoopsFrom: -1f, LoopsAt: -1f);

    [Test]
    public void TimeAt_BeforeTheLoopIsReached_IsTheElapsedTime()
    {
        // The clock runs forward untouched until the LOOP event is crossed.
        Held.TimeAt(0f).ShouldBe(0f);
        Held.TimeAt(2.4f).ShouldBe(2.4f);
    }

    [Test]
    public void TimeAt_PastTheLoop_FoldsIntoItsWindow()
    {
        // The window is [0.5, 2.5), two seconds wide, so 2.5 folds to 0.5 and 3.0 to 1.0.
        Held.TimeAt(2.5f).ShouldBe(0.5f);
        Held.TimeAt(3f).ShouldBe(1f);
        // A whole extra window on: 0.5 + ((4.5 - 0.5) mod 2) = 0.5, back at the start of the window.
        Held.TimeAt(4.5f).ShouldBe(0.5f);

        // Twenty seconds of holding the pose is nine and a half windows past the fold, and lands in
        // the same place as half a window: this is the assertion that stepping and folding agree
        // however long the taunt is held.
        Held.TimeAt(20.5f).ShouldBe(0.5f, 0.0001d);
    }

    [Test]
    public void TimeAt_ASceneThatNeverLoops_IsNeverFolded()
    {
        // **The control.** Without it a fold applied unconditionally would pass every case above,
        // and every one-shot taunt in the game would restart for ever instead of ending.
        Once.Loops.ShouldBeFalse();
        Once.TimeAt(0f).ShouldBe(0f);
        Once.TimeAt(300f).ShouldBe(300f);
    }

    [Test]
    public void Loops_ALoopWhoseWindowIsEmptyOrBackwards_IsNotALoop()
    {
        // A back-time at or past the loop's own position is not a window, and folding by a
        // non-positive period would divide by zero or run backwards.
        new SceneTaunt([], LoopsFrom: 2.5f, LoopsAt: 2.5f).Loops.ShouldBeFalse();
        new SceneTaunt([], LoopsFrom: 3f, LoopsAt: 2.5f).Loops.ShouldBeFalse();
        new SceneTaunt([], LoopsFrom: -1f, LoopsAt: 2.5f).Loops.ShouldBeFalse();
    }

    [Test]
    public void At_TheLastGestureToHaveBegun_IsTheOnePlaying()
    {
        // **Each start RESETS the slot**, so a scene staging two gestures never layers them:
        // `ResetGestureSlot( GESTURE_SLOT_VCD )` then `AddVCDSequenceToGestureSlot` on the next line
        // (`c_tf_player.cpp:9477`). The second therefore replaces the first the moment it begins.
        SceneTaunt two = new(
            [new SceneTauntGesture(0f, "first"), new SceneTauntGesture(1.5f, "second")],
            LoopsFrom: -1f,
            LoopsAt: -1f);

        two.At(0f).ShouldNotBeNull().Gesture.Sequence.ShouldBe("first");
        two.At(1.4f).ShouldNotBeNull().Gesture.Sequence.ShouldBe("first");
        two.At(1.5f).ShouldNotBeNull().Gesture.Sequence.ShouldBe("second");
        two.At(9f).ShouldNotBeNull().Gesture.Sequence.ShouldBe("second");
    }

    [Test]
    public void At_TheElapsedTime_IsMeasuredFromTheGesturesOwnStart()
    {
        // The layer's cycle is elapsed time times the sequence's rate, so a gesture staged 1.5s into
        // the scene must be handed 0.5s of its own life at scene time 2.0 — not 2.0.
        SceneTaunt late = new(
            [new SceneTauntGesture(1.5f, "second")], LoopsFrom: -1f, LoopsAt: -1f);

        late.At(2f).ShouldNotBeNull().Elapsed.ShouldBe(0.5f, 0.0001d);
    }

    [Test]
    public void At_BeforeTheFirstGestureBegins_IsNothing()
    {
        // A scene whose gesture is staged a second in animates nothing for that second, which is the
        // control that stops `At` from simply returning the first entry.
        SceneTaunt late = new(
            [new SceneTauntGesture(1f, "second")], LoopsFrom: -1f, LoopsAt: -1f);

        late.At(0.9f).ShouldBeNull();
    }

    [Test]
    public void At_ASceneStagingNothing_IsNothing()
    {
        new SceneTaunt([], LoopsFrom: -1f, LoopsAt: -1f).At(5f).ShouldBeNull();
    }
}
