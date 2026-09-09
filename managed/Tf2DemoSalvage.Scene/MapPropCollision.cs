using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The static props a map places, added to its physics world the way the engine adds them (B58).
/// </summary>
/// <remarks>
/// **The engine's physics world is the brush collision AND every static prop, built two lines
/// apart.** `PhysicsLevelInit` on the CLIENT — the same function this project already cites for the
/// simulation timestep — does both and then stops:
///
/// <code>
/// g_PhysWorldObject = PhysCreateWorld_Shared( GetClientWorldEntity(), modelinfo-&gt;GetVCollide(1),
///                                             g_PhysDefaultObjectParams );
/// staticpropmgr-&gt;CreateVPhysicsRepresentations( physenv, &amp;g_SolidSetup, NULL );
/// </code>
///
/// `game/client/physics.cpp:184-186`. It is the client's environment, which is the one a ragdoll
/// lives in, so a corpse in TF2 lands on a crate because the crate is in `physenv` beside the
/// world.
///
/// **This project's world had the brushes and the terrain and not the props, and it was measured
/// costing three corpses.** On `z1800` at tick 14270, the point each escaping corpse crossed out of
/// the world at: two of the three had NOTHING within 512 units beneath them in the collision as it
/// stood, and both were standing in a doorway or a window opening — `spawnroom_door_left.mdl` and
/// `window005a.mdl` within a hundred and fifty units. A map leaves those gaps in its brushwork
/// precisely because a prop fills them.
///
/// **The owner said it from the other side and it is the same fact**: *"the ragdoll collision on
/// the ground and sceaneary is the same as the alive players"* — a live player is stopped by the
/// crate in the doorway, and a corpse was walking through it.
/// </remarks>
public static class MapPropCollision
{
    /// <summary>Adds every solid static prop's hull to a map's physics world.</summary>
    /// <param name="physics">The world to add to.</param>
    /// <param name="map">The map's bytes.</param>
    /// <param name="read">Reads a game file by path, returning null when it is not there.</param>
    /// <param name="log">Where failures are reported.</param>
    /// <returns>How many props were added, and how many the map declared as solid.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A prop with no `.phy` is skipped and says nothing**, because that is the normal case:
    /// 4,718 of the game's 4,755 models have no physics file at all, and a crate without one is a
    /// crate the compiler decided needed no hull rather than a missing asset.
    ///
    /// **Each model is read ONCE and placed many times**, which matters on a map that repeats a
    /// fence forty times — the hull is the same file every time and only the transform differs.
    /// </remarks>
    public static (int Placed, int Solid) Add(
        IvpWorldCollision physics,
        ReadOnlyMemory<byte> map,
        Func<string, byte[]?> read,
        ILogger log)
    {
        ArgumentNullException.ThrowIfNull(physics);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(log);

        IReadOnlyList<BspStaticProp> placements;

        try
        {
            placements = BspStaticProps.Read(map);
        }
        catch (InvalidDataException failure)
        {
            log.LogWarning(failure, "reading the map's static props for collision");
            return (0, 0);
        }

        Dictionary<string, IReadOnlyList<IReadOnlyList<PhysicsLedge>>?> hulls =
            new(StringComparer.OrdinalIgnoreCase);

        int placed = 0;
        int solid = 0;

        foreach (BspStaticProp prop in placements)
        {
            // **The whole rule, and it is one comparison.** `m_Solid` is the mapper's own `solid`
            // key copied through by `vbsp` (`utils/vbsp/staticprop.cpp:603`), so anything that is
            // not `SOLID_NONE` collides — `SOLID_VPHYSICS` and `SOLID_BBOX` both do, and so does
            // whatever else a map happens to carry. The entity flag `FSOLID_NOT_SOLID` plays no
            // part: a static prop lump has no flags word of that kind.
            if (prop.Solid == BspStaticProps.SolidNone)
            {
                continue;
            }

            solid++;

            if (!hulls.TryGetValue(prop.Model, out IReadOnlyList<IReadOnlyList<PhysicsLedge>>? hull))
            {
                hull = Read(prop.Model, read, log);
                hulls[prop.Model] = hull;
            }

            if (hull is null)
            {
                continue;
            }

            Matrix4x4 placement = Placement(prop);

            foreach (IReadOnlyList<PhysicsLedge> solidHull in hull)
            {
                foreach (PhysicsLedge ledge in solidHull)
                {
                    physics.Add(
                        ledge.Points, ledge.Triangles, ledge.Center, ledge.Radius, placement);
                }
            }

            placed++;
        }

        return (placed, solid);
    }

    /// <summary>The prop's placement, as the matrix the ledge points are transformed by.</summary>
    /// <remarks>
    /// **Built from <see cref="PropTransform"/> rather than from a second copy of Valve's
    /// `AngleMatrix`.** The drawing side already turns a placement's origin, angles and scale into
    /// exactly this matrix, and a prop whose collision sat at a different angle from its mesh would
    /// be the worst possible bug to look at: the crate stops you where it is not.
    /// </remarks>
    private static Matrix4x4 Placement(BspStaticProp prop)
    {
        float[] rows = new PropTransform(prop).ToMatrix();

        return new Matrix4x4(
            rows[0], rows[1], rows[2], rows[3],
            rows[4], rows[5], rows[6], rows[7],
            rows[8], rows[9], rows[10], rows[11],
            rows[12], rows[13], rows[14], rows[15]);
    }

    /// <summary>Reads a model's collision hulls, or null when it has none.</summary>
    /// <remarks>
    /// **A `.phy` is a stranger's file (D32)**, so a corrupt one costs that model its collision and
    /// not the map's. Absence is silent for the reason the ragdoll reader gives — most models
    /// genuinely have no physics file.
    /// </remarks>
    private static IReadOnlyList<IReadOnlyList<PhysicsLedge>>? Read(
        string model, Func<string, byte[]?> read, ILogger log)
    {
        string path = Path.ChangeExtension(model, ".phy");

        byte[]? file = read(path);

        if (file is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            IReadOnlyList<IReadOnlyList<PhysicsLedge>> hulls = PhysicsModel.Read(file).Hulls;

            return hulls.Count == 0 ? null : hulls;
        }
        catch (Exception failure) when (failure is InvalidDataException or ArgumentException)
        {
            log.LogWarning(failure, "reading the collision of {Path}", path);
            return null;
        }
    }
}
