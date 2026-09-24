using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>A map's collision put into the ported driver's world, in the order the client builds its environment (B369, D172).</summary>
/// <remarks>
/// **`PhysicsLevelInit`** (`game/client/physics.cpp:184-186`) builds the world and then the static props. The world is
/// <c>PhysCreateWorld_Shared</c> (`game/shared/physics_shared.cpp:588-690`): solid 0, the static solids, and — at its end, when the
/// collide carries a `virtualterrain` block — <c>PhysCreateVirtualTerrain</c>. Brush entities come after, when their entities spawn.
/// </remarks>
public static class IvpMapWorld
{
    /// <summary><c>LUMP_PHYSDISP</c>, <c>bspfile.h:309</c>.</summary>
    private const int PhysDispLump = 28;

    /// <summary><c>LUMP_PHYSCOLLIDE</c>, <c>bspfile.h:310</c>.</summary>
    private const int PhysCollideLump = 29;

    /// <summary>What was made, by kind.</summary>
    /// <param name="World">The world model's objects.</param>
    /// <param name="Terrain">The displacements'.</param>
    /// <param name="Props">The static props'.</param>
    /// <param name="BrushEntities">The brush entities' models'.</param>
    public sealed record Objects(
        IReadOnlyList<IvpCollisionObject> World,
        IReadOnlyList<IvpCollisionObject> Terrain,
        IReadOnlyList<IvpCollisionObject> Props,
        IReadOnlyList<IvpCollisionObject> BrushEntities);

    /// <summary>A map's collide as read from its lumps and its props' models — read once, loaded into as many worlds as need it.</summary>
    /// <param name="World">The world model.</param>
    /// <param name="BrushEntities">Every other brush model.</param>
    /// <param name="Origins">Each brush model's entity origin, by model index.</param>
    /// <param name="Terrain">Each displacement's tree and flag, or null when the world carries no virtual terrain.</param>
    /// <param name="Hulls">Each displacement's <c>LUMP_PHYSDISP</c> blob.</param>
    /// <param name="Props">The static props, in lump order.</param>
    /// <param name="Collides">Each prop model's first solid, or null when it has none.</param>
    public sealed record Collide(
        IReadOnlyList<MapPhysicsModel> World,
        IReadOnlyList<MapPhysicsModel> BrushEntities,
        IReadOnlyDictionary<int, System.Numerics.Vector3> Origins,
        IReadOnlyList<(DisplacementCollisionTree Tree, bool NoPhysics)?>? Terrain,
        IReadOnlyList<byte[]?> Hulls,
        IReadOnlyList<BspStaticProp> Props,
        IReadOnlyDictionary<string, IvpStaticPropCollide?> Collides);

    /// <summary>Reads a map's collide — the lumps, the displacements and each static prop's model.</summary>
    /// <param name="map">The map's bytes.</param>
    /// <param name="read">Reads a game file, the map's pakfile first; null when absent.</param>
    /// <param name="log">Where a lump or model that will not read is reported.</param>
    /// <returns>What <see cref="Load(IvpRagdollWorld, Collide)"/> builds a world from.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The map's headers or displacement lumps are malformed.</exception>
    public static Collide Parse(ReadOnlyMemory<byte> map, Func<string, byte[]?> read, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(log);

        BspHeader header = BspHeader.Parse(map.Span);
        IReadOnlyList<MapPhysicsModel> models = BspPhysicsCollision.Read(BspLumpData.Read(map, header.Lump(PhysCollideLump)));
        MapPhysicsModel[] worldModel = [.. models.Where(model => model.ModelIndex == 0)];
        bool terrain = worldModel.Length > 0 && MapSurfaceTable.Parse(worldModel[0].Text).HasVirtualTerrain;
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(map);
        Dictionary<string, IvpStaticPropCollide?> collides = [];

        foreach (BspStaticProp prop in props)
        {
            if (!collides.ContainsKey(prop.Model))
            {
                collides[prop.Model] = FirstSolid(prop.Model, read, log);
            }
        }

        return new Collide(
            worldModel,
            [.. models.Where(model => model.ModelIndex != 0)],
            MapLevel.BrushModelOrigins(BspEntities.ReadFrom(map)),
            terrain ? Displacements(map) : null,
            terrain ? BspPhysicsDisplacements.Read(BspLumpData.Read(map, header.Lump(PhysDispLump)), BspTerrain.Create(map).Count) : [],
            props,
            collides);
    }

    /// <summary>Loads a read collide into a world, in the order <c>PhysicsLevelInit</c> builds it.</summary>
    /// <param name="world">The world.</param>
    /// <param name="collide">The map's collide, from <see cref="Parse"/>.</param>
    /// <returns>The objects of each kind.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static Objects Load(IvpRagdollWorld world, Collide collide)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(collide);

        IReadOnlyList<IvpCollisionObject> worldObjects = world.AddMap(collide.World, collide.Origins);
        IReadOnlyList<IvpCollisionObject> terrain = collide.Terrain is { } displacements
            ? world.AddVirtualTerrain(displacements, collide.Hulls)
            : [];
        IReadOnlyList<IvpCollisionObject> props = world.AddStaticProps(
            collide.Props, model => collide.Collides.TryGetValue(model, out IvpStaticPropCollide? found) ? found : null);
        IReadOnlyList<IvpCollisionObject> brushEntities = world.AddMap(collide.BrushEntities, collide.Origins);

        return new Objects(worldObjects, terrain, props, brushEntities);
    }

    /// <summary>Loads a map into a world.</summary>
    /// <param name="world">The world.</param>
    /// <param name="map">The map's bytes.</param>
    /// <param name="read">Reads a game file, the map's pakfile first; null when absent.</param>
    /// <param name="log">Where a lump or model that will not read is reported.</param>
    /// <returns>The objects of each kind.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The map's headers or displacement lumps are malformed.</exception>
    public static Objects Load(IvpRagdollWorld world, ReadOnlyMemory<byte> map, Func<string, byte[]?> read, ILogger log) =>
        Load(world, Parse(map, read, log));

    /// <summary>What builds a corpse environment for a map — a fresh world with the map's collide loaded — at any tick interval.</summary>
    /// <param name="map">The map's bytes.</param>
    /// <param name="game">The install, for the static props' models, or null for none.</param>
    /// <param name="surfaces">The game's surfaces.</param>
    /// <param name="log">Where a lump or model that will not read is reported.</param>
    /// <returns>The factory <see cref="CorpsePhysics.CreateWorld"/> takes.</returns>
    /// <remarks>
    /// **`PhysicsLevelInit`'s environment** (`game/client/physics.cpp:163-187`): gravity at <c>sv_gravity</c>'s default and the
    /// demo's tick as the step. A model is read from the map's pakfile first, then the install, as the engine's search path orders them.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="surfaces"/> or <paramref name="log"/> is null.</exception>
    public static Func<float, IvpRagdollWorld> Factory(ReadOnlyMemory<byte> map, GameContent? game, VphysicsSurfaceProps surfaces, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(log);

        PakFile pak = PakFile.ReadFrom(map);

        // **Read once, at level init, built per world**: a backward seek rebuilds the environment (D179), and reading the lumps and
        // every prop's model was 1,207 ms of a 1,298 ms rebuild on cp_process_f12. The engine reads the collide at `PhysicsLevelInit`,
        // so the read belongs to the map load and a seek pays only for making the objects, which are the environment's own.
        Collide collide = Parse(map, file => Read(pak, game, file, log), log);

        return interval =>
        {
            IvpRagdollWorld world = new(interval, new System.Numerics.Vector3(0f, 0f, -PhysicsEnvironment.DefaultGravity), surfaces);
            Load(world, collide);
            return world;
        };
    }

    /// <summary>The map's solid static props as a line trace meets them (<see cref="StaticPropCollision"/>).</summary>
    /// <param name="map">The map's bytes.</param>
    /// <param name="game">The install, for the props' models, or null for none.</param>
    /// <param name="surfaces">The game's surfaces, which name each model's `$surfaceprop`.</param>
    /// <param name="log">Where a model that will not read is reported.</param>
    /// <returns>The props.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surfaces"/> or <paramref name="log"/> is null.</exception>
    public static StaticPropCollision StaticProps(ReadOnlyMemory<byte> map, GameContent? game, VphysicsSurfaceProps surfaces, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(log);

        PakFile pak = PakFile.ReadFrom(map);
        Dictionary<string, IvpStaticPropCollide?> collides = new(StringComparer.OrdinalIgnoreCase);

        return StaticPropCollision.From(
            BspStaticProps.Read(map),
            model =>
            {
                if (!collides.TryGetValue(model, out IvpStaticPropCollide? found))
                {
                    collides[model] = found = FirstSolid(model, file => Read(pak, game, file, log), log);
                }

                return found;
            },
            model => ModelSurfaceProp(Read(pak, game, model, log)) is { } name ? surfaces.GetSurfaceIndex(name) : -1);
    }

    /// <summary>`studiohdr_t::pszSurfaceProp()`: the name at the offset in <c>surfacepropindex</c>, <c>+0x134</c>.</summary>
    /// <remarks>The offset engine.dll's `ClipRayToCollideable` reads (`0x18018f510`); `public/studio.h:2304`.</remarks>
    internal static string? ModelSurfaceProp(byte[]? model)
    {
        const int SurfacePropIndex = 0x134;

        if (model is null || model.Length < SurfacePropIndex + 4)
        {
            return null;
        }

        int at = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(model.AsSpan(SurfacePropIndex));

        if (at <= 0 || at >= model.Length)
        {
            return null;
        }

        int end = Array.IndexOf(model, (byte)0, at);

        return System.Text.Encoding.ASCII.GetString(model, at, (end < 0 ? model.Length : end) - at);
    }

    /// <summary>Reads a game file, the map's own pakfile first — the order the engine's search path gives them.</summary>
    /// <remarks>
    /// **A malformed archive entry costs one prop its collision, not the map its load**, and is reported; a missing file is the
    /// normal case and is not.
    /// </remarks>
    private static byte[]? Read(PakFile pak, GameContent? game, string file, ILogger log)
    {
        try
        {
            return pak.ReadFile(file) ?? game?.Archives.Read(file);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            log.LogWarning(failure, "reading {File} for the map's collide", file);
            return null;
        }
    }

    /// <summary>Each displacement's tree and flag, by displacement index — through the face that names it.</summary>
    private static List<(DisplacementCollisionTree Tree, bool NoPhysics)?> Displacements(ReadOnlyMemory<byte> map)
    {
        BspTerrain terrain = BspTerrain.Create(map);
        List<(DisplacementCollisionTree, bool)?> byIndex = [.. Enumerable.Repeat<(DisplacementCollisionTree, bool)?>(null, terrain.Count)];

        foreach (BspSurface surface in BspSurfaces.Read(map))
        {
            if (surface.IsDisplacement && surface.DisplacementIndex >= 0 && surface.DisplacementIndex < terrain.Count)
            {
                byIndex[surface.DisplacementIndex] = terrain.ReadCollisionTree(surface);
            }
        }

        return byIndex;
    }

    /// <summary>A model's first solid and its first <c>solid</c> block's surface property, or null when it has no collide.</summary>
    /// <remarks>**A `.phy` is a stranger's file (D32)**: one that will not read costs that model its collision, and is reported.</remarks>
    private static IvpStaticPropCollide? FirstSolid(string model, Func<string, byte[]?> read, ILogger log)
    {
        string path = Path.ChangeExtension(model, ".phy");

        if (read(path) is not { Length: > 0 } file)
        {
            return null;
        }

        try
        {
            PhysicsModel physics = PhysicsModel.Read(file);

            if (physics.Surfaces.Count == 0 || physics.Surfaces[0] is not { } surface)
            {
                return null;
            }

            string? surfaceProp = physics.Solids.Count > 0 ? physics.Solids[0].SurfaceProperty : null;

            return new IvpStaticPropCollide(surface, surfaceProp);
        }
        catch (InvalidDataException failure)
        {
            log.LogWarning(failure, "reading the collision of {Path}", path);
            return null;
        }
    }
}
