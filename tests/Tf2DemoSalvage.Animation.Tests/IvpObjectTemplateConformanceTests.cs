using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// What vphysics hands IVP for a polygon object, and the mass and rotational inertia IVP gives its core
/// from that (B403).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `vphysics.dll`** — `docs/findings/51`, *An IVP object's core sits at the
/// hull's mass center*.
///
/// <code>
///   FUN_18001c9d0  objectparams_t → template
///     +0x20  mass     = MINSS(MAXSS(params.mass, 0.1f), 50000f), widened
///     +0x28  = 1      (inertia is a factor on the hull's)
///     +0x30/34/38     = MINSS(params.inertia &gt; 0 ? params.inertia : 1.0f, 1e18f), all three
///     +0x40  rotInertiaLimit, copied
///   FUN_180073df0  template + hull → core
///     mass      = template mass, or 1.0 below 1e-8
///     inertia_i = (float)((double)(hull_i · factor_i) · mass)
///     floor     = (float)(sqrt((double)(float)(Ix² + Iy² + Iz²)) · (double)limit), when limit ≠ 0
///     inertia_i = floor, where floor &gt; inertia_i
/// </code>
///
/// `RagdollAddSolid` sets `rotInertiaLimit = 0.1` for every ragdoll element (`ragdoll_shared.cpp:192`).
/// </remarks>
public sealed class IvpObjectTemplateConformanceTests
{
    private const float Tolerance = 1e-6f;

    private static IvpObjectTemplate Template(float mass = 10f, float inertia = 1f, float limit = 0.1f) =>
        IvpObjectTemplate.FromParameters(mass, inertia, damping: 0f, rotationDamping: 0f, rotationInertiaLimit: limit);

    /// <remarks>
    /// **Clamped to `[0.1, 50000]` in float before widening**, so the floor is the float `0.1`,
    /// `0.10000000149011612`, and NaN takes it too: `MAXSS` answers its second operand when either is NaN.
    /// </remarks>
    [TestCase(80f, 80d)]
    [TestCase(0.05f, 0.10000000149011612d)]
    [TestCase(60000f, 50000d)]
    [TestCase(float.NaN, 0.10000000149011612d)]
    public void FromParameters_AMass_IsClampedToTheEnginesRange(float mass, double expected)
    {
        Template(mass: mass).Mass.ShouldBe(expected);
    }

    /// <remarks>
    /// **An inertia scale that is not above zero becomes 1**, and one past `1e18` is held there; the same value
    /// goes to all three axes.
    /// </remarks>
    [TestCase(2f, 2f)]
    [TestCase(0f, 1f)]
    [TestCase(-3f, 1f)]
    [TestCase(float.NaN, 1f)]
    [TestCase(1e19f, 1e18f)]
    public void FromParameters_AnInertiaScale_IsKeptOnlyAboveZeroAndBelowTheCap(float inertia, float expected)
    {
        Template(inertia: inertia).InertiaFactor.ShouldBe((expected, expected, expected));
    }

    /// <remarks>
    /// **Each axis is the hull's own inertia, times its factor, times the mass.** A hull of `(0.01, 0.02, 0.03)`
    /// per unit mass at a factor of 2 and a mass of 10 is `(0.2, 0.4, 0.6)`. No floor bites: the length is
    /// about 0.75, a tenth of which is below every axis.
    /// </remarks>
    [Test]
    public void CoreInertia_PerAxis_IsTheHullsTimesTheFactorTimesTheMass()
    {
        (float mass, (float X, float Y, float Z) inertia) = Template(mass: 10f, inertia: 2f).CoreInertia((0.01f, 0.02f, 0.03f));

        mass.ShouldBe(10f);
        inertia.X.ShouldBe(0.2f, Tolerance);
        inertia.Y.ShouldBe(0.4f, Tolerance);
        inertia.Z.ShouldBe(0.6f, Tolerance);
    }

    /// <remarks>
    /// **The floor is a tenth of the inertia's LENGTH, not of its largest axis.** `(1, 1, 0.001)` has length
    /// √2, so the thin axis is raised to 0.1414; a floor from the largest axis would give 0.1. The two large
    /// axes are above the floor and stay.
    /// </remarks>
    [Test]
    public void CoreInertia_AThinAxis_IsRaisedToATenthOfTheLength()
    {
        (float _, (float X, float Y, float Z) inertia) = Template(mass: 1f).CoreInertia((1f, 1f, 0.001f));

        inertia.X.ShouldBe(1f);
        inertia.Y.ShouldBe(1f);
        inertia.Z.ShouldBe(0.14142136f, Tolerance);
    }

    /// <remarks>
    /// **A limit of zero is no floor at all** — the engine skips the whole step on `UCOMISS 0, limit`.
    /// </remarks>
    [Test]
    public void CoreInertia_WithNoLimit_LeavesAThinAxisThin()
    {
        (float _, (float X, float Y, float Z) inertia) = Template(mass: 1f, limit: 0f).CoreInertia((1f, 1f, 0.001f));

        inertia.Z.ShouldBe(0.001f, Tolerance);
    }

    /// <remarks>
    /// **Below `1e-8` the core's mass is 1**, and the inertia is scaled by that 1. vphysics' own clamp never
    /// lets a template get there, so the template is built directly.
    /// </remarks>
    [Test]
    public void CoreInertia_ATemplateMassBelowTheFloor_UsesAMassOfOne()
    {
        IvpObjectTemplate template = Template() with { Mass = 1e-9d, RotationInertiaLimit = 0f };

        (float mass, (float X, float Y, float Z) inertia) = template.CoreInertia((0.5f, 0.25f, 0.125f));

        mass.ShouldBe(1f);
        inertia.ShouldBe((0.5f, 0.25f, 0.125f));
    }
}
