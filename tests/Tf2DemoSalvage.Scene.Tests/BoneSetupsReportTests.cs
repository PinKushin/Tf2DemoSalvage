using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The entities a frame set bones up for are that frame's (B878), which is what a view reports to its source.</summary>
public sealed class BoneSetupsReportTests
{
    /// <summary>
    /// **Posed in one frame and gone from the next**: named after the first, not after the second — though the model set
    /// still holds its last pose, which is what <see cref="EntityModelSet.IsPosed"/> answers (the control).
    /// </summary>
    [Test]
    public void BonesSetUpSinceWorldPass_AFramesBuilds_NamesOnlyWhatThatFramePosed()
    {
        SceneProp locker = new(7, "models/props_gameplay/resupply_locker.mdl", SceneModelKind.Studio, new ScenePose { X = 100f }, null);
        EntityModelSet models = new();
        HashSet<int> setUp = [];

        models.Add([locker], path => ModelFramesFixture.OneSecondLoop(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) });
        models.Instances([locker], [], seconds: 0d);
        models.BonesSetUpSinceWorldPass(setUp);
        setUp.ShouldBe([7]);

        models.Instances([], [], seconds: 0.015d);
        models.BonesSetUpSinceWorldPass(setUp);
        setUp.ShouldBeEmpty();
        models.IsPosed(7).ShouldBeTrue("the control: the model set still holds the last pose");
    }
}
