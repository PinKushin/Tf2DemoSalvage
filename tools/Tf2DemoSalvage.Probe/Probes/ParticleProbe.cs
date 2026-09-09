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

                }

                // **The PRODUCTION reader, on the file the walk above just described.** The
                // structure came out of these bytes, so a probe that read them a second time by
                // hand would only agree with itself; `VtfSheet` is what the viewer calls, and this
                // is the only thing that can catch it disagreeing
                // (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
                int width = BitConverter.ToUInt16(vtf, 0x10);
                int height = BitConverter.ToUInt16(vtf, 0x12);

                IReadOnlyList<SheetSequence> read = VtfSheet.Read(vtf);

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    VtfSheet.Read: {read.Count} sequences, texture {width}x{height}"));

                foreach (SheetSequence sequence in read)
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"      sequence {sequence.Id}: clamp {sequence.Clamp}, " +
                        $"{sequence.Frames.Count} frames, total time {sequence.TotalTime:0.##}"));

                    // **Every frame, not the first four.** The cap this loop used to carry is what
                    // made a five-frame sequence print as four and put an open question into the
                    // finding — the INSTRUMENT's limit read as the format's, which is the fault
                    // `docs/memory/instrument-bugs-outnumber-decoder-bugs.md` collects.
                    for (int frame = 0; frame < sequence.Frames.Count; frame++)
                    {
                        SheetFrame one = sequence.Frames[frame];

                        output.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"        frame {frame}: duration {one.Duration:0.##}  " +
                            $"uv ({one.U0:0.####} {one.V0:0.####})-({one.U1:0.####} {one.V1:0.####})" +
                            $"  = px ({one.U0 * width:0}, {one.V0 * height:0})-" +
                            $"({one.U1 * width:0}, {one.V1 * height:0})"));
                    }
                }

                // **What the SYSTEM says about the sheet, which is the parity question.** The frames
                // exist in the texture; which one a particle shows is decided by the renderer's own
                // parameters, and `render_animated_sprites` is in the closed client. The `.pcf` is
                // shipped data and states them, so this prints every parameter rather than the ones
                // already guessed at — `docs/memory/shipped-data-settles-what-closed-code-cannot.md`.
                // **A second walk over EVERY archive, because the `.pcf` is not in the texture
                // one.** Reusing the archive that held `smokelit.vtf` printed nothing at all and
                // read as "the renderer declares no parameters" — the same shape as
                // `docs/memory/an-empty-search-needs-a-control.md`, one archive deep.
                foreach (IReadOnlyDictionary<string, ParticleSystem> systems in archives
                    .Select(one => VpkArchive.Open(one).ReadFile("particles/rockettrail.pcf"))
                    .Where(one => one is not null)
                    .Select(one => ParticleSystems.Read(one!))
                    .Where(one => one.ContainsKey("rockettrail")))
                {
                    ParticleSystem trail = systems["rockettrail"];

                    // **Initializers as well as the renderer, because which SEQUENCE a particle
                    // plays is set at birth and the renderer only reads it.** Printing the renderer
                    // alone would have left the sequence number looking like a constant 0 — the
                    // definition's default — when an initializer may pick one per particle.
                    // The children too, because their particles are on screen beside the parent's
                    // and nothing had ever printed what they declare.
                    List<(string, IReadOnlyList<ParticleFunction>)> kinds =
                    [
                        ("renderer", trail.Renderers),
                        ("initializer", trail.Initializers),
                        ("operator", trail.Operators),
                    ];

                    foreach (string childName in trail.Children)
                    {
                        if (systems.TryGetValue(childName, out ParticleSystem? child))
                        {
                            kinds.Add(($"CHILD {childName} emitter", child.Emitters));
                            kinds.Add(($"CHILD {childName} initializer", child.Initializers));
                        }
                    }

                    foreach ((string kind, IReadOnlyList<ParticleFunction> functions) in kinds)
                    {
                        foreach (ParticleFunction function in functions)
                        {
                            output.WriteLine($"    {kind} {function.Function}");

                            foreach (KeyValuePair<string, DmxValue> parameter in function.Parameters)
                            {
                                // **A colour printed as `.Number` reads as zero and says nothing.**
                                // `Color Random`'s two bounds came out "0" and "0" that way, which
                                // looked like an unset parameter rather than an unprinted one — the
                                // instrument answering about itself again.
                                bool vector = parameter.Value.Type
                                    is DmxAttributeType.Colour or DmxAttributeType.Vector3
                                    or DmxAttributeType.Vector4 or DmxAttributeType.Angle;

                                string shown = vector
                                    ? string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"({parameter.Value.Vector.X:0.##} {parameter.Value.Vector.Y:0.##} {parameter.Value.Vector.Z:0.##} {parameter.Value.Vector.W:0.##})")
                                    : string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"{parameter.Value.Number:0.####}");

                                output.WriteLine(
                                    $"      {parameter.Key} = {shown} ({parameter.Value.Type})");
                            }
                        }
                    }
                }

                return;
            }

            output.WriteLine("  materials/effects/rocketrailsmoke.vtf not found.");
            return;
        }

        // **What every shipped `SpriteCard` actually declares**, because two open items in B373 are
        // only worth building if some material uses them. `$modblend` is the precedent: it is
        // declared in three VMTs, read by nothing, and its correct implementation is nothing
        // (`docs/findings/12-shader-parity.md`). A census of REQUESTS is the only thing that can
        // tell those apart from a real gap
        // (`docs/memory/a-census-of-requests-beats-a-list-of-features.md`).
        if (filter.Equals("materials", StringComparison.OrdinalIgnoreCase))
        {
            string[] keys =
            [
                "$dualsequence", "$additive2ndtexture", "$texture2", "$sequence_blend_mode",
                "$extractgreenalpha", "$maxlumframeblend1", "$maxlumframeblend2", "$ramptexture",
                "$additive", "$addoverblend", "$addself", "$blendframes", "$overbrightfactor",
                "$orientation", "$depthblend", "$nocull",
            ];

            Dictionary<string, int> declared = new(StringComparer.OrdinalIgnoreCase);

            foreach (string key in keys)
            {
                declared[key] = 0;
            }

            int cards = 0;
            int materials = 0;

            foreach (string archive in archives)
            {
                VpkArchive open = VpkArchive.Open(archive);

                foreach (string path in open.Paths)
                {
                    if (!path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase) ||
                        open.ReadFile(path) is not { Length: > 0 } raw)
                    {
                        continue;
                    }

                    materials++;

                    VmtMaterial material = VmtMaterial.Parse(raw);

                    if (!material.Shader.Contains("SpriteCard", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    cards++;

                    foreach (string key in keys.Where(one => material.Value(one) is { Length: > 0 }))
                    {
                        declared[key]++;
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {cards} SpriteCard materials of {materials} shipped"));

            foreach (string key in keys)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture, $"    {key,-22} {declared[key],5}"));
            }

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
                // **With the whole file, so children resolve.** Passing the trail alone is the
                // control: it gives a rocket smoke and no glow, and the counts below say which.
                ParticleEffect effect = new(trail, systems);
                ParticleStore store = effect.Particles;
                const float step = 1f / 66f;

                for (int tick = 0; tick < 20; tick++)
                {
                    // Flying down +X with the world axes as its own, so a local-frame speed of
                    // (0 0 -10) reads as straight down and is recognisable in the output.
                    effect.Step(
                        new ParticleControlPoint(
                            new Vector3(tick * 4f, 0f, 0f),
                            Vector3.UnitX,
                            Vector3.UnitY,
                            Vector3.UnitZ),
                        step);
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  after 20 steps: {store.Count} alive, " +
                    $"first alpha {(store.Count > 0 ? store.AlphaOf(0) : 0f):0.###}, " +
                    $"first radius {(store.Count > 0 ? store.RadiusOf(0) : 0f):0.###}"));

                // **The two initializers that have no other witness.** A sphere spawn shows as
                // spread ACROSS the flight axis — the emitter walks down +X, so any Y or Z extent
                // came from the sphere and from the local-frame speed, and zero would mean the
                // initializer never ran. Rotation shows as a spread of angles; every particle at
                // the same angle is a trail drawn as a grid.
                float acrossLeast = float.MaxValue;
                float acrossMost = float.MinValue;
                float turnLeast = float.MaxValue;
                float turnMost = float.MinValue;

                for (int one = 0; one < store.Count; one++)
                {
                    acrossLeast = Math.Min(acrossLeast, store.PositionOf(one).Z);
                    acrossMost = Math.Max(acrossMost, store.PositionOf(one).Z);
                    turnLeast = Math.Min(turnLeast, store.RotationOf(one));
                    turnMost = Math.Max(turnMost, store.RotationOf(one));
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  spread across the flight axis: {acrossMost - acrossLeast:0.###} units; " +
                    $"rotation {float.RadiansToDegrees(turnLeast):0.#} to " +
                    $"{float.RadiansToDegrees(turnMost):0.#} degrees"));

                // **Each child by name, with its own material and its own particle count.** A
                // child that resolved but never emitted looks identical to one that was never
                // resolved, from the parent's side — so both numbers are printed.
                foreach (ParticleEffect child in effect.Children)
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"    child '{child.System.Name}': {child.Particles.Count} alive, " +
                        $"material '{ParticleEffects.MaterialOf(child.System)}'"));
                }

                if (effect.Children.Count == 0)
                {
                    output.WriteLine("    no children resolved");
                }

                List<DetailSpriteVertex> corners = [];

                // **The real sheet and the renderer's real parameters**, so this reports what the
                // viewer would draw rather than what a default would. A `0..1` span here means the
                // sheet did not reach the builder, whatever the reader said in isolation — and that
                // is exactly what it reported the first time it ran, because the search below was
                // `open` alone. The `.pcf` is in `tf2_misc_dir.vpk` and the texture is in
                // `tf2_textures_dir.vpk`, so a one-archive search finds the definition and none of
                // its material (`docs/memory/an-empty-search-needs-a-control.md`).
                IReadOnlyList<SheetSequence> sheet = [];

                foreach (string other in archives)
                {
                    if (VpkArchive.Open(other).ReadFile("materials/effects/smoke/smokelit.vtf")
                        is { Length: > 96 } texture)
                    {
                        sheet = VtfSheet.Read(texture);
                        break;
                    }
                }

                ParticleFunction? draws = trail.Renderers.FirstOrDefault(one =>
                    string.Equals(one.Function, "render_animated_sprites", StringComparison.Ordinal));

                ParticleSprites.Build(
                    store,
                    Vector3.UnitX,
                    Vector3.UnitZ,
                    corners,
                    sheet,
                    (float)(draws?.Number("animation rate", 1d) ?? 1d),
                    (draws?.Number("use animation rate as FPS", 0d) ?? 0d) != 0d,
                    (draws?.Number("animation_fit_lifetime", 0d) ?? 0d) != 0d);

                // **The span of ONE QUAD, not of the whole batch, and that distinction is the
                // control.** Across 45 particles sitting on different frames the batch covers the
                // full width either way, so a batch-wide 0..1 proves nothing. A single quad must
                // cover ONE TILE — a quarter of the width — and a builder that ignored the sheet
                // would put 1.0 here while the batch figure looked identical.
                float widest = 0f;
                float mostBlend = 0f;
                float apart = 0f;

                for (int at = 0; at + ParticleSprites.CornersPerParticle <= corners.Count;
                     at += ParticleSprites.CornersPerParticle)
                {
                    float least = float.MaxValue;
                    float most = float.MinValue;

                    for (int corner = 0; corner < ParticleSprites.CornersPerParticle; corner++)
                    {
                        DetailSpriteVertex one = corners[at + corner];

                        least = Math.Min(least, one.U);
                        most = Math.Max(most, one.U);
                        mostBlend = Math.Max(mostBlend, one.Blend);

                        // How far the frame being mixed TOWARD sits from the one showing. Zero on
                        // every particle would mean both coordinate sets name the same frame, which
                        // is a crossfade that fades to itself.
                        apart = Math.Max(apart, Math.Abs(one.NextU - one.U));
                    }

                    widest = Math.Max(widest, most - least);
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  ParticleSprites: {corners.Count} corners for {store.Count} particles " +
                    $"({ParticleSprites.CornersPerParticle} each), {sheet.Count} sequences, " +
                    $"widest single quad {widest:0.####} of u, frames up to {apart:0.###} apart, " +
                    $"largest blend {mostBlend:0.###}"));

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
