using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// <c>CLuminanceHistogramSystem::Update</c> (`viewpostprocess.cpp:532-613`): which bin is counted each frame, and when its
/// count is taken (B575, B520).
/// </summary>
/// <remarks>
/// *Read from published source.* Under <c>mat_tonemap_algorithm 1</c> there are <c>N_LUMINANCE_RANGES_NEW</c> = 17
/// entries (`:391`, `:543-545`): sixteen bins and a seventeenth spanning 0 to 100000 (`:885-892`) that the target never
/// reads (`:620`, `:636`) but that takes its turn in the queue. <c>MAX_QUERIES_PER_FRAME</c> is 1 (`:392`):
///
/// <code>
/// case HESTATE_INITIAL:
///     if ( n_queries_issued_this_frame&lt;MAX_QUERIES_PER_FRAME ) { CurHistogram[i].IssueQuery(cur_query_frame); ... }
/// case HESTATE_FIRST_QUERY_IN_FLIGHT:
/// case HESTATE_QUERY_IN_FLIGHT:
///     if ( cur_query_frame &gt; CurHistogram[i].m_frame_queued + 2 )
///     {   int np = pRenderContext-&gt;OcclusionQuery_GetNumPixelsRendered( CurHistogram[i].m_occ_handle );
///         if ( np !=- 1 )     // -1=query not finished. wait until next time
///             ... m_state = HESTATE_QUERY_DONE; }
/// // now, issue queries for the oldest finished queries we have
/// </code>
///
/// and <c>ContainsValidData</c> is DONE or IN_FLIGHT, never INITIAL or FIRST_QUERY_IN_FLIGHT (`:405-408`).
/// </remarks>
public sealed class LuminanceHistogramSystemConformanceTests
{
    /// <summary>A device whose every query answers <see cref="Answer"/> once asked, and records what was issued.</summary>
    private sealed class FakeQueries : IHistogramQueries
    {
        public List<(int Entry, float Min, float Max)> Issued { get; } = [];

        public Func<int, int?> Answer { get; set; } = entry => 100 + entry;

        public void Issue(int entry, float min, float max) => Issued.Add((entry, min, max));

        public int? TryRead(int entry) => Answer(entry);
    }

    [Test]
    public void Update_FromTheStart_IssuesOneEntryAFrameInIndexOrder()
    {
        LuminanceHistogramSystem system = new();
        FakeQueries queries = new() { Answer = _ => null };

        for (int frame = 0; frame < LuminanceHistogramSystem.Entries; frame++)
        {
            system.Update(queries);
            queries.Issued.Count.ShouldBe(frame + 1, "MAX_QUERIES_PER_FRAME 1");
            queries.Issued[frame].Entry.ShouldBe(frame);
        }
    }

    /// <remarks>`:426` caps bin 15 at 10000, "Count all pixels &gt;1.0 as 1.0"; `:890-891` the seventeenth's 0 to 100000.</remarks>
    [Test]
    public void Update_EachEntry_IsIssuedWithItsOwnRange()
    {
        LuminanceHistogramSystem system = new();
        FakeQueries queries = new() { Answer = _ => null };

        for (int frame = 0; frame < LuminanceHistogramSystem.Entries; frame++)
        {
            system.Update(queries);
        }

        queries.Issued[0].ShouldBe((0, 0f, AutoExposure.BinMax(0)));
        queries.Issued[8].ShouldBe((8, AutoExposure.BinMin(8), AutoExposure.BinMax(8)));
        queries.Issued[15].ShouldBe((15, AutoExposure.BinMin(15), 10000f));
        queries.Issued[16].ShouldBe((16, 0f, 100000f));
    }

    [Test]
    public void Update_AnEntry_IsReadFromTheThirdFrameAfterItWasIssued()
    {
        LuminanceHistogramSystem system = new();
        List<int> asked = [];
        FakeQueries queries = new();
        queries.Answer = entry =>
        {
            asked.Add(entry);
            return 7;
        };

        system.Update(queries);
        system.Update(queries);
        system.Update(queries);
        asked.ShouldNotContain(0, "frames 2 and 3 are not past m_frame_queued + 2");

        system.Update(queries);
        asked.ShouldContain(0);
        system.Counts[0].ShouldBe(7);
    }

    /// <remarks>"-1=query not finished. wait until next time": asked again, never issued again.</remarks>
    [Test]
    public void Update_AnUnfinishedQuery_IsAskedAgainAndNotReissued()
    {
        LuminanceHistogramSystem system = new();
        FakeQueries queries = new() { Answer = _ => null };

        for (int frame = 0; frame < 40; frame++)
        {
            system.Update(queries);
        }

        queries.Issued.Count.ShouldBe(LuminanceHistogramSystem.Entries, "every entry is in flight, so nothing is issued");
    }

    /// <remarks>Once every entry has answered, the oldest finished is the next issued: each entry every seventeen frames.</remarks>
    [Test]
    public void Update_InTheSteadyState_ReissuesEachEntryEverySeventeenFrames()
    {
        LuminanceHistogramSystem system = new();
        FakeQueries queries = new();

        for (int frame = 0; frame < 200; frame++)
        {
            system.Update(queries);
        }

        List<int> frames = [];

        for (int i = 0; i < queries.Issued.Count; i++)
        {
            if (queries.Issued[i].Entry == 5)
            {
                frames.Add(i);
            }
        }

        for (int i = frames.Count - 4; i < frames.Count; i++)
        {
            (frames[i] - frames[i - 1]).ShouldBe(LuminanceHistogramSystem.Entries);
        }
    }

    /// <remarks>FIRST_QUERY_IN_FLIGHT holds no data; IN_FLIGHT after a first answer still does (`:405-408`).</remarks>
    [Test]
    public void IsValid_BeforeTheFirstAnswerAndWhileReissued_IsFalseThenTrue()
    {
        LuminanceHistogramSystem system = new();
        FakeQueries queries = new();

        system.Update(queries);
        system.IsValid(0).ShouldBeFalse("issued, never answered");

        for (int frame = 0; frame < 3; frame++)
        {
            system.Update(queries);
        }

        system.IsValid(0).ShouldBeTrue("answered in frame 4");

        for (int frame = 0; frame < 30; frame++)
        {
            system.Update(queries);
            system.IsValid(0).ShouldBeTrue("a re-issued entry keeps its last count");
        }
    }

    /// <remarks>Ours, for a still capture (B520): every bin re-counted after a reset, and how many frames since.</remarks>
    [Test]
    public void FramesFresh_AfterMarkStale_CountsOnlyOnceEveryBinIsReadAgain()
    {
        LuminanceHistogramSystem system = new();
        FakeQueries queries = new();

        for (int frame = 0; frame < 100; frame++)
        {
            system.Update(queries);
        }

        system.FramesFresh.ShouldBeGreaterThan(50);
        system.MarkStale();
        system.FramesFresh.ShouldBe(0);

        int frames = 0;

        while (system.FramesFresh == 0)
        {
            system.Update(queries);
            frames++;
        }

        // Frame 100 issued bin 14, so bin 14 is the last re-issued after the mark, seventeen frames on, and read three
        // after that: a count issued before the mark measured the frame before the reset, and does not count.
        frames.ShouldBe(LuminanceHistogramSystem.Entries + 3);
    }
}
