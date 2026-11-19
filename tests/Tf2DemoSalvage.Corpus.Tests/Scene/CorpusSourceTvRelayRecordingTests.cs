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
}
