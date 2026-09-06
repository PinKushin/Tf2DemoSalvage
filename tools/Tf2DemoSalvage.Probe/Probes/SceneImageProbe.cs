using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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
    }
}
