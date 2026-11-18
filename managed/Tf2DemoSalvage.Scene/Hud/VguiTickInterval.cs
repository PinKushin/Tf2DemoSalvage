namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// One entry of `ivgui()->AddTickSignal( panel, intervalMilliseconds )`'s schedule. *Read from disassembly* (`vgui2.dll`
/// x64): `AddTickSignal` (`0x180019fd0`) sets the next tick to `GetTimeMillis() + interval`, and so does adding a panel
/// already listed; `CVGui::RunFrame` (`0x18001b200`) ticks an entry with no interval every frame, skips one while
/// `now &lt; next`, and otherwise sets `next = interval + now` before `OnTick`, so the next is counted from the tick that
/// fired, not from the one that was due.
/// </summary>
/// <param name="intervalMilliseconds">The interval; 0 ticks every frame.</param>
public sealed class VguiTickInterval(int intervalMilliseconds)
{
    private int _next;
    private bool _started;

    /// <summary>
    /// `system()->GetTimeMillis()`, the clock vgui's tick signals run on (`CSystem::GetTimeMillis`, vgui2.dll `0x1800186d0`:
    /// `GetCurrentTime()` times 1000.0, truncated by `CVTTSD2SI`).
    /// </summary>
    /// <param name="realTime">`gpGlobals->realtime`, seconds. *Interpolated:* the same clock as `GetCurrentTime`.</param>
    /// <returns>Milliseconds.</returns>
    public static int TimeMillis(double realTime) => (int)(realTime * 1000.0);

    /// <summary>`AddTickSignal`: the first tick is one interval from now.</summary>
    /// <param name="nowMilliseconds">`system()->GetTimeMillis()`.</param>
    public void Start(int nowMilliseconds)
    {
        _next = nowMilliseconds + intervalMilliseconds;
        _started = true;
    }

    /// <summary>`RunFrame`'s test for this entry; a schedule never started starts here and does not tick.</summary>
    /// <param name="nowMilliseconds">`system()->GetTimeMillis()`.</param>
    /// <returns>Whether `OnTick` runs this frame.</returns>
    public bool Due(int nowMilliseconds)
    {
        if (!_started)
        {
            Start(nowMilliseconds);
            return intervalMilliseconds == 0;
        }

        if (intervalMilliseconds == 0)
        {
            return true;
        }

        if (nowMilliseconds < _next)
        {
            return false;
        }

        _next = intervalMilliseconds + nowMilliseconds;
        return true;
    }
}
