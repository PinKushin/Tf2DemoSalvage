using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The 2D skybox's six quads — that they form a closed box whose faces meet.
/// </summary>
/// <remarks>
/// **What a test can settle here and what it cannot.** That each face lies on its own plane, that
/// the box is closed, that the winding faces inward and that adjacent faces share their edges are
/// all arithmetic. Whether a given texture then looks the right way up is not — that is the
/// picture, and `sky_harvest_01` cannot even answer it, because its four sides are ONE repeated
/// image and its floor is a single pixel.
/// </remarks>
public sealed class SkyboxGeometryTests
{
    private const double Tolerance = 1e-4;
    private const float Reach = 100f;

    [Test]
    public void Face_EachOne_LiesEntirelyOnItsOwnPlane()
    {
        // rt is +X, lf −X, bk +Y, ft −Y, up +Z, dn −Z — vbsp's cube order (cubemap.cpp:195), not
        // the precache order, which swaps bk and lf.
        (int Face, int Axis, float Sign)[] planes =
        [
            (0, 0, 1f), (1, 0, -1f), (2, 1, 1f), (3, 1, -1f), (4, 2, 1f), (5, 2, -1f),
        ];

        foreach ((int face, int axis, float sign) in planes)
        {
            foreach (SkyboxGeometry.Corner corner in SkyboxGeometry.Face(face, Reach))
            {
                float on = axis switch
                {
                    0 => corner.X,
                    1 => corner.Y,
                    _ => corner.Z,
                };

                on.ShouldBe(sign * Reach, Tolerance, $"face {face} is a plane");
            }
        }
    }

    [Test]
    public void Face_EveryFace_CoversItsWholeSquare()
    {
        // The two axes that are not the plane's must each reach both ends, or the face is a strip
        // rather than a wall and the box has gaps at its edges.
        for (int face = 0; face < SkyboxGeometry.Faces; face++)
        {
            SkyboxGeometry.Corner[] corners = SkyboxGeometry.Face(face, Reach);

            foreach (int axis in (int[])[0, 1, 2])
            {
                float low = float.MaxValue;
                float high = float.MinValue;

                foreach (SkyboxGeometry.Corner corner in corners)
                {
                    float on = axis switch
                    {
                        0 => corner.X,
                        1 => corner.Y,
                        _ => corner.Z,
                    };

                    low = MathF.Min(low, on);
                    high = MathF.Max(high, on);
                }

                float width = high - low;

                (width < Tolerance || MathF.Abs(width - (2f * Reach)) < Tolerance).ShouldBeTrue(
                    $"face {face} axis {axis} is either the plane or the full width, was {width}");
            }
        }
    }

    /// <remarks>
    /// **Winding is what makes a box seen from INSIDE draw at all.** Each triangle's normal must
    /// point back toward the eye at the centre; the order that is front-facing for a solid box is
    /// exactly wrong here, and the symptom is a sky that is present, textured and invisible.
    /// </remarks>
    [Test]
    public void Face_EveryTriangle_FacesTheEyeAtTheCentre()
    {
        for (int face = 0; face < SkyboxGeometry.Faces; face++)
        {
            SkyboxGeometry.Corner[] corners = SkyboxGeometry.Face(face, Reach);

            for (int triangle = 0; triangle < 2; triangle++)
            {
                SkyboxGeometry.Corner a = corners[(triangle * 3) + 0];
                SkyboxGeometry.Corner b = corners[(triangle * 3) + 1];
                SkyboxGeometry.Corner c = corners[(triangle * 3) + 2];

                (float X, float Y, float Z) normal = Cross(
                    (b.X - a.X, b.Y - a.Y, b.Z - a.Z),
                    (c.X - a.X, c.Y - a.Y, c.Z - a.Z));

                // The eye is at the origin, so the vector from the triangle toward it is -a.
                float toward = (normal.X * -a.X) + (normal.Y * -a.Y) + (normal.Z * -a.Z);

                toward.ShouldBeGreaterThan(0f, $"face {face} triangle {triangle} faces inward");
            }
        }
    }

    [Test]
    public void Face_EveryFace_UsesTheWholeTexture()
    {
        // A face that sampled a sub-rectangle would tile or crop the sky, which on a 512-wide
        // side is a visible seam rather than a subtle one.
        for (int face = 0; face < SkyboxGeometry.Faces; face++)
        {
            SkyboxGeometry.Corner[] corners = SkyboxGeometry.Face(face, Reach);

            float lowU = float.MaxValue, highU = float.MinValue;
            float lowV = float.MaxValue, highV = float.MinValue;

            foreach (SkyboxGeometry.Corner corner in corners)
            {
                lowU = MathF.Min(lowU, corner.U);
                highU = MathF.Max(highU, corner.U);
                lowV = MathF.Min(lowV, corner.V);
                highV = MathF.Max(highV, corner.V);
            }

            lowU.ShouldBe(0f, Tolerance);
            highU.ShouldBe(1f, Tolerance);
            lowV.ShouldBe(0f, Tolerance);
            highV.ShouldBe(1f, Tolerance);
        }
    }

    /// <remarks>
    /// **The four side faces must circle the box the same way round**, or two of them meet back to
    /// back and the horizon jumps. Tested as a property of their edges rather than of their names:
    /// each side's u = 1 edge is the next side's u = 0 edge.
    /// </remarks>
    [Test]
    public void Face_TheFourSides_MeetEdgeToEdge()
    {
        // Going round: rt (+X) → ft (−Y) → lf (−X) → bk (+Y) → rt.
        (int From, int To)[] around = [(0, 3), (3, 1), (1, 2), (2, 0)];

        foreach ((int from, int to) in around)
        {
            SkyboxGeometry.Corner leaving = Corner(from, u: 1f, v: 0f);
            SkyboxGeometry.Corner arriving = Corner(to, u: 0f, v: 0f);

            leaving.X.ShouldBe(arriving.X, Tolerance, $"face {from} meets face {to}");
            leaving.Y.ShouldBe(arriving.Y, Tolerance);
            leaving.Z.ShouldBe(arriving.Z, Tolerance);
        }
    }

    /// <remarks>
    /// **The box is closed: the four walls' top edges end exactly on the `up` face's corners, and
    /// their bottom edges on the `dn` face's.** That catches a cap at the wrong size, the wrong
    /// place, or spanning the wrong axes.
    ///
    /// **It does NOT catch a cap rotated a quarter turn, and this comment claimed it did until a
    /// sabotage said otherwise.** Turning the `up` face's u and v a quarter turn leaves the same
    /// four CORNERS in the same four places — only which UV sits at each one changes — so a
    /// comparison of corner sets is blind to it. The sabotage was applied expecting a red test and
    /// every one stayed green, which is the wrong-instrument fault this repository has a casebook
    /// for: a proxy that is unfaithful to the variable
    /// (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).
    ///
    /// **And a rotation is not settleable geometrically at all.** Which quarter turn is right is a
    /// fact about how the artist authored the image against the cube-face convention, not about the
    /// box — so it needs a sky with four DISTINCT sides and a person looking at it. Neither corpus
    /// map has one: `sky_harvest_01` shares a single texture across all four sides and uses one
    /// pixel for its floor.
    /// </remarks>
    [Test]
    public void Face_TheTopAndBottom_MeetTheFourSides()
    {
        (int Cap, float SideV)[] caps = [(4, 0f), (5, 1f)];

        foreach ((int cap, float sideV) in caps)
        {
            HashSet<string> capCorners = [];

            foreach (SkyboxGeometry.Corner corner in SkyboxGeometry.Face(cap, Reach))
            {
                capCorners.Add(Key(corner));
            }

            HashSet<string> sideEdges = [];

            foreach (int side in (int[])[0, 1, 2, 3])
            {
                foreach (SkyboxGeometry.Corner corner in SkyboxGeometry.Face(side, Reach))
                {
                    if (MathF.Abs(corner.V - sideV) < 1e-4f)
                    {
                        sideEdges.Add(Key(corner));
                    }
                }
            }

            sideEdges.SetEquals(capCorners).ShouldBeTrue(
                $"face {cap}'s corners are exactly where the four walls' edges end; " +
                $"walls gave {sideEdges.Count} distinct, cap has {capCorners.Count}");
        }
    }

    /// <summary>A corner's position as a key, so two faces' corners can be compared as sets.</summary>
    private static string Key(SkyboxGeometry.Corner corner) =>
        $"{MathF.Round(corner.X, 3)},{MathF.Round(corner.Y, 3)},{MathF.Round(corner.Z, 3)}";

    private static SkyboxGeometry.Corner Corner(int face, float u, float v)
    {
        foreach (SkyboxGeometry.Corner corner in SkyboxGeometry.Face(face, Reach))
        {
            if (MathF.Abs(corner.U - u) < 1e-4f && MathF.Abs(corner.V - v) < 1e-4f)
            {
                return corner;
            }
        }

        throw new InvalidOperationException($"face {face} has no corner at ({u}, {v})");
    }

    private static (float X, float Y, float Z) Cross(
        (float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        ((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
}
