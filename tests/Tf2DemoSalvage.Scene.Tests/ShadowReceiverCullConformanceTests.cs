using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CClientShadowMgr::CullReceiver</c> (`clientshadowmgr.cpp:3297-3433`) and the box test it calls,
/// <c>ComputeSeparatingPlane</c> (`collisionutils.cpp:2158-2464`) (finding 107).
/// </summary>
/// <remarks>
/// **The fixture shadow** is cast straight down from a 16-unit cube at the origin with no rotation: its axes are
/// x' = +Y and y' = +X, it is 20 units square (16 plus two texels at each edge), and its depth runs from the cube's top
/// face, z = 8, downward. The usual receiver is a flat card 6 by 8 units, whose diagonal is 10, so its sphere's radius
/// is exactly 5.
/// </remarks>
public sealed class ShadowReceiverCullConformanceTests
{
    private static readonly Vector3 Down = new(0f, 0f, -1f);

    private static ShadowEntity Box(Vector3 origin, Vector3 half, (float, float, float) angles = default) =>
        new(1, null, ShadowCast.RenderToTexture, origin, angles, -half, half);

    private static readonly ShadowEntity Caster = Box(Vector3.Zero, new Vector3(8f));

    private static ShadowProjection ShadowOf(ShadowEntity caster) =>
        ShadowProjection.ForRenderToTexture(caster.RenderOrigin, caster.RenderAngles, caster.Mins, caster.Maxs, Down, 50f);

    private static ShadowEntity Card(Vector3 origin) =>
        new(2, null, ShadowCast.None, origin, (0f, 0f, 0f), new Vector3(0f, -3f, -4f), new Vector3(0f, 3f, 4f));

    /// <remarks>
    /// A card 15 units out along y' is 5 past the shadow's edge, its radius exactly: `IsBoxIntersectingSphere` asks
    /// `dmin &lt; radius²`, so touching is not meeting and the card is culled — and a tenth of a unit closer it receives.
    /// </remarks>
    [Test]
    public void Culls_ASphereTouchingTheShadowFromOutside_IsCulledAndJustInsideReceives()
    {
        ShadowProjection shadow = ShadowOf(Caster);

        shadow.Size.ShouldBe(new Vector2(20f, 20f));
        ShadowReceiverCull.Culls(shadow, Card(new Vector3(15f, 0f, -20f)), Caster).ShouldBeTrue();
        ShadowReceiverCull.Culls(shadow, Card(new Vector3(14.9f, 0f, -20f)), Caster).ShouldBeFalse();
    }

    /// <remarks>
    /// A card 12 above the cube's centre still meets the shadow's box by its sphere, and the boxes are separated along
    /// +Z (15 against 12); the shadow is cast along −Z, so a receiver on the plane's far side from the cast — above its
    /// caster — is culled, and the same card 12 below receives.
    /// </remarks>
    [Test]
    public void Culls_AReceiverAboveItsCaster_IsCulledAndBelowReceives()
    {
        ShadowProjection shadow = ShadowOf(Caster);

        ShadowReceiverCull.SeparatingPlane(Card(new Vector3(0f, 0f, 12f)), Caster).ShouldBe(Vector3.UnitZ);
        ShadowReceiverCull.Culls(shadow, Card(new Vector3(0f, 0f, 12f)), Caster).ShouldBeTrue();
        ShadowReceiverCull.Culls(shadow, Card(new Vector3(0f, 0f, -12f)), Caster).ShouldBeFalse();
    }

    /// <remarks>
    /// A 4-unit caster and a card of radius 6.25 level with it, 10 units along +X: the card's sphere reaches 0.25 into
    /// the shadow's side, the spheres miss (10 against 3.46 + 6.25), so the plane is the line between them, (10, 0, 0),
    /// and its dot with the cast is exactly 0 — not greater, so the `else` side, which culls a receiver that is not behind
    /// its caster along that line.
    /// </remarks>
    [Test]
    public void Culls_AReceiverLevelWithItsCaster_IsCulled()
    {
        ShadowEntity small = Box(Vector3.Zero, new Vector3(2f));
        ShadowEntity card = new(2, null, ShadowCast.None, new Vector3(10f, 0f, 0f), (0f, 0f, 0f), new Vector3(0f, -3.75f, -5f), new Vector3(0f, 3.75f, 5f));
        ShadowProjection shadow = ShadowOf(small);
        (Vector3 centre, float radius) = ShadowReceiverCull.BoundingSphere(card);

        // The control: the first gate keeps it — 6 out from an 8-unit shadow against a radius of 6.25 — so the cull is
        // the plane's.
        ShadowReceiverCull.BoxMeetsSphere(
            new Vector3(-shadow.Size.X * 0.5f, -shadow.Size.Y * 0.5f, 0f),
            new Vector3(shadow.Size.X * 0.5f, shadow.Size.Y * 0.5f, shadow.MaxHeight),
            shadow.WorldToShadow(centre),
            radius).ShouldBeTrue();
        ShadowReceiverCull.Culls(shadow, card, small).ShouldBeTrue();
    }

    /// <remarks>
    /// The boxes overlap by 2 along Z (centres 10 apart, half heights 4 and 8), but the tolerance is 3, so `15 &gt; 12`
    /// still finds +Z: <c>CClientShadowMgr::ComputeSeparatingPlane</c> passes 3.0.
    /// </remarks>
    [Test]
    public void SeparatingPlane_BoxesOverlappingByLessThanTheTolerance_AreSeparated() =>
        ShadowReceiverCull.SeparatingPlane(Card(new Vector3(0f, 0f, 10f)), Caster).ShouldBe(Vector3.UnitZ);

    /// <remarks>
    /// Centres 8.5 apart: every axis compares 11.5 against at least 11.5 — none separates, and with no plane the
    /// receiver is never culled, even above its caster.
    /// </remarks>
    [Test]
    public void Culls_BoxesOverlappingPastTheTolerance_HaveNoPlaneAndReceive()
    {
        ShadowReceiverCull.SeparatingPlane(Card(new Vector3(0f, 0f, 8.5f)), Caster).ShouldBeNull();
        ShadowReceiverCull.Culls(ShadowOf(Caster), Card(new Vector3(0f, 0f, 8.5f)), Caster).ShouldBeFalse();
    }

    /// <remarks>
    /// The port reads the fifteen axes off <c>box2ToBox1</c>'s entries, index by index; this is the same test written
    /// as projections in the world — each axis L, unnormalised, separates when <c>|T·L| + 3 &gt; Σ s₁|a·L| + Σ s₂|b·L|</c>
    /// — in the engine's order, over 400 seeded poses. The control: the sweep must reach a face axis, an edge axis and
    /// no plane at all, or it proved nothing about the branch it missed.
    /// </remarks>
    [Test]
    public void SeparatingPlane_SeededPoses_MatchTheFifteenAxesProjectedInTheWorld()
    {
        uint state = 702;
        Dictionary<string, int> decided = new() { ["face"] = 0, ["edge"] = 0, ["none"] = 0 };

        for (int n = 0; n < 400; n++)
        {
            ShadowEntity first = SeededBox(ref state);
            ShadowEntity second = SeededBox(ref state);

            (Vector3? expected, string kind) = Reference(first, second);
            Vector3? actual = ShadowReceiverCull.SeparatingPlane(first, second);

            decided[kind]++;
            (actual is null).ShouldBe(expected is null, $"pose {n}");

            if (expected is { } plane)
            {
                Vector3.Distance(actual!.Value, plane).ShouldBeLessThan(1e-4f, $"pose {n}");
            }
        }

        decided["face"].ShouldBeGreaterThan(0);
        decided["edge"].ShouldBeGreaterThan(0);
        decided["none"].ShouldBeGreaterThan(0);
    }

    /// <summary>A box from a xorshift32 stream: the same 400 poses every run, so a failure names a reproducible one.</summary>
    private static ShadowEntity SeededBox(ref uint state)
    {
        Vector3 half = new(Next(ref state, 1f, 40f), Next(ref state, 1f, 40f), Next(ref state, 1f, 40f));
        Vector3 offset = new(Next(ref state, -10f, 10f), Next(ref state, -10f, 10f), Next(ref state, -10f, 10f));
        Vector3 origin = new(Next(ref state, -60f, 60f), Next(ref state, -60f, 60f), Next(ref state, -60f, 60f));
        (float, float, float) angles = (Next(ref state, -180f, 180f), Next(ref state, -180f, 180f), Next(ref state, -180f, 180f));

        return new ShadowEntity(1, null, ShadowCast.None, origin, angles, offset - half, offset + half);
    }

    private static float Next(ref uint state, float min, float max)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        return min + ((state / (float)uint.MaxValue) * (max - min));
    }

    private static (Vector3? Plane, string Kind) Reference(ShadowEntity first, ShadowEntity second)
    {
        (Vector3[] a, Vector3 s1, Vector3 c1) = Frame(first);
        (Vector3[] b, Vector3 s2, Vector3 c2) = Frame(second);
        Vector3 t = c2 - c1;

        List<(Vector3 Axis, string Kind)> axes = [(a[0], "face"), (a[1], "face"), (a[2], "face"), (b[0], "face"), (b[1], "face"), (b[2], "face")];

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (MathF.Abs(Vector3.Dot(a[i], b[j])) < 1f - 1e-3f)
                {
                    axes.Add((Vector3.Cross(a[i], b[j]), "edge"));
                }
            }
        }

        foreach ((Vector3 axis, string kind) in axes)
        {
            float r1 = (s1.X * MathF.Abs(Vector3.Dot(a[0], axis))) + (s1.Y * MathF.Abs(Vector3.Dot(a[1], axis))) + (s1.Z * MathF.Abs(Vector3.Dot(a[2], axis)));
            float r2 = (s2.X * MathF.Abs(Vector3.Dot(b[0], axis))) + (s2.Y * MathF.Abs(Vector3.Dot(b[1], axis))) + (s2.Z * MathF.Abs(Vector3.Dot(b[2], axis)));

            if (MathF.Abs(Vector3.Dot(t, axis)) + 3f > r1 + r2)
            {
                return (axis, kind);
            }
        }

        return (null, "none");
    }

    private static (Vector3[] Axes, Vector3 Half, Vector3 Centre) Frame(ShadowEntity entity)
    {
        (Vector3 forward, Vector3 left, Vector3 up) = ShadowProjection.Basis(entity.RenderAngles);
        Vector3 centroid = (entity.Mins + entity.Maxs) * 0.5f;

        return (
            [forward, left, up],
            (entity.Maxs - entity.Mins) * 0.5f,
            entity.RenderOrigin + (centroid.X * forward) + (centroid.Y * left) + (centroid.Z * up));
    }
}
