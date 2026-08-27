using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Where a player model's animations actually live.
/// </summary>
/// <remarks>
/// A probe, not a test. scout.mdl declares 306 sequences and two local animations of one frame
/// each — the reference pose and nothing else. The rest is in models a player model INCLUDES, via
/// <c>numincludemodels</c>, and this prints which ones so the next step is aimed rather than
/// guessed.
///
/// The offsets are counted from <c>studio.h</c>'s field order and checked against two anchors this
/// project already verified against real files: <c>numbodyparts</c> at 232 and
/// <c>bodypartindex</c> at 236.
/// </remarks>
public sealed class IncludedAnimationProbe
{
    private static string Game => GameInstall.Require();

    /// <summary><c>studiohdr_t.numincludemodels</c> and <c>includemodelindex</c>.</summary>
    private const int IncludeCountOffset = 336;
    private const int IncludeIndexOffset = 340;

    /// <summary>Bytes per <c>mstudiomodelgroup_t</c>: a label index and a name index.</summary>
    private const int GroupStride = 8;

    [Test]
    public void IncludedAnimations_APlayerModel_AreReported()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        VpkArchive[] archives =
        [
            .. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
                .Select(name => Path.Combine(Game, name))
                .Where(File.Exists)
                .Select(VpkArchive.Open),
        ];

        foreach (string path in new[]
        {
            "models/player/scout.mdl",
            "models/player/soldier.mdl",
            "models/items/medkit_small.mdl",
        })
        {
            if (archives.Select(a => a.ReadFile(path)).FirstOrDefault(f => f is not null)
                is not { } file)
            {
                continue;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(IncludeCountOffset));
            int at = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(IncludeIndexOffset));

            TestContext.Out.WriteLine(
                $"INC {Path.GetFileName(path)}: {count} included models, " +
                $"{StudioAnimation.Count(file)} local animations, " +
                $"{StudioSequences.Read(file).Count} sequences");

            for (int index = 0; index < count && index < 32; index++)
            {
                int entry = at + (index * GroupStride);

                if (entry < 0 || entry + GroupStride > file.Length)
                {
                    break;
                }

                int nameAt = entry + BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(entry + 4));

                if (nameAt < 0 || nameAt >= file.Length)
                {
                    continue;
                }

                int end = Array.IndexOf(file, (byte)0, nameAt);
                string name = Encoding.UTF8.GetString(
                    file, nameAt, (end < 0 ? file.Length : end) - nameAt);

                byte[]? included = archives
                    .Select(a => a.ReadFile(name.Replace('\\', '/')))
                    .FirstOrDefault(f => f is not null);

                TestContext.Out.WriteLine(
                    $"INC   {name} -> {(included is null ? "NOT FOUND" : $"{included.Length:N0} bytes, " +
                        $"{StudioAnimation.Count(included)} animations, " +
                        $"{StudioSequences.Read(included).Count} sequences")}");
            }
        }

        Assert.Pass();
    }
}
