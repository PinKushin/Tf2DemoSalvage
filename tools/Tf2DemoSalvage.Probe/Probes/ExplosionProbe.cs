using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every explosion a demo carries, and where to stand to see one (B415).</summary>
/// <remarks>
/// **This exists to aim a camera from the DATA.** Eight guessed camera positions hit walls before
/// `docs/memory/point-the-camera-from-the-data.md` was written; a blast's own origin and normal say where it is
/// and which way it faces, so a screenshot of one is arithmetic rather than a search.
///
/// <code>
///   explosions &lt;demo&gt;             — the census, by weapon and by surface
///   explosions &lt;demo&gt; shots [n]   — n blasts with a TF2VIEW_CAMERA line for each
///   explosions &lt;demo&gt; hits [n]    — the same, only blasts that struck a player
/// </code>
/// </remarks>
public sealed class ExplosionProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "explosions";

    /// <inheritdoc/>
    public string Summary =>
        "every explosion a demo carries, with a camera for each: explosions <demo> [shots|hits [n]]";

    /// <summary>How far back to stand from a blast, in units.</summary>
    /// <remarks>Far enough that `ExplosionCore_MidAir`'s whole plume is in frame rather than filling it.</remarks>
    private const float StandOff = 220f;

    /// <summary>How far above it, so the camera looks slightly down rather than through the floor.</summary>
    private const float Rise = 60f;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("Usage: explosions <demo> [shots [n]]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        IReadOnlyList<SceneExplosion> blasts = timeline.Explosions.All;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {blasts.Count} explosions, ticks " +
            $"{timeline.FirstTick} to {timeline.LastTick}"));

        if (blasts.Count == 0)
        {
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {blasts.Count(one => one.InAir)} in mid air, " +
            $"{blasts.Count(one => one.HasEntity)} against an entity, " +
            $"{blasts.Count(one => one.StruckPlayer)} of those a player, " +
            $"{blasts.Count(one => one.HasCustomParticle)} naming their own particle"));

        foreach ((int weapon, int count) in blasts
            .GroupBy(one => one.WeaponId)
            .Select(one => (one.Key, one.Count()))
            .OrderByDescending(one => one.Item2))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    weapon {weapon,3} ({Content.Assets.TfWeaponAliases.Of(weapon) ?? "unknown"}): {count}"));
        }

        // `m_nDefID` and `m_nSound`, which pick an item's replacement explosion sound.
        foreach ((int item, int sound, int count) in blasts
            .GroupBy(one => (one.ItemDefinition, one.WeaponSound))
            .Select(one => (one.Key.ItemDefinition, one.Key.WeaponSound, one.Count()))
            .OrderByDescending(one => one.Item3))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    item {item,5} sound {sound,2}: {count}"));
        }

        bool hits = arguments.Count >= 2 && arguments[1].Equals("hits", StringComparison.OrdinalIgnoreCase);

        if (!hits && (arguments.Count < 2 ||
            !arguments[1].Equals("shots", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // `hits` aims only at blasts that struck a player, which draw the weapon's player effect.
        if (hits)
        {
            blasts = [.. blasts.Where(one => one.StruckPlayer)];
        }

        int wanted = arguments.Count > 2
            ? int.Parse(arguments[2], CultureInfo.InvariantCulture)
            : 5;

        output.WriteLine();

        // **Spread across the match rather than the first few**, because the first explosions of a demo tend to
        // cluster in one place and a camera that works for all of them proves less than one that works anywhere.
        int step = Math.Max(1, blasts.Count / wanted);

        for (int at = 0; at < blasts.Count && at / step < wanted; at += step)
        {
            SceneExplosion blast = blasts[at];

            // **Standing back along the blast's own NORMAL where it has one**, so the camera is on the side of
            // the surface the effect is drawn on rather than inside it. A mid-air blast has no normal to use, so
            // it is viewed from due south, which is as good as any direction and is stated rather than tuned.
            (float nx, float ny) = blast.InAir ? (0f, -1f) : (blast.Normal.X, blast.Normal.Y);

            float length = MathF.Sqrt((nx * nx) + (ny * ny));

            if (length < 1e-3f)
            {
                (nx, ny, length) = (0f, -1f, 1f);
            }

            float ex = blast.X + (nx / length * StandOff);
            float ey = blast.Y + (ny / length * StandOff);
            float ez = blast.Z + Rise;

            (float pitch, float yaw, float _) =
                Scene.AngleVectors.Angles(blast.X - ex, blast.Y - ey, blast.Z - ez);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  #{at} tick {blast.Tick} at ({blast.X:0} {blast.Y:0} {blast.Z:0}) " +
                $"weapon {blast.WeaponId} {(blast.InAir ? "MID AIR" : "on a surface")}" +
                $"{(blast.StruckPlayer ? $" HIT PLAYER {blast.Entity}" : string.Empty)}"));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"      TF2VIEW_CAMERA=\"{ex:0} {ey:0} {ez:0} {pitch:0} {yaw:0}\" " +
                $"--tick {blast.Tick}"));
        }
    }
}
