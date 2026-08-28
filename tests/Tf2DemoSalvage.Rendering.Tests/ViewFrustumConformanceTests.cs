using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// How the engine builds its view frustum and what it means to cull a box against it.
/// </summary>
/// <remarks>
/// **Written from `mathlib_base.cpp` and `clientleafsystem.cpp` before this renderer culled
/// anything**, so what follows is the engine's behaviour rather than a description of what got
/// built. `Device3D` currently hands every model to the GPU regardless of where the camera points.
///
/// **The whole of the engine's box cull, and it is six of one test:**
///
/// <code>
/// bool R_CullBox( const Vector&amp; mins, const Vector&amp; maxs, const Frustum_t &amp;frustum )
/// {
///     return (( BoxOnPlaneSide( mins, maxs, frustum.GetPlane(FRUSTUM_RIGHT) ) == 2 ) || …
/// }
/// </code>
///
/// `BoxOnPlaneSide` answers 1 for wholly in front, 2 for wholly behind, 3 for straddling — so
/// **`== 2` on any one plane culls**, and the frustum's normals therefore point INWARD. A box that
/// straddles a plane survives; only one entirely outside is dropped. That asymmetry is the
/// conservative half of every visibility structure and it is why a cull can never remove something
/// that should have been drawn.
///
/// **Two separate cull entry points exist and they are not interchangeable.** `R_CullBox` tests all
/// six planes; `R_CullBoxSkipNear` omits `FRUSTUM_NEARZ` and is what shadow and reflection work
/// uses. This project wants the six-plane one.
///
/// **Where the client applies it:** `CClientLeafSystem::CollateRenderablesInLeaf` computes each
/// renderable's world-space AABB and drops it with `engine->CullBox( absMins, absMaxs )`
/// (`clientleafsystem.cpp:1647`) — the same box `D110` already computes for the size buckets, which
/// is why the sort had to come first.
/// </remarks>
public sealed class ViewFrustumConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private const string MathBase = "src/mathlib/mathlib_base.cpp";
    private const string MathLib = "src/public/mathlib/mathlib.h";
    private const string LeafSystem = "src/game/client/clientleafsystem.cpp";

    /// <summary>That a box is culled when it is wholly behind any one of the six planes.</summary>
    /// <remarks>
    /// **The `== 2` is the load-bearing part.** `BoxOnPlaneSide` returns a two-bit answer and only
    /// the bare "behind" bit culls; an implementation testing `!= 1` would additionally drop every
    /// box that straddles a plane, which is every object at the edge of the screen.
    /// </remarks>
    [Test]
    public void Sdk_CullBox_DropsABoxWhollyBehindAnyPlane()
    {
        string source = Flat(Sdk(MathBase));

        Match cull = Regex.Match(
            source,
            @"bool R_CullBox\( const Vector& mins, const Vector& maxs, const Frustum_t &frustum \)\s*"
            + @"\{\s*return \(\((.*?)\);\s*\}",
            RegexOptions.Singleline,
            Limit);

        cull.Success.ShouldBeTrue("R_CullBox is the engine's box-versus-frustum test");

        string body = cull.Groups[1].Value;

        foreach (string plane in Planes)
        {
            body.ShouldContain(
                $"BoxOnPlaneSide( mins, maxs, frustum.GetPlane({plane}) ) == 2",
                Case.Sensitive,
                $"all six planes cull, {plane} included");
        }
    }

    /// <summary>That the near plane is the one the shadow variant leaves out.</summary>
    /// <remarks>
    /// Pinned because picking the wrong variant is invisible in ordinary play: `SkipNear` only
    /// differs for geometry behind the camera but within the side planes, which for a normal view
    /// is a handful of surfaces at the very edge and for a shadow frustum is most of them.
    /// </remarks>
    [Test]
    public void Sdk_CullBoxSkipNear_OmitsOnlyTheNearPlane()
    {
        string source = Flat(Sdk(MathBase));

        Match cull = Regex.Match(
            source,
            @"bool R_CullBoxSkipNear\(.*?\{\s*return \(\((.*?)\);\s*\}",
            RegexOptions.Singleline,
            Limit);

        cull.Success.ShouldBeTrue("R_CullBoxSkipNear exists beside R_CullBox");

        string body = cull.Groups[1].Value;

        body.ShouldNotContain("FRUSTUM_NEARZ");
        body.ShouldContain("FRUSTUM_FARZ");
    }

    /// <summary>That the six planes are indexed right, left, top, bottom, near, far.</summary>
    /// <remarks>
    /// **Valve's own header warns about this**: *"WARNING: there is code that depends on these
    /// values"*. The order matters here for the same reason — the near plane is index 4, and
    /// `R_CullBoxSkipNear` is written as an explicit list rather than a loop with a skip.
    /// </remarks>
    [Test]
    public void Sdk_TheFrustumPlaneIndices_AreRightLeftTopBottomNearFar()
    {
        string source = Flat(Sdk(MathLib));

        Match indices = Regex.Match(
            source,
            @"FRUSTUM_RIGHT\s*= 0,\s*FRUSTUM_LEFT\s*= 1,\s*FRUSTUM_TOP\s*= 2,\s*"
            + @"FRUSTUM_BOTTOM\s*= 3,\s*FRUSTUM_NEARZ\s*= 4,\s*FRUSTUM_FARZ\s*= 5,\s*"
            + @"FRUSTUM_NUMPLANES\s*= 6",
            RegexOptions.Singleline,
            Limit);

        indices.Success.ShouldBeTrue("the frustum plane enum is fixed and depended upon");
    }

    /// <summary>That "behind" means the box's far corner along the normal is still short of it.</summary>
    /// <remarks>
    /// **`BoxOnPlaneSide`'s two distances are the two extreme corners**, chosen by the sign of each
    /// normal component. `dist1` takes `maxs` where the normal is positive and `mins` where it is
    /// negative — the corner furthest ALONG the normal — and `dist2` takes the opposite corner.
    /// Then:
    ///
    /// <code>
    /// sides = 0;
    /// if (dist1 >= p->dist) sides = 1;
    /// if (dist2 &lt; p->dist) sides |= 2;
    /// </code>
    ///
    /// So the answer is 2 alone exactly when even the furthest corner falls short of the plane.
    /// **Note the asymmetry in the comparisons**: `>=` for the front bit and a bare `&lt;` for the
    /// back bit, so a box lying exactly ON the plane answers 1, not 3. Writing both as `>=` would
    /// make a coplanar box straddle, and writing both as `>` would let it answer 0 — which Valve
    /// asserts cannot happen (`Assert( sides != 0 )`).
    /// </remarks>
    [Test]
    public void Sdk_BoxOnPlaneSide_SetsTheBackBitOnAStrictLessThan()
    {
        string source = Flat(Sdk(MathBase));

        Match verdict = Regex.Match(
            source,
            @"sides = 0;\s*if \(dist1 >= p->dist\)\s*sides = 1;\s*if \(dist2 < p->dist\)\s*sides \|= 2;",
            RegexOptions.Singleline,
            Limit);

        verdict.Success.ShouldBeTrue("the two bits are set by dist1 >= dist and dist2 < dist");
    }

    /// <summary>That the side planes are built from the camera basis and the half-angle tangents.</summary>
    /// <remarks>
    /// **`GeneratePerspectiveFrustum` takes the FULL field of view and halves it itself**, which is
    /// the detail most likely to be got wrong by half. It then builds each pair as
    /// `right + tan(fovX/2)·forward` and that vector reflected through `-2·right`, normalises both,
    /// and uses `normal.Dot(origin)` as the plane distance — so every side plane passes through the
    /// eye, as a perspective frustum's must.
    ///
    /// **The near and far planes are `forward` and `-forward` offset by the eye's own projection**
    /// along forward: `flIntercept = DotProduct( origin, forward )`. Far is negated on both the
    /// normal and the distance, which is what makes "in front" mean "nearer than far".
    ///
    /// **Valve's own note that this is more work than culling needs** — *"OPTIMIZE: Normalizing
    /// these planes is not necessary for culling"* — is worth keeping visible, because it is
    /// permission the engine gives itself and does not take. The normalisation is still there.
    /// </remarks>
    [Test]
    public void Sdk_GeneratePerspectiveFrustum_BuildsSidePlanesThroughTheEye()
    {
        string source = Flat(Sdk(MathBase));

        // `flFovY` is what distinguishes the two overloads: the QAngle form takes an aspect ratio
        // and derives the vertical angle itself. Matching the parameter list literally would break
        // on the line wrap Valve's own formatting puts inside it.
        Match generate = Regex.Match(
            source,
            @"void GeneratePerspectiveFrustum\([^)]*float flFovY[^)]*\)\s*\{(.*?)\n\}",
            RegexOptions.Singleline,
            Limit);

        generate.Success.ShouldBeTrue("the vector form of the frustum builder");

        string body = generate.Groups[1].Value;

        // The eye's own distance along forward, which both depth planes are offset by.
        body.ShouldContain("float flIntercept = DotProduct( origin, forward );");

        // Far is negated in both normal and distance; near is not.
        body.ShouldContain("SetPlane( FRUSTUM_FARZ, PLANE_ANYZ, -forward, -flZFar - flIntercept );");
        body.ShouldContain("SetPlane( FRUSTUM_NEARZ, PLANE_ANYZ, forward, flZNear + flIntercept );");

        // The full angle is halved here rather than by the caller.
        body.ShouldContain("flFovX *= 0.5f;");
        body.ShouldContain("flFovY *= 0.5f;");

        // Each pair is one vector and its reflection through -2 * the basis axis.
        body.ShouldContain("VectorMA( right, flTanX, forward, normalPos );");
        body.ShouldContain("VectorMA( normalPos, -2.0f, right, normalNeg );");
        body.ShouldContain("VectorMA( up, flTanY, forward, normalPos );");
        body.ShouldContain("VectorMA( normalPos, -2.0f, up, normalNeg );");

        // Every side plane passes through the eye.
        body.ShouldContain("SetPlane( FRUSTUM_LEFT, PLANE_ANYZ, normalPos, normalPos.Dot( origin ) );");
        body.ShouldContain("SetPlane( FRUSTUM_RIGHT, PLANE_ANYZ, normalNeg, normalNeg.Dot( origin ) );");
        body.ShouldContain("SetPlane( FRUSTUM_BOTTOM, PLANE_ANYZ, normalPos, normalPos.Dot( origin ) );");
        body.ShouldContain("SetPlane( FRUSTUM_TOP, PLANE_ANYZ, normalNeg, normalNeg.Dot( origin ) );");
    }

    /// <summary>That the client culls a renderable by its world-space box, and only then.</summary>
    /// <remarks>
    /// **The order in `CollateRenderablesInLeaf` is what this project has to copy**: compute the
    /// world-space AABB, cull against the frustum, and only afterwards decide the render group and
    /// size bucket. Culling later would mean bucketing objects that are never drawn, and culling on
    /// the model-space box would mean culling the wrong volume for anything rotated — which is
    /// exactly the shortcut D110 records being stopped.
    /// </remarks>
    [Test]
    public void Sdk_TheClient_CullsEachRenderableByItsWorldSpaceBox()
    {
        string source = Flat(Sdk(LeafSystem));

        Match collate = Regex.Match(
            source,
            @"CalcRenderableWorldSpaceAABB\( renderable.m_pRenderable, absMins, absMaxs \);(.*?)"
            + @"if \( engine->CullBox\( absMins, absMaxs \) \)\s*continue;",
            RegexOptions.Singleline,
            Limit);

        collate.Success.ShouldBeTrue(
            "the world-space box is computed and then culled with the main frustum");
    }

    private static string[] Planes =>
    [
        "FRUSTUM_RIGHT",
        "FRUSTUM_LEFT",
        "FRUSTUM_TOP",
        "FRUSTUM_BOTTOM",
        "FRUSTUM_NEARZ",
        "FRUSTUM_FARZ",
    ];

    private static string Sdk(string relativePath) =>
        Skip.Unless(SourceSdk.Text(relativePath), SourceSdk.Missing);

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
