using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>Whether a gesture sequence blends by delta or by ordinary slerp.</summary>
/// <remarks>
/// A probe for B112. <c>SlerpBones</c> takes a completely different branch under
/// <c>STUDIO_DELTA</c> (<c>0x0004</c>, "this sequence 'adds' to the base sequences, not slerp
/// blends") — additive per-bone, rather than interpolated toward the gesture's own pose. Which
/// branch <c>ACT_MP_JUMP_LAND</c> actually needs is a fact about the shipped model, not something
/// to assume before building the blend.
/// </remarks>
public sealed class GestureSequenceProbe
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
    private const int StudioDelta = 0x0004;

    [Test]
    public void IsTheJumpLandGestureAdditive()
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
            "models/player/scout_animations.mdl",
            "models/player/soldier_animations.mdl",
        })
        {
            if (archives.Select(a => a.ReadFile(path)).FirstOrDefault(f => f is not null) is not { } file)
            {
                TestContext.Out.WriteLine($"MISSING {path}");
                continue;
            }

            foreach (StudioSequence sequence in StudioSequences.Read(file)
                .Where(s => s.Activity.Contains("JUMP_LAND", StringComparison.Ordinal) ||
                            s.Activity.Contains("FLINCH", StringComparison.Ordinal)))
            {
                bool delta = (sequence.Flags & StudioDelta) != 0;

                TestContext.Out.WriteLine(
                    $"GESTURE {Path.GetFileName(path)} {sequence.Label} activity {sequence.Activity} " +
                    $"flags 0x{sequence.Flags:X} delta={delta}");
            }
        }
    }
}
