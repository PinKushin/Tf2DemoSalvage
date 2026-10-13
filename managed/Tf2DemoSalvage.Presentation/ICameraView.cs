namespace Tf2DemoSalvage.Presentation;

/// <summary>What the camera asks of the window it is drawn in (D214).</summary>
/// <remarks>
/// **Each member is a place the camera's code reached into the window when it lived there**, called at that same
/// moment by <see cref="CameraPresenter"/> [no engine counterpart: the engine's view code and its window are one
/// program]. No member takes or returns a domain type.
/// </remarks>
public interface ICameraView
{
    /// <summary>Says why a camera could not be entered, on the status line; null says nothing new.</summary>
    /// <param name="status">The words.</param>
    public void Say(string? status);

    /// <summary>The camera changed: the world's projection and the viewport are stale.</summary>
    public void CameraChanged();

    /// <summary>Lets go of every key recorded as held, so a re-placed camera does not move at once.</summary>
    public void ReleaseHeldKeys();

    /// <summary>Ends a headless run that cannot have its subject: stderr, exit 1, close. A person's window does nothing.</summary>
    /// <param name="reason">What went wrong.</param>
    public void FailHeadless(string reason);
}
