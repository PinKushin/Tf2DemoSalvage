using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// How far a hull vertex of one body is from a face of another — the signed distance IVP's
/// time-of-impact search drives to its target (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `vphysics.dll`** — `docs/findings/51`, *The point-plane evaluator*.
/// `FUN_1800a1b50` fills the evaluator: the vertex from body A's ledge points and the face's own first point
/// from body B's, both widened from float, and the face normal from `FUN_18007b940` passed through
/// `FUN_18006e080` **with its result discarded**. `FUN_1800a3470` then returns
///
/// <code>
///   v = A.ToWorld(vertex)          -- a call to FUN_180070bc0
///   p = B.ToWorld(planePoint)      -- the same instructions, inlined
///   n = B.Rotate(normal)           -- rotation only, no translation
///   distance = ((v.x − p.x)·n.x + (v.y − p.y)·n.y) + (v.z − p.z)·n.z
/// </code>
/// </remarks>
public sealed class IvpPointPlaneEvaluatorConformanceTests
{
    private static readonly IvpMatrix Identity = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d));

    /// <remarks>
    /// Both bodies at the identity: the vertex `(1, 2, 5)` is two units above the face at `z = 3`.
    /// </remarks>
    [Test]
    public void Distance_AVertexAboveAFaceWithBothBodiesUnmoved_IsTheHeightAbove()
    {
        IvpPointPlaneEvaluator evaluator =
            IvpPointPlaneEvaluator.ForFace((1f, 2f, 5f), (0f, 0f, 3f), (1f, 0f, 3f), (0f, 1f, 3f));

        evaluator.Distance(Identity, Identity).ShouldBe(2d);
    }

    /// <remarks>
    /// **The face's body is a quarter turn about X**, `(sin 45°, 0, 0, cos 45°)`. That sends its local `+Z`
    /// normal to world `(0, −1, 0)` and its local plane point `(0, 0, 3)` to `(0, −3, 0)`, so the vertex
    /// `(1, 2, 5)` measures `(1, 5, 5) · (0, −1, 0)` = **−5**. Leaving the normal unturned measures +5;
    /// leaving the plane point unturned measures −2.
    /// </remarks>
    [Test]
    public void Distance_WithTheFacesBodyTurnedAQuarterAboutX_MeasuresAlongTheTurnedNormal()
    {
        IvpPointPlaneEvaluator evaluator =
            IvpPointPlaneEvaluator.ForFace((1f, 2f, 5f), (0f, 0f, 3f), (1f, 0f, 3f), (0f, 1f, 3f));

        IvpMatrix turned = IvpMatrix.FromRotation((0.70710677f, 0f, 0f, 0.70710677f), (0d, 0d, 0d));

        evaluator.Distance(Identity, turned).ShouldBe(-5d, 1e-6d);
    }

    /// <remarks>
    /// **Each body moves its own half, and the normal is not translated.** The vertex's body is lifted by
    /// one, to `z = 6`, and the face's by ten, to `z = 13`: **−7**. Placing the vertex with the face's body
    /// and the face with the vertex's measures +11; ignoring the vertex's body measures −8; translating the
    /// normal as a point stretches it elevenfold.
    /// </remarks>
    [Test]
    public void Distance_WithBothBodiesTranslated_PlacesTheVertexByItsBodyAndTheFaceByTheOther()
    {
        IvpPointPlaneEvaluator evaluator =
            IvpPointPlaneEvaluator.ForFace((1f, 2f, 5f), (0f, 0f, 3f), (1f, 0f, 3f), (0f, 1f, 3f));

        IvpMatrix vertexBody = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 1d));
        IvpMatrix faceBody = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 10d));

        evaluator.Distance(vertexBody, faceBody).ShouldBe(-7d);
    }

    /// <remarks>
    /// **The engine discards whether the normal could be scaled to unit length**, so a degenerate face keeps its zero
    /// normal and every vertex measures zero from it. A port that refused the face, or returned NaN, would
    /// fail here.
    /// </remarks>
    [Test]
    public void Distance_AgainstADegenerateFace_IsZero()
    {
        IvpPointPlaneEvaluator evaluator =
            IvpPointPlaneEvaluator.ForFace((1f, 2f, 5f), (0f, 0f, 3f), (1f, 0f, 3f), (2f, 0f, 3f));

        evaluator.Distance(Identity, Identity).ShouldBe(0d);
    }

    /// <remarks>
    /// **The dot adds x and y before z.** With a difference of `(1, 1, 1)` and a normal of `(0.1, 0.2, 2.2)`,
    /// `(0.1 + 0.2) + 2.2` is exactly `2.5`, while both other groupings land one ulp above it at
    /// `2.5000000000000004`. The normal is set directly, not at unit length, because only the grouping is under test.
    /// </remarks>
    [Test]
    public void Distance_TermsThatRoundDifferentlyByGrouping_SumsXAndYBeforeZ()
    {
        IvpPointPlaneEvaluator evaluator = new(
            Vertex: (1d, 1d, 1d), Normal: (0.1d, 0.2d, 2.2d), PlanePoint: (0d, 0d, 0d));

        BitConverter.DoubleToInt64Bits(evaluator.Distance(Identity, Identity))
            .ShouldBe(BitConverter.DoubleToInt64Bits(2.5d));
    }
}
