namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// A panel's place on its viewport's tick list. vgui's list is global, so a panel joins it in its constructor whatever its
/// parent; ours is the viewport's, so a panel made before it hangs under one (as a `.res` makes them) joins when it first
/// can — <see cref="AddOnce"/>, called from its scheme pass.
/// </summary>
/// <param name="panel">The panel, whose viewport holds the list.</param>
/// <param name="signal">Its `OnTick`.</param>
/// <param name="intervalMilliseconds">`AddTickSignal`'s interval; 0 ticks every frame.</param>
internal sealed class HudTickSignal(VguiPanel panel, IVguiTickSignal signal, int intervalMilliseconds)
{
    private HudViewport? _viewport;

    /// <summary>`ivgui()->AddTickSignal( GetVPanel(), interval )`: joins the list, or restarts the interval when listed.</summary>
    public void Add()
    {
        _viewport = HudViewport.Of(panel);
        _viewport?.AddTickSignal(signal, intervalMilliseconds);
    }

    /// <summary>Joins the list unless it already has.</summary>
    public void AddOnce()
    {
        if (_viewport is null)
        {
            Add();
        }
    }

    /// <summary>`ivgui()->RemoveTickSignal( GetVPanel() )`.</summary>
    public void Remove()
    {
        _viewport?.RemoveTickSignal(signal);
        _viewport = null;
    }
}
