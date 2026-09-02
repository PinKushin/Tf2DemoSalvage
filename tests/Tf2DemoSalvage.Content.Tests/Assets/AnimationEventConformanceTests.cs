using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A sequence's animation events, laid out as the engine reads them.
/// </summary>
/// <remarks>
/// **The events belong to the SEQUENCE.** <c>mstudioseqdesc_t</c>, <c>public/studio.h:817</c>:
///
/// <code>
///   int                 numevents;
///   int                 eventindex;
///   inline mstudioevent_t *pEvent( int i ) const {
///       Assert( i &gt;= 0 &amp;&amp; i &lt; numevents);
///       return (mstudioevent_t *)(((byte *)this) + eventindex) + i; };
/// </code>
///
/// **One event.** <c>mstudioevent_t</c>, <c>public/studio.h:495</c>:
///
/// <code>
///   struct mstudioevent_t
///   {
///       DECLARE_BYTESWAP_DATADESC();
///       float               cycle;
///       int                 event;
///       int                 type;
///       inline const char * pszOptions( void ) const { return options; }
///       char                options[64];
///
///       int                 szeventindex;
///       inline char * const pszEventName( void ) const { return ((char *)this) + szeventindex; }
///   };
/// </code>
///
/// **`docs/PARITY-AUDIT.md` named the wrong structure**, saying the array hung off
/// <c>mstudioanimdesc_t</c> — which has no event members of any kind. Corrected there; asserted
/// here, so the claim has a test rather than a paragraph.
///
/// **Which events a CLIENT fires**, from <c>C_BaseAnimating::DoAnimationEvents</c>
/// (<c>client/c_baseanimating.cpp:3550</c>) — the same filter twice, once for the looped tail and
/// once for the ordinary span:
///
/// <code>
///   if ( pevent[i].type &amp; AE_TYPE_NEWEVENTSYSTEM )
///   {
///       if ( !( pevent[i].type &amp; AE_TYPE_CLIENT ) )
///            continue;
///   }
///   else if ( pevent[i].event &lt; 5000 ) //Adrian - Support the old event system
///       continue;
/// </code>
///
/// So an event is the client's business when it declares <c>AE_TYPE_CLIENT</c> under the new
/// system, or numbers itself at or above 5000 under the old one — and those constants are
/// <c>AE_TYPE_NEWEVENTSYSTEM = 1 &lt;&lt; 10</c> and <c>AE_TYPE_CLIENT = 1 &lt;&lt; 4</c>
/// (<c>shared/eventlist.h:14</c>).
/// </remarks>
public sealed class AnimationEventConformanceTests
{
    [Test]
    public void EventStride_MatchesTheStudioStruct_IsEightyBytes()
    {
        // cycle 4 + event 4 + type 4 + options 64 + szeventindex 4.
        StudioLayout.EventStride.ShouldBe(80);

        StudioLayout.EventCycleOffset.ShouldBe(0);
        StudioLayout.EventIdOffset.ShouldBe(4);
        StudioLayout.EventTypeOffset.ShouldBe(8);
        StudioLayout.EventOptionsOffset.ShouldBe(12);
        StudioLayout.EventOptionsLength.ShouldBe(64);
        StudioLayout.EventNameIndexOffset.ShouldBe(76);
    }

    /// <remarks>
    /// **The pair sits between `actweight` and `bbmin`, and the file already knew it.**
    /// `SequenceBoundsMinOffset` is documented as 32 because eight ints precede it — the last two
    /// of which are exactly these. Asserting the relationship rather than the numbers alone means
    /// a future edit to one has to disturb the other.
    /// </remarks>
    [Test]
    public void SequenceEventFields_SitBetweenActivityWeightAndBounds()
    {
        StudioLayout.SequenceEventCountOffset.ShouldBe(24);
        StudioLayout.SequenceEventIndexOffset.ShouldBe(28);

        StudioLayout.SequenceEventCountOffset
            .ShouldBe(StudioLayout.SequenceActivityWeightOffset + 4);

        StudioLayout.SequenceBoundsMinOffset
            .ShouldBe(StudioLayout.SequenceEventIndexOffset + 4);
    }

    /// <remarks>
    /// **The new-system filter: declared client, or not ours.** An event whose type carries
    /// `AE_TYPE_NEWEVENTSYSTEM` is judged only by `AE_TYPE_CLIENT`; its NUMBER is irrelevant, which
    /// is why a low-numbered client event still fires.
    /// </remarks>
    [Test]
    public void FiresOnTheClient_UnderTheNewSystem_RequiresTheClientFlag()
    {
        const int NewSystem = 1 << 10;
        const int Client = 1 << 4;
        const int Server = 1 << 0;

        StudioEvent.FiresOnTheClient(type: NewSystem | Client, id: 12).ShouldBeTrue();

        StudioEvent.FiresOnTheClient(type: NewSystem | Server, id: 12)
            .ShouldBeFalse("a new-system event without AE_TYPE_CLIENT is not the client's");

        StudioEvent.FiresOnTheClient(type: NewSystem | Server, id: 6004)
            .ShouldBeFalse("and its number does not rescue it: the flag decides, not the id");
    }

    /// <remarks>
    /// **The old system had no flags, so the number is the whole test** — Valve's own comment on
    /// the branch is *"Adrian - Support the old event system"*. 5000 is the boundary and the
    /// comparison is `&lt; 5000`, so 5000 itself belongs to the client.
    /// </remarks>
    [Test]
    public void FiresOnTheClient_UnderTheOldSystem_IsDecidedByFiveThousand()
    {
        StudioEvent.FiresOnTheClient(type: 0, id: 4999)
            .ShouldBeFalse("below 5000 is a server event under the old numbering");

        StudioEvent.FiresOnTheClient(type: 0, id: 5000)
            .ShouldBeTrue("the comparison is `< 5000`, so 5000 is the client's");

        StudioEvent.FiresOnTheClient(type: 0, id: 6004)
            .ShouldBeTrue("CL_EVENT_FOOTSTEP_LEFT, squarely in client territory");
    }

    /// <remarks>
    /// The reader takes a sequence's events out of the model's bytes, at the offsets above. Built
    /// by hand rather than read from a real model so the expected values are ones this test put
    /// there — a corpus model would only let the test agree with whatever the parser did.
    ///
    /// **The second event is placed at a LITERAL 80, not at `StudioLayout.EventStride`, and that
    /// is the whole point of the second event.** Written the obvious way this test used the same
    /// constant to lay the bytes down that the reader uses to pick them up, so both moved together
    /// and a wrong stride went undetected — proved by sabotage: setting the stride to 76 reddened
    /// only the arithmetic assertion above while this test stayed green, exactly the shape
    /// `docs/memory/instrument-bugs-outnumber-decoder-bugs.md` is about. The fixture has to state
    /// the layout independently or it is measuring the code against itself.
    /// </remarks>
    [Test]
    public void Read_ASequenceWithTwoEvents_ReturnsBothInOrder()
    {
        byte[] model = new byte[512];

        const int Sequence = 64;
        const int Events = 240;

        // The struct's own size, stated here rather than borrowed: cycle, event, type,
        // options[64], szeventindex.
        const int StrideFromTheSdk = 80;

        Write(model, Sequence + StudioLayout.SequenceEventCountOffset, 2);
        Write(model, Sequence + StudioLayout.SequenceEventIndexOffset, Events - Sequence);

        WriteEvent(model, Events, cycle: 0.25f, id: 6004, type: 0, options: "Concrete");
        WriteEvent(
            model,
            Events + StrideFromTheSdk,
            cycle: 0.75f,
            id: 12,
            type: (1 << 10) | (1 << 4),
            options: "spyMask");

        IReadOnlyList<StudioEvent> read = StudioEvent.Read(model, Sequence);

        read.Count.ShouldBe(2);

        read[0].Cycle.ShouldBe(0.25f);
        read[0].Id.ShouldBe(6004);
        read[0].Options.ShouldBe("Concrete");
        read[0].FiresOnTheClient().ShouldBeTrue();

        read[1].Cycle.ShouldBe(0.75f);
        read[1].Id.ShouldBe(12);
        read[1].Options.ShouldBe("spyMask");
        read[1].FiresOnTheClient().ShouldBeTrue();
    }

    /// <remarks>
    /// The control: a sequence declaring no events must produce none, so "returns two" above is
    /// about the array rather than about the reader always finding something.
    /// </remarks>
    [Test]
    public void Read_ASequenceWithNoEvents_ReturnsNothing()
    {
        byte[] model = new byte[256];

        Write(model, 64 + StudioLayout.SequenceEventCountOffset, 0);
        Write(model, 64 + StudioLayout.SequenceEventIndexOffset, 128);

        StudioEvent.Read(model, 64).ShouldBeEmpty();
    }

    /// <remarks>
    /// A truncated file must answer nothing rather than reading past its end: a model is untrusted
    /// input like any other, and `numevents` is a number from the file.
    /// </remarks>
    [Test]
    public void Read_AnEventArrayPastTheEndOfTheFile_ReturnsNothing()
    {
        byte[] model = new byte[128];

        Write(model, 0 + StudioLayout.SequenceEventCountOffset, 4);
        Write(model, 0 + StudioLayout.SequenceEventIndexOffset, 120);

        StudioEvent.Read(model, 0).ShouldBeEmpty();
    }

    private static void Write(byte[] into, int at, int value) =>
        BitConverter.GetBytes(value).CopyTo(into, at);

    private static void WriteEvent(
        byte[] into, int at, float cycle, int id, int type, string options)
    {
        BitConverter.GetBytes(cycle).CopyTo(into, at + StudioLayout.EventCycleOffset);
        Write(into, at + StudioLayout.EventIdOffset, id);
        Write(into, at + StudioLayout.EventTypeOffset, type);

        System.Text.Encoding.ASCII.GetBytes(options)
            .CopyTo(into, at + StudioLayout.EventOptionsOffset);
    }
}
