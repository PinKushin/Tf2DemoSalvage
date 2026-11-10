using System.Collections.Generic;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The frame's sub-views in draw order, as their glows are asked and issued (B716).</summary>
public sealed class GlowViewStepTests
{
    /// <remarks>
    /// **`RenderView`'s order** (`viewrender.cpp:2074-2081`): every monitor before the scene, in `DrawMonitors`' walk
    /// (`:3274-3290`), each camera's water views before its world view (`DrawWorldAndEntities`); then the 3D sky, then the main
    /// view's water views. Two cameras with one water view each, a sky and one main water view.
    /// </remarks>
    [Test]
    public void Of_TwoCamerasEachWithAWaterViewASkyAndAMainWaterView_IsTheFramesDrawOrder()
    {
        RenderableView view = new([], default);
        List<GlowViewStep> steps = [];

        GlowViewStep.Of(
            new RenderableViews(
                [], null, default, [view, view, view], [new MonitorRenderableView(view, 1), new MonitorRenderableView(view, 1)],
                SkyCamera: new FreeCamera()),
            steps);

        steps.ShouldBe([
            new GlowViewStep(GlowViewKind.Water, 0), new GlowViewStep(GlowViewKind.Monitor, 0),
            new GlowViewStep(GlowViewKind.Water, 1), new GlowViewStep(GlowViewKind.Monitor, 1),
            new GlowViewStep(GlowViewKind.Sky, 0), new GlowViewStep(GlowViewKind.Water, 2)]);
    }
}
