using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What one loaded map keeps, by the field that owns it, in bytes.</summary>
/// <remarks>
/// **The D217 step 4 census method, pointed at a map.** The map is read by the production path, <see cref="LoadedMap.Read"/>
/// with no demo, which is what the viewer holds per map. The owners are the fields of <see cref="MapLevel"/> (the lumps),
/// then of <see cref="MapAssets"/> (textures, models, particles), then of <see cref="LoadedMap"/> itself, then the game
/// content and every static field of this project's types. The bytes reachable from an owner that no earlier owner
/// reached are its own (<see cref="StateCopy.Footprint"/>, first owner wins), so the sum counts nothing twice.
///
/// **Three controls.** The sum, less what existed before the load, is printed beside the managed heap the load left
/// live (full collections before and after); an owner list that missed a root falls short of it. The texture slots
/// holding pixels are printed, which must be non-zero on any real map. And after <see cref="MapAssets.ReleaseUploaded"/>
/// every released pixel array is watched by a weak reference, which must die.
///
/// **A wrong turn this probe made, kept because it is the trap:** a first version read <c>assets.Textures.Count</c> in
/// <see cref="Run"/>'s own frame, and the JIT kept that list alive for the whole method — every "released" pixel array
/// stayed alive and the heap disagreed with the census by 188 MB. Every read of a pre-release list is now in a
/// non-inlined helper. A heap control disagreeing with a census is the instrument's problem until shown otherwise.
///
/// What this cannot see: GPU memory, native allocations, and anything the viewer builds and keeps OUTSIDE the
/// <see cref="LoadedMap"/> (the world build is printed separately because the viewer drops it after upload).
/// </remarks>
public sealed class MapMemoryProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "map-memory";

    /// <inheritdoc/>
    public string Summary => "bytes one loaded map keeps, by owning field: map-memory <map> [maxTextureSize=256]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine(Summary);
            return;
        }

        int size = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 256;
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments[0]) is not { } path || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine($"No map '{arguments[0]}' or no game folder.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        long gameBefore = StateCopy.Footprint(game, Empty(), Empty());
        output.WriteLine($"{path}: file {Mb(bytes.Length)} MB, maxTextureSize {size}");
        output.WriteLine($"control: GameContent before the load = {Mb(gameBefore)} MB");

        long before = Settled();
        LoadedMap map = LoadedMap.Read(bytes, game, timeline: null, size, NullLoggerFactory.Instance);
        long after = Settled();

        if (map.Assets is not { } assets)
        {
            output.WriteLine($"No assets: {map.Problem}");
            return;
        }

        output.WriteLine($"control: managed heap live after load minus before = {Mb(after - before)} MB");
        output.WriteLine($"control: {WithPixels(assets)} base texture slots hold pixels");
        output.WriteLine($"base texture formats: {Formats(assets)}");

        Report(output, "AS LOADED", map, assets, game, bytes, gameBefore);
        ReportModels(output, assets);
        ReportWorld(output, map);

        // The released pixels must die unless something outside the map holds them: watch each one.
        List<WeakReference> pixels = Watch(assets);
        assets.ReleaseUploaded();
        long released = Settled();
        output.WriteLine($"control: {pixels.Count(pixel => pixel.IsAlive)} of {pixels.Count} released base-texture pixel arrays still alive");
        output.WriteLine($"control: managed heap live after ReleaseUploaded minus before load = {Mb(released - before)} MB");
        Report(output, "AFTER ReleaseUploaded (what the viewer keeps)", map, assets, game, bytes, gameBefore);

        GC.KeepAlive(map);
    }

    private static HashSet<object> Empty() => new(ReferenceEqualityComparer.Instance);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S1215:\"GC.Collect\" should not be called",
        Justification = "The control is the heap a load leaves live: a full collection before each reading.")]
    private static long Settled()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    // Out of Run's frame, so no stack slot of Run keeps a pre-release list alive (the wrong turn in the remarks).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string WithPixels(MapAssets assets) =>
        $"{assets.Textures.Count(texture => texture is { Image.IsEmpty: false })} of {assets.Textures.Count}";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> Watch(MapAssets assets)
    {
        List<WeakReference> watched = [];

        foreach (MapTexture? texture in assets.Textures)
        {
            if (texture is { } present)
            {
                foreach (ReadOnlyMemory<byte> level in present.Image.Levels)
                {
                    if (MemoryMarshal.TryGetArray(level, out ArraySegment<byte> segment) && segment.Array is { } array)
                    {
                        watched.Add(new WeakReference(array));
                    }
                }
            }
        }

        return watched;
    }

    /// <summary>The world the viewer builds and uploads, then drops: a transient, measured alone.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportWorld(TextWriter output, LoadedMap map)
    {
        long before = Settled();
        MapWorld world = map.BuildWorld(NullLoggerFactory.Instance);
        long after = Settled();
        HashSet<object> seen = Empty();
        seen.Add(map);

        // Charged only for what the map did not already hold: the build shares the map's lumps.
        foreach (object owned in StateCopy.Reachable(map))
        {
            seen.Add(owned);
        }

        output.WriteLine();
        output.WriteLine($"== WORLD BUILD (uploaded, then dropped by the viewer): {Mb(StateCopy.Footprint(world, Empty(), seen))} MB beyond the map; heap grew {Mb(after - before)} MB");
        GC.KeepAlive(world);
    }

    /// <summary>The largest owner one level down: each entity model, and what holds its bytes.</summary>
    private static void ReportModels(TextWriter output, MapAssets assets)
    {
        HashSet<object> seen = Empty();
        List<(string Path, long Bytes, Dictionary<Type, long> ByType)> models = [];

        foreach ((string path, PropModels.ModelFrames frames) in assets.EntityModels)
        {
            Dictionary<Type, long> byType = [];
            models.Add((path, StateCopy.Footprint(frames, Empty(), seen, byType), byType));
        }

        output.WriteLine();
        output.WriteLine($"== Assets.EntityModels: {models.Count} models, {Mb(models.Sum(model => model.Bytes))} MB; PropVertex is {Unsafe.SizeOf<PropVertex>()} bytes; largest:");

        foreach ((string path, long owned, Dictionary<Type, long> byType) in models.OrderByDescending(model => model.Bytes).Take(12))
        {
            string top = string.Join(", ", byType.OrderByDescending(pair => pair.Value).Take(3)
                .Select(pair => $"{pair.Key.Name} {Mb(pair.Value)}"));
            PropModels.ModelFrames frames = assets.EntityModels[path];
            output.WriteLine(
                $"{Mb(owned),9} MB  {path,-60} {top}; skinned {frames.Skinned is not null}, {frames.Geometry.Count} vertex lists " +
                $"of {(frames.Geometry.Count > 0 ? frames.Geometry.Min(list => list.Count) : 0)}-{(frames.Geometry.Count > 0 ? frames.Geometry.Max(list => list.Count) : 0)} vertices");
        }
    }

    /// <summary>What the base textures are in memory: block-compressed or decoded, and how large the largest is.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Formats(MapAssets assets)
    {
        List<MapTexture> present = [.. assets.Textures.OfType<MapTexture>().Where(texture => !texture.Image.IsEmpty)];
        MapTexture largest = present.MaxBy(texture => texture.Width * texture.Height);
        string formats = string.Join(", ", present.GroupBy(texture => texture.Image.Format)
            .OrderByDescending(group => group.Count()).Select(group => $"{group.Key} {group.Count()}"));
        return $"{formats}; largest decoded {largest.Width}x{largest.Height} (authored {largest.MappingWidth}x{largest.MappingHeight})";
    }

    private static IEnumerable<(string Name, object? Value)> FieldsOf(object root, string prefix)
    {
        // Reaching the private backing fields is the whole of a census by owner.
#pragma warning disable S3011
        FieldInfo[] fields = root.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
#pragma warning restore S3011

        return fields.Select(field => ($"{prefix}{field.Name.Trim('<').Split('>')[0]}", field.GetValue(root)));
    }

    private static void Report(TextWriter output, string title, LoadedMap map, MapAssets assets, GameContent game, byte[] file, long gameBefore)
    {
        output.WriteLine();
        output.WriteLine($"== {title}");

        // The map file is the caller's: a field reaching a slice of it is not charged for it.
        HashSet<object> seen = Empty();
        seen.Add(file);
        HashSet<object> none = Empty();
        List<(string Owner, long Bytes, Dictionary<Type, long> ByType)> rows = [];

        IEnumerable<(string Name, object? Value)> owners = FieldsOf(map.Level, "Level.")
            .Concat(FieldsOf(assets, "Assets."))
            .Concat(FieldsOf(map, "LoadedMap."))
            .Append(("(GameContent, not per map)", game))
            .Concat(Statics());

        foreach ((string owner, object? value) in owners)
        {
            if (value is null)
            {
                continue;
            }

            Dictionary<Type, long> byType = [];
            rows.Add((owner, StateCopy.Footprint(value, none, seen, byType), byType));
        }

        foreach ((string owner, long owned, Dictionary<Type, long> byType) in rows.OrderByDescending(row => row.Bytes))
        {
            // Every lump owner is printed however small: "is collision or vis kept" needs its row even at 0.1 MB.
            if (owned < 1 << 20 && !owner.StartsWith("Level.", StringComparison.Ordinal))
            {
                continue;
            }

            string top = string.Join(", ", byType.OrderByDescending(pair => pair.Value).Take(3)
                .Select(pair => $"{pair.Key.Name} {Mb(pair.Value)}"));
            output.WriteLine($"{Mb(owned),9} MB  {owner,-36} {top}");
        }

        long sum = rows.Sum(row => row.Bytes);
        output.WriteLine($"{Mb(sum),9} MB  SUM over {rows.Count} owners (owners under 1 MB not listed); less GameContent before the load: {Mb(sum - gameBefore)} MB");
    }

    /// <summary>Every static field of this project's types: a cache outside the map holds a map just as well.</summary>
    private static IEnumerable<(string Name, object? Value)> Statics()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("Tf2DemoSalvage", StringComparison.Ordinal) == true))
        {
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException partial)
            {
                types = [.. partial.Types.OfType<Type>()];
            }

            foreach (Type type in types.Where(type => !type.ContainsGenericParameters))
            {
#pragma warning disable S3011
                FieldInfo[] fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
#pragma warning restore S3011

                foreach (FieldInfo field in fields.Where(field => !field.IsLiteral))
                {
                    yield return ($"static {type.Name}.{field.Name}", field.GetValue(null));
                }
            }
        }
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture);
}
