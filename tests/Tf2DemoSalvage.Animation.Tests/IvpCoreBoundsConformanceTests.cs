using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>What the searches read of a core — <c>+0x4</c>, <c>+0x54</c>, <c>+0x80</c>, <c>+0x1dc</c>, <c>+0x254</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly, 2026-09-16.** `IvpObjectTemplate::ConstructCore` sets the radius (`FUN_180078b90`) and then runs
/// `FUN_180076f80`, whose last store is `+0x54 = 0.5f / +0x4` — for a static core as for a moving one. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpCoreBoundsConformanceTests
{
    /// <remarks>`0.5f / 0.25f`: two.</remarks>
    [Test]
    public void InverseDiameter_ACoreWithARadius_IsHalfOverIt() =>
        new IvpRigidBody { Radius = 0.25f }.InverseDiameter.ShouldBe(2f);

    /// <remarks>
    /// **The bounds a search is handed carry the core's own inverse diameter and angular bound.** They were written zero, as fields
    /// no range slot reads — but the vertex-face search's edge target is `−(0.1·d · face core+0x54)`, and zero there made a flat edge
    /// touch at the interval's start, so a crate on a static pallet collided a millimetre-scale gap late.
    /// </remarks>
    [Test]
    public void Bounds_ACore_CarriesItsInverseDiameterAndAngularSpeedBound()
    {
        IvpRigidBody core = new() { Radius = 0.25f, AngularSpeedBound = 3f, LinearSpeed = 4f, SurfaceSpeedBound = 5f };

        IvpRangeManager.Bounds(core).ShouldBe(new IvpCoreBounds(0.25f, 2f, 3f, 4f, 5f));
    }
}
