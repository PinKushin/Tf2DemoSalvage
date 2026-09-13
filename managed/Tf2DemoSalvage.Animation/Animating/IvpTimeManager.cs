using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>What IVP's time manager queues: an event that remembers its own slot — an <c>IVP_Time_Event</c>'s <c>+0x8</c>.</summary>
public interface IIvpTimeEvent
{
    /// <summary>The event's slot in the queue, or null when it is not queued — the engine's <c>0xffff</c>.</summary>
    public int? QueueSlot { get; set; }
}

/// <summary>
/// IVP's time manager: events drained from a queue in time order up to a target — <c>FUN_18008a110</c> (B369).
/// </summary>
/// <typeparam name="T">What is queued.</typeparam>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *Found: the event loop*, read again for the port). Each event's time
/// is a float relative to <see cref="Base"/>; the environment's clock is set before each event fires and snapped to the
/// target after the last.
/// </remarks>
public sealed class IvpTimeManager<T>
    where T : class, IIvpTimeEvent
{
    /// <summary>The queue, <c>+0x10</c>.</summary>
    public IvpMinList<T> Queue { get; } = new();

    /// <summary>The base the queued times are relative to, <c>+0x28</c>.</summary>
    public double Base { get; set; }

    /// <summary>The last fired event's relative time, <c>+0x20</c>.</summary>
    public double Clock { get; private set; }

    /// <summary>The stop flag — the state at <c>+0x8</c>'s <c>+0x8</c> being <c>1</c> — read after each event fires.</summary>
    public bool Stopping { get; set; }

    /// <summary>Fires every event due before a target — <c>FUN_18008a110</c>.</summary>
    /// <param name="target">The absolute time to run to.</param>
    /// <param name="setEnvironmentTime">
    /// The environment's clock set, <c>FUN_180082460</c>: <c>env+0x1a0 += 1; env+0x188 = time</c>.
    /// </param>
    /// <param name="fire">The event's own slot 1, handed the event.</param>
    /// <exception cref="ArgumentNullException">A callback is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The queue is empty while its <c>1e10f</c> minimum is under the limit, where the engine reads the head from index
    /// <c>0xffff</c>.
    /// </exception>
    /// <remarks>
    /// **Due while the minimum is under `(float)(target − base)` or either is NaN** (`COMISS` with `JNC`, then `JC`). The
    /// minimum is captured before the head is unlinked, and both clocks are set from it; the stop flag is read after the
    /// event fires, so an event can end the run; the environment's clock is snapped to the target either way.
    /// </remarks>
    public void Run(double target, Action<double> setEnvironmentTime, Action<T> fire)
    {
        ArgumentNullException.ThrowIfNull(setEnvironmentTime);
        ArgumentNullException.ThrowIfNull(fire);

        float minimum = Queue.Minimum;

        while (IsDue(minimum, target))
        {
            if (!Queue.TryFirst(out T? due, out int slot))
            {
                throw new InvalidOperationException("The event loop reads the head of an empty queue, from index 0xffff.");
            }

            Queue.Remove(slot);
            due.QueueSlot = null;

            Clock = minimum;
            setEnvironmentTime(Clock + Base);
            fire(due);

            if (Stopping)
            {
                break;
            }

            minimum = Queue.Minimum;
        }

        setEnvironmentTime(target);
    }

    private bool IsDue(float minimum, double target)
    {
        float limit = (float)(target - Base);
        return minimum < limit || float.IsNaN(minimum) || float.IsNaN(limit);
    }
}
