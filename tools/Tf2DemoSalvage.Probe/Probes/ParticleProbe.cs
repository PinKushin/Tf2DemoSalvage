using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What TF2's particle data actually is, measured before anything is built against it (B373).
/// </summary>
/// <remarks>
/// **A rocket draws its model and none of its effects, and the question is how big that gap is.**
/// `C_TFProjectile_Rocket::CreateTrails` (`c_tf_projectile_rocket.cpp:48`) asks for named systems —
/// `rockettrail`, `rockettrail_underwater`, `rockettrail_airstrike`, `critical_rocket_red` — and
/// those names index `.pcf` files the game ships. This counts them rather than guessing, because
/// "a particle system is a big subsystem" is an assertion until somebody measures the denominator.
///
/// <code>
///   particles                 the files and how much they hold
///   particles rockettrail     which file declares one system
/// </code>
/// </remarks>
public sealed class ParticleProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "particles";

    /// <inheritdoc/>
    public string Summary =>
        "the particle files TF2 ships and the systems they declare: particles [system substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } game)
        {
            output.WriteLine("No TF2 install found, so there is nothing to measure.");
            return;
        }

        string filter = arguments.Count > 0 ? arguments[0] : string.Empty;

        // **Every archive, because a `.pcf` can live in any of them** — and a probe that opens one
        // by hand and reports "not found" is answering about itself
        // (`docs/memory/an-empty-search-needs-a-control.md`).
        List<string> archives =
        [
            .. Directory.EnumerateFiles(game, "*_dir.vpk").OrderBy(one => one, StringComparer.Ordinal),
        ];

        output.WriteLine($"{archives.Count} archives in {game}");

        long files = 0;
        long bytes = 0;
        List<(string Archive, string Path, long Size)> found = [];

        foreach (string archive in archives)
        {
            VpkArchive open = VpkArchive.Open(archive);

            foreach (string path in open.Paths)
            {
                if (!path.EndsWith(".pcf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                files++;

                long size = open.TryFind(path, out VpkEntry entry) ? entry.Size : 0;

                bytes += size;
                found.Add((Path.GetFileName(archive), path, size));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {files} .pcf files, {bytes / 1024d / 1024d:0.0} MB total"));

        // **The largest few, because a format's cost is carried by its biggest files** and a count
        // alone cannot say whether this is a weekend or a month.
        foreach ((string archive, string path, long size) in found
            .OrderByDescending(one => one.Size)
            .Take(8))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {size / 1024d:0} KB  {path}  ({archive})"));
        }

        if (found.Count == 0)
        {
            output.WriteLine("  none — which for a game that draws rocket trails means this probe " +
                "is looking in the wrong place, not that TF2 ships no particles.");
            return;
        }

        // **The format's own header, read rather than assumed.** A `.pcf` is a DMX file and its
        // first line is plain text naming the encoding and version, which decides how much of a
        // reader is needed: a binary DMX is a different job from a keyvalues one.
        (string _, string sample, long _) = found.OrderByDescending(one => one.Size).First();

        foreach (string archive in archives)
        {
            VpkArchive open = VpkArchive.Open(archive);

            if (open.ReadFile(sample) is not { Length: > 64 } bytesOf)
            {
                continue;
            }

            string header = Encoding.ASCII.GetString(bytesOf, 0, 64).Split('\0')[0];

            output.WriteLine($"  header of {Path.GetFileName(sample)}: {header}");
            break;
        }

        if (filter.Length == 0)
        {
            return;
        }

        // **Which OPERATORS the effects a rocket needs actually use, ranked.** The SDK ships
        // `particles.h` and no operator implementations, so every one is a closed-binary read
        // (B373) — and there is no point starting with the ones TF2 ships but this project never
        // reaches. `functionName` is the class each operator element names.
        if (filter.Equals("operators", StringComparison.OrdinalIgnoreCase))
        {
            Dictionary<string, int> used = new(StringComparer.Ordinal);
            int systems = 0;

            foreach (string archive in archives)
            {
                VpkArchive open = VpkArchive.Open(archive);

                foreach ((string _, string path, long _) in found.Where(
                    one => one.Archive == Path.GetFileName(archive)))
                {
                    if (open.ReadFile(path) is not { } raw)
                    {
                        continue;
                    }

                    foreach (DmxElement element in DmxFile.Read(raw))
                    {
                        if (element.Type == "DmeParticleSystemDefinition")
                        {
                            systems++;
                        }

                        if (element.Type != "DmeParticleOperator" ||
                            !element.Attributes.TryGetValue("functionName", out DmxValue function) ||
                            function.Text is not { Length: > 0 } named)
                        {
                            continue;
                        }

                        used[named] = used.TryGetValue(named, out int already) ? already + 1 : 1;
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {systems} particle systems across {found.Count} files, " +
                $"{used.Count} distinct operators"));

            foreach ((string named, int count) in used
                .OrderByDescending(one => one.Value)
                .Take(20))
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture, $"    {count,5}  {named}"));
            }

            // **What each one is PARAMETERISED by, which is the strongest source short of the
            // binary.** The implementations are closed, so a name alone would have to be guessed
            // at — but the attributes a real operator carries constrain it hard: an operator with
            // `lifetime_min` and `lifetime_max` picks a value in a range, and one with
            // `gravity` and `drag` integrates a velocity.
            foreach (string archive in archives)
            {
                VpkArchive open = VpkArchive.Open(archive);

                if (open.ReadFile("particles/rockettrail.pcf") is not { } trail)
                {
                    continue;
                }

                HashSet<string> shown = new(StringComparer.Ordinal);

                foreach (IReadOnlyDictionary<string, DmxValue> bag in DmxFile.Read(trail)
                    .Where(one => one.Type == "DmeParticleOperator")
                    .Select(one => one.Attributes))
                {
                    if (!bag.TryGetValue("functionName", out DmxValue what) ||
                        what.Text is not { Length: > 0 } named ||
                        !shown.Add(named))
                    {
                        continue;
                    }

                    output.WriteLine($"    {named}");

                    foreach ((string attribute, DmxValue value) in bag
                        .Where(one => one.Key is not ("functionName" or "name"))
                        .OrderBy(one => one.Key, StringComparer.Ordinal))
                    {
                        output.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"        {attribute} ({value.Type}) = " +
                            $"{value.Text ?? value.Number.ToString("0.###", CultureInfo.InvariantCulture)}"));
                    }
                }

                break;
            }

            return;
        }

        // **The sheet resource in a real `.vtf`, because its payload format is not published.**
        // `CSheet` is forward-declared in `particles.h` and defined nowhere in the SDK, so the
        // structure has to come from the FILES — the same route the compact ledges took
        // (`docs/findings/51`). The container IS published: `vtf.h:531` gives
        // `ResourceEntryInfo { uint32 eType; uint32 resData; }` after `numResources`, the id is
        // `MK_VTF_RSRC_ID( 0x10, 0, 0 )`, and the HIGH BYTE of `eType` is flags — so
        // `RSRCF_HAS_NO_DATA_CHUNK` (`0x02 << 24`) means `resData` IS the data rather than an
        // offset, and reading the offset unconditionally returns garbage.
        if (filter.Equals("sheet", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string archive in archives)
            {
                VpkArchive open = VpkArchive.Open(archive);

                // **The texture the MATERIAL names, not the material's own name.**
                // `effects/rocketrailsmoke.vmt` is a `SpriteCard` whose `$basetexture` is
                // `effects/smoke/smokelit` — asking for a `.vtf` beside the `.vmt` finds nothing,
                // which is how this probe first reported "not found" for a texture that ships.
                if (open.ReadFile("materials/effects/smoke/smokelit.vtf") is not
                    { Length: > 96 } vtf)
                {
                    continue;
                }

                int version = BitConverter.ToInt32(vtf, 8);
                int headerSize = BitConverter.ToInt32(vtf, 12);

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  rocketrailsmoke.vtf: {vtf.Length} bytes, version 7.{version}, " +
                    $"header {headerSize}"));

                if (version < 3)
                {
                    output.WriteLine("    below 7.3, so it carries no resources at all.");
                    return;
                }

                // **The header's tail, dumped rather than indexed.** `numResources` sits after the
                // 7.2 fields and three pad bytes, and getting that offset wrong reports zero
                // resources for a file that has them — which it did, at 0x4C. The bytes settle it.
                output.WriteLine("    --- header tail, 0x38 to 0x58 ---");

                for (int row = 0; row < 2; row++)
                {
                    int from = 0x38 + (row * 16);

                    string hex = string.Join(' ', vtf.Skip(from).Take(16)
                        .Select(one => one.ToString("x2", CultureInfo.InvariantCulture)));

                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture, $"      0x{from:x2}  {hex}"));
                }

                // Both candidate offsets, so the right one is chosen by which is plausible rather
                // than by which was assumed.
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"      numResources at 0x44 = {BitConverter.ToInt32(vtf, 0x44)}, " +
                    $"at 0x48 = {BitConverter.ToInt32(vtf, 0x48)}, " +
                    $"at 0x4C = {BitConverter.ToInt32(vtf, 0x4C)}"));

                int resources = BitConverter.ToInt32(vtf, 0x44);

                if (resources is < 0 or > 32)
                {
                    output.WriteLine("    that count is not plausible; the offset is still wrong.");
                    return;
                }

                output.WriteLine($"    {resources} resources");

                // Entries follow numResources, after its own four bytes of padding.
                for (int index = 0; index < resources && 0x50 + (index * 8) + 8 <= vtf.Length; index++)
                {
                    int at = 0x50 + (index * 8);

                    uint type = BitConverter.ToUInt32(vtf, at);
                    uint data = BitConverter.ToUInt32(vtf, at + 4);

                    uint id = type & 0x00FFFFFF;
                    uint flags = (type >> 24) & 0xFF;

                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"      id 0x{id:x6} flags 0x{flags:x2} data {data}" +
                        $"{((flags & 0x02) != 0 ? "  (IS the data, no chunk)" : "  (offset)")}"));

                    if (id != 0x10 || (flags & 0x02) != 0 || data + 64 > vtf.Length)
                    {
                        continue;
                    }

                    output.WriteLine("    --- sheet payload, first 64 bytes ---");

                    for (int row = 0; row < 4; row++)
                    {
                        int from = (int)data + (row * 16);

                        string hex = string.Join(' ', vtf.Skip(from).Take(16)
                            .Select(one => one.ToString("x2", CultureInfo.InvariantCulture)));

                        output.WriteLine($"      {from,8}  {hex}");
                    }

                    // The first words, which is where a version and a count would sit.
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"      as int32: {BitConverter.ToInt32(vtf, (int)data)}, " +
                        $"{BitConverter.ToInt32(vtf, (int)data + 4)}, " +
                        $"{BitConverter.ToInt32(vtf, (int)data + 8)}, " +
                        $"{BitConverter.ToInt32(vtf, (int)data + 12)}"));
                }

                return;
            }

            output.WriteLine("  materials/effects/rocketrailsmoke.vtf not found.");
            return;
        }

        // **The simulator run on Valve's OWN definition, end to end.** The synthetic tests prove
        // each operator does what its parameters say; this proves the layers join — a real `.pcf`
        // through `DmxFile`, `ParticleSystems`, a `ParticleStore` and the operators, with the
        // numbers coming from the file rather than from a fixture
        // (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
        if (filter.Equals("simulate", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string archive in archives)
            {
                VpkArchive open = VpkArchive.Open(archive);

                if (open.ReadFile("particles/rockettrail.pcf") is not { } raw)
                {
                    continue;
                }

                IReadOnlyDictionary<string, ParticleSystem> systems = ParticleSystems.Read(raw);

                if (!systems.TryGetValue("rockettrail_!", out ParticleSystem? trail) &&
                    !systems.TryGetValue("rockettrail", out trail))
                {
                    output.WriteLine("  no rockettrail system in the file.");
                    return;
                }

                IReadOnlyDictionary<string, IParticleOperator> known = ParticleOperators.All();

                int applied = 0;
                int unknown = 0;

                foreach (string named in trail.Operators.Select(one => one.Function))
                {
                    if (known.ContainsKey(named))
                    {
                        applied++;
                    }
                    else
                    {
                        unknown++;
                        output.WriteLine($"    NOT IMPLEMENTED: '{named}'");
                    }
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  '{trail.Name}': {applied} of {trail.Operators.Count} operators implemented, " +
                    $"{unknown} not"));

                // **The real runtime**, emitting at the system's own rate rather than one a step:
                // emit, operate, reap, with the emitter moving as a rocket would.
                ParticleEffect effect = new(trail);
                ParticleStore store = effect.Particles;
                const float step = 1f / 66f;

                for (int tick = 0; tick < 20; tick++)
                {
                    effect.Step(new Vector3(tick * 4f, 0f, 0f), step);
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  after 20 steps: {store.Count} alive, " +
                    $"first alpha {(store.Count > 0 ? store.AlphaOf(0) : 0f):0.###}, " +
                    $"first radius {(store.Count > 0 ? store.RadiusOf(0) : 0f):0.###}"));

                List<DetailSpriteVertex> corners = [];

                ParticleSprites.Build(store, Vector3.UnitX, Vector3.UnitZ, corners);

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  ParticleSprites: {corners.Count} corners for {store.Count} particles " +
                    $"({ParticleSprites.CornersPerParticle} each)"));

                return;
            }

            output.WriteLine("  particles/rockettrail.pcf not found.");
            return;
        }

        // **The bytes themselves, because the SDK ships dmxloader's HEADERS and not its
        // implementation.** `dmattributetypes.h` gives the attribute type enum — the schema — and
        // `src/dmxloader/*.cpp` is absent, so the binary LAYOUT has to come from a file. The header
        // line is plain text and everything after it is the string table.
        if (filter.Equals("dump", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string archive in archives)
            {
                VpkArchive open = VpkArchive.Open(archive);

                if (open.ReadFile("particles/rockettrail.pcf") is not { Length: > 512 } raw)
                {
                    continue;
                }

                int start = Array.IndexOf(raw, (byte)0) + 1;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  rockettrail.pcf: {raw.Length} bytes, header ends at {start}"));

                // **Walk the string table so the ELEMENT layout can be seen.** `binary 2` writes a
                // uint16 count and then that many null-terminated strings; everything after them is
                // the element section, and its shape is what a reader has to get right.
                int count = BitConverter.ToUInt16(raw, start);
                int after = start + 2;

                for (int index = 0; index < count && after < raw.Length; index++)
                {
                    after = Array.IndexOf(raw, (byte)0, after) + 1;
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    string table: {count} strings, ends at {after}; " +
                    $"next int32 = {BitConverter.ToInt32(raw, after)} (element count)"));

                // **The reader against the real file, which is the only thing that can say the
                // layout above was read correctly.** A synthetic fixture proves the reader agrees
                // with the test that wrote it; this proves it agrees with Valve.
                IReadOnlyList<DmxElement> parsed = DmxFile.Read(raw);

                output.WriteLine($"    DmxFile.Read: {DmxFile.Census(parsed)}");

                // **The schema layer against the same file**, which is what a simulator consumes.
                IReadOnlyDictionary<string, ParticleSystem> systems = ParticleSystems.Read(raw);

                output.WriteLine($"    ParticleSystems.Read: {systems.Count} systems");

                foreach (ParticleSystem system in systems.Values.Take(2))
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"      '{system.Name}': {system.Emitters.Count} emitters, " +
                        $"{system.Initializers.Count} initializers, " +
                        $"{system.Operators.Count} operators, " +
                        $"{system.Renderers.Count} renderers, " +
                        $"{system.Children.Count} children"));

                    foreach (ParticleFunction one in system.Operators.Take(3))
                    {
                        output.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"        operator '{one.Function}' " +
                            $"gravity {one.Vector("gravity", default).Z:0.##} " +
                            $"drag {one.Number("drag", -1d):0.##}"));
                    }
                }

                foreach (DmxElement element in parsed
                    .Where(one => one.Type == "DmeParticleSystemDefinition")
                    .Take(3))
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"      '{element.Name}': {element.Attributes.Count} attributes"));

                    foreach ((string name, DmxValue value) in element.Attributes
                        .Where(one => one.Value.Type is DmxAttributeType.Text
                            or DmxAttributeType.Real or DmxAttributeType.Whole))
                    {
                        output.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"        {name} ({value.Type}) = " +
                            $"{value.Text ?? value.Number.ToString("0.##", CultureInfo.InvariantCulture)}"));
                    }
                }

                for (int row = 0; row < 6; row++)
                {
                    int at = after + 4 + (row * 16);

                    if (at + 16 > raw.Length)
                    {
                        break;
                    }

                    string hex = string.Join(
                        ' ', raw.Skip(at).Take(16).Select(one => one.ToString("x2", CultureInfo.InvariantCulture)));

                    string text = string.Concat(raw.Skip(at).Take(16)
                        .Select(one => one is >= 0x20 and < 0x7F ? (char)one : '.'));

                    output.WriteLine($"    {at,6}  {hex}  {text}");
                }

                return;
            }

            output.WriteLine("  particles/rockettrail.pcf not found in any archive.");
            return;
        }

        // **Which file declares a named system**, since `CreateTrails` asks by name and the name is
        // all the engine carries. The names are stored as plain strings in the file, so a byte
        // search answers without a DMX reader — enough to say where to look, not what it means.
        byte[] wanted = Encoding.ASCII.GetBytes(filter);
        int declaring = 0;

        foreach (string archive in archives)
        {
            VpkArchive open = VpkArchive.Open(archive);

            foreach ((string _, string path, long _) in found.Where(
                one => one.Archive == Path.GetFileName(archive)))
            {
                if (open.ReadFile(path) is not { } content ||
                    content.AsSpan().IndexOf(wanted) < 0)
                {
                    continue;
                }

                declaring++;
                output.WriteLine($"    '{filter}' appears in {path}");
            }
        }

        output.WriteLine($"  '{filter}' named by {declaring} of {found.Count} files");
    }
}
