using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Where an IVP object sits inside its core — <c>object+0x60</c>, the offset every transform the engine hands
/// out for the object composes back in (B403).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `vphysics.dll`** (`docs/findings/51`, *An IVP object's core sits at the hull's
/// mass center*): `FUN_180074380` stores `−massCenter` there, and `FUN_1800734e0`, `FUN_180032740` and
/// `FUN_180037620` all put the object's own points through the core's transform only after adding it. The
/// core-object rotation is the identity for every object `FUN_180073df0` creates, so a point in the object's
/// frame is in the core's frame once the offset is added.
/// </remarks>
public sealed class IvpRigidBodyObjectOffsetConformanceTests
{
    /// <remarks>
    /// **A hull point is stored in the object's frame and reaches the core's by the offset.** `(1, 2, 3)` with
    /// an offset of `(−1, 0.5, 0)` is `(0, 2.5, 3)` in the core's frame.
    /// </remarks>
    [Test]
    public void CoreHullPoint_WithAnObjectOffset_AddsItToThePoint()
    {
        IvpRigidBody body = new()
        {
            Hull = [(1f, 2f, 3f)],
            ObjectOffset = (-1f, 0.5f, 0f),
        };

        body.CoreHullPoint(0).ShouldBe((0f, 2.5f, 3f));
    }

    /// <remarks>
    /// **The object's own origin is the core's position plus the TURNED offset** — what the engine reports as
    /// the object's position. A core at `(10, 20, 30)` turned a quarter about Z, with the object a unit back
    /// along the core's X, has its origin a unit back along world Y: `(10, 19, 30)`.
    /// </remarks>
    [Test]
    public void ObjectOrigin_OfATurnedCoreWithAnOffset_IsTheCorePlusTheTurnedOffset()
    {
        IvpRigidBody body = new()
        {
            Position = (10d, 20d, 30d),
            Orientation = (0f, 0f, 0.70710677f, 0.70710677f),
            ObjectOffset = (-1f, 0f, 0f),
        };

        (double X, double Y, double Z) origin = body.ObjectOrigin();

        origin.X.ShouldBe(10d, 1e-5d);
        origin.Y.ShouldBe(19d, 1e-5d);
        origin.Z.ShouldBe(30d, 1e-5d);
    }
}
