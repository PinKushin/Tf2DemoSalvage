using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

public sealed class StaleModelProbe
{
    [Test]
    public void PackedModels_AgainstTheirLighting_AreReported()
    {
        string tf = GameInstall.Require();
        string mapPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!File.Exists(mapPath))
        {
            Assert.Ignore("map or game missing");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(mapPath);
        PakFile pak = PakFile.ReadFrom(map);
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(map);
        VpkArchive archive = VpkArchive.Open(Path.Combine(tf, "tf2_misc_dir.vpk"));

        for (int index = 0; index < props.Count; index++)
        {
            foreach (string vhv in StudioVertexLighting.PathsFor(index))
            {
                if (pak.ReadFile(vhv) is not { } file || file.Length < 8)
                {
                    continue;
                }

                int stamped = BitConverter.ToInt32(file, 4);
                byte[]? packed = pak.ReadFile(props[index].Model);
                byte[]? shipped = archive.ReadFile(props[index].Model);

                int? packedSum = packed is null ? null : StudioModel.Read(packed).Checksum;
                int? shippedSum = shipped is null ? null : StudioModel.Read(shipped).Checksum;

                if (stamped != (packedSum ?? shippedSum))
                {
                    TestContext.Out.WriteLine(
                        $"MISMATCH prop {index} {props[index].Model}: vhv {stamped}, " +
                        $"packed {(packedSum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none")}, shipped {(shippedSum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none")}");
                }

                break;
            }
        }

        Assert.Pass();
    }
}
