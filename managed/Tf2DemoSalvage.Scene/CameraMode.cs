namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which camera the viewport is drawn through.
/// </summary>
/// <remarks>
/// **This was a boolean, and a boolean cannot hold three answers.** The viewer had a map view and a
/// free camera with a <c>_freeLook</c> flag between them; a first-person view is a third, so the
/// flag becomes a mode. Written as an enum rather than two booleans because two booleans have four
/// states and one of them is nonsense — and it is exactly the nonsense state a keyboard shortcut
/// reaches when two toggles are pressed in the wrong order.
/// </remarks>
public enum CameraMode
{
    /// <summary>
    /// Looking down at the map, which is how a demo is normally watched here.
    /// </summary>
    /// <remarks>
    /// First rather than merely default: this is the view that works on every demo, including the
    /// ones with no recorded camera and no players worth following.
    /// </remarks>
    Map,

    /// <summary>Flying anywhere, looking anywhere.</summary>
    Free,

    /// <summary>
    /// Through a player's eyes — the recorder's on a point-of-view demo, a chosen player's
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// **One mode covering two mechanisms, because that is what it is to the person watching.**
    /// A point-of-view demo carries the camera the client computed, in <c>democmdinfo_t</c>, and it
    /// already accounts for death, spectating and every observer mode. A SourceTV demo carries no
    /// camera at all, so the view is built from the spectated player's own position and eye angles
    /// — which is what the engine does when you spectate in game.
    /// </remarks>
    FirstPerson,
}
