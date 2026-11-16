namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// A panel on `ivgui()->AddTickSignal`'s list: its `OnTick` runs every frame, before the panels think, whether or not it is
/// visible (vgui's `RunFrame`). Held by <see cref="HudViewport.AddTickSignalToHead"/>.
/// </summary>
public interface IVguiTickSignal
{
    /// <summary>`Panel::OnTick`.</summary>
    public void OnTick();
}
