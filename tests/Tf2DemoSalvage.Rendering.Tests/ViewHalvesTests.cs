using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The two view halves `Device3D` alternates between (B581 rung 2, finding 98 §9 steps 1-2): the draw must read the half
/// its frame was handed, even after the next frame's `SetCamera` has flipped to the other.
/// </summary>
/// <remarks>
/// **The only control that can tell the halves apart while the drain stays before `PlaceCamera`.** Both halves then hold
/// the same contents for the same view, so a draw that read the wrong half draws identical pixels and an identical cull
/// hash; the frame-level instruments cannot redden until frames overlap (step 5).
/// </remarks>
public sealed class ViewHalvesTests
{
    [Test]
    public void Job_RunAfterTheNextFrameFlipped_DrawsTheHalfItWasHanded()
    {
        ViewHalves halves = new(_ => { });

        halves.Flip();
        ViewHalf handed = halves.Cull;
        ViewHalf? seen = null;
        Action job = halves.Job(handed, () => seen = halves.Drawn);

        // The next frame's cull, on the game thread, before the render thread runs the job.
        halves.Flip();
        job();

        seen.ShouldBeSameAs(handed);
        halves.Cull.ShouldNotBeSameAs(handed);
    }

    [Test]
    public void Job_WhenRun_ReplaysTheHandedHalfsUploadsBeforeTheDraw()
    {
        List<object> order = [];
        ViewHalves halves = new(order.Add);

        halves.Flip();
        ViewHalf handed = halves.Cull;
        Action job = halves.Job(handed, () => order.Add("draw"));

        halves.Flip();
        job();

        order.ShouldBe([handed, "draw"]);
    }

    [Test]
    public void Begin_WithAHalf_ReplaysItAndDrawsFromIt()
    {
        ViewHalf? replayed = null;
        ViewHalves halves = new(half => replayed = half);

        halves.Flip();
        halves.Begin(halves.Cull);

        replayed.ShouldBeSameAs(halves.Both[1]);
        halves.Drawn.ShouldBeSameAs(halves.Both[1]);
    }

    [Test]
    public void TakeDetailUpload_ARevisitAfterTheOtherHalfsView_UploadsAgain()
    {
        ViewHalves halves = new(_ => { });
        ViewHalf a = halves.Both[0];
        ViewHalf b = halves.Both[1];

        a.DetailBuiltFor = Key(1f);
        b.DetailBuiltFor = Key(2f);

        // A, A again, B, then A once more: the device's one buffer holds B's quads by then, whose leaf ranges are B's.
        bool[] taken = [halves.TakeDetailUpload(a), halves.TakeDetailUpload(a), halves.TakeDetailUpload(b), halves.TakeDetailUpload(a)];

        taken.ShouldBe([true, false, true, true]);
    }

    [Test]
    public void TakeDetailUpload_BothHalvesBuiltForOneKey_UploadsOnce()
    {
        ViewHalves halves = new(_ => { });
        ViewHalf a = halves.Both[0];
        ViewHalf b = halves.Both[1];

        a.DetailBuiltFor = Key(1f);
        b.DetailBuiltFor = Key(1f);

        bool[] taken = [halves.TakeDetailUpload(a), halves.TakeDetailUpload(b), halves.TakeDetailUpload(a)];

        taken.ShouldBe([true, false, false]);
    }

    [Test]
    public void TakeDetailUpload_WithNothingBuilt_IsFalse()
    {
        ViewHalves halves = new(_ => { });

        halves.TakeDetailUpload(halves.Both[0]).ShouldBeFalse();
    }

    [Test]
    public void TakeDetailUpload_AfterForgetDetailUpload_IsTrue()
    {
        ViewHalves halves = new(_ => { });
        ViewHalf a = halves.Both[0];

        a.DetailBuiltFor = Key(1f);
        _ = halves.TakeDetailUpload(a);
        halves.ForgetDetailUpload();

        halves.TakeDetailUpload(a).ShouldBeTrue();
    }

    private static (float X, float Y, float Z, float Distance, float Fade, (float, float, float) Forward) Key(float x) =>
        (x, 0f, 0f, 1200f, 0f, (1f, 0f, 0f));
}
