using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Decodes a player's own texture and says what colour it actually is.
/// </summary>
/// <remarks>
/// **Every player in a first-person capture came out magenta**, and the three obvious explanations
/// are already dead: the missing-material chequer is bound zero times, the materials pair to the
/// right names, and the diagnostic colour view was not enabled. That leaves the decode and the
/// shading, and only one of the two can be settled without a window.
///
/// So this reads the VMT the way the viewer does, decodes its base texture, and reports the format
/// and the average colour. A player's own skin is brown, blue or red. If it decodes magenta the
/// fault is in <see cref="VtfTexture"/>; if it decodes correctly the fault is downstream, in the
/// model shading path, and this narrows it in one run either way.
/// </remarks>
public sealed class PlayerTextureProbe
{
    [Test]
    [Explicit("diagnostic")]
    public void PlayerTextures_AsDecoded_AreReported()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

        List<VpkArchive> archives = [.. new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" }
            .Select(name => Path.Combine(tf, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)];

        if (archives.Count == 0)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        byte[]? Find(string path)
        {
            byte[]? found = null;

            foreach (VpkArchive archive in archives)
            {
                found ??= archive.ReadFile(path);
            }

            return found;
        }

        string[] materials =
        [
            "materials/models/player/scout/scout_red.vmt",
            "materials/models/player/scout/scout_head_red.vmt",
            "materials/models/player/sniper/sniper_blue.vmt",
            "materials/models/player/soldier/soldier_red.vmt",
            "materials/models/player/medic/medic_red.vmt",
            "materials/models/player/medic/medic_blue.vmt",
            "materials/models/player/medic/medic_head_red.vmt",
        ];

        string outputDirectory = Path.Combine(Path.GetTempPath(), "claude");
        Directory.CreateDirectory(outputDirectory);

        int reported = 0;

        foreach (string path in materials)
        {
            if (Find(path) is not { } vmtBytes)
            {
                TestContext.Out.WriteLine($"{path}: no such material");
                continue;
            }

            VmtMaterial vmt = VmtMaterial.Parse(vmtBytes);

            TestContext.Out.WriteLine(
                $"{path}: shader '{vmt.Shader}' base '{vmt.BaseTexture ?? "none"}'");

            if (vmt.BaseTexture is not { Length: > 0 } name)
            {
                continue;
            }

            string texturePath = $"materials/{name.Replace('\\', '/')}.vtf";

            if (Find(texturePath) is not { } vtfBytes)
            {
                TestContext.Out.WriteLine($"  {texturePath}: no such texture");
                continue;
            }

            VtfTexture texture = VtfTexture.Decode(vtfBytes);

            // The average of the decoded pixels. A scout's jersey is brown and red; magenta would
            // be a red channel at full with a blue channel to match and no green at all, which is
            // the signature worth recognising.
            long red = 0;
            long green = 0;
            long blue = 0;
            long alpha = 0;
            int counted = 0;

            ReadOnlySpan<byte> pixels = texture.Pixels;

            for (int at = 0; at + 3 < pixels.Length; at += 4 * 97)
            {
                red += pixels[at];
                green += pixels[at + 1];
                blue += pixels[at + 2];
                alpha += pixels[at + 3];
                counted++;
            }

            if (counted == 0)
            {
                TestContext.Out.WriteLine("  decoded to no pixels at all");
                continue;
            }

            TestContext.Out.WriteLine(
                $"  format {texture.Format}, {texture.Width}x{texture.Height}, " +
                $"average rgba {red / counted},{green / counted},{blue / counted},{alpha / counted}");

            // **Written out to be LOOKED AT, because an average hides a checkerboard.** Alternating
            // blocks of magenta and grey average to something unremarkable, which is exactly how the
            // first pass of this probe reported four healthy browns while the screen showed a
            // chequered coat.
            string png = Path.Combine(
                outputDirectory, Path.GetFileNameWithoutExtension(path) + ".bmp");

            Save(texture, png);
            TestContext.Out.WriteLine($"  wrote {png}");

            reported++;
        }

        // A positive control: an empty sweep reports nothing and proves nothing.
        reported.ShouldBeGreaterThan(0, "no player material was decoded at all");
    }

    /// <summary>Writes a decoded texture out as a BMP so a person can look at it.</summary>
    /// <remarks>
    /// **Hand-rolled rather than through System.Drawing**, which this project does not reference
    /// and which is not worth a package for a diagnostic. A 24-bit BMP is a 54-byte header and
    /// bottom-up rows padded to four bytes, and nothing about it can be got subtly wrong without
    /// being obvious.
    /// </remarks>
    private static void Save(VtfTexture texture, string path)
    {
        int width = texture.Width;
        int height = texture.Height;
        int stride = ((width * 3) + 3) / 4 * 4;
        int size = 54 + (stride * height);

        byte[] file = new byte[size];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.TryWriteBytes(file.AsSpan(2), size);
        BitConverter.TryWriteBytes(file.AsSpan(10), 54);
        BitConverter.TryWriteBytes(file.AsSpan(14), 40);
        BitConverter.TryWriteBytes(file.AsSpan(18), width);
        BitConverter.TryWriteBytes(file.AsSpan(22), height);
        BitConverter.TryWriteBytes(file.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(file.AsSpan(28), (short)24);

        ReadOnlySpan<byte> pixels = texture.Pixels;

        for (int y = 0; y < height; y++)
        {
            // Bottom-up, which is the format's own order.
            int row = 54 + ((height - 1 - y) * stride);

            for (int x = 0; x < width; x++)
            {
                int at = ((y * width) + x) * 4;

                if (at + 2 >= pixels.Length)
                {
                    continue;
                }

                file[row + (x * 3)] = pixels[at + 2];
                file[row + (x * 3) + 1] = pixels[at + 1];
                file[row + (x * 3) + 2] = pixels[at];
            }
        }

        File.WriteAllBytes(path, file);
    }
}
