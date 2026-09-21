using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every tracer a demo's shots draw on its map, and where to stand to see one (B415).</summary>
/// <remarks>
/// **Through `LoadedMap.Read`, the viewer's own load**, so the tracers reported are the list the viewer draws
/// (B243) — not a second run of `HitscanTracers` that could be fed a different sweep or a different convar.
///
/// <code>
///   tracers &lt;demo&gt; &lt;map&gt; [n]   — the census by effect, then n tracers with a TF2VIEW_CAMERA line each
/// </code>
/// </remarks>
public sealed class TracerProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "tracers";

    /// <inheritdoc/>
    public string Summary => "every tracer a demo's shots draw, through the viewer's own map load: tracers <demo> <map> [n]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("Usage: tracers <demo> <map> [n]");
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (DemoCorpus.Find(arguments[0], output) is not { } path ||
            locator.FindGameFolder() is not { } folder ||
            locator.Find(arguments[1]) is not { } mapPath)
        {
            output.WriteLine("Demo, game or map not found.");
            return;
        }

        int shown = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : 5;

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 256, NullLoggerFactory.Instance);

        IReadOnlyList<ShotTracer> tracers = map.Tracers;
        IReadOnlyList<SceneShot> shots = timeline.Shots.All;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {shots.Count} shots, {shots.Count(one => one.By is null)} from nobody, " +
            $"{tracers.Count} tracers"));

        foreach (IGrouping<int, SceneShot> weapon in shots.GroupBy(one => one.WeaponId).OrderByDescending(one => one.Count()))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  weapon {weapon.Key,3} {TfAlias(weapon.Key),-34} {weapon.Count(),5} shots"));
        }

        foreach (IGrouping<string, ShotTracer> effect in tracers.GroupBy(one => one.Effect).OrderByDescending(one => one.Count()))
        {
            bool loaded = map.Assets?.ParticleSystemsByName?.ContainsKey(effect.Key) ?? false;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {effect.Key,-32} {effect.Count(),5}  {(loaded ? "definition loaded" : "NO DEFINITION")}"));
        }

        foreach (ShotTracer tracer in tracers.Take(shown))
        {
            (float X, float Y, float Z) delta = (
                tracer.End.X - tracer.Start.X, tracer.End.Y - tracer.Start.Y, tracer.End.Z - tracer.Start.Z);
            float length = MathF.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y) + (delta.Z * delta.Z));

            // Stand off to the side of the tracer's midpoint, looking back at it square on.
            (float X, float Y, float Z) middle = (
                tracer.Start.X + (delta.X / 2f), tracer.Start.Y + (delta.Y / 2f), tracer.Start.Z + (delta.Z / 2f));
            (float X, float Y) side = length > 0f ? (-delta.Y / length, delta.X / length) : (1f, 0f);
            float stand = Math.Clamp(length * 0.6f, 150f, 900f);
            (float X, float Y, float Z) camera = (middle.X + (side.X * stand), middle.Y + (side.Y * stand), middle.Z + 40f);
            float yaw = float.RadiansToDegrees(MathF.Atan2(middle.Y - camera.Y, middle.X - camera.X));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  tick {tracer.Tick} shot {tracer.Shot} bullet {tracer.Bullet} {tracer.Effect} " +
                $"({tracer.Start.X:0},{tracer.Start.Y:0},{tracer.Start.Z:0}) -> ({tracer.End.X:0},{tracer.End.Y:0},{tracer.End.Z:0}) " +
                $"{length:0} units; TF2VIEW_CAMERA=\"{camera.X:0} {camera.Y:0} {camera.Z:0} 5 {yaw:0}\""));
        }

        // **The impacts, and what each resolves to through the production chain** (B415): the decal counts are the
        // values `ImpactDecals.For` returned, not a second reading of the surface.
        IReadOnlyList<ShotImpact> impacts = map.Impacts;
        List<(ShotImpact Impact, DecalMaterial? Decal)> resolved =
            [.. impacts.Select(one => (one, map.ImpactDecals?.For(one)))];

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{impacts.Count} impacts, {resolved.Count(one => one.Decal is not null)} with a decal, " +
            $"{impacts.Count(one => one.Texinfo < 0)} on terrain; " +
            $"{map.Assets?.DecalMaterials.Count ?? 0} decal materials loaded"));

        foreach (IGrouping<string, (ShotImpact Impact, DecalMaterial? Decal)> group in
                 resolved.GroupBy(one => one.Decal?.Name ?? "(none)").OrderByDescending(one => one.Count()))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {group.Key,-40} {group.Count(),6}"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"{timeline.Blood.All.Count} blood events, {timeline.Blood.All.Count(one => one.IsPlayer)} on players"));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{timeline.Dispatches.All.Count} effect dispatches; {impacts.Count(one => one.FromServer)} server impacts on the world, " +
            $"{impacts.Count(one => one.BrushOnly)} bolt impacts"));

        foreach (IGrouping<(string?, int), SceneEffectDispatch> kind in timeline.Dispatches.All
                     .GroupBy(one => (timeline.Dispatches.Names.Name(one.Name), Math.Sign(one.Entity)))
                     .OrderByDescending(one => one.Count()))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {kind.Key.Item1 ?? "(unnamed " + kind.First().Name + ")",-40} {kind.Key.Item2 switch { 0 => "world", < 0 => "unsent", _ => "entity" },-6} {kind.Count(),5}"));
        }

        foreach (IGrouping<int, SceneEffectDispatch> struck in timeline.Dispatches.All
                     .Where(one => timeline.Dispatches.Names.Name(one.Name) is "Impact" or "TF_3rdPersonMuzzleFlash_SentryGun" or "Tracer")
                     .GroupBy(one => (one.Name << 16) | one.Entity)
                     .OrderByDescending(one => one.Count()))
        {
            SceneEffectDispatch first = struck.First();
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {timeline.Dispatches.Names.Name(first.Name)} entity {first.Entity,5} x{struck.Count(),4}; first tick {first.Tick} " +
                $"flags {first.Flags} attachment {first.Attachment} scale {first.Scale} start ({first.Start.X:0},{first.Start.Y:0},{first.Start.Z:0}) hitbox {first.HitBox} " +
                $"damage {first.DamageType} surfaceprop {first.SurfaceProp} at ({first.Origin.X:0},{first.Origin.Y:0},{first.Origin.Z:0})"));
        }

        foreach (IGrouping<(string?, int, int), SceneEffectDispatch> particle in timeline.Dispatches.All
                     .Where(one => timeline.Dispatches.Names.Name(one.Name) == "ParticleEffect")
                     .Concat(timeline.TfParticleEffects.All)
                     .GroupBy(one => (timeline.Dispatches.ParticleNames.Name(one.HitBox), one.Flags, one.DamageType))
                     .OrderByDescending(one => one.Count()))
        {
            SceneEffectDispatch first = particle.First();
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  particle {particle.Key.Item1 ?? "(index " + first.HitBox + ")",-36} flags {particle.Key.Item2} attach {particle.Key.Item3} " +
                $"x{particle.Count(),5}; first tick {first.Tick} entity {first.Entity} attachment {first.Attachment} " +
                $"colours {first.CustomColours} cp1 {first.HasControlPoint1}"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"{timeline.MuzzleFlashes.All.Count} weapon muzzle flashes"));

        WeaponMuzzleFlashes muzzles = new(game.Archives.Read, game.Weapons.Items);

        foreach (IGrouping<int?, SceneMuzzleFlash> item in timeline.MuzzleFlashes.All
                     .GroupBy(one => one.Item)
                     .OrderByDescending(one => one.Count())
                     .Take(12))
        {
            WeaponMuzzleFlash flash = muzzles.For(item.Key, item.First().Team);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  item {item.Key,6} x{item.Count(),5}; first tick {item.First().Tick} weapon {item.First().Weapon}: " +
                $"{game.Weapons.Items?.ItemClass(item.Key ?? -1)} particle {flash.Particle ?? "(none)"} model {flash.Model ?? "(none)"}"));
        }

        foreach (IGrouping<SparkKind, SceneSpark> kind in timeline.Sparks.All.GroupBy(one => one.Kind))
        {
            SceneSpark first = kind.First();

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  sparks {kind.Key,-9} x{kind.Count(),4}; first tick {first.Tick} at ({first.Position.X:0},{first.Position.Y:0},{first.Position.Z:0}) " +
                $"dir ({first.Direction.X:0.##},{first.Direction.Y:0.##},{first.Direction.Z:0.##}) magnitude {first.Magnitude} trail {first.TrailLength}"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{timeline.HealBeams.All.Count} medigun beams, {timeline.HealBeams.All.Count(one => one.ChargeRelease)} charged; " +
            $"first {(timeline.HealBeams.All.Count > 0 ? timeline.HealBeams.All[0].ToString() : "none")}"));

        foreach (SceneBlood blood in timeline.Blood.All.Take(shown))
        {
            // 120 units out along the normal — towards the shooter — looking back at the hit.
            (float X, float Y, float Z) at = (
                blood.Origin.X + (blood.Normal.X * 120f), blood.Origin.Y + (blood.Normal.Y * 120f), blood.Origin.Z + (blood.Normal.Z * 120f));
            float yaw = float.RadiansToDegrees(MathF.Atan2(-blood.Normal.Y, -blood.Normal.X));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  tick {blood.Tick} entity {blood.Entity} at ({blood.Origin.X:0},{blood.Origin.Y:0},{blood.Origin.Z:0}); " +
                $"TF2VIEW_CAMERA=\"{at.X:0} {at.Y:0} {at.Z:0} 0 {yaw:0}\""));
        }

        foreach ((ShotImpact impact, DecalMaterial? decal) in resolved.Where(one => one.Decal is not null).Take(shown))
        {
            // Back along the bullet 96 units, looking at where it stopped.
            (float X, float Y, float Z) delta = (
                impact.End.X - impact.Start.X, impact.End.Y - impact.Start.Y, impact.End.Z - impact.Start.Z);
            float length = MathF.Max(1f, MathF.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y) + (delta.Z * delta.Z)));
            float back = MathF.Min(96f, length);
            (float X, float Y, float Z) camera = (
                impact.End.X - (delta.X / length * back),
                impact.End.Y - (delta.Y / length * back),
                impact.End.Z - (delta.Z / length * back));
            float yaw = float.RadiansToDegrees(MathF.Atan2(delta.Y, delta.X));
            float pitch = -float.RadiansToDegrees(MathF.Asin(delta.Z / length));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  tick {impact.Tick} shot {impact.Shot} bullet {impact.Bullet} texinfo {impact.Texinfo} {decal?.Name} " +
                $"at ({impact.End.X:0},{impact.End.Y:0},{impact.End.Z:0}); " +
                $"TF2VIEW_CAMERA=\"{camera.X:0} {camera.Y:0} {camera.Z:0} {pitch:0} {yaw:0}\""));
        }
    }

    private static string TfAlias(int weaponId) => Content.Assets.TfWeaponAliases.Of(weaponId) ?? "?";
}
