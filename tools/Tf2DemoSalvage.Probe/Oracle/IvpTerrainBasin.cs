using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// The virtual-terrain scene the binary and the port are both dropped onto: a 2000-inch power-2 displacement basin, its
/// border ring raised 100 inches, the middle flat at Source z 0.
/// </summary>
/// <remarks>
/// **One scene for three runners** — `vphysics-virtual-terrain-drop` (the shipped DLL), `ivp-virtual-terrain-drop` (the port
/// through <c>IvpRagdollWorld</c>) and <c>IvpSimulationBroadPhaseTests</c> — so a difference between them is never a difference
/// in the ground.
/// </remarks>
internal static class IvpTerrainBasin
{
    /// <summary>The basin's side, in Source inches.</summary>
    public const float Size = 2000f;

    /// <summary>How far the border ring is raised, in Source inches.</summary>
    public const float BorderHeight = 100f;

    /// <summary>IVP's metre in Source inches' terms.</summary>
    public const float MetresPerInch = 0.0254f;

    /// <summary>The dropped cube's half extent: the test's 4 metres, in Source inches.</summary>
    public const float HalfInches = 4f / MetresPerInch;

    /// <summary>The drop height: the test's 10 metres above the flat middle, in Source inches.</summary>
    public const float DropAltitudeInches = 10f / MetresPerInch;

    /// <summary>Gravity: the test's 10 m/s², in Source inches a second squared.</summary>
    public const float GravityInches = 10f / MetresPerInch;

    /// <summary>The drop's x and y: the basin's metric centre 25.4, in Source inches — 1000 exactly.</summary>
    public const float CentreInches = 25.4f / MetresPerInch;

    /// <summary>The dropped cube's mass, kilograms.</summary>
    public const float BodyMass = 10f;

    /// <summary>The simulation timestep both runners step at: IVP's PSI rate, as the test's own environment.</summary>
    public const float Timestep = 1f / 66f;

    /// <summary>Ticks run, and how often one is printed.</summary>
    public const int TotalTicks = 198;

    /// <inheritdoc cref="TotalTicks"/>
    public const int PrintEveryTicks = 13;

    /// <summary>How often to print: an <c>every=N</c> argument, else <see cref="PrintEveryTicks"/>.</summary>
    /// <param name="arguments">The probe's arguments.</param>
    /// <returns>The print interval in ticks.</returns>
    public static int Every(IReadOnlyList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (argument.StartsWith("every=", System.StringComparison.Ordinal) &&
                int.TryParse(argument.AsSpan(6), System.Globalization.CultureInfo.InvariantCulture, out int every) && every > 0)
            {
                return every;
            }
        }

        return PrintEveryTicks;
    }

    /// <summary>The surfaces both runners parse: Valve's <c>default</c> (friction 0.8, elasticity 0.25) and a frictionless one.</summary>
    public const string SurfaceText = """
        "default"
        {
        "friction"      "0.8"
        "elasticity"    "0.25"
        "density"       "2700"
        "thickness"     "-1"
        "dampening"     "0"
        }
        "frictionless"
        {
        "friction"      "0"
        "elasticity"    "0"
        "density"       "2700"
        "thickness"     "-1"
        "dampening"     "0"
        }
        """;

    /// <summary>The convex hull: the raised corners 0, 4, 24, 20 over the flat middle's corners 6, 8, 18, 16, each face wound outward.</summary>
    private static readonly (int A, int B, int C)[] HullFaces =
    [
        (0, 4, 24), (0, 24, 20),
        (6, 16, 18), (6, 18, 8),
        (0, 6, 8), (0, 8, 4),
        (4, 8, 18), (4, 18, 24),
        (24, 18, 16), (24, 16, 20),
        (20, 16, 6), (20, 6, 0),
    ];

    /// <summary>The basin's displacement collision tree.</summary>
    /// <returns>25 vertices, 32 triangles.</returns>
    public static DisplacementCollisionTree Tree()
    {
        (Vector3, float)[] field = new (Vector3, float)[25];

        for (int index = 0; index < 25; index++)
        {
            if (index / 5 is 0 or 4 || index % 5 is 0 or 4)
            {
                field[index] = (Vector3.UnitZ, BorderHeight);
            }
        }

        return DisplacementCollisionTree.Build(
            [Vector3.Zero, new Vector3(0f, Size, 0f), new Vector3(Size, Size, 0f), new Vector3(Size, 0f, 0f)], 2, field);
    }

    /// <summary>The basin's <c>LUMP_PHYSDISP</c> hull over <paramref name="vertices"/>.</summary>
    /// <param name="vertices">The tree's vertices.</param>
    /// <returns>One hull, every face and edge virtual: edges numbered as first met, each triangle's pierce the face turned most against it.</returns>
    public static byte[] Hull(IReadOnlyList<Vector3> vertices)
    {
        List<(int From, int To)> edges = [];
        List<byte> body = [];

        foreach ((int a, int b, int c) in HullFaces)
        {
            foreach ((int from, int to) in ((int, int)[])[(a, b), (b, c), (c, a)])
            {
                int id = edges.FindIndex(edge => edge == (to, from));

                if (id < 0)
                {
                    id = edges.Count;
                    edges.Add((from, to));
                }

                body.Add((byte)id);
            }

            Vector3 normal = Normal(vertices, (a, b, c));
            int pierce = Enumerable.Range(0, HullFaces.Length).MinBy(other => Vector3.Dot(Normal(vertices, HullFaces[other]), normal));
            body.Add((byte)pierce);
        }

        foreach ((int from, int to) in edges)
        {
            body.Add((byte)from);
            body.Add((byte)to);
        }

        return [1, 0, 0, 0, (byte)HullFaces.Length, (byte)HullFaces.Length, (byte)edges.Count, (byte)edges.Count, 0, .. body];
    }

    private static Vector3 Normal(IReadOnlyList<Vector3> vertices, (int A, int B, int C) triangle) =>
        Vector3.Normalize(Vector3.Cross(vertices[triangle.B] - vertices[triangle.A], vertices[triangle.C] - vertices[triangle.A]));
}
