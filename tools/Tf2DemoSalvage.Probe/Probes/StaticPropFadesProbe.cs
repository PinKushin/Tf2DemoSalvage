using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How many of a map's static props earn a fade entry, and of which kind (B430).</summary>
/// <remarks>
/// Through <see cref="StaticPropFade.For"/>, the production rule `UnserializeModels` (`engine.dll`
/// `0x180206590`) follows. The control is the total: every prop is counted in exactly one row.
///
/// With no argument it is a census of every installed map for the inputs B430 left out: props
/// flagged <c>STATIC_PROP_SCREEN_SPACE_FADE</c> (0x20), a nonzero <c>m_flForcedFadeScale</c>
/// (offset 56 from lump version 5, `gamebspfile.h`), and `worldspawn`'s `minpropscreenwidth` /
/// `maxpropscreenwidth` (`world.cpp:390-391`), the level screen fade's inputs.
/// <code>
///   static-prop-fades [map]
/// </code>
/// </remarks>
public sealed class StaticPropFadesProbe : IProbe
{
    private const int ScreenSpaceFadeFlag = 0x20;
    private const int ForcedFadeScaleOffset = 56;
    private const int ForcedFadeScaleVersion = 5;
    private const int FadeMinimumOffset = 36;

    /// <inheritdoc/>
    public string Name => "static-prop-fades";

    /// <inheritdoc/>
    public string Summary =>
        "static prop fades on one map, or with no map a census of screen-space fades, forced fade scales and worldspawn prop screen widths: static-prop-fades [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Count == 0)
        {
            Census(output, locator);
            return;
        }

        string mapName = arguments[0];

        if (locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine($"No map named '{mapName}'.");
            return;
        }

        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(File.ReadAllBytes(mapPath));
        List<(BspStaticProp Prop, StaticPropFade Fade)> fading =
        [
            .. props
                .Select(prop => (prop, fade: StaticPropFade.For(prop.Flags, prop.FadeMinimum, prop.FadeMaximum)))
                .Where(pair => pair.fade is not null)
                .Select(pair => (pair.prop, pair.fade!.Value)),
        ];

        int screen = fading.Count(pair => pair.Fade.ScreenSpace);

        output.WriteLine(
            $"{mapName}: {props.Count} static props, {props.Count - fading.Count} without a fade entry, "
            + $"{fading.Count - screen} distance, {screen} screen-space");
        output.WriteLine(
            $"  nonzero max: {fading.Count(pair => pair.Prop.FadeMaximum > 0f)}; "
            + $"max range {fading.Select(pair => pair.Prop.FadeMaximum).DefaultIfEmpty().Min():0}"
            + $"..{fading.Select(pair => pair.Prop.FadeMaximum).DefaultIfEmpty().Max():0}");

        foreach ((BspStaticProp prop, _) in fading.Take(5))
        {
            output.WriteLine(
                $"  {prop.Model} at ({prop.X:0} {prop.Y:0} {prop.Z:0}) fades {prop.FadeMinimum:0}..{prop.FadeMaximum:0}");
        }
    }

    private static void Census(TextWriter output, MapLocator locator)
    {
        if (locator.Find("koth_harvest_final") is not { } anyMap)
        {
            output.WriteLine("No installed maps found.");
            return;
        }

        string[] maps =
            [.. Directory.EnumerateFiles(Path.GetDirectoryName(anyMap)!, "*.bsp").Order(StringComparer.Ordinal)];

        int propsTotal = 0, fades = 0, screenFlag = 0, screenEntries = 0, forced = 0, withWidths = 0;
        int worldspawns = 0, scaleReadable = 0, controlMismatch = 0, unreadable = 0;

        foreach (string path in maps)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            byte[] file = File.ReadAllBytes(path);
            IReadOnlyList<BspStaticProp> props;
            IReadOnlyList<BspEntity> entities;

            try
            {
                props = BspStaticProps.Read(file);
                entities = BspEntities.ReadFrom(file);
            }
            catch (InvalidDataException failure)
            {
                // Reported, not skipped: an unreadable map counted as "none" would understate the census.
                output.WriteLine($"{name}: unreadable — {failure.Message}");
                unreadable++;
                continue;
            }

            propsTotal += props.Count;
            int mapScreenFlag = props.Count(prop => (prop.Flags & ScreenSpaceFadeFlag) != 0);
            screenFlag += mapScreenFlag;

            foreach (BspStaticProp prop in props)
            {
                if (StaticPropFade.For(prop.Flags, prop.FadeMinimum, prop.FadeMaximum) is { } fade)
                {
                    fades++;
                    screenEntries += fade.ScreenSpace ? 1 : 0;
                }
            }

            (float[]? scales, float[] minimums) = RawRecords(file);

            // The control: this second walk of the records must agree with the production reader
            // on a field both read, or its forced fade scale column is not the one it claims.
            if (minimums.Length != props.Count
                || props.Where((prop, index) =>
                    BitConverter.SingleToInt32Bits(prop.FadeMinimum) != BitConverter.SingleToInt32Bits(minimums[index])).Any())
            {
                controlMismatch++;
                output.WriteLine($"{name}: CONTROL MISMATCH — raw walk disagrees with BspStaticProps");
            }

            int mapForced = 0;

            if (scales is not null)
            {
                scaleReadable++;
                mapForced = scales.Count(scale => BitConverter.SingleToInt32Bits(scale) != BitConverter.SingleToInt32Bits(1f));
                forced += mapForced;
            }

            BspEntity? world = entities.FirstOrDefault(entity =>
                string.Equals(entity.ClassName, "worldspawn", StringComparison.OrdinalIgnoreCase));
            worldspawns += world is null ? 0 : 1;

            string minWidth = world is not null && world.TryGetValue("minpropscreenwidth", out string a) ? a : "-";
            string maxWidth = world is not null && world.TryGetValue("maxpropscreenwidth", out string b) ? b : "-";
            // An absent key is the field's zero; max <= min is the disabled sentinel (EntityFade: min 0, max -1).
            bool widths = Width(maxWidth) > Width(minWidth);
            withWidths += widths ? 1 : 0;

            if (mapScreenFlag > 0 || widths)
            {
                output.WriteLine(
                    $"{name}: {props.Count} props, {mapScreenFlag} flag 0x20, {mapForced} forced scale≠1, "
                    + $"minpropscreenwidth={minWidth} maxpropscreenwidth={maxWidth}");
            }
        }

        output.WriteLine(
            $"{maps.Length} maps ({unreadable} unreadable), {propsTotal} static props, {fades} fade entries.");
        output.WriteLine(
            $"flag 0x20: {screenFlag} props ({screenEntries} with a screen-space fade entry); "
            + $"forced fade scale ≠ 1 (the default): {forced} props on {scaleReadable} maps whose version carries it; "
            + $"level screen fade enabled (max > min): {withWidths} maps.");
        output.WriteLine(
            $"controls: {worldspawns} worldspawns (must be {maps.Length - unreadable}); "
            + $"{controlMismatch} raw-walk mismatches (must be 0).");
    }

    private static float Width(string value) =>
        float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : 0f;

    /// <summary>
    /// The forced fade scale of every prop (null below version 5), and each prop's fade minimum
    /// for the control. A second walk because production does not read the scale (D180).
    /// </summary>
    private static (float[]? Scales, float[] Minimums) RawRecords(byte[] file)
    {
        foreach (BspGameLumpEntry entry in BspGameLumps.Directory(file))
        {
            if (entry.Name != "sprp")
            {
                continue;
            }

            ReadOnlySpan<byte> payload = BspGameLumps.Payload(file, entry).Span;
            int at = 0;
            int models = BinaryPrimitives.ReadInt32LittleEndian(payload[at..]);
            at += sizeof(int) + (models * 128);
            int leaves = BinaryPrimitives.ReadInt32LittleEndian(payload[at..]);
            at += sizeof(int) + (leaves * sizeof(ushort));
            int count = BinaryPrimitives.ReadInt32LittleEndian(payload[at..]);
            at += sizeof(int);

            if (count == 0)
            {
                return (entry.Version >= ForcedFadeScaleVersion ? [] : null, []);
            }

            int stride = (payload.Length - at) / count;
            bool hasScale = entry.Version >= ForcedFadeScaleVersion && stride >= ForcedFadeScaleOffset + sizeof(float);
            float[] scales = new float[count];
            float[] minimums = new float[count];

            for (int index = 0; index < count; index++)
            {
                ReadOnlySpan<byte> prop = payload.Slice(at + (index * stride), stride);
                minimums[index] = BinaryPrimitives.ReadSingleLittleEndian(prop[FadeMinimumOffset..]);
                scales[index] = hasScale ? BinaryPrimitives.ReadSingleLittleEndian(prop[ForcedFadeScaleOffset..]) : 0f;
            }

            return (hasScale ? scales : null, minimums);
        }

        return (null, []);
    }
}
