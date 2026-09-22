using System;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`TFBloodSprayCallback` (`tf_fx_blood.cpp`) for a blood event (B415).</summary>
public sealed class BloodEffectsConformanceTests
{
    /// <summary>A hit at the origin, the normal back along +X.</summary>
    private static readonly SceneBlood Hit = new(1, (0f, 0f, 0f), (1f, 0f, 0f), 2, true);

    private static readonly Func<float, float, float> Middle = static (least, most) => (least + most) * 0.5f;

    [Test]
    public void For_TheImpact_FacesAgainstTheNormal()
    {
        (BloodBurst impact, _) = BloodEffects.For(Hit, new Vector3(0f, 300f, 0f), (0f, -90f, 0f), Middle);

        impact.System.ShouldBe(BloodEffects.Impact);
        impact.Point.Forward.X.ShouldBe(-1f, 1e-5f);
    }

    [TestCase(399f, BloodEffects.Spray)]
    [TestCase(400f, BloodEffects.SprayFar)]
    public void For_TheSpray_IsNearOrFarBy400Units(float distance, string expected)
    {
        // A view looking down −Y, across the normal, so nothing is turned aside.
        (_, BloodBurst spray) = BloodEffects.For(Hit, new Vector3(0f, distance, 0f), (0f, -90f, 0f), Middle);

        spray.System.ShouldBe(expected);
        spray.Point.Forward.X.ShouldBe(1f, 1e-5f);
    }

    [Test]
    public void For_ASprayAlongTheView_IsTurnedAside()
    {
        // The view looks down −X from 600 units: |normal · forward| is 1. Past 512 the side is the one the normal already
        // leans to — here none, `normal · right` is 0, so left — by 1.0 (the middle draw) plus 0.25 · 600 / 512.
        (_, BloodBurst spray) = BloodEffects.For(Hit, new Vector3(600f, 0f, 0f), (0f, 180f, 0f), Middle);

        float push = 1f + (0.25f * 600f / 512f);
        Vector3 turned = Vector3.Normalize(new Vector3(1f, 0f, 0f) - (new Vector3(0f, 1f, 0f) * push));

        spray.Point.Forward.X.ShouldBe(turned.X, 1e-4f);
        spray.Point.Forward.Y.ShouldBe(turned.Y, 1e-4f);
    }
}
