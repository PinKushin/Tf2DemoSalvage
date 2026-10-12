namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// When a luminance measurement is queued and when it is read — `CLuminanceHistogramSystem::Update`
/// (`viewpostprocess.cpp:551-613`), which the exposure and every `--shot` wait on (B520, the CI hang of 2026-10-11).
/// </summary>
/// <remarks>
/// *Read from published source.* A histogram entry is one of four states, and the two rules that matter are both in
/// the switch:
///
/// <code>
/// case HESTATE_FIRST_QUERY_IN_FLIGHT:
/// case HESTATE_QUERY_IN_FLIGHT:
///     if ( cur_query_frame > CurHistogram[i].m_frame_queued + 2 )
///     {
///         int np = pRenderContext->OcclusionQuery_GetNumPixelsRendered( CurHistogram[i].m_occ_handle );
///         if ( np !=- 1 )     // -1=query not finished. wait until next time
///             ... m_state = HESTATE_QUERY_DONE;
///     }
/// ...
/// // now, issue queries for the oldest finished queries we have
/// </code>
///
/// **A query in flight is never issued again**: only `HESTATE_INITIAL` and `HESTATE_QUERY_DONE` entries are. The
/// viewer's ring of three frame copies overwrote the oldest every frame whether or not it had been read, so the copy
/// the reader was waiting for was always the one the device had just been told to redo. *Measured*: ten readings took
/// 16 seconds on a GPU at 300 frames a second and 77 on WARP, and a capture waits for ten — on the CI runner, more
/// than the test's 180.
/// </remarks>
public sealed class HistogramRingConformanceTests
{
    /// <remarks>The fault itself: with nothing read, the fourth frame must not take back the first copy's slot.</remarks>
    [Test]
    public void SlotToQueue_WithEveryCopyStillInFlight_IsNone()
    {
        HistogramRing ring = new(3);

        for (int frame = 0; frame < 3; frame++)
        {
            ring.BeginFrame();
            ring.Queued(ring.SlotToQueue().ShouldNotBeNull());
        }

        ring.BeginFrame();

        ring.SlotToQueue().ShouldBeNull("a copy in flight is not queued again; the reader is still waiting for it");
    }

    /// <remarks>`cur_query_frame > m_frame_queued + 2`: asked for in the third frame after its own, not before.</remarks>
    [Test]
    public void SlotToRead_OfACopy_IsOfferedFromTheThirdFrameAfterItWasQueued()
    {
        HistogramRing ring = new(3);

        ring.BeginFrame();
        ring.Queued(ring.SlotToQueue().ShouldNotBeNull());
        ring.SlotToRead().ShouldBeNull();

        ring.BeginFrame();
        ring.SlotToRead().ShouldBeNull();
        ring.BeginFrame();
        ring.SlotToRead().ShouldBeNull();

        ring.BeginFrame();
        ring.SlotToRead().ShouldBe(0);
    }

    /// <remarks>"wait until next time": a copy the device has not finished is offered again, and stays where it is.</remarks>
    [Test]
    public void SlotToRead_OfACopyNotYetFinished_IsOfferedAgainNextFrame()
    {
        HistogramRing ring = new(3);

        for (int frame = 0; frame < 4; frame++)
        {
            ring.BeginFrame();

            if (ring.SlotToQueue() is { } slot)
            {
                ring.Queued(slot);
            }
        }

        ring.SlotToRead().ShouldBe(0);

        ring.BeginFrame();

        ring.SlotToRead().ShouldBe(0, "not read, so not moved past");
        ring.SlotToQueue().ShouldBeNull();
    }

    /// <remarks>"issue queries for the oldest finished queries we have": a read slot is the next one queued.</remarks>
    [Test]
    public void SlotToQueue_AfterTheOldestWasRead_IsThatSlotAgain()
    {
        HistogramRing ring = new(3);

        for (int frame = 0; frame < 4; frame++)
        {
            ring.BeginFrame();

            if (ring.SlotToQueue() is { } slot)
            {
                ring.Queued(slot);
            }
        }

        ring.Read(ring.SlotToRead().ShouldNotBeNull());

        ring.SlotToQueue().ShouldBe(0);
        ring.SlotToRead().ShouldBeNull("the next oldest was queued a frame later, so it is a frame from being read");

        ring.BeginFrame();

        ring.SlotToRead().ShouldBe(1);
    }
}
