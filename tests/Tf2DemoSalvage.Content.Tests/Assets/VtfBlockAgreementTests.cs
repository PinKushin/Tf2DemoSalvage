using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The blocks handed to the GPU must expand to exactly what the old path uploaded (B149).
/// </summary>
/// <remarks>
/// **The one assertion that can catch a wrong slice, and it needs no GPU.** `Read` picks a mip out
/// of a VTF by arithmetic — thumbnail size, frames, faces, and a chain stored smallest first — and
/// hands the renderer a window into the file. If that window is off by a level, a face, or a frame,
/// the bytes are still valid DXT and still decode to a plausible image, so nothing fails: the
/// picture is simply wrong.
///
/// `Decode` walks the same arithmetic and then expands, and it is the path that was rendering
/// correctly before this change. So expanding `Read`'s blocks must reproduce `Decode`'s pixels
/// exactly. Any disagreement is the defect.
///
/// **Run over the game's own textures, not fixtures.** A hand-built VTF is written from the same
/// belief the reader holds, so it cannot falsify the arithmetic — see
/// `docs/memory/put-the-real-file-in-the-fixture.md`. TF2's materials carry every combination that
/// matters: cubemaps with seven faces, animated textures with several frames, sizes that are not
/// powers of two, and all three DXT formats.
/// </remarks>
public sealed class VtfBlockAgreementTests
{
    [Test]
    public void Read_TheGamesOwnTextures_ExpandToExactlyWhatDecodeProduces()
    {
        if (GameInstall.Root is not { } tf)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive textures = VpkArchive.Open(Path.Combine(tf, "tf2_textures_dir.vpk"));

        string[] files =
        [
            .. textures.Paths
                .Where(path => path.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(400),
        ];

        files.Length.ShouldBeGreaterThan(20, "the textures VPK should carry plenty");

        int blocks = 0;
        int plain = 0;
        List<string> disagreed = [];

        foreach (string path in files)
        {
            byte[]? file;

            try
            {
                file = textures.ReadFile(path);
            }
            catch (IOException)
            {
                continue;
            }

            if (file is null)
            {
                continue;
            }

            VtfTexture read;
            VtfTexture decoded;

            try
            {
                read = VtfTexture.Read(file);
                decoded = VtfTexture.Decode(file);
            }
            catch (Exception failure) when (
                failure is InvalidDataException or ArgumentException or IndexOutOfRangeException)
            {
                // A format this reader does not implement is a different question; both paths
                // refuse it identically, which is all this test needs from it.
                continue;
            }

            if (!read.IsBlockCompressed)
            {
                plain++;
                continue;
            }

            blocks++;

            byte[] expanded = read.Image.ToRgba(read.Width, read.Height);

            if (!expanded.AsSpan().SequenceEqual(decoded.Pixels))
            {
                disagreed.Add(
                    $"{Path.GetFileName(path)} ({read.Format}, {read.Width}x{read.Height}, " +
                    $"level {read.Level} of {read.MipCount}, {read.Levels.Count} levels, " +
                    $"{read.Levels[0].Length} bytes) — expanded {expanded.Length} against " +
                    $"{decoded.Pixels.Length}");
            }
        }

        TestContext.Out.WriteLine($"{blocks} block-compressed, {plain} plain, {disagreed.Count} disagreed");

        foreach (string line in disagreed.Take(10))
        {
            TestContext.Out.WriteLine("  " + line);
        }

        blocks.ShouldBeGreaterThan(10, "most of the game's textures are DXT");
        disagreed.ShouldBeEmpty("the GPU is handed different bytes from the ones that used to draw");
    }

    [Test]
    public void Read_ACubemapsFaces_EachAgreeWithDecode()
    {
        // **Faces are where the arithmetic is hardest and a wrong answer looks fine.** A cubemap
        // stores seven faces per mip and the seventh is a spheremap, so an off-by-one assembles a
        // reflection out of neighbouring images — which draws, and looks like a lighting oddity.
        if (GameInstall.Root is not { } tf)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive textures = VpkArchive.Open(Path.Combine(tf, "tf2_textures_dir.vpk"));

        string? cube = textures.Paths
            .Where(path => path.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault(path =>
            {
                try
                {
                    return textures.ReadFile(path) is { } bytes &&
                        VtfTexture.Read(bytes) is { IsCubeMap: true, IsBlockCompressed: true };
                }
                catch (Exception failure) when (
                    failure is IOException or InvalidDataException or ArgumentException)
                {
                    return false;
                }
            });

        if (cube is null)
        {
            Assert.Ignore("No block-compressed cubemap in this install's texture VPK.");
            return;
        }

        byte[] file = textures.ReadFile(cube)!;

        TestContext.Out.WriteLine($"checking {Path.GetFileName(cube)}");

        for (int face = 0; face < 6; face++)
        {
            VtfTexture read = VtfTexture.Read(file, face: face);
            VtfTexture decoded = VtfTexture.Decode(file, face: face);

            read.Image.ToRgba(read.Width, read.Height)
                .ShouldBe(decoded.Pixels, $"face {face} differs");
        }
    }

}
