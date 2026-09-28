using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Probe.Oracle;

using static Tf2DemoSalvage.Probe.Oracle.IvpTerrainBasin;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The port's twin of <c>vphysics-virtual-terrain-drop</c> (B369): the same basin and the same cube, run through the production
/// seam — <see cref="IvpRagdollWorld.AddVirtualTerrain"/> for the ground, <see cref="IvpRagdollWorld.Simulate"/> for every tick —
/// and printed on the same line, for a diff.
/// </summary>
/// <remarks>
/// **Why the seam matters:** a copy of the broad-phase test stepped straight through <c>IvpSimulation.Advance</c> lagged the binary
/// by one PSI in free fall (2026-09-28), because it skipped <c>CPhysicsEnvironment::Simulate</c>'s frame dispatch and clock read.
///
/// **The cube's core is built as <c>CreatePolyObject</c> builds it from the binary probe's parameters**, not through a
/// <c>RagdollBody</c>, which needs a <c>.phy</c>: mass 10, inertia scale 1 (a solid box's (a² + b²)/3 per kilogram), no damping,
/// and the engine's own <c>BBoxToCollide</c> ledge (<see cref="IvpTestCube"/>).
/// </remarks>
public sealed class IvpVirtualTerrainDropProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "ivp-virtual-terrain-drop";

    /// <inheritdoc/>
    public string Summary =>
        "the port's twin of vphysics-virtual-terrain-drop through IvpRagdollWorld's seam, printing the same line: ivp-virtual-terrain-drop";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);

        VphysicsSurfaceProps surfaces = new([]);
        surfaces.ParseSurfaceData(Encoding.UTF8.GetBytes(SurfaceText));
        IvpRagdollWorld world = new(Timestep, new Vector3(0f, 0f, -GravityInches), surfaces);

        if (surfaces.ObjectMaterial("default") is not { } material)
        {
            output.WriteLine("control: 'default' did not parse");
            return;
        }

        DisplacementCollisionTree tree = Tree();
        output.WriteLine($"DisplacementCollisionTree: {tree.Vertices.Count} vertices, {tree.Triangles.Count} triangles");
        world.AddVirtualTerrain([(tree, false)], [Hull(tree.Vertices)]);

        // Source (x, y, z) is IVP (x, −z, y) in metres.
        float half = HalfInches * MetresPerInch;
        (double X, double Y, double Z) at = (CentreInches * MetresPerInch, -DropAltitudeInches * MetresPerInch, CentreInches * MetresPerInch);
        float perKilogram = 2f * half * half / 3f;
        IvpRigidBody body = new()
        {
            Position = at,
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), at),
            Radius = half * MathF.Sqrt(3f),
            InverseMass = 1f / BodyMass,
            InverseInertia = (1f / (perKilogram * BodyMass), 1f / (perKilogram * BodyMass), 1f / (perKilogram * BodyMass)),
            Damping = 0f,
            RotationDamping = 0f,
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
            RestAnchorPosition = ((float)at.X, (float)at.Y, (float)at.Z),
            SettleAnchorPosition = ((float)at.X, (float)at.Y, (float)at.Z),
            Ledges = IvpTestCube.Ledges(half),
        };

        world.Simulation.Add(body);
        world.Simulation.Collide(body, material);
        output.WriteLine($"dropped from {Format(Source(body))}, expecting rest at Z~{HalfInches:F3}");

        for (int tick = 1; tick <= TotalTicks; tick++)
        {
            world.Simulate(Timestep);

            if (tick % PrintEveryTicks != 0)
            {
                continue;
            }

            (float vx, float vy, float vz) = (
                body.Velocity.X + body.PendingVelocity.X, body.Velocity.Y + body.PendingVelocity.Y, body.Velocity.Z + body.PendingVelocity.Z);
            (float svx, float svy, float svz) = IvpTransform.SourcePosition(vx, vy, vz);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"tick {tick,4} t={tick * Timestep,5:F2}  pos={Format(Source(body))}  vel=({svx:F2}, {svy:F2}, {svz:F2})"));
        }

        Vector3 last = Source(body);
        output.WriteLine($"final: pos={Format(last)}, expected rest Z={HalfInches:F3}, |difference|={MathF.Abs(last.Z - HalfInches):F3} in");

        Vector3 Source(IvpRigidBody core)
        {
            ((double X, double Y, double Z) position, (double X, double Y, double Z, double W) rotation) = core.TransformAt(world.Simulation.Now);
            (double X, double Y, double Z) origin = IvpMatrix.FromRotation(rotation, position).ToWorld(core.ObjectOffset);
            (float x, float y, float z) = IvpTransform.SourcePosition((float)origin.X, (float)origin.Y, (float)origin.Z);

            return new Vector3(x, y, z);
        }
    }

    private static string Format(Vector3 v) =>
        string.Create(CultureInfo.InvariantCulture, $"({v.X:F2}, {v.Y:F2}, {v.Z:F2})");
}
