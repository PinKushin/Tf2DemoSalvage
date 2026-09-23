namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The order the UI fixtures run in, in one place (D189).</summary>
/// <remarks>
/// **Every fixture but `PlaybackUiTests` is ordered, so the playback test runs last.** NUnit starts an `[Order]`ed fixture
/// BEFORE every unordered one, so the one way to put a fixture last is to order all the others. The owner: *"the only way to
/// force the playback test to be last is to use the order property and basically order everything but the playback
/// test"*. Playback also seeks the shared viewer back to the tick it found; the two are separate safeties, and a new fixture
/// added without an order runs among the unordered ones, where the seek covers it.
/// </remarks>
public static class UiFixtureOrder
{
    public const int Capture = 1;
    public const int FirstPerson = 2;
    public const int LoadingOverlay = 3;
    public const int MapFullScreen = 4;
    public const int PositionReadout = 5;
    public const int Shell = 6;
    public const int StartupSplash = 7;
    public const int ThirdPerson = 8;
    public const int Transport = 9;
    public const int ViewportPicture = 10;
    public const int Wiring = 11;
}
