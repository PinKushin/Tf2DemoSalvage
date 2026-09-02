using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One animation event a sequence fires, as the model states it.</summary>
/// <param name="Cycle">Where in the sequence it fires, nought to one.</param>
/// <param name="Id">The event id — resolved at load for the new system, literal for the old.</param>
/// <param name="Type">The <c>AE_TYPE_*</c> flags.</param>
/// <param name="Options">The event's argument, as the model wrote it.</param>
/// <remarks>
/// **<c>mstudioevent_t</c>, <c>public/studio.h:495</c>**, read off the SEQUENCE rather than the
/// animation — `mstudioseqdesc_t` carries `numevents`, `eventindex` and `pEvent(i)`
/// (`studio.h:817`), and `mstudioanimdesc_t` has no event members at all.
///
/// **The name is deliberately not read here.** For a new-system event the id in the file is a
/// placeholder until `SetEventIndexForSequence` (`shared/animation.cpp:60`) resolves
/// `pszEventName()` through the shared registry and writes the answer back. That registry is
/// filled by `EventList_RegisterSharedEvents()` at world init, so resolving a name needs the whole
/// event table — a separate piece of work from getting the array off disk. What this type reports
/// is what the FILE says.
/// </remarks>
public readonly record struct StudioEvent(float Cycle, int Id, int Type, string Options)
{
    /// <summary><c>AE_TYPE_CLIENT</c>, <c>shared/eventlist.h:19</c>.</summary>
    public const int ClientType = 1 << 4;

    /// <summary><c>AE_TYPE_NEWEVENTSYSTEM</c>, <c>shared/eventlist.h:21</c>.</summary>
    /// <remarks>Valve's own comment on it is <c>//Temporary flag.</c>, and it is still here.</remarks>
    public const int NewSystemType = 1 << 10;

    /// <summary>The id at and above which an OLD-system event belongs to the client.</summary>
    /// <remarks>
    /// The engine's test is <c>pevent[i].event &lt; 5000</c>, so 5000 itself is the client's. The
    /// client-only ids are listed in <c>client/cl_animevent.h</c> and start at 5001.
    /// </remarks>
    public const int OldSystemClientId = 5000;

    /// <summary>Whether the CLIENT fires this event.</summary>
    /// <returns>Whether <c>DoAnimationEvents</c> would act on it rather than skipping it.</returns>
    public bool FiresOnTheClient() => FiresOnTheClient(Type, Id);

    /// <summary>Whether the client fires an event with these flags and id.</summary>
    /// <param name="type">The event's <c>AE_TYPE_*</c> flags.</param>
    /// <param name="id">The event's id.</param>
    /// <returns>Whether the client acts on it.</returns>
    /// <remarks>
    /// **<c>C_BaseAnimating::DoAnimationEvents</c>, <c>client/c_baseanimating.cpp:3550</c>**, which
    /// applies this filter twice — once to the looped tail and once to the ordinary span:
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
    /// **Two systems, and the second is not a fallback for the first.** A new-system event is
    /// judged only by its flag and its NUMBER is irrelevant; an old-system one has no flags at all,
    /// so its number is the whole test. Reading it as "client flag, or else id ≥ 5000" would fire
    /// server-side new-system events that happen to be numbered high.
    /// </remarks>
    public static bool FiresOnTheClient(int type, int id) =>
        (type & NewSystemType) != 0
            ? (type & ClientType) != 0
            : id >= OldSystemClientId;

    /// <summary>Every event one sequence declares, in the order the model lists them.</summary>
    /// <param name="model">The whole <c>.mdl</c> file.</param>
    /// <param name="sequence">Byte offset of the <c>mstudioseqdesc_t</c>.</param>
    /// <returns>The events, or empty when there are none or the file cannot hold them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <remarks>
    /// **Order is preserved because the engine depends on it.** `DoAnimationEvents` walks the array
    /// forwards and fires everything whose cycle falls in the span since the last check, so two
    /// events at the same cycle fire in file order.
    ///
    /// **`eventindex` is relative to the SEQUENCE**, not to the file — <c>pEvent</c> is
    /// <c>((byte *)this) + eventindex</c> — which is the convention every index in this format
    /// follows and the one that produces silently wrong reads when it is assumed to be absolute.
    ///
    /// **A count from the file is bounded before it is trusted.** `numevents` is untrusted input
    /// like any other number in a model, and a truncated or hostile file must produce nothing
    /// rather than a read past the end.
    /// </remarks>
    public static IReadOnlyList<StudioEvent> Read(byte[] model, int sequence)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (sequence < 0 ||
            sequence > model.Length - StudioLayout.SequenceEventIndexOffset - sizeof(int))
        {
            return [];
        }

        int count = BitConverter.ToInt32(
            model, sequence + StudioLayout.SequenceEventCountOffset);

        int at = sequence + BitConverter.ToInt32(
            model, sequence + StudioLayout.SequenceEventIndexOffset);

        if (count <= 0 || at < 0)
        {
            return [];
        }

        // Checked as a whole rather than per event, so a count that overruns cannot return the
        // prefix that happened to fit and call it the sequence's events.
        long end = (long)at + ((long)count * StudioLayout.EventStride);

        if (end > model.Length)
        {
            return [];
        }

        List<StudioEvent> events = new(count);

        for (int index = 0; index < count; index++)
        {
            int each = at + (index * StudioLayout.EventStride);

            events.Add(new StudioEvent(
                BitConverter.ToSingle(model, each + StudioLayout.EventCycleOffset),
                BitConverter.ToInt32(model, each + StudioLayout.EventIdOffset),
                BitConverter.ToInt32(model, each + StudioLayout.EventTypeOffset),
                OptionsAt(model, each + StudioLayout.EventOptionsOffset)));
        }

        return events;
    }

    /// <summary>The options string, which is sixty-four bytes in place rather than a pointer.</summary>
    /// <remarks>
    /// **Terminated at the first NUL, not at sixty-four.** The field is a fixed buffer and the
    /// engine reads it as a C string (<c>pszOptions</c> returns <c>options</c> directly), so
    /// whatever follows the terminator is stale padding — and padding is not zero
    /// (`docs/memory/padding-is-not-zero.md`), so taking the whole field would append rubbish to
    /// every short option.
    /// </remarks>
    private static string OptionsAt(byte[] model, int at)
    {
        int length = 0;

        while (length < StudioLayout.EventOptionsLength && model[at + length] != 0)
        {
            length++;
        }

        return Encoding.ASCII.GetString(model, at, length);
    }
}
