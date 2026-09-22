using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`FX_Tracer` (`clientsideeffects_test.cpp:292`) and `CFXDiscreetLine::Draw` (`fx_discreetline.cpp:64`) (B415).</summary>
public sealed class DiscreetLineConformanceTests
{
    /// <summary>`random->RandomFloat` answering its lower bound: a length of 64 and a scale of 0.75.</summary>
    private static float Least(float least, float most) => least;

    [Test]
    public void Tracer_ShorterThan256_IsNone()
    {
        // "Don't make short tracers." — `if ( dist >= 256 )`.
        DiscreetLines.Tracer(Vector3.Zero, new Vector3(255.9f, 0f, 0f), 5000f, Least).ShouldBeNull();
        DiscreetLines.Tracer(Vector3.Zero, new Vector3(256f, 0f, 0f), 5000f, Least).ShouldNotBeNull();
    }

    [Test]
    public void Tracer_AThousandUnits_LivesUntilItsTailArrives()
    {
        // `life = ( dist + length ) / velocity` — "we want the tail to finish its run as well".
        DiscreetLine line = DiscreetLines.Tracer(Vector3.Zero, new Vector3(0f, 1000f, 0f), 5000f, Least)!.Value;

        line.Direction.ShouldBe(Vector3.UnitY);
        line.Length.ShouldBe(64f);
        line.ClipLength.ShouldBe(1000f);
        line.Scale.ShouldBe(0.75f);
        line.Life.ShouldBe(1064f / 5000f, 1e-6f);
    }

    [Test]
    public void Draw_Mid_Flight_IsACoreAndAnOutlineFromTailToHead()
    {
        // Head at velocity · age = 100, tail 64 behind it at 36; the view far enough to the side that the width is
        // not widened. `cross = lineDir × viewDir`, normalised.
        DiscreetLine line = new(Vector3.Zero, Vector3.UnitX, 1000f, 64f, 1000f, 2f, 1f);
        List<DetailSpriteVertex> corners = [];

        DiscreetLines.Draw(line, 0.1f, new Vector3(0f, -100f, 0f), Vector3.UnitY, 1000f, corners).ShouldBeTrue();

        corners.Count.ShouldBe(8);

        // head − view = (100, 100, 0); (64, 0, 0) × that is +Z.
        corners[0].ShouldBe(new DetailSpriteVertex(36f, 0f, -2f, 1f, 0f, 1f, 1f, 1f, 1f, 1f, 0f, 0f));
        corners[1].ShouldBe(new DetailSpriteVertex(36f, 0f, 2f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));
        corners[2].ShouldBe(new DetailSpriteVertex(100f, 0f, 2f, 0f, 1f, 1f, 1f, 1f, 1f, 0f, 1f, 0f));
        corners[3].ShouldBe(new DetailSpriteVertex(100f, 0f, -2f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0f));

        // The soft outline: twice as wide, at 64 of 255.
        corners[4].Z.ShouldBe(-4f);
        corners[4].Red.ShouldBe(64f / 255f);
    }

    [Test]
    public void Draw_JustLeaving_ShortensAndOffsetsTheTexture()
    {
        // Head at 30, tail clipped at 0: `fOffset = |s − e| / length` = 30 / 64.
        DiscreetLine line = new(Vector3.Zero, Vector3.UnitX, 1000f, 64f, 1000f, 2f, 1f);
        List<DetailSpriteVertex> corners = [];

        DiscreetLines.Draw(line, 0.03f, new Vector3(0f, -100f, 0f), Vector3.UnitY, 1000f, corners).ShouldBeTrue();

        corners[0].X.ShouldBe(0f);
        corners[2].X.ShouldBe(30f, 1e-4f);
        corners[2].V.ShouldBe(30f / 64f, 1e-6f);
    }

    [Test]
    public void Draw_FarAway_IsWidenedToHalfAPixelAndFaded()
    {
        // `flScreenSpaceWidth = scale · halfWidth / z` = 2 · 100 / 1000 = 0.2, under 0.5: the scale becomes
        // `0.5 · z / halfWidth` = 5, and the alpha `RemapVal( 0.2, 0.25, 2, 0.3, 1 )` = 0.28, inside the clamp.
        DiscreetLine line = new(new Vector3(0f, 1000f, 0f), Vector3.UnitX, 1000f, 64f, 1000f, 2f, 1f);
        List<DetailSpriteVertex> corners = [];

        DiscreetLines.Draw(line, 0.1f, Vector3.Zero, Vector3.UnitY, 100f, corners).ShouldBeTrue();

        float alpha = 0.3f + ((0.2f - 0.25f) / (2f - 0.25f) * 0.7f);

        MathF.Abs(corners[1].Z - corners[0].Z).ShouldBe(10f, 1e-3f);
        corners[0].Red.ShouldBe(MathF.Floor(255f * alpha) / 255f);
        corners[4].Red.ShouldBe(MathF.Floor(64f * alpha) / 255f);
    }

    [TestCase(0f)]
    [TestCase(1.5f)]
    public void Draw_BeforeItsFirstStepOrAfterItsLife_IsNothing(float age)
    {
        // At age 0 both distances are 0 and `Draw` returns; past `m_fLife`, `IsActive` has removed it.
        DiscreetLine line = new(Vector3.Zero, Vector3.UnitX, 1000f, 64f, 1000f, 2f, 1f);
        List<DetailSpriteVertex> corners = [];

        DiscreetLines.Draw(line, age, new Vector3(0f, -100f, 0f), Vector3.UnitY, 1000f, corners).ShouldBeFalse();
        corners.ShouldBeEmpty();
    }
}
