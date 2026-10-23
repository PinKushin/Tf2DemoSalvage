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
        ViewHalves halves = new((_, _) => { });

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
        ViewHalves halves = new((half, _) => order.Add(half));

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
        ViewHalves halves = new((half, _) => replayed = half);

        halves.Flip();
        halves.Begin(halves.Cull);

        replayed.ShouldBeSameAs(halves.Both[1]);
        halves.Drawn.ShouldBeSameAs(halves.Both[1]);
    }

    [Test]
    public void TakeDetailUpload_ARevisitAfterTheOtherHalfsView_UploadsAgain()
    {
        ViewHalves halves = new((_, _) => { });
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
        ViewHalves halves = new((_, _) => { });
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
        ViewHalves halves = new((_, _) => { });

        halves.TakeDetailUpload(halves.Both[0]).ShouldBeFalse();
    }

    [Test]
    public void TakeDetailUpload_AfterForgetDetailUpload_IsTrue()
    {
        ViewHalves halves = new((_, _) => { });
        ViewHalf a = halves.Both[0];

        a.DetailBuiltFor = Key(1f);
        _ = halves.TakeDetailUpload(a);
        halves.ForgetDetailUpload();

        halves.TakeDetailUpload(a).ShouldBeTrue();
    }

    [Test]
    public void Job_ForTheHalfACameraWasRecordedInto_ReplaysItOnceBeforeTheDraw()
    {
        List<object> order = [];
        ViewHalves halves = new((_, camera) => order.Add(camera?.Matrix ?? (object)"none"));
        ViewHalf a = halves.Cull;
        float[] matrix = new float[16];

        a.RecordCamera(Upload(matrix));
        halves.Job(a, () => order.Add("draw"))();
        halves.Job(a, () => order.Add("draw"))();

        // The draw is where `DrawFrame` sets `LinearLightScale`, which the camera upload writes into the constant buffer.
        order.ShouldBe([matrix, "draw", "none", "draw"]);
    }

    [Test]
    public void Job_ForTheOtherHalf_ReplaysNoneOfThisHalfsCamera()
    {
        CameraUpload? replayed = default;
        ViewHalves halves = new((_, camera) => replayed = camera);
        ViewHalf a = halves.Cull;

        a.RecordCamera(Upload(new float[16]));
        halves.Flip();
        halves.Job(halves.Cull, () => { })();

        replayed.ShouldBeNull();
    }

    [Test]
    public void RecordCamera_TwiceBeforeTheDraw_ReplaysTheLastValues()
    {
        CameraUpload? replayed = default;
        ViewHalves halves = new((_, camera) => replayed = camera);
        ViewHalf a = halves.Cull;
        float[] last = new float[16];

        a.RecordCamera(Upload(new float[16]));
        a.RecordCamera(Upload(last) with { Specular = false });
        halves.Begin(a);

        replayed.ShouldBe(Upload(last) with { Specular = false });
    }

    private static CameraUpload Upload(float[] matrix) =>
        new(matrix, Colours: false, Specular: true, Fullbright.Off, DebugModes.None, Phong: true, Fog: null);

    private static (float X, float Y, float Z, float Distance, float Fade, (float, float, float) Forward) Key(float x) =>
        (x, 0f, 0f, 1200f, 0f, (1f, 0f, 0f));
}
