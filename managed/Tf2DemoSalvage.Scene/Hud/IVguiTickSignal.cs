namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// A panel on `ivgui()->AddTickSignal`'s list: its `OnTick` runs before the panels think, whether or not it is visible,
/// each frame or on its interval (vgui's `RunFrame`). Held by <see cref="HudViewport.AddTickSignal(IVguiTickSignal, int)"/> and
/// <see cref="HudViewport.AddTickSignalToHead"/>.
/// </summary>
public interface IVguiTickSignal
{
    /// <summary>`Panel::OnTick`.</summary>
    public void OnTick();
}
