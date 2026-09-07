using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// A REAL ragdoll dropped on REAL map collision, with no viewer in the way (B58).
/// </summary>
/// <remarks>
/// **This exists because a synthetic fixture was steering parity decisions, which is backwards.**
/// The owner: *"it has to be a parity issue, you need to actually run the demo to check or make
/// synthetic test demos that run"*. A two-unit cube on a single tilted triangle is a shape this
/// project invented; the engine's behaviour is what a seventeen-body player ragdoll does on a
/// compiled map, and an afternoon went into tuning against the cube instead.
///
/// **The other instrument was the viewer, and it costs ninety seconds a run.** It loads textures,
/// materials, lightmaps and models before a corpse is stepped at all, takes the desktop, and
/// orphans itself when killed — three separate "hangs" this session turned out to be a viewer
/// detached from the wrapper that launched it, or a wait on the machine-wide lock. None of that is
/// needed to ask where a corpse comes to rest.
///
/// **So this runs the production path and nothing else**: `MapLevel.Read` for the collision the
/// viewer uses, `RagdollBody.Build` for the bodies and joints a `.phy` declares, and
/// `RagdollSimulation` stepped at the demo's own tick rate. Seconds, deterministic, no window.
///
/// <code>
///   corpse-drop                                  koth_harvest_final, scout, the measured deaths
///   corpse-drop &lt;map&gt; &lt;model&gt; x y z              one drop, at a place you choose
/// </code>
///
/// **The static props and the contents filter are NOT here**, and that is a stated difference from
/// the viewer: `MapLevel.Read` builds brushes and terrain, and props are added later by
/// `LoadedMap` because a `.phy` needs the pakfile and the archives. A corpse resting on a crate in
/// the viewer will fall past it here, so a disagreement between the two instruments is expected in
/// exactly that case and means nothing on its own.
/// </remarks>
public sealed class CorpseDropProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "corpse-drop";

    /// <inheritdoc/>
    public string Summary =>
        "where a real ragdoll settles on real map collision: corpse-drop [map model x y z]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed, so no model can be read.");
            return;
        }

        string mapName = arguments.Count > 0 ? arguments[0] : "koth_harvest_final";
        string model = arguments.Count > 1 ? arguments[1] : "models/player/scout.mdl";

        if (locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine($"No map named '{mapName}'.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (RagdollFor(game, model) is not { } ragdoll)
        {
            output.WriteLine($"{model}: no ragdoll.");
            return;
        }

        MapLevel level = MapLevel.Read(File.ReadAllBytes(mapPath), NullLogger.Instance);

        output.WriteLine(
            $"{mapName}: {level.Physics.Ledges.Count} ledges, {level.Physics.TriangleCount} " +
            $"terrain triangles; {model} has {ragdoll.Elements.Count} bodies");

        foreach ((float X, float Y, float Z) at in Places(arguments))
        {
            Drop(output, level, ragdoll, game.Surfaces, at);
        }
    }

    /// <summary>Where to drop, from the command line or the measured deaths on `z1800`.</summary>
    /// <remarks>
    /// **The defaults are the seed positions of the corpses that actually misbehave**, read out of
    /// the viewer's own log rather than chosen: two of these are the pair that still leave the
    /// world, and the rest settle. A probe whose default case is the known-bad one is worth more
    /// than one that needs arguments to say anything.
    /// </remarks>
    private static IEnumerable<(float X, float Y, float Z)> Places(IReadOnlyList<string> arguments)
    {
        if (arguments.Count >= 5 &&
            float.TryParse(arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(arguments[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(arguments[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            yield return (x, y, z);
            yield break;
        }

        yield return (361.7f, -1614.3f, 57.6f);      // 2185, leaves the world
        yield return (-11.5f, -1558.8f, 47.3f);      // 2348, leaves the world
        yield return (-972.6f, -1400.3f, 77.5f);     // 2277, used to leave and now rests
        yield return (256.9f, -1416.1f, 55.2f);      // 2080, rests
        yield return (-953.8f, -1556.3f, 77.5f);     // 2132, rests
    }

    /// <summary>Steps one ragdoll from a standing pose at a spot until it stops moving.</summary>
    /// <remarks>
    /// **No death force, deliberately.** The wire's `m_vecForce` is what makes a corpse fly, and
    /// including it would make this a test of the blow rather than of the collision. A body
    /// released from rest asks the narrower question — does the map hold it — which is the one
    /// every measurement this session has actually been about.
    /// </remarks>
    private static void Drop(
        TextWriter output,
        MapLevel level,
        RagdollBody ragdoll,
        SurfaceTable surfaces,
        (float X, float Y, float Z) at)
    {
        (Vector3, Quaternion)[] start = new (Vector3, Quaternion)[ragdoll.Elements.Count];

        for (int index = 0; index < start.Length; index++)
        {
            Vector3 offset = ragdoll.Elements[index].OriginParentSpace;

            start[index] = (
                new Vector3(at.X + offset.X, at.Y + offset.Y, at.Z + offset.Z),
                Quaternion.Identity);
        }

        RagdollSimulation simulation = RagdollSimulation.Create(ragdoll, Step, start, surfaces);

        simulation.Environment.World = level.Physics;

        float lowest = float.MaxValue;
        int settled = -1;

        for (int tick = 0; tick < Ticks; tick++)
        {
            simulation.Step();

            float speed = Fastest(simulation);
            float height = simulation.Environment.Bodies[0].Position.Z is var z ? (float)z : 0f;

            lowest = MathF.Min(lowest, height);

            if (settled < 0 && tick > 20 && speed < Still)
            {
                settled = tick;
            }

            if (Trace && tick % 33 == 0)
            {
                (float oppose, float separate, float rub) = simulation.Environment.Split;

                output.WriteLine(
                    $"    tick {tick,3} speed {speed,7:0.#} " +
                    $"contacts {simulation.Environment.Contacts,3} " +
                    $"oppose {oppose,11:0.#} separate {separate,11:0.#} rub {rub,11:0.#}");
            }
        }

        IvpRigidBody root = simulation.Environment.Bodies[0];

        output.WriteLine(
            $"  from ({at.X:0.#}, {at.Y:0.#}, {at.Z:0.#}) -> " +
            $"({root.Position.X:0.#}, {root.Position.Y:0.#}, {root.Position.Z:0.#}) " +
            $"lowest {lowest:0.#} speed {Fastest(simulation):0.##} " +
            $"{(simulation.Asleep ? "asleep" : "AWAKE")} " +
            $"{(settled < 0 ? "NEVER SETTLED" : $"settled at tick {settled}")} " +
            $"{(root.Position.Z < Lost ? "LEFT THE WORLD" : string.Empty)}");
    }

    private static float Fastest(RagdollSimulation simulation) =>
        simulation.Environment.Bodies.Max(
            body => MathF.Sqrt(
                (body.Velocity.X * body.Velocity.X) +
                (body.Velocity.Y * body.Velocity.Y) +
                (body.Velocity.Z * body.Velocity.Z)));

    private static RagdollBody? RagdollFor(GameContent game, string model)
    {
        if (game.Archives.Read(model) is not { } modelBytes ||
            game.Archives.Read(Path.ChangeExtension(model, ".phy")) is not { } physicsBytes)
        {
            return null;
        }

        try
        {
            return RagdollBody.Build(
                PhysicsModel.Read(physicsBytes), StudioBones.Read(modelBytes));
        }
        catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The demo's own tick interval, which is what the engine steps physics at.</summary>
    private const float Step = 1f / 66f;

    /// <summary>Ten seconds, which is longer than any corpse takes to stop.</summary>
    private const int Ticks = 660;

    /// <summary>Below this a body counts as stopped, in Source units per second.</summary>
    private const float Still = 1f;

    /// <summary>The height the viewer's own diagnostic calls leaving the world.</summary>
    private const float Lost = -50f;

    /// <summary>Whether to print the speed every half second, for diagnosing a solve.</summary>
    /// <remarks>
    /// **A corpse's speed over TIME says which fault it has, where its resting place does not.**
    /// Growing means the solve injects energy, steady means something drives it, and falling means
    /// it is simply slow to settle — three different bugs behind one symptom.
    /// </remarks>
    private const bool Trace = true;
}
