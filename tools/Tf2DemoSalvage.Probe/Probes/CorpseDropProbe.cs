using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>One ragdoll dropped on a real map through the ported driver (<see cref="IvpRagdollWorld"/>, <see cref="IvpSimulation"/>) (B369, D172).</summary>
/// <remarks>
/// **The production path**: the world loaded by
/// <see cref="IvpMapWorld.Load(IvpRagdollWorld, ReadOnlyMemory{byte}, Func{string, byte[]}, Microsoft.Extensions.Logging.ILogger)"/>, the
/// corpse by <see cref="IvpRagdoll.Create"/>. The same map and model by default, `cp_process_f12` and the scout — the parity reference
/// (`docs/memory/the-f12-demo-is-the-parity-reference.md`). It compared against the old solver until that was deleted (D172 step 7).
/// <code>
///   corpse-drop [map model x y z [fx fy fz]]      a point with no floor under it is dropped from the map's middle
///   corpse-drop void [model]                      the joints alone: bind pose, no gravity, no map
/// </code>
/// </remarks>
public sealed class CorpseDropProbe : IProbe
{
    /// <summary>The demo's tick interval.</summary>
    private const float Step = 1f / 66f;

    /// <summary>Ten seconds.</summary>
    private const int Ticks = 660;

    /// <inheritdoc/>
    public string Name => "corpse-drop";

    /// <inheritdoc/>
    public string Summary =>
        "where a ragdoll dropped on a real map settles, through the ported driver: corpse-drop [map model x y z [fx fy fz]] | void [model]";

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

        if (mapName == "void")
        {
            Void(output, folder, model);
            return;
        }

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

        Vector3 at = Start(arguments, MapLevel.Read(map, NullLogger.Instance));
        Vector3 blow = arguments.Count > 7 ? new Vector3(Number(arguments[5]), Number(arguments[6]), Number(arguments[7])) : Vector3.Zero;
        (Vector3, Quaternion)[] start = Pose(ragdoll, at);

        IvpRagdollWorld world = new(Step, new Vector3(0f, 0f, -800f), game.Surfaces);
        IvpMapWorld.Objects loaded = IvpMapWorld.Load(world, map, Read, NullLogger.Instance);
        int mapMindists = world.Simulation.Mindists;
        IvpRagdoll ported = IvpRagdoll.Create(world, ragdoll, start);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"mindists: {mapMindists} after the map alone, {world.Simulation.Mindists} with the corpse added"));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{mapName}, {model} ({ragdoll.Elements.Count} bodies) from ({at.X:0.#}, {at.Y:0.#}, {at.Z:0.#}); ported world: " +
            $"{loaded.World.Count} world objects, {loaded.Terrain.Count} displacements, {loaded.Props.Count} props, " +
            $"{loaded.BrushEntities.Count} brush entity objects"));

        if (blow.LengthSquared() > 0f)
        {
            ported.Kill(blow, forceBone: 0);
        }

        float lowest = float.MaxValue;

        // Impacts after two seconds, by pair — a corpse at rest should be held by its contacts' pushes, not by impacts.
        Dictionary<string, int> lateImpacts = [];
        bool late = false;

        world.Simulation.Collided = (first, second) =>
        {
            if (late)
            {
                string key = $"{Describe(ported, loaded, first)} x {Describe(ported, loaded, second)}";
                lateImpacts[key] = lateImpacts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        };

        long allocated = 0;
        long stepping = 0;
        int asleepTicks = 0;
        long asleepBytes = 0;

        for (int tick = 1; tick <= Ticks; tick++)
        {
            // **What the step itself costs**, bytes and time around the one call — the seek replays exactly this per tick (D179).
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            bool wasAsleep = ported.Asleep;
            world.Simulate(Step);
            stepping += System.Diagnostics.Stopwatch.GetTimestamp() - startedAt;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            allocated += bytes;

            if (wasAsleep)
            {
                asleepTicks++;
                asleepBytes += bytes;
            }

            // The client's own settle check, after the step as `CRagdoll` runs it.
            ported.CheckSettle(Step);

            late = tick >= 132;

            // Four seconds in, still awake: what each body is doing, and what it stands on.
            if (tick == 264)
            {
                Contacts(output, ported, loaded);

                foreach ((string pair, int count) in lateImpacts.OrderByDescending(entry => entry.Value).Take(12))
                {
                    output.WriteLine($"  impacts 2-4 s: {count,4}  {pair}");
                }
            }

            Vector3 root = ported.State()[0].Position;
            lowest = MathF.Min(lowest, root.Z);

            if (tick % 33 == 0)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {tick * Step,5:0.0}s  root ({root.X,8:0.0}, {root.Y,8:0.0}, {root.Z,8:0.0})  " +
                    $"impacts {world.Simulation.Environment.Impacts}  fastest {Fastest(ported),6:0.00} in/s{(ported.Asleep ? " asleep" : string.Empty)}"));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  stepping {Ticks} ticks: {stepping * 1000d / System.Diagnostics.Stopwatch.Frequency:0} ms, " +
            $"{allocated / Ticks:N0} bytes allocated a tick; asleep {asleepTicks} ticks at " +
            $"{(asleepTicks == 0 ? 0 : asleepBytes / asleepTicks):N0} bytes a tick"));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  lowest root z {lowest:0.0}; {(ported.Asleep ? "asleep" : "awake")}, {ported.State().Length} bodies"));
    }

    /// <summary>
    /// The joints alone: the corpse in its bind pose, no gravity, no map. **Nothing should move**, so any speed that
    /// appears or grows is the joint solve's own.
    /// </summary>
    private static void Void(TextWriter output, string folder, string model)
    {
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (game.Archives.Read(model) is not { } modelBytes || game.Archives.Read(Path.ChangeExtension(model, ".phy")) is not { } physicsBytes ||
            RagdollBody.Build(PhysicsModel.Read(physicsBytes), StudioBones.Read(modelBytes)) is not { } ragdoll)
        {
            output.WriteLine($"{model}: no ragdoll.");
            return;
        }

        (Vector3, Quaternion)[] start = Pose(ragdoll, Vector3.Zero);
        IvpRagdollWorld world = new(Step, Vector3.Zero, game.Surfaces);
        IvpRagdoll ported = IvpRagdoll.Create(world, ragdoll, start);

        for (int tick = 1; tick <= Ticks; tick++)
        {
            world.Simulate(Step);

            if (tick % 66 == 0)
            {
                float speed = 0f;
                int fastest = 0;

                for (int index = 0; index < ported.Bodies.Count; index++)
                {
                    (float x, float y, float z) = ported.Bodies[index].Velocity;
                    float body = MathF.Sqrt((x * x) + (y * y) + (z * z)) / IvpTransform.MetresPerInch;

                    if (body > speed)
                    {
                        speed = body;
                        fastest = index;
                    }
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {tick * Step,5:0.0}s  fastest body {fastest} at {speed:0.000} in/s, root {ported.State()[0].Position}"));
            }
        }
    }

    /// <summary>An object named for a trace: the corpse's body index, or the kind of static object.</summary>
    private static string Describe(IvpRagdoll ragdoll, IvpMapWorld.Objects loaded, IvpCollisionObject collisionObject)
    {
        for (int index = 0; index < ragdoll.Bodies.Count; index++)
        {
            if (ReferenceEquals(ragdoll.Bodies[index], collisionObject.Core))
            {
                return $"body {index}";
            }
        }

        if (loaded.World.Contains(collisionObject))
        {
            return "world";
        }

        return loaded.Terrain.Contains(collisionObject) ? "terrain" : "prop or brush entity";
    }

    /// <summary>The fastest body's linear speed, back in Source inches per second.</summary>
    private static float Fastest(IvpRagdoll ragdoll)
    {
        float fastest = 0f;

        foreach (IvpRigidBody body in ragdoll.Bodies)
        {
            (float x, float y, float z) = body.Velocity;
            fastest = MathF.Max(fastest, MathF.Sqrt((x * x) + (y * y) + (z * z)) / IvpTransform.MetresPerInch);
        }

        return fastest;
    }

    /// <summary>Every contact each body stands in, with the kind of object across it.</summary>
    private static void Contacts(TextWriter output, IvpRagdoll ragdoll, IvpMapWorld.Objects loaded)
    {
        HashSet<IvpCollisionObject> world = [.. loaded.World];
        HashSet<IvpCollisionObject> terrain = [.. loaded.Terrain];
        HashSet<IvpCollisionObject> props = [.. loaded.Props];

        string Kind(IvpCollisionObject other)
        {
            if (world.Contains(other))
            {
                return "world";
            }

            if (terrain.Contains(other))
            {
                return "terrain";
            }

            return props.Contains(other) ? "prop" : "other";
        }

        for (int index = 0; index < ragdoll.Bodies.Count; index++)
        {
            IvpRigidBody body = ragdoll.Bodies[index];
            (float vx, float vy, float vz) = body.Velocity;
            (float wx, float wy, float wz) = body.AngularVelocity;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  body {index,2}: speed {MathF.Sqrt((vx * vx) + (vy * vy) + (vz * vz)) / IvpTransform.MetresPerInch,7:0.00} in/s, " +
                $"spin {MathF.Sqrt((wx * wx) + (wy * wy) + (wz * wz)),6:0.00} rad/s, unit {(body.Unit is null ? "none" : "yes")}"));

            IEnumerable<IvpFrictionSystem> systems = body.FrictionInfos.Keys;

            if (body.FrictionInfo?.System is { } own && !body.FrictionInfos.ContainsKey(own))
            {
                systems = systems.Append(own);
            }

            output.WriteLine($"    {body.FrictionInfos.Count} friction infos, own system {(body.FrictionInfo is null ? "none" : "yes")}");

            foreach (IvpContactPoint? first in systems.Select(system => system.FirstContact))
            {
                for (IvpContactPoint? contact = first; contact is not null; contact = contact.Next)
                {
                    bool firstIsThis = ReferenceEquals(contact.FirstObject.Core, body);
                    bool secondIsThis = ReferenceEquals(contact.SecondObject.Core, body);

                    if (!firstIsThis && !secondIsThis)
                    {
                        continue;
                    }

                    IvpCollisionObject other = firstIsThis ? contact.SecondObject : contact.FirstObject;
                    int otherIndex = Array.FindIndex([.. ragdoll.Bodies], candidate => ReferenceEquals(candidate, other.Core));

                    if (otherIndex >= 0)
                    {
                        RagdollElement mine = ragdoll.Body.Elements[index];
                        RagdollElement theirs = ragdoll.Body.Elements[otherIndex];

                        IvpCollisionObject self = firstIsThis ? contact.FirstObject : contact.SecondObject;

                        output.WriteLine(
                            $"    self contact {index}-{otherIndex}: parents {mine.ParentIndex}/{theirs.ParentIndex}, " +
                            $"rules collide {ragdoll.Body.ShouldCollide(index, otherIndex)}, " +
                            $"filter {ragdoll.World.Simulation.ShouldCollide?.Invoke(self, other)}, " +
                            $"friction cores {(ReferenceEquals(self.FrictionCore, body) ? "own" : "shared")}");
                    }
                    (float nx, float ny, float nz) = contact.LastNormal;

                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  body {index,2} touches {Kind(other)}: gap {contact.Gap:0.0000} " +
                        $"normal IVP ({nx:0.00}, {ny:0.00}, {nz:0.00}) slide {contact.Slide} push {contact.NormalPush:0.000}"));
                }
            }
        }
    }

    /// <summary>Each element placed at its parent's position plus its offset, the root at the drop point.</summary>
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
