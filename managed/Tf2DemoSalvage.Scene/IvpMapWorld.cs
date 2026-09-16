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

    /// <summary>Loads a map into a world.</summary>
    /// <param name="world">The world.</param>
    /// <param name="map">The map's bytes.</param>
    /// <param name="read">Reads a game file, the map's pakfile first; null when absent.</param>
    /// <param name="log">Where a lump or model that will not read is reported.</param>
    /// <returns>The objects of each kind.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The map's headers or displacement lumps are malformed.</exception>
    public static Objects Load(IvpRagdollWorld world, ReadOnlyMemory<byte> map, Func<string, byte[]?> read, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(log);

        BspHeader header = BspHeader.Parse(map.Span);
        IReadOnlyList<MapPhysicsModel> models = BspPhysicsCollision.Read(BspLumpData.Read(map, header.Lump(PhysCollideLump)));
        Dictionary<int, System.Numerics.Vector3> origins = MapLevel.BrushModelOrigins(BspEntities.ReadFrom(map));

        MapPhysicsModel[] worldModel = [.. models.Where(model => model.ModelIndex == 0)];
        IReadOnlyList<IvpCollisionObject> worldObjects = world.AddMap(worldModel, origins);
        IReadOnlyList<IvpCollisionObject> terrain = [];

        if (worldModel.Length > 0 && MapSurfaceTable.Parse(worldModel[0].Text).HasVirtualTerrain)
        {
            terrain = world.AddVirtualTerrain(Displacements(map), BspPhysicsDisplacements.Read(
                BspLumpData.Read(map, header.Lump(PhysDispLump)), BspTerrain.Create(map).Count));
        }

        IReadOnlyList<IvpCollisionObject> props = world.AddStaticProps(BspStaticProps.Read(map), model => FirstSolid(model, read, log));
        IReadOnlyList<IvpCollisionObject> brushEntities = world.AddMap([.. models.Where(model => model.ModelIndex != 0)], origins);

        return new Objects(worldObjects, terrain, props, brushEntities);
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
