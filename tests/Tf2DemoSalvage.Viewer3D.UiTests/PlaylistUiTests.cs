using System;

using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Opening demos from the playlist.
/// </summary>
/// <remarks>
/// **Switching demos had no UI coverage at all**, on an application whose entire purpose is opening
/// them. The session used to launch with one file on the command line, which `MainForm` opens
/// directly — so the playlist was built, displayed, and never used by any test.
///
/// **Every test here restores the session's demo**, because fixture order within an assembly is not
/// guaranteed and other fixtures assert against the point-of-view recording. A leaked demo would
/// fail them for a reason nothing in them mentions.
///
/// **Kept small on purpose.** Loading the match demo is expensive — B146 measures the timeline build
/// at 4.9 s — so the demo picked is the smallest one that still changes maps. What a real match
/// would buy is recorded in B147. What is left here is the switching itself, which cannot be
/// shared, because switching is the thing under test.
/// </remarks>
public sealed class PlaylistUiTests
{
    private static ViewerApplication Viewer => ViewerSession.App;

    [TearDown]
    public void RestoreTheSessionsDemo()
    {
        if (Viewer.LastLine(ViewerSession.OpeningLine)?.Contains(
                ViewerSession.SecondDemoName, StringComparison.Ordinal) == true)
        {
            ViewerSession.LoadFromPlaylist(ViewerSession.DemoName);
        }
    }

    [Test]
    public void Playlist_ActivatingEachDemoInTurn_LoadsThemAndTheirMaps()
    {
        // **The load path a person uses, there and back, in one test.** Switching away and switching
        // back were two tests for a while, and the second one is the control for the first — a
        // viewer that opened the other demo and then could not return would pass "it opened the
        // other demo" perfectly. Neither is worth having alone.
        //
        // **They were merged because a switch costs about twenty seconds** and the two tests
        // together were four of them. Measured breakdown is in B146; it is dominated by reading a
        // map's surfaces and textures, not by the demo. One test is two switches instead of four,
        // for the same two claims.
        //
        // **The world count is checked as well as the file name**, because "it opened the file"
        // without a world is a demo that was read and then failed to draw — a real failure mode
        // here, since the two demos are on different maps and each map has to be found and loaded.
        int worlds = Viewer.Count(ViewerSession.WorldBuildLine);

        ViewerSession.LoadFromPlaylist(ViewerSession.SecondDemoName);

        Viewer.LastLine(ViewerSession.OpeningLine).ShouldNotBeNull();
        Viewer.LastLine(ViewerSession.OpeningLine)!
            .ShouldContain(ViewerSession.SecondDemoName, Case.Sensitive);

        Viewer.Count(ViewerSession.WorldBuildLine).ShouldBeGreaterThan(
            worlds, "the second demo's map should have been built");

        int afterSecond = Viewer.Count(ViewerSession.WorldBuildLine);

        ViewerSession.LoadFromPlaylist(ViewerSession.DemoName);

        Viewer.LastLine(ViewerSession.OpeningLine)!
            .ShouldContain(ViewerSession.DemoName, Case.Sensitive);

        Viewer.Count(ViewerSession.WorldBuildLine).ShouldBeGreaterThan(
            afterSecond, "and the first demo's map should have been built again");
    }
}
