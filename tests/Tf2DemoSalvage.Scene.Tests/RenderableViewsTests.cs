namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The view identities the renderables are collated and their glows queried by (B711).</summary>
public sealed class RenderableViewsTests
{
    /// <remarks>
    /// **Only <c>CReflectionView</c> is <c>VIEW_REFLECTION</c>** (`viewrender.cpp:5705-5751`): its id is 1 + its kind, and
    /// every other water view, the main view (0), the sky and the monitor are not.
    /// </remarks>
    [Test]
    public void IsReflection_EachView_IsTrueForTheReflectionAlone()
    {
        RenderableViews.IsReflection(RenderableViews.WaterGlowView(WaterViewKind.Reflection)).ShouldBeTrue();

        foreach (WaterViewKind kind in System.Enum.GetValues<WaterViewKind>())
        {
            if (kind != WaterViewKind.Reflection)
            {
                RenderableViews.IsReflection(RenderableViews.WaterGlowView(kind)).ShouldBeFalse(kind.ToString());
            }
        }

        (RenderableViews.IsReflection(0), RenderableViews.IsReflection(RenderableViews.SkyGlowView),
            RenderableViews.IsReflection(RenderableViews.MonitorGlowView)).ShouldBe((false, false, false));
    }
}
