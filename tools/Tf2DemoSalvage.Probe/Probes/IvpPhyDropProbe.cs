using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The ported driver's own equivalent of <c>vphysics-drop phy</c> (B369): the same two models' own solid 0, the same
/// gravity, timestep and drop height, driven through <see cref="IvpRagdollWorld"/> and <see cref="IvpSimulation"/> instead
/// of the shipped DLL, printing the same per-tick line so the two runs can be diffed directly.
/// </summary>
/// <remarks>
/// **Every object is built through the SAME production calls a real physics prop uses.** <see cref="RagdollBody.BuildProp"/>
/// reads the one-solid body exactly as a gib does (B371); <see cref="IvpRagdollWorld.AddStatic(PhysicsLedgeTree, Vector3, IIvpMaterial)"/>
/// is the port's own <c>CreatePolyObjectStatic</c>; and <see cref="IvpRagdoll.Create"/> — normally a corpse's several jointed
/// elements — is used here for ONE element with no constraints, which is what a physics prop's object already is on the
/// engine side (<c>CreatePolyObject</c> with no <c>CreateRagdollConstraint</c> call). No separate "physics prop" builder
/// exists on the ported side and none is added here: a second path would be free to disagree with the one a corpse's own
/// elements already go through.
///
/// **The orientation column cannot be compared like-for-like.** The shipped probe reads vphysics' own Euler <c>QAngle</c>
/// (<c>IPhysicsObject::GetPosition</c>'s second out-param); the port carries a quaternion end to end and never converts to
/// Euler, because nothing downstream needs it. Printed as <c>orient=(x, y, z, w)</c> rather than under the misleading label
/// <c>angles=</c>, so a textual diff is not invited to line up two numbers that mean different things.
///
/// **Mass, inertia, damping and rotational damping are each solid's own <c>.phy</c> values**, read the same way
/// <see cref="RagdollBody.BuildProp"/> reads them for any prop and turned into the core's mass/inertia by
/// <see cref="IvpObjectTemplate.FromParameters"/> — this project's own port of <c>FUN_18001c9d0</c>, which is
/// <c>objectparams_t</c>'s fill from a solid's own fields, the same struct <c>g_PhysDefaultObjectParams</c>
/// (<c>game/shared/physics_shared.cpp:43-56</c>) supplies defaults for when a `.phy` omits a field. Both real props used
/// against this probe declare their own mass, so neither run exercises a default.
/// </remarks>
public sealed class IvpPhyDropProbe : IProbe
{
    private const float Step = 1f / 66f;
    private const int Ticks = 660;
    private const int PrintEveryTicks = 33;
    private const float DefaultDropHeight = 64f;

    /// <inheritdoc/>
    public string Name => "ivp-phy-drop";

    /// <inheritdoc/>
    public string Summary =>
        "the ported driver's own equivalent of 'vphysics-drop phy', for a diff against it: " +
        "ivp-phy-drop <dynamicModel.mdl> <staticModel.mdl> [z] [every]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("ivp-phy-drop <dynamicModel.mdl> <staticModel.mdl> [z] [every]");
            return;
        }

        string dynamicModel = arguments[0];
        string staticModel = arguments[1];
        float z = arguments.Count > 2
            ? float.Parse(arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture)
            : DefaultDropHeight;
        int every = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : PrintEveryTicks;

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (LoadProp(output, game, staticModel) is not { } staticBody ||
            LoadProp(output, game, dynamicModel) is not { } dynamicBody)
        {
            return;
        }

        RagdollElement staticElement = staticBody.Elements[0];
        RagdollElement dynamicElement = dynamicBody.Elements[0];

        if (staticElement.Surface is not { } staticSurface)
        {
            output.WriteLine($"{staticModel}: solid 0 has no ported surface (its compact surface did not read)");
            return;
        }

        if (dynamicElement.Surface is null)
        {
            output.WriteLine($"{dynamicModel}: solid 0 has no ported surface (its compact surface did not read)");
            return;
        }

        IvpRagdollWorld world = new(Step, new Vector3(0f, 0f, -800f), game.Surfaces);

        if (game.Surfaces.ObjectMaterial(staticElement.SurfaceProp) is not { } staticMaterial ||
            game.Surfaces.ObjectMaterial(dynamicElement.SurfaceProp) is not { } dynamicMaterial)
        {
            output.WriteLine(
                "Neither the solid's own surfaceprop nor 'default' is parsed - is scripts/surfaceproperties_manifest.txt missing?");
            return;
        }

        output.WriteLine($"control: gravity (0.00, 0.00, -800.00), timestep {Step:F6}");
        output.WriteLine(
            $"static  '{staticModel}': solid 0 surfaceprop '{staticElement.SurfaceProp}' -> material '{staticMaterial.Name}', " +
            $"mass {staticElement.Mass:F2}");
        output.WriteLine(
            $"dynamic '{dynamicModel}': solid 0 surfaceprop '{dynamicElement.SurfaceProp}' -> material '{dynamicMaterial.Name}', " +
            $"mass {dynamicElement.Mass:F2}");
        output.Flush();

        IvpCollisionObject staticObject = world.AddStatic(staticSurface, Vector3.Zero, staticMaterial);
        IvpRagdoll dynamicRagdoll = IvpRagdoll.Create(
            world, dynamicBody, [(new Vector3(0f, 0f, z), Quaternion.Identity)]);

        output.WriteLine(
            $"control: {staticModel} GetPosition -> {Format(SourceOrigin(staticObject.Core))} (expected (0.00, 0.00, 0.00))");

        Vector3 dynamicStartPosition = dynamicRagdoll.State()[0].Position;
        output.WriteLine(
            $"control: {dynamicModel} GetPosition -> {Format(dynamicStartPosition)} (expected (0.00, 0.00, {z:F2}))");
        IvpRigidBody built = dynamicRagdoll.Bodies[0];
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"core: inertia ({built.Inertia.X:g9}, {built.Inertia.Y:g9}, {built.Inertia.Z:g9})  " +
            $"inverse ({built.InverseInertia.X:g9}, {built.InverseInertia.Y:g9}, {built.InverseInertia.Z:g9})  " +
            $"inverse mass {built.InverseMass:g9}"));
        output.Flush();

        if (every == 1)
        {
            world.Simulation.PairFired = (mindist, due, outcome) => output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  pair at {due:R}: length {mindist.Length:R} flags 0x{mindist.Flags:x} {outcome}"));
            world.Simulation.Examined = (mindist, outcome) => output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  examined at {world.Simulation.Now:R} (psi end {world.Simulation.Environment.PsiEnd:R}): length {mindist.Length:R} flags 0x{mindist.Flags:x} {outcome}") +
                (mindist.QueueSlot is int slot
                    ? ", queued " + world.Simulation.Collisions.EventQueue.ValueOf(slot).ToString("R", CultureInfo.InvariantCulture)
                    : string.Empty));
        }

        for (int tick = 1; tick <= Ticks; tick++)
        {
            world.Simulate(Step);
            dynamicRagdoll.CheckSettle(Step);

            if (tick % every != 0)
            {
                continue;
            }

            (Vector3 Position, Quaternion Orientation) state = dynamicRagdoll.State()[0];
            // As `GetVelocity` (`FUN_18001c1f0`) reads them: each with its pending change, the linear one to Source inches and the
            // spin in the core's axes as `(x, z, −y)` degrees.
            IvpRigidBody core = dynamicRagdoll.Bodies[0];
            (float vx, float vy, float vz) = (
                core.Velocity.X + core.PendingVelocity.X, core.Velocity.Y + core.PendingVelocity.Y, core.Velocity.Z + core.PendingVelocity.Z);
            (float ax, float ay, float az) = (
                core.AngularVelocity.X + core.PendingAngularVelocity.X,
                core.PendingAngularVelocity.Z + core.AngularVelocity.Z,
                core.AngularVelocity.Y + core.PendingAngularVelocity.Y);
            (float wx, float wy, float wz) = (ax * 57.29578f, ay * 57.29578f, az * -57.29578f);
            (float svx, float svy, float svz) = IvpTransform.SourcePosition(vx, vy, vz);

            output.WriteLine(
                $"tick {tick,4} t={tick * Step,6:F3}  pos={Format(state.Position)}  " +
                $"orient=({state.Orientation.X:F2}, {state.Orientation.Y:F2}, {state.Orientation.Z:F2}, {state.Orientation.W:F2})  " +
                $"vel=({svx:F2}, {svy:F2}, {svz:F2})  spin=({wx:F2}, {wy:F2}, {wz:F2})" +
                (dynamicRagdoll.Asleep ? " asleep" : string.Empty));
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  core now={world.Simulation.Now:R} stepped={core.LastStepped:R}  " +
                $"p=({core.Position.X:R}, {core.Position.Y:R}, {core.Position.Z:R})  " +
                $"v=({core.Velocity.X:R}, {core.Velocity.Y:R}, {core.Velocity.Z:R})  " +
                $"v0=({core.PreviousVelocity.X:R}, {core.PreviousVelocity.Y:R}, {core.PreviousVelocity.Z:R})  " +
                $"w=({core.AngularVelocity.X:R}, {core.AngularVelocity.Y:R}, {core.AngularVelocity.Z:R})"));
            output.Flush();
        }
    }

    /// <summary>Reads a model's <c>.phy</c> and builds its one-solid body — the same content path <c>ragdoll</c> and <c>vphysics-drop phy</c> use.</summary>
    private static RagdollBody? LoadProp(TextWriter output, GameContent game, string model)
    {
        string physicsPath = Path.ChangeExtension(model, ".phy");

        if (game.Archives.Read(model) is null)
        {
            output.WriteLine($"{model}: not in the game's content");
            return null;
        }

        if (game.Archives.Read(physicsPath) is not { } bytes)
        {
            output.WriteLine($"{physicsPath}: not in the game's content");
            return null;
        }

        PhysicsModel parsed;

        try
        {
            parsed = PhysicsModel.Read(bytes);
        }
        catch (InvalidDataException failure)
        {
            output.WriteLine($"{physicsPath}: {failure.Message}");
            return null;
        }

        if (RagdollBody.BuildProp(parsed) is not { } body)
        {
            output.WriteLine($"{physicsPath}: RagdollBody.BuildProp refused (no hull, or no mass properties - B403)");
            return null;
        }

        return body;
    }

    /// <summary>A static core's own origin, back in Source space — the same read <see cref="IvpRagdoll.State"/> does per element.</summary>
    private static Vector3 SourceOrigin(IvpRigidBody? core)
    {
        if (core is null)
        {
            return Vector3.Zero;
        }

        (double x, double y, double z) = core.ObjectOrigin();
        (float sx, float sy, float sz) = IvpTransform.SourcePosition((float)x, (float)y, (float)z);

        return new Vector3(sx, sy, sz);
    }

    private static string Format(Vector3 v) =>
        string.Create(CultureInfo.InvariantCulture, $"({v.X:F2}, {v.Y:F2}, {v.Z:F2})");
}
