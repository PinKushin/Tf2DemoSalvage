using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Logging;
using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation;

public sealed partial class ViewerSession
{
    /// <summary>Renders continuously for as long as Windows has nothing else for this thread.</summary>
    /// <remarks>
    /// **This loop is the render loop; the event only starts it.** <c>Application.Idle</c> fires
    /// once when the queue empties and not again until a message arrives and is dispatched, so a
    /// handler that draws one frame and returns draws at whatever rate Windows happens to be
    /// posting messages — irregular by nature, which is what the jitter was. Staying here while
    /// the queue is empty is the documented shape and it is also the engine's: Source pumps
    /// messages and then runs a frame, over and over, rather than waiting to be asked.
    ///
    /// Yielding the moment anything arrives is what keeps the UI responsive. The mistake before
    /// this one was the opposite — a timer invalidating the viewport, which queued paint messages
    /// faster than the pump could drain them and made the mouse sluggish over the transport bar.
    /// </remarks>
    public void Idle(IIdleHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        uint? waiting;

        // A cross-thread caller (UI Automation, over COM) sends its next call right after our reply.
        // Serve it before drawing, or each call waits a whole frame.
        if (FramePacer.AwaitsTraffic(_idleEndedBy, _burstDrew) && host.ArrivesWithin(FramePacer.TrafficWaitSeconds))
        {
            _idleEndedBy = host.Waiting() ?? 0;
            _burstDrew = false;
            _frames.Yielded();
            return;
        }

        bool drew = false;

        do
        {
            // **`engine_no_focus_sleep`, before the frame decision rather than inside the wait**
            // (B209). The engine sleeps because nobody is looking, which is a different question
            // from whether the next frame is due — putting it in `WaitForTheNextFrame` would mean an
            // unfocused window still rendered at full rate whenever a frame WAS due, which is every
            // frame at any reachable limit.
            // On activation, not focus: CEngine::Frame (engine.dll FUN_18020cbd0) sleeps when `!IsActiveApp()`.
            int idle = FramePacer.NoFocusSleep(_window.IsActiveApp, MeasuredNoFocusSleep());

            if (idle > 0)
            {
                host.Sleep(idle);
            }

            if (_clock.IsDue(_settingsOwner.FrameRateLimit))
            {
                // **Counted only when something was drawn.** RenderFrame declines during a map
                // read, and counting those turned the per-second report into a measurement of how
                // fast an empty loop spins — 186 "frames a second" with every duration at zero.
                if (RenderFrame())
                {
                    drew = true;
                    _frames.Drew(
                        _clock.LastFrameSeconds,
                        new FrameView(
                            _transport.Playing,
                            _camera.FreeLook && _console.AnyHeld,

                            // The one part of the line a second frontend could not produce: a
                            // Windows message id, named by the window that received it.
                            MessageName(_idleEndedBy)));
                }
            }
            else
            {
                WaitForTheNextFrame(host);
            }

            // The id of whatever ended this burst, kept for the per-second report. See B148: the
            // viewer drops to twenty frames a second after a demo switch, and because this loop
            // runs only while the queue is empty, that is a statement about who is posting messages
            // rather than about how long a frame takes.
            // Null, not zero: a WM_NULL is a message to yield to, see MessageQueue.Waiting.
            waiting = host.Waiting();
        }
        while (waiting is null);

        _idleEndedBy = waiting.Value;
        _burstDrew = drew;
        _frames.Yielded();
    }

    /// <summary>Whether the burst that <see cref="_idleEndedBy"/> ended drew a frame: its paint is then the frame's own (finding 88).</summary>
    private bool _burstDrew;

    /// <summary>Names the Windows messages worth recognising in a render report.</summary>
    /// <remarks>
    /// Only the ones that plausibly arrive in a tight loop. Anything else is reported as its number,
    /// which is enough to look up and better than a name this table guessed at.
    /// </remarks>
    private static string MessageName(uint message) => message switch
    {
        0x0000 => "nothing",
        0x000F => "WM_PAINT",
        0x0113 => "WM_TIMER",
        0x0200 => "WM_MOUSEMOVE",
        0x0007 => "WM_SETFOCUS",
        0x0008 => "WM_KILLFOCUS",
        0x0014 => "WM_ERASEBKGND",
        0x0018 => "WM_SHOWWINDOW",
        0x0046 => "WM_WINDOWPOSCHANGING",
        0x0047 => "WM_WINDOWPOSCHANGED",
        0x0084 => "WM_NCHITTEST",
        0x0020 => "WM_SETCURSOR",
        0x8000 => "WM_APP (a posted callback)",
        _ => $"message 0x{message:X4}",
    };

    /// <summary>`engine_no_focus_sleep` as this frame takes it: zero for a `--measure` run, whatever set it.</summary>
    /// <remarks>
    /// **Held where the sleep is READ**, so neither the saved settings nor a `.cfg` executed after launch can put it back: a
    /// viewer an agent launches never takes focus, and the 50 ms sleep would then BE the measurement. Said once per value.
    /// </remarks>
    private int MeasuredNoFocusSleep()
    {
        int set = _settingsOwner.NoFocusSleep;

        if (_launch.MeasureSeconds is null)
        {
            return set;
        }

        if (set > 0 && set != _focusSleepIgnored)
        {
            _focusSleepIgnored = set;
            _log.LogInformation("{Message}", $"{Tf2DemoSalvage.Scene.ViewerSettings.NoFocusSleepCommand} {set} ignored while measuring");
        }

        return 0;
    }

    /// <summary>The last `engine_no_focus_sleep` a measurement said it ignored, so the line is written once per value.</summary>
    private int _focusSleepIgnored;

    /// <summary>The message that last ended a render burst.</summary>
    private uint _idleEndedBy;

    /// <summary>Waits until the next frame is due, without burning a core doing it.</summary>
    /// <remarks>
    /// **Sleep when there is time to spare, yield when there is not.** A low cap - 24 or 30 for
    /// recording - has tens of milliseconds of budget and can afford a real sleep, which keeps the
    /// processor idle. A high cap has less budget than the clock's own granularity, so sleeping
    /// overshoots it entirely and the only accurate wait is to give up the timeslice and check
    /// again.
    ///
    /// This is a wait on a CLOCK, which is the one thing a frame limiter can legitimately do:
    /// there is no condition to synchronise on, because the condition is the passage of time.
    /// </remarks>
    private void WaitForTheNextFrame(IIdleHost host)
    {
        // **`FramePacer` decides, this acts** (B208). The threading primitive stays beside the
        // message pump; the policy — including the granularity threshold — is testable without any
        // test ever sleeping.
        switch (_clock.WaitFor(_settingsOwner.FrameRateLimit))
        {
            case FrameWait.Sleep:
                host.Sleep(1);
                break;

            case FrameWait.Yield:
                host.Yield();
                break;

            default:
                break;
        }
    }
}
