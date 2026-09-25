using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which file a cubemap loads from, as `materialsystem.dll` chooses it at TF2's default HDR level.</summary>
/// <remarks>
/// `CShaderSystem_LoadCubeMap` (`materialsystem.dll` 0x180050530, renamed in `tf2materialsystem.gpr`) appends `.hdr` to
/// every `$envmap` name when the HDR type is not `HDR_TYPE_NONE`; `CTexture_ReadTextureFromFile_HdrFallback`
/// (0x18003acf0) strips it and loads the LDR file when the `.hdr.vtf` is missing. So the HDR bake comes first and the
/// LDR one is the fallback — for a material's own cubemap and a map's baked `c&lt;x&gt;_&lt;y&gt;_&lt;z&gt;` alike.
/// </remarks>
public sealed class CubemapFileConformanceTests
{
    [Test]
    public void Candidates_ACubemapName_TriesTheHdrBakeFirst() =>
        CubemapFile.Candidates("maps/koth_harvest_final/c0_0_96")
            .ShouldBe(new List<string>
            {
                "materials/maps/koth_harvest_final/c0_0_96.hdr.vtf",
                "materials/maps/koth_harvest_final/c0_0_96.vtf",
            });

    [Test]
    public void Find_OnlyTheLdrBakeExists_FallsBackToIt()
    {
        byte[] ldr = [1];

        CubemapFile.Find("env/sky", path => path == "materials/env/sky.vtf" ? ldr : null).ShouldBeSameAs(ldr);
    }

    [Test]
    public void Find_BothExist_TakesTheHdrBake()
    {
        byte[] hdr = [2];

        CubemapFile.Find("c0_0_96", path => path.EndsWith(".hdr.vtf", System.StringComparison.Ordinal) ? hdr : [1])
            .ShouldBeSameAs(hdr);
    }
}
