using System.Numerics;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// `FX_TracerSound( start, end, TRACER_TYPE_DEFAULT )` (`clientsideeffects_test.cpp:211`): a tracer whizzes when it passes
/// within 24 units of the listener, whose point slides down a 24-unit segment below the eye toward the bullet. Missing
/// entirely until f12 was compared with TF2 from gummo's eyes (13770-14100: one `nearmiss\bulletltor`, ours none).
/// </summary>
public sealed class TracerWhizTests
{
    private static readonly Vector3 Eye = new(0, 0, 64);

    [Test]
    public void Hears_ATracerPassing20UnitsBeside_Whizzes()
    {
        TracerWhiz.Hears(new Vector3(-500, 20, 64), new Vector3(500, 20, 64), Eye).ShouldBeTrue();
    }

    [Test]
    public void Hears_ATracerPassing30UnitsBeside_IsSilent()
    {
        TracerWhiz.Hears(new Vector3(-500, 30, 64), new Vector3(500, 30, 64), Eye).ShouldBeFalse();
    }

    [Test]
    public void Hears_ATracerPassingAtTheChest_WhizzesBecauseTheListenerSlidesDown()
    {
        // 20 units below the eye is 20 units from it, but the listener moves down to meet the bullet — `LISTENER_HEIGHT`.
        TracerWhiz.Hears(new Vector3(-500, 10, 44), new Vector3(500, 10, 44), Eye).ShouldBeTrue();
        TracerWhiz.Hears(new Vector3(-500, 10, 14), new Vector3(500, 10, 14), Eye).ShouldBeFalse("50 below is past the 24 it slides");
    }

    [Test]
    public void Hears_ATracerThatEndsBeforeReachingTheListener_IsSilent()
    {
        TracerWhiz.Hears(new Vector3(-500, 0, 64), new Vector3(-100, 0, 64), Eye).ShouldBeFalse();
    }
}
