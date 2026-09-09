using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether the game's compiled scene archive is reachable, and what it declares (B351).
/// </summary>
/// <remarks>
/// **A taunt is a choreographed SCENE, and the wire carries only its filename.** `DT_SceneEntity`
/// sends `m_nSceneStringIndex` into the `"Scenes"` network string table
/// (`gameinterface.cpp:1448`), and the client then loads the compiled VCD for that name through
/// `ISceneFileCache` (`c_sceneentity.cpp:753`). The sequence the player actually plays is a
/// parameter string inside that compiled scene — `LookupSequence( event->GetParameters() )`,
/// `c_tf_player.cpp:9456` — so nothing about the animation arrives over the network.
///
/// **So B351's cost is decided by whether this file is readable**, and this probe answers exactly
/// that: is `scenes/scenes.image` present, does it carry `VSIF` version 2, and how many scenes does
/// it declare.
///
/// <code>
///   scene-image
/// </code>
///
/// **The header is published** even though the reader is not: `SceneImageFile.h` gives
/// `SCENE_IMAGE_ID = MAKEID('V','S','I','F')` and `SCENE_IMAGE_VERSION = 2`, then a header of five
/// ints followed by a string-offset table. `scenefilecache.dll` — the thing that parses it at
/// runtime — ships no source, but it does not have to: the format is declared.
/// </remarks>
public sealed class SceneImageProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "scene-image";

    /// <inheritdoc/>
    public string Summary => "the compiled scene archive taunts live in: scene-image";

    /// <summary>Where the archive sits inside the game's VPKs.</summary>
    private const string ScenePath = "scenes/scenes.image";

    /// <summary><c>MAKEID('V','S','I','F')</c>.</summary>
    private const uint SceneImageId = 0x46495356;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        if (folder is null)
        {
            output.WriteLine("No TF2 install found, so the scene archive cannot be read.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);

        byte[]? image = archives.Read(ScenePath);

        if (image is null)
        {
            // **Before believing an absence, ask for something that must be there.** A shipped
            // material proves the archive search works at all, so "no scenes.image" cannot be
            // confused with "the reader found nothing anywhere".
            byte[]? control = archives.Read("materials/detail/detailsprites.vmt");

            output.WriteLine(
                $"'{ScenePath}' is not in the game's archives. Control: " +
                $"detailsprites.vmt {(control is null ? "ALSO missing — the reader is broken" : $"read, {control.Length} bytes — the reader works")}.");

            return;
        }

        output.WriteLine(
            $"{ScenePath}: {image.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes");

        if (image.Length < 20)
        {
            output.WriteLine("  too short to carry a header.");
            return;
        }

        uint id = BinaryPrimitives.ReadUInt32LittleEndian(image);
        int version = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4));
        int scenes = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(8));
        int strings = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(12));
        int entries = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(16));

        output.WriteLine(
            $"  id 0x{id.ToString("X8", CultureInfo.InvariantCulture)} " +
            $"({(id == SceneImageId ? "VSIF, as SceneImageFile.h declares" : "NOT VSIF")}), " +
            $"version {version.ToString(CultureInfo.InvariantCulture)} " +
            $"({(version == 2 ? "matches SCENE_IMAGE_VERSION" : "UNEXPECTED")})");

        output.WriteLine(
            $"  {scenes.ToString("N0", CultureInfo.InvariantCulture)} scenes, " +
            $"{strings.ToString("N0", CultureInfo.InvariantCulture)} pooled strings, " +
            $"directory at offset {entries.ToString("N0", CultureInfo.InvariantCulture)}");

        // **Each directory entry is four ints — CRC, offset, length, summary offset — and they are
        // sorted by CRC so the runtime can binary search.** Reporting the first few proves the
        // directory is where the header says rather than only that the header parsed.
        if (id != SceneImageId || entries <= 0 || entries + 16 > image.Length)
        {
            return;
        }

        int shown = Math.Min(3, scenes);

        for (int index = 0; index < shown; index++)
        {
            int at = entries + (index * 16);

            if (at + 16 > image.Length)
            {
                break;
            }

            output.WriteLine(
                $"    entry {index.ToString(CultureInfo.InvariantCulture)}: " +
                $"crc 0x{BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at)).ToString("X8", CultureInfo.InvariantCulture)}, " +
                $"data at {BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 4)).ToString("N0", CultureInfo.InvariantCulture)}, " +
                $"{BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 8)).ToString("N0", CultureInfo.InvariantCulture)} bytes");
        }

        // **The PRODUCTION reader on the same file, and the thing B351 actually needs.** Everything
        // above proves the archive can be walked; only this says a scene NAME reaches a sequence
        // name, which is what `LookupSequence` is handed (`c_tf_player.cpp:9456`).
        if (SceneImage.Read(image) is not { } archive)
        {
            output.WriteLine("  SceneImage.Read refused it.");
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"  SceneImage.Read: {archive.Count:N0} scenes"));

        // **The control, first: hashes taken OUT of the directory must be found in it.** Without
        // this, every empty answer below reads as "that name is wrong" when it could equally be
        // "the search is broken", and those need different fixes.
        int found = ((int[])[0, 1, 2, archive.Count / 2, archive.Count - 1])
            .Count(slot => archive.Contains(archive.CrcAt(slot)));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  control: {found} of 5 hashes read from the directory were found by the search," +
            $" and an invented name resolves to " +
            $"{archive.SequenceFor("no_such_scene_at_all") ?? "(nothing)"}"));

        // **The census over the WHOLE archive, not over the taunts.** The taunt paths below are 730
        // of 9,939 scenes, so passing all of them says the walk works on 7.3% of the population — a
        // stride wrong for a kind of event no taunt uses would pass every one.
        int whole = 0;
        int named = 0;
        List<(int Slot, SceneWalk Walk)> truncated = [];

        for (int slot = 0; slot < archive.Count; slot++)
        {
            SceneWalk walk = archive.SequenceAt(slot);

            if (walk.Complete)
            {
                whole++;
            }
            else
            {
                truncated.Add((slot, walk));
            }

            if (walk.Sequence is { Length: > 0 })
            {
                named++;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  census: {whole:N0} of {archive.Count:N0} scenes walked to the end, " +
            $"{truncated.Count:N0} ran out of bytes, {named:N0} name a sequence"));

        // **Name the ones that did not finish rather than reporting a rate.** A 99.98% pass over a
        // decode is this project's defect until shown otherwise
        // (`docs/memory/decode-must-be-total.md`), and the cursor plus the bytes around it is what
        // makes one diagnosable instead of merely counted.
        foreach ((int slot, SceneWalk walk) in truncated.Take(6))
        {
            ReadOnlySpan<byte> body = archive.BodyAt(slot).Span;
            int from = Math.Max(0, Math.Min(walk.Stopped, body.Length) - 24);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    INCOMPLETE slot {slot:N0}, crc 0x{archive.CrcAt(slot):X8}, " +
                $"stopped at {walk.Stopped:N0} of {walk.Length:N0}, " +
                $"bytes {from:N0}..: {Convert.ToHexString(body[from..])}, " +
                $"head {Convert.ToHexString(body[..Math.Min(20, body.Length)])}"));

            // **The last few events, with the offset each began at.** The cursor alone says the
            // walk over-consumed; the event it was in says by which stride.
            IReadOnlyList<SceneEvent> events = archive.EventsAt(slot);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"      {events.Count:N0} events; last six:"));

            foreach (SceneEvent one in events.Skip(Math.Max(0, events.Count - 6)))
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"        at {one.At:N0}: type {one.Type}, {one.Start:F3}..{one.End:F3}, " +
                    $"'{one.Parameters}'"));
            }
        }

        // **Real scene paths, taken from the game's own schema rather than guessed.** Four invented
        // names were tried first and every one came back empty, which says nothing while the names
        // are unverified — `items_game.txt` carries `custom_taunt_scene_per_class`, and the shipped
        // data is the source people forget.
        if (archives.Read("scripts/items/items_game.txt") is not { Length: > 0 } schema)
        {
            output.WriteLine("  items_game.txt is not in the archives.");
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"  items_game.txt: {schema.Length:N0} bytes"));

        List<string> paths = [];

        foreach (string line in Encoding.UTF8.GetString(schema).Split('\n'))
        {
            int at = line.IndexOf(".vcd", StringComparison.OrdinalIgnoreCase);

            if (at < 0)
            {
                continue;
            }

            // The value is the last quoted field on the line.
            int close = line.LastIndexOf('"');
            int open = close > 0 ? line.LastIndexOf('"', close - 1) : -1;

            if (open >= 0 && close > open + 1 && !paths.Contains(line[(open + 1)..close]))
            {
                paths.Add(line[(open + 1)..close]);
            }
        }

        output.WriteLine($"  {paths.Count} distinct taunt scene paths read from the schema");

        // **The measurement, not a sample.** Six resolving proves the walk works on six files; the
        // rate over every path the schema names is what says whether it works on the format.
        List<string> silent = [];
        int resolved = 0;

        foreach (string scene in paths)
        {
            if (archive.SequenceFor(scene) is { Length: > 0 })
            {
                resolved++;
            }
            else
            {
                silent.Add(scene);
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {resolved:N0} of {paths.Count:N0} named a sequence"));

        foreach (string scene in paths.Take(6))
        {
            output.WriteLine($"    '{scene}' -> {archive.SequenceFor(scene) ?? "(nothing)"}");
        }

        foreach (string scene in silent.Take(8))
        {
            output.WriteLine(
                $"    SILENT '{scene}': in the directory {archive.BodyFor(scene).Length} bytes");
        }

        // **Which of the three causes a null sequence has, separated.** The CRC lookup is proven
        // above, so an empty answer for a name that IS in the directory is either a failed
        // decompress or a failed event walk — and those need different fixes.
        foreach (string scene in paths.Take(2))
        {
            ReadOnlyMemory<byte> body = archive.BodyFor(scene);

            if (body.IsEmpty)
            {
                output.WriteLine($"    body '{scene}': EMPTY");
                continue;
            }

            ReadOnlySpan<byte> head = body.Span[..Math.Min(24, body.Length)];

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    body '{scene}': {body.Length:N0} bytes, " +
                $"tag '{Encoding.ASCII.GetString(head[..Math.Min(4, head.Length)])}', " +
                $"{Convert.ToHexString(head)}"));
        }

        // **Which byte order the digest is in, tested rather than assumed.** The normalisation is
        // now known to match the compiler exactly — `V_strlower`, `V_FixSlashes`, from `scenes\`,
        // hashed without the terminator (`sceneimage.cpp:439`) — and Valve's CRC32 is the standard
        // one, so the only variable left is how the digest bytes are read back.
        foreach (string scene in paths.Take(2))
        {
            byte[] name = Encoding.ASCII.GetBytes(scene.Replace('/', '\\'));

            for (int index = 0; index < name.Length; index++)
            {
                if (name[index] is >= (byte)'A' and <= (byte)'Z')
                {
                    name[index] += 'a' - 'A';
                }
            }

            byte[] digest = System.IO.Hashing.Crc32.Hash(name);

            uint little = BinaryPrimitives.ReadUInt32LittleEndian(digest);
            uint big = BinaryPrimitives.ReadUInt32BigEndian(digest);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    '{scene}': little 0x{little:X8} {(archive.Contains(little) ? "FOUND" : "no")}, " +
                $"big 0x{big:X8} {(archive.Contains(big) ? "FOUND" : "no")}"));
        }
    }
}
