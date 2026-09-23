using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>The server's `Impact` dispatches on entities — the bullets that decal a player's model (B415).</summary>
/// <remarks>
/// **Through `ServerImpacts.OnEntities`, the viewer's own selection** (B243), counted per entity, then the ticks with the
/// most hits and a camera line placed back along the first one's shot.
///
/// **On TF2 these are crossbow bolts, not bullets**: `CTFProjectile_Arrow` traces a "blood mesh decal" from
/// `vecOrigin - vecVelocity * frametime` (`tf_projectile_arrow.cpp:553`), so the shot is about 31 units long on f12 (median
/// of 233) and its start is the bolt's, not a shooter's eye. Bullet blood on players is the client's own (`FireBullet`'s
/// server `bDoEffects` is false).
/// </remarks>
public sealed class EntityImpactProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "entity-impacts";

    /// <inheritdoc/>
    public string Summary => "the server's impacts on entities (player decals), per entity and by tick: entity-impacts <demo> [n] [from tick]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Usage: entity-impacts <demo> [n]");
            return;
        }

        int shown = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 10;
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        IReadOnlyList<(int Index, SceneEffectDispatch Dispatch)> impacts =
            ServerImpacts.OnEntities(timeline.Dispatches.All, timeline.Dispatches.Names.Name);

        output.WriteLine($"{impacts.Count} impacts on entities");

        foreach (IGrouping<int, (int Index, SceneEffectDispatch Dispatch)> entity in impacts.GroupBy(one => one.Dispatch.Entity))
        {
            output.WriteLine($"  entity {entity.Key}: {entity.Count()}");
        }

        int from = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : 0;

        foreach ((int _, SceneEffectDispatch hit) in impacts.Where(one => one.Dispatch.Tick >= from).Take(shown))
        {
            string who = timeline.Roster.Values.FirstOrDefault(player => player.EntityIndex == hit.Entity).Name ?? "?";

            System.Numerics.Vector3 start = new(hit.Start.X, hit.Start.Y, hit.Start.Z);
            System.Numerics.Vector3 at = new(hit.Origin.X, hit.Origin.Y, hit.Origin.Z);
            System.Numerics.Vector3 back = at + (System.Numerics.Vector3.Normalize(start - at) * 80f);
            System.Numerics.Vector3 look = at - back;
            float yaw = MathF.Atan2(look.Y, look.X) * 180f / MathF.PI;
            float pitch = -MathF.Atan2(look.Z, MathF.Sqrt((look.X * look.X) + (look.Y * look.Y))) * 180f / MathF.PI;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"tick {hit.Tick} entity {hit.Entity} ({who}) shot {(at - start).Length():0} long, hitbox {hit.HitBox} surfaceprop {hit.SurfaceProp} team {hit.DamageType} at ({at.X:0} {at.Y:0} {at.Z:0})  TF2VIEW_CAMERA=\"{back.X:0} {back.Y:0} {back.Z:0} {pitch:0} {yaw:0}\""));
        }
    }
}
