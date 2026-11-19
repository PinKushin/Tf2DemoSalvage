using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A demo a client recorded while connected to a SourceTV relay plays as SourceTV, free camera included (B775).
/// </summary>
/// <remarks>
/// **Reported by a user**: on the PC Gamer 2008 badlands demos the view was *"stuck at a fixed point and i cant move
/// around"*. Each packet's <c>democmdinfo_t</c> is the relay client's own, (0, 0, 50) throughout, and the viewer took a
/// non-zero view for a point-of-view recording, locking the camera there. <c>svc_ServerInfo</c>'s <c>is_hltv</c> is set,
/// and <c>CViewRender::SetUpViews</c> draws through the HLTV camera whenever it is (view.cpp:660-663). Output level: the
/// refusal the camera presenter asks, on the real files.
/// </remarks>
public sealed class CorpusSourceTvRelayRecordingTests
{
    [TestCase("pcgs_4k-vs-tcm-28092008-badlands-srctv")]
    [TestCase("pcgs_tcm-vs-crack-28092008-badlands-srctv")]
    public void Refuses_TheFreeCameraOnARelayClientsRecording_IsNothing(string demo)
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(demo));
        SpectatorView view = new(NullLogger.Instance) { Eyes = new TimelineEyes(timeline, new DemoPlayer(timeline)) };

        timeline.IsSourceTv.ShouldBeTrue("the condition: svc_ServerInfo says SourceTV");
        timeline.HasRecordedView.ShouldBeFalse();
        view.Refuses(CameraMode.Free).ShouldBeNull();
        view.Refuses(CameraMode.ThirdPerson).ShouldBeNull();
    }

    [TestCase("pcgs_4k-vs-tcm-28092008-badlands-srctv")]
    public void Eye_FirstPersonOnARelayClientsRecording_IsTheSpectatedPlayersNotTheRelayView(string demo)
    {
        // First person reads the demo player's viewpoint, not HasRecordedView (B775 review): the eye must be the
        // spectated player's (CalcInEyeCamView, hltvcamera.cpp:315), never the relay client's (0, 0, 50).
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(demo));
        SpectatorView view = new(NullLogger.Instance)
        {
            Eyes = new TimelineEyes(timeline, new DemoPlayer(timeline)),
            Interpolating = () => false,
        };

        int tick = FirstTickWithALiveTarget(timeline, view);
        view.Eyes.ViewpointAt(tick).ShouldBeNull();

        int target = view.Target(tick).ShouldNotBeNull().EntityIndex;
        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players, interpolating: false);
        ScenePlayer drawn = players.Find(player => player.EntityIndex == target);

        FreeCamera eye = view.Eye(tick, 1f).ShouldNotBeNull();
        eye.Origin.X.ShouldBe(drawn.X, 0.001f);
        eye.Origin.Y.ShouldBe(drawn.Y, 0.001f);
        (eye.Origin.X, eye.Origin.Y).ShouldNotBe((0f, 0f), "the control: the relay view's X and Y");
    }

    private static int FirstTickWithALiveTarget(DemoTimeline timeline, SpectatorView view)
    {
        for (int frame = timeline.Frames.Count / 2; frame < timeline.Frames.Count; frame++)
        {
            if (view.Target(timeline.Frames[frame].Tick) is { IsAlive: true })
            {
                return timeline.Frames[frame].Tick;
            }
        }

        Assert.Fail("no live target in the second half");
        return 0;
    }
}
