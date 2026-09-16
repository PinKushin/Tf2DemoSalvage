using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// One ragdoll dropped twice on the same map — through the ported driver (<see cref="IvpRagdollWorld"/>, <see cref="IvpSimulation"/>)
/// and through the invented solver (<see cref="RagdollSimulation"/>, <c>IvpEnvironment</c>) — side by side (B369, D172).
/// </summary>
/// <remarks>
/// **The gate before the switchover.** Both run their production paths: the ported world loaded by <see cref="IvpMapWorld.Load"/>, the
/// old one by <see cref="MapLevel.Read"/> and <see cref="MapPropCollision.Add"/>, each from the same start pose, force and step. The
/// same map and model by default, `cp_process_f12` and the scout — the parity reference (`docs/memory/the-f12-demo-is-the-parity-reference.md`).
/// <code>
///   ivp-drop-compare [map model x y z [fx fy fz]]      a point with no floor under it is dropped from the map's middle
/// </code>
/// </remarks>
public sealed class IvpDropCompareProbe : IProbe
{
    /// <summary>The demo's tick interval.</summary>
    private const float Step = 1f / 66f;

    /// <summary>Ten seconds.</summary>
    private const int Ticks = 660;

    /// <inheritdoc/>
    public string Name => "ivp-drop-compare";

    /// <inheritdoc/>
    public string Summary =>
        "a ragdoll dropped through the ported driver and the old solver side by side: ivp-drop-compare [map model x y z [fx fy fz]]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        string mapName = arguments.Count > 0 ? arguments[0] : "cp_process_f12";
        string model = arguments.Count > 1 ? arguments[1] : "models/player/scout.mdl";

        if (locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine($"No map named '{mapName}'.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        byte[] map = File.ReadAllBytes(mapPath);
        PakFile pak = PakFile.ReadFrom(map);
        byte[]? Read(string file) => pak.ReadFile(file) ?? game.Archives.Read(file);

        if (game.Archives.Read(model) is not { } modelBytes || game.Archives.Read(Path.ChangeExtension(model, ".phy")) is not { } physicsBytes ||
            RagdollBody.Build(PhysicsModel.Read(physicsBytes), StudioBones.Read(modelBytes)) is not { } ragdoll)
        {
            output.WriteLine($"{model}: no ragdoll.");
            return;
        }

        MapLevel level = MapLevel.Read(map, NullLogger.Instance);
        MapPropCollision.Add(level.Physics, map, Read, NullLogger.Instance);

        Vector3 at = Start(arguments, level);
        Vector3 blow = arguments.Count > 7 ? new Vector3(Number(arguments[5]), Number(arguments[6]), Number(arguments[7])) : Vector3.Zero;
        (Vector3, Quaternion)[] start = Pose(ragdoll, at);

        RagdollSimulation old = RagdollSimulation.Create(ragdoll, Step, start, game.Surfaces);
        old.Environment.World = level.Physics;

        IvpRagdollWorld world = new(Step, new Vector3(0f, 0f, -800f), game.Surfaces);
        IvpMapWorld.Counts counts = IvpMapWorld.Load(world, map, Read, NullLogger.Instance);
        IvpRagdoll ported = IvpRagdoll.Create(world, ragdoll, start);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{mapName}, {model} ({ragdoll.Elements.Count} bodies) from ({at.X:0.#}, {at.Y:0.#}, {at.Z:0.#}); ported world: " +
            $"{counts.World} world objects, {counts.Terrain} displacements, {counts.Props} props, {counts.BrushEntities} brush entity objects"));

        if (blow.LengthSquared() > 0f)
        {
            old.Kill((blow.X, blow.Y, blow.Z), forceBone: 0);
            ported.Kill(blow, forceBone: 0);
        }

        float oldLowest = float.MaxValue;
        float portedLowest = float.MaxValue;

        for (int tick = 1; tick <= Ticks; tick++)
        {
            old.Step();
            world.Simulate(Step);

            Vector3 oldRoot = Root(old);
            Vector3 portedRoot = ported.State()[0].Position;
            oldLowest = MathF.Min(oldLowest, oldRoot.Z);
            portedLowest = MathF.Min(portedLowest, portedRoot.Z);

            if (tick % 33 == 0)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {tick * Step,5:0.0}s  old ({oldRoot.X,8:0.0}, {oldRoot.Y,8:0.0}, {oldRoot.Z,8:0.0})  " +
                    $"ported ({portedRoot.X,8:0.0}, {portedRoot.Y,8:0.0}, {portedRoot.Z,8:0.0})  apart {Vector3.Distance(oldRoot, portedRoot),7:0.0}  " +
                    $"impacts {world.Simulation.Environment.Impacts}"));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  lowest root z: old {oldLowest:0.0}, ported {portedLowest:0.0}; the old solver {(old.Asleep ? "asleep" : "awake")}, the ported corpse {ported.State().Length} bodies"));
    }

    private static Vector3 Root(RagdollSimulation simulation)
    {
        (double x, double y, double z) = simulation.Environment.Bodies[0].Position;
        return new Vector3((float)x, (float)y, (float)z);
    }

    /// <summary>Each element placed at its parent's position plus its offset, the root at the drop point — as <c>corpse-drop</c> does.</summary>
    private static (Vector3, Quaternion)[] Pose(RagdollBody ragdoll, Vector3 at)
    {
        (Vector3, Quaternion)[] start = new (Vector3, Quaternion)[ragdoll.Elements.Count];

        for (int index = 0; index < start.Length; index++)
        {
            RagdollElement element = ragdoll.Elements[index];
            Vector3 origin = element.ParentIndex >= 0 && element.ParentIndex < index
                ? start[element.ParentIndex].Item1 + element.OriginParentSpace
                : at;

            start[index] = (origin, Quaternion.Identity);
        }

        return start;
    }

    /// <summary>The given point, or 64 units above the floor the BSP tree finds under the map's middle.</summary>
    private static Vector3 Start(IReadOnlyList<string> arguments, MapLevel level)
    {
        if (arguments.Count > 4)
        {
            return new Vector3(Number(arguments[2]), Number(arguments[3]), Number(arguments[4]));
        }

        const float From = 2000f;
        float fraction = level.Sweep((0f, 0f, From), (0f, 0f, -From), halfExtent: 0f);

        return new Vector3(0f, 0f, From - (fraction * 2f * From) + 64f);
    }

    private static float Number(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
