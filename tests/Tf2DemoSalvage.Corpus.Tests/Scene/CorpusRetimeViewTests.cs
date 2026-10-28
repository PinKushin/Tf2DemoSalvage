using System.IO;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Probe.Probes;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>demo_retimeview</c> on a real POV recording, through the production camera (D238, finding 106).
/// </summary>
/// <remarks>
/// **Restated from "byte-identical with the setting on"**, which cannot hold here: the 2013 specimen is not regular —
/// 125 of its packet steps are stamp 0 and 211 are stamp 2 — and fixing those is the point. What is held instead is the
/// half that can: wherever no packet near a frame was placed off its stamp, the camera <c>SpectatorView.Eye</c> builds
/// is the engine's to the bit; and the setting does move frames near the ones it placed. The comparison is the probe's
/// (<see cref="CameraPathProbe.RetimeIdentity"/>), so the probe and the test cannot disagree about it.
/// </remarks>
public sealed class CorpusRetimeViewTests
{
    private const string Pov2013 = "tf2-2013-build1729296-pov-cp_badlands";

    [Test]
    public void Eye_OnThe2013PovWithRetimeOn_IsTheEnginesCameraWhereverNoPacketNearWasPlaced()
    {
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo(Pov2013)));

        (int Placed, int Same, int ChangedNear, int ChangedFar) identity = CameraPathProbe.RetimeIdentity(timeline, 144);

        identity.Placed.ShouldBeGreaterThan(50, "the control: the specimen has packets the setting placed");
        identity.ChangedFar.ShouldBe(0, "a frame with no placed packet near it is the engine's");
        identity.ChangedNear.ShouldBeGreaterThan(0, "the setting moves frames near the packets it placed");
        identity.Same.ShouldBeGreaterThan(identity.ChangedNear, "most of a calm recording is regular");
    }
}
