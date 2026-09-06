using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which game sub-lumps a map carries, and which of them this project reads.
/// </summary>
/// <remarks>
/// **Lump 35 holds several lumps of its own, and only one of them is read here.** `sprp` is the
/// static props; `dprp` is the DETAIL props — the grass, weeds and clutter `vbsp` scatters from a
/// material's `%detailtype`. Nothing in this project has ever opened the second, and a probe that
/// lists the directory is the cheapest way to see what else is in there.
///
/// **The count is the interesting column, not the presence.** A map that declares `dprp` with an
/// empty payload is a map with no detail props, which is a different answer from a map this reader
/// cannot open.
///
/// <code>
///   game-lumps koth_harvest_final
/// </code>
/// </remarks>
public sealed class GameLumpProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "game-lumps";

    /// <inheritdoc/>
    public string Summary => "which game sub-lumps a map carries: game-lumps <map>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("game-lumps <map>");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);

        IReadOnlyList<BspGameLumpEntry> entries = BspGameLumps.Directory(file);

        output.WriteLine($"{Path.GetFileName(path)}: {entries.Count} game sub-lumps");

        foreach (BspGameLumpEntry entry in entries)
        {
            int packed = entry.PackedEnd - entry.Offset;

            string compressed = packed < entry.StoredLength ? "  (COMPRESSED)" : string.Empty;

            // **The LAST entry has no next offset, so its bound is the end of the file** — and
            // printing that subtraction as a byte count reads as corruption. Measured on
            // `cp_granary`: `dplh` reported "4 bytes stored, 24,769,230 on disk", which is not a
            // 24 MB lump but the distance to EOF. `BspGameLumps.Payload` already truncates to
            // `StoredLength`, so nothing over-reads; only the report was misleading, and an
            // instrument that invites a wrong conclusion is a defect in the instrument
            // (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).
            //
            // **Decided by the offsets rather than by how big the number looks.** A threshold
            // would be a guess that happens to fit granary; the last entry is exactly the one no
            // other entry starts after, which is the same question `BspGameLumps.NextOffset` asks.
            bool last = entries.All(other => other.Offset <= entry.Offset);

            string extent = last
                ? "bounded by the end of the file (nothing starts after it)"
                : packed.ToString("N0", CultureInfo.InvariantCulture) + " on disk";

            output.WriteLine(
                $"  '{entry.Name}' at " +
                entry.Offset.ToString("N0", CultureInfo.InvariantCulture) + ", version " +
                entry.Version.ToString(CultureInfo.InvariantCulture) + ", flags 0x" +
                entry.Flags.ToString("X", CultureInfo.InvariantCulture) + ", " +
                entry.StoredLength.ToString("N0", CultureInfo.InvariantCulture) +
                " bytes stored, " + extent + compressed);

            // **The first integer of a detail-prop payload is its object count**, which is the one
            // number that says whether implementing it would change any picture.
            if (!string.Equals(entry.Name, "dprp", StringComparison.Ordinal))
            {
                continue;
            }

            ReadOnlyMemory<byte> payload = BspGameLumps.Payload(file, entry);

            output.WriteLine(
                $"      detail props: {BspDetailProps.Count(payload).ToString("N0", CultureInfo.InvariantCulture)}");

            // **The type split decides what drawing them would cost.** A sprite needs a quad
            // builder and the one shared `detail/detailsprites` material; a model goes through the
            // path static props already use. Reporting the split rather than the total is what
            // separates "a day of work" from "an afternoon".
            (IReadOnlyList<string> models, IReadOnlyList<BspDetailSprite> sprites,
                IReadOnlyList<BspDetailProp> objects) = BspDetailProps.Read(file);

            Dictionary<DetailPropType, int> byType = [];

            foreach (DetailPropType type in objects.Select(prop => prop.Type))
            {
                byType[type] = byType.TryGetValue(type, out int seen) ? seen + 1 : 1;
            }

            output.WriteLine(
                $"      dictionaries: {models.Count} models, {sprites.Count} sprite rectangles");

            // **The rectangle is what decides whether a quad has any area at all.** A count of
            // 28,699 sprites says nothing if the dictionary they index describes a degenerate
            // quad, and that failure is invisible on screen — it draws nothing, exactly like not
            // being implemented.
            for (int rect = 0; rect < sprites.Count; rect++)
            {
                BspDetailSprite sprite = sprites[rect];

                output.WriteLine(
                    $"        sprite rect {rect.ToString(CultureInfo.InvariantCulture)}: " +
                    $"ul ({sprite.UpperLeft.X.ToString("F2", CultureInfo.InvariantCulture)}, " +
                    $"{sprite.UpperLeft.Y.ToString("F2", CultureInfo.InvariantCulture)}) " +
                    $"lr ({sprite.LowerRight.X.ToString("F2", CultureInfo.InvariantCulture)}, " +
                    $"{sprite.LowerRight.Y.ToString("F2", CultureInfo.InvariantCulture)}) " +
                    $"tex ul ({sprite.TextureUpperLeft.X.ToString("F3", CultureInfo.InvariantCulture)}, " +
                    $"{sprite.TextureUpperLeft.Y.ToString("F3", CultureInfo.InvariantCulture)}) " +
                    $"lr ({sprite.TextureLowerRight.X.ToString("F3", CultureInfo.InvariantCulture)}, " +
                    $"{sprite.TextureLowerRight.Y.ToString("F3", CultureInfo.InvariantCulture)})");
            }

            foreach ((DetailPropType type, int count) in byType)
            {
                output.WriteLine(
                    $"        {type}: {count.ToString("N0", CultureInfo.InvariantCulture)}");
            }

            // **Orientation decides whether a quad can be built once or must face the camera.**
            // `DETAIL_PROP_ORIENT_NORMAL` is a fixed quad and could go in a static buffer;
            // the two screen-aligned kinds have to be turned every frame, which is the difference
            // between reusing the world's geometry path and needing a billboard of its own.
            Dictionary<int, int> byOrientation = [];

            foreach (int orientation in objects.Select(prop => prop.Orientation))
            {
                byOrientation[orientation] =
                    byOrientation.TryGetValue(orientation, out int seen) ? seen + 1 : 1;
            }

            // **Crossed with the type, because the two are not the same split and the difference
            // decides what is drawn.** `koth_harvest_final` is 28,699 sprites of which 20,117 are
            // fixed; `cp_granary` is 19,189 sprites of which NONE are, plus 324 models. Reporting
            // the two margins separately made granary look like a map with 324 fixed sprites.
            Dictionary<(DetailPropType Type, int Orientation), int> crossed = [];

            foreach (BspDetailProp prop in objects)
            {
                (DetailPropType, int) key = (prop.Type, prop.Orientation);

                crossed[key] = crossed.TryGetValue(key, out int both) ? both + 1 : 1;
            }

            foreach (((DetailPropType type, int orientation), int count) in
                crossed.OrderByDescending(entry => entry.Value))
            {
                output.WriteLine(
                    $"        {type} at orientation {orientation.ToString(CultureInfo.InvariantCulture)}: " +
                    count.ToString("N0", CultureInfo.InvariantCulture));
            }

            foreach ((int orientation, int count) in byOrientation)
            {
                string named = orientation switch
                {
                    0 => "NORMAL (fixed)",
                    1 => "SCREEN_ALIGNED",
                    2 => "SCREEN_ALIGNED_VERTICAL",
                    _ => "unknown",
                };

                output.WriteLine(
                    $"        orientation {named}: " +
                    count.ToString("N0", CultureInfo.InvariantCulture));
            }

            // **Whether the angles use pitch and roll decides which basis this needs.**
            // `AngleVectors.Right`/`Up` in this project assume roll is zero and say so; a detail
            // prop carries a full QAngle, so the question is whether any map fills it in.
            int pitched = 0;
            int rolled = 0;

            foreach ((float pitch, float _, float roll) in objects.Select(prop => prop.Angles))
            {
                if (pitch != 0f)
                {
                    pitched++;
                }

                if (roll != 0f)
                {
                    rolled++;
                }
            }

            output.WriteLine(
                $"        angles: {pitched.ToString("N0", CultureInfo.InvariantCulture)} with " +
                $"pitch, {rolled.ToString("N0", CultureInfo.InvariantCulture)} with roll");

            foreach (string model in models)
            {
                output.WriteLine($"        model '{model}'");
            }

            // **Type and orientation are different questions and correlate differently per map**,
            // which is a trap this probe walked into: on `cp_granary` every fixed-orientation
            // object is a MODEL and every SPRITE is screen-aligned, so selecting on orientation
            // alone reported models as sprites — and printed `m_flScale`, which a model does not
            // use, as −181,657,600. Selecting on the TYPE is what these two lines are about.
            Dictionary<(int X, int Y), int> cells = [];

            foreach ((float x, float y, float _) in objects
                .Where(prop => prop.Type == DetailPropType.Sprite)
                .Select(prop => prop.Origin))
            {
                (int X, int Y) cell = ((int)(x / 512f), (int)(y / 512f));

                cells[cell] = cells.TryGetValue(cell, out int seen) ? seen + 1 : 1;
            }

            // **Where to point a camera.** A count says the lump was read; it says nothing about
            // whether the quads land on the ground somebody can look at. The densest 512-unit cell
            // is the one place a screenshot is guaranteed to contain grass, and reporting it beats
            // guessing at a map's layout from a picture.
            foreach (((int x, int y), int count) in
                cells.OrderByDescending(cell => cell.Value).Take(3))
            {
                output.WriteLine(
                    $"        densest sprite cell {(x * 512).ToString(CultureInfo.InvariantCulture)} " +
                    $"{(y * 512).ToString(CultureInfo.InvariantCulture)}: " +
                    count.ToString("N0", CultureInfo.InvariantCulture) + " sprites");
            }

            foreach (BspDetailProp prop in
                objects.Where(prop => prop.Type == DetailPropType.Sprite).Take(3))
            {
                output.WriteLine(
                    $"        sprite {prop.DetailModel.ToString(CultureInfo.InvariantCulture)} at " +
                    $"({prop.Origin.X.ToString("F0", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Origin.Y.ToString("F0", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Origin.Z.ToString("F0", CultureInfo.InvariantCulture)}) angles " +
                    $"({prop.Angles.Pitch.ToString("F1", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Angles.Yaw.ToString("F1", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Angles.Roll.ToString("F1", CultureInfo.InvariantCulture)}) scale " +
                    prop.Scale.ToString("F2", CultureInfo.InvariantCulture) + " light " +
                    $"({prop.Lighting.Red.ToString("F0", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Lighting.Green.ToString("F0", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Lighting.Blue.ToString("F0", CultureInfo.InvariantCulture)})");
            }

            // **The MODELS, which this reported only as a count** (B363). A count says the map has
            // them; a coordinate is what lets somebody point a camera at one and see whether it
            // draws — and the two populations are nowhere near each other on `cp_granary`, so the
            // sprite examples above cannot stand in for them.
            foreach (BspDetailProp prop in
                objects.Where(prop => prop.Type == DetailPropType.Model).Take(3))
            {
                output.WriteLine(
                    $"        MODEL {prop.DetailModel.ToString(CultureInfo.InvariantCulture)} " +
                    $"'{(prop.DetailModel >= 0 && prop.DetailModel < models.Count ? models[prop.DetailModel] : "OUT OF RANGE")}' at " +
                    $"({prop.Origin.X.ToString("F0", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Origin.Y.ToString("F0", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Origin.Z.ToString("F0", CultureInfo.InvariantCulture)}) angles " +
                    $"({prop.Angles.Pitch.ToString("F1", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Angles.Yaw.ToString("F1", CultureInfo.InvariantCulture)}, " +
                    $"{prop.Angles.Roll.ToString("F1", CultureInfo.InvariantCulture)}) orientation " +
                    prop.Orientation.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
