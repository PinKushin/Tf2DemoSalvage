namespace Tf2DemoSalvage.Scene;

/// <summary>How a frame's 3D view is drawn, as <c>CViewRender::RenderView</c> decides it (B738).</summary>
public enum FreezeFrameDraw
{
    /// <summary>Drawn as ever.</summary>
    Normal,

    /// <summary>Drawn as ever, then copied to <c>_rt_FullScreen</c> after post-processing and before the HUD.</summary>
    Take,

    /// <summary>
    /// The copy is shown in place of the whole 3D view (<c>CFreezeFrameView</c>), and the HUD still paints over it.
    /// </summary>
    Hold,
}

/// <summary>The engine's freeze frame: <c>CViewRender::FreezeFrame</c> and the branch <c>RenderView</c> opens with (B738).</summary>
/// <remarks>
/// <code>
/// if ( flFreezeTime == 0 ) { m_flFreezeFrameUntil = 0; m_rbTakeFreezeFrame[ i ] = false; }
/// else if ( m_flFreezeFrameUntil &gt; gpGlobals-&gt;curtime ) m_flFreezeFrameUntil += flFreezeTime;
/// else { m_flFreezeFrameUntil = gpGlobals-&gt;curtime + flFreezeTime; m_rbTakeFreezeFrame[ i ] = true; }
/// </code>
/// (<c>viewrender.cpp:1989-2017</c>), and each frame <c>if ( !m_rbTakeFreezeFrame &amp;&amp; m_flFreezeFrameUntil &gt;
/// gpGlobals-&gt;curtime )</c> draws the held copy, else draws the view and, when a frame is to be taken, copies it and
/// clears the flag (<c>:2059</c>, <c>:2218-2240</c>). Every time is <c>gpGlobals-&gt;curtime</c>, so a paused demo holds
/// for as long as it is paused. One eye: the viewer draws no stereo.
/// </remarks>
public sealed class FreezeFrame
{
    /// <summary><c>m_flFreezeFrameUntil</c>.</summary>
    private double _until;

    /// <summary><c>m_rbTakeFreezeFrame</c>.</summary>
    private bool _take;

    /// <summary><c>FreezeFrame( flFreezeTime )</c> with a time: hold for that long, or that much longer when holding.</summary>
    /// <param name="seconds">How long; <c>spec_freeze_time</c> from the freeze cam.</param>
    /// <param name="curTime"><c>gpGlobals->curtime</c>.</param>
    public void Freeze(float seconds, double curTime)
    {
        if (_until > curTime)
        {
            _until += seconds;
            return;
        }

        _until = curTime + seconds;
        _take = true;
    }

    /// <summary><c>FreezeFrame( 0 )</c>: no hold and nothing to take — leaving the freeze cam, and <c>LevelInit</c>.</summary>
    public void Release()
    {
        _until = 0d;
        _take = false;
    }

    /// <summary>This frame's decision after the view's own `FreezeFrame` calls since the last frame (B738).</summary>
    /// <param name="view">The spectator view whose camera was just computed.</param>
    /// <returns>Whether the 3D view is drawn, taken or held.</returns>
    /// <remarks>
    /// Only a point-of-view recorder's freeze cam calls it: a SourceTV demo's local player is never in one
    /// (`c_baseplayer.cpp:642-682`). *Interpolated:* `spec_freeze_time` is its TF2 default, 4, where a server may have
    /// replicated another.
    /// </remarks>
    public FreezeFrameDraw After(SpectatorView view)
    {
        System.ArgumentNullException.ThrowIfNull(view);

        (bool started, bool ended, bool endedLast) = view.TakeFreezeFrameCalls();

        // In the order they were made: an end after a start leaves no hold (B738 review).
        if (ended && !endedLast)
        {
            Release();
        }

        if (started)
        {
            Freeze(ObserverCamera.FreezeTime, view.CurTime);
        }

        if (ended && endedLast)
        {
            Release();
        }

        return Next(view.CurTime);
    }

    /// <summary>How this frame's 3D view is drawn, taking the frame when one is due.</summary>
    /// <param name="curTime"><c>gpGlobals->curtime</c>.</param>
    /// <returns>The decision.</returns>
    public FreezeFrameDraw Next(double curTime)
    {
        if (!_take && _until > curTime)
        {
            return FreezeFrameDraw.Hold;
        }

        if (!_take)
        {
            return FreezeFrameDraw.Normal;
        }

        _take = false;

        return FreezeFrameDraw.Take;
    }
}
