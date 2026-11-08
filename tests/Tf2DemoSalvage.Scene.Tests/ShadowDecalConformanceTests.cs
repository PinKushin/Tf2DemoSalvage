using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The shadow's polygon on one surface and its fade — `engine.dll` `FUN_1801238a0` and the clips it calls, and the vertex
/// write `FUN_180121970` (finding 107).
/// </summary>
/// <remarks>
/// **The fixture** is the standing player of <see cref="ShadowProjectionConformanceTests"/> — feet at (100, 200, 300), box
/// 20 × 20 × 72, cast straight down 50 — over a floor quad at z = 300 facing up. Shadow space: x along +y, y along +x,
/// origin (100, 200, 372), size 24 × 24, so the shadow's square on the floor runs x 88 to 112 and y 188 to 212, at depth 72.
/// **The extra clip planes then take one padded edge off each axis**: with the direction straight down the local direction's
/// x and y are 0, not below 0, so `ComputeExtraClipPlanes` steps to the box's MIN there with the plane facing −x and −y
/// (`clientshadowmgr.cpp:2183-2192`), and `FUN_180120230` keeps −x &lt; −90 and −y &lt; −190 — x above 90, y above 190. The
/// decal runs x 90 to 112 and y 190 to 212.
/// </remarks>
public sealed class ShadowDecalConformanceTests
{
    private const float Tolerance = 1e-3f;

    private static readonly ShadowProjection Player = ShadowProjection.ForRenderToTexture(
        new Vector3(100f, 200f, 300f), (0f, 0f, 0f), new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f), new Vector3(0f, 0f, -1f), 50f);

    private static Vector3[] Floor(float z, float half = 100f) =>
    [
        new(100f - half, 200f - half, z),
        new(100f + half, 200f - half, z),
        new(100f + half, 200f + half, z),
        new(100f - half, 200f + half, z),
    ];

    /// <remarks>
    /// A floor larger than the shadow is cut to the shadow's square (u, v in 0..1) — four corners — each lifted 0.1 along the
    /// floor's normal (`FUN_1801246f0`).
    /// </remarks>
    [Test]
    public void Clip_AFloorLargerThanTheShadow_IsCutToItsSquareAndLiftedATenth()
    {
        IReadOnlyList<ShadowDecalVertex>? decal = ShadowDecal.Clip(Floor(300f), Vector3.UnitZ, Player);

        decal.ShouldNotBeNull();
        decal.Count.ShouldBe(4);

        foreach (ShadowDecalVertex vertex in decal)
        {
            vertex.Position.Z.ShouldBe(300.1f, Tolerance);
            vertex.Position.X.ShouldBeInRange(90f - Tolerance, 112f + Tolerance);
            vertex.Position.Y.ShouldBeInRange(190f - Tolerance, 212f + Tolerance);
            vertex.Texture.Z.ShouldBe(72f, Tolerance, "the depth below the shadow's origin, before the lift");
        }
    }

    /// <remarks>Nothing of the surface lies nearer than the max height (122 + √5984 / 2 below the origin), so no decal (`FUN_180128250`).</remarks>
    [Test]
    public void Clip_AFloorBeyondTheMaxHeight_HasNoDecal() =>
        ShadowDecal.Clip(Floor(300f - 200f), Vector3.UnitZ, Player).ShouldBeNull();

    /// <remarks>
    /// The extra clip planes keep only what is below the caster's far corner along the shadow (`FUN_180120230`, dot &lt; dist):
    /// a "floor" above the box's top, at z = 380, is in front of the third plane (0, 0, 1)·p &lt; 372 nowhere, and is also behind
    /// the shadow origin (z &lt; 0), so it has no decal.
    /// </remarks>
    [Test]
    public void Clip_ASurfaceAboveTheCaster_HasNoDecal() =>
        ShadowDecal.Clip(Floor(380f), Vector3.UnitZ, Player).ShouldBeNull();

    /// <remarks>
    /// A surface crossing one edge of the shadow keeps only the part inside it: a strip from x = 50 to x = 100 keeps
    /// x 90 to 100 of it — the clip plane's 90, inside the square's 88.
    /// </remarks>
    [Test]
    public void Clip_ASurfaceCrossingOneEdge_KeepsOnlyTheInsidePart()
    {
        Vector3[] strip =
        [
            new(50f, 150f, 300f),
            new(100f, 150f, 300f),
            new(100f, 250f, 300f),
            new(50f, 250f, 300f),
        ];

        IReadOnlyList<ShadowDecalVertex>? decal = ShadowDecal.Clip(strip, Vector3.UnitZ, Player);

        decal.ShouldNotBeNull();
        float left = float.MaxValue;
        float right = float.MinValue;

        foreach (ShadowDecalVertex vertex in decal)
        {
            left = System.MathF.Min(left, vertex.Position.X);
            right = System.MathF.Max(right, vertex.Position.X);
        }

        left.ShouldBe(90f, Tolerance);
        right.ShouldBe(100f, Tolerance);
    }

    /// <remarks>
    /// `FUN_180121970`'s color: `min(255, max(z − offset, 0) × amount / (maxDist − offset) + bias)`, all four bytes. At the
    /// feet the depth is 72, under the falloff start (√5984 / 2 + 36 ≈ 74.68), so a bias of 0 gives 0 — no fade.
    /// </remarks>
    [TestCase(72f, 0, 0)]
    [TestCase(74.6782f + 25f, 0, 120)]
    [TestCase(74.6782f + 50f, 0, 240)]
    [TestCase(74.6782f + 50f, 30, 255)]
    public void Fade_ADepthAndABias_IsTheEnginesByte(float depth, int bias, int expected)
    {
        float falloffStart = (System.MathF.Sqrt(5984f) * 0.5f) + 36f;

        ShadowDecal.Fade(depth, falloffStart, falloffStart + 50f, ShadowProjection.MaxFalloffAmount, bias).ShouldBe((byte)expected);
    }

    /// <remarks>When the max distance does not exceed the falloff offset the engine divides by 1 (`0x180123830`, `1.0f` at `0x18035e93c`).</remarks>
    [Test]
    public void Fade_NoRoomToFall_DividesByOne() =>
        ShadowDecal.Fade(depth: 12f, falloffOffset: 10f, maxDistance: 10f, falloffAmount: 240f, bias: 0).ShouldBe((byte)255);
}
