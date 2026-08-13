using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Counts which kind of bump map a real map's materials actually use.
/// </summary>
/// <remarks>
/// **A probe rather than a test, because it answers a design question rather than guarding a
/// behaviour.** Source has two completely separate bumped-lighting combines - the ordinary one
/// takes squared dot products against the basis and normalises by their sum, the self-shadowing
/// one uses the normal's components directly with no dots and no normalisation - and which of them
/// matters here decides which gets written first.
///
/// The finding claimed ssbump was probably the common case, on the evidence of exactly one
/// material. That was flagged as interpolated. This counts it.
/// </remarks>
public sealed class BumpMapPrevalenceProbe
{
    [Test]
    public void HowManyMaterialsUseWhichKindOfBumpMap()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
        string map = Path.Combine(tf, "maps", "cp_process_final.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore("the game or the map is not installed");
            return;
        }

        ReadOnlyMemory<byte> bytes = File.ReadAllBytes(map);
        PakFile pak = PakFile.ReadFrom(bytes);

        List<VpkArchive> archives = [.. new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" }
            .Select(name => Path.Combine(tf, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)];

        byte[]? Find(string path)
        {
            byte[]? found = pak.ReadFile(path);

            foreach (VpkArchive archive in archives)
            {
                found ??= archive.ReadFile(path);
            }

            return found;
        }

        int materials = 0;
        int withBump = 0;
        int declaringSsbump = 0;
        int flaggedSsbump = 0;
        int missingBumpTexture = 0;
        List<string> examples = [];

        foreach (BspMaterial material in BspMaterials.Read(bytes))
        {
            if (Find("materials/" + material.Name + ".vmt") is not { } vmt)
            {
                continue;
            }

            materials++;

            VmtMaterial parsed = VmtMaterial.Parse(vmt);

            if (parsed.IsPatch && parsed.Include is { } include && Find(include) is { } based)
            {
                parsed = VmtMaterial.ApplyPatch(parsed, VmtMaterial.Parse(based));
            }

            if (parsed.Value("$bumpmap") is not { } bump)
            {
                continue;
            }

            withBump++;

            if (parsed.Value("$ssbump") is "1")
            {
                declaringSsbump++;
            }

            string bare = bump.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)
                ? bump[..^4]
                : bump;

            if (Find("materials/" + bare + ".vtf") is not { } texture)
            {
                missingBumpTexture++;
                continue;
            }

            try
            {
                if (VtfTexture.Decode(texture, maximumSize: 4).IsSelfShadowBump)
                {
                    flaggedSsbump++;
                }
            }
            catch (InvalidDataException failure)
            {
                TestContext.Out.WriteLine($"BUMP undecodable {bare}: {failure.Message}");
                continue;
            }

            if (examples.Count < 8)
            {
                examples.Add($"{material.Name} -> {bare}");
            }
        }

        TestContext.Out.WriteLine($"BUMP {materials} materials resolved on this map");
        TestContext.Out.WriteLine($"BUMP {withBump} name a $bumpmap");
        TestContext.Out.WriteLine($"BUMP {declaringSsbump} declare $ssbump 1");
        TestContext.Out.WriteLine($"BUMP {flaggedSsbump} have the SSBUMP flag on the texture");
        TestContext.Out.WriteLine($"BUMP {missingBumpTexture} name a bump texture that is missing");

        foreach (string example in examples)
        {
            TestContext.Out.WriteLine("BUMP   " + example);
        }
    }
}
