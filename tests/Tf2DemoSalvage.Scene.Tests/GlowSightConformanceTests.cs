using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `GlowSightDistance( glowOrigin, true )` (`c_pixel_visibility.cpp:783`), the line of sight
/// `PixelVisibility_FractionVisible` falls back to (`:825`): a trace from the view origin to the glow, pulled 4 units back
/// along the view direction "in case the glow is inside some parent object"; any stop short of the end hides it.
/// </summary>
public sealed class GlowSightConformanceTests
{
    private static readonly Vector3 Eye = Vector3.Zero;
    private static readonly Vector3 Forward = Vector3.UnitX;

    [Test]
    public void Visible_AClearLine_SeesTheGlow()
    {
        GlowSight.Visible(Eye, Forward, new Vector3(100f, 0f, 0f), static (_, _) => true).ShouldBeTrue();
    }

    /// <remarks>A roof between the eye and a lamp below it: the trace stops, and the glow is hidden.</remarks>
    [Test]
    public void Visible_ABlockedLine_HidesTheGlow()
    {
        GlowSight.Visible(Eye, Forward, new Vector3(100f, 0f, 0f), static (_, _) => false).ShouldBeFalse();
    }

    [Test]
    public void Visible_AFarGlow_IsTracedToFourUnitsShortAlongTheView()
    {
        Vector3 end = default;

        GlowSight.Visible(Eye, Forward, new Vector3(100f, 0f, 0f), (_, to) => { end = to; return true; });

        end.ShouldBe(new Vector3(96f, 0f, 0f));
    }

    /// <remarks>Within 4 units the end is the glow itself.</remarks>
    [Test]
    public void Visible_ANearGlow_IsTracedToItself()
    {
        Vector3 end = default;

        GlowSight.Visible(Eye, Forward, new Vector3(3f, 0f, 0f), (_, to) => { end = to; return true; });

        end.ShouldBe(new Vector3(3f, 0f, 0f));
    }
}
