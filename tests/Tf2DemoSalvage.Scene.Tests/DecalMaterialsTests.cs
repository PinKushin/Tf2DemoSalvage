using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Decal names to sized, mapped materials — Subrects and plain decals (B415).</summary>
public sealed class DecalMaterialsTests
{
    /// <remarks>
    /// `decals/concrete/shot1_subrect` as TF2 ships it: sized by <c>$Size</c>, drawn with the atlas, its window at
    /// <c>$Pos</c> over the atlas's 1024 × 512.
    /// </remarks>
    [Test]
    public void Resolve_ASubrect_IsSizedByItsWindowAndDrawsTheAtlas()
    {
        DecalMaterial hole = Resolver().Resolve("decals/concrete/shot1_subrect").ShouldNotBeNull();

        hole.Width.ShouldBe(64);
        hole.Height.ShouldBe(64);
        hole.DecalScale.ShouldBe(0.16f);
        hole.Paged.ShouldBeTrue();
        hole.PageOffset.ShouldBe((512f / 1024f, 256f / 512f));
        hole.PageScale.ShouldBe((64f / 1024f, 64f / 512f));
        hole.Draws.ShouldBe("decals/decals_mod2x");
    }

    [Test]
    public void Resolve_APlainDecal_IsSizedByItsBaseTexture()
    {
        DecalMaterial scorch = Resolver().Resolve("decals/plain").ShouldNotBeNull();

        scorch.Width.ShouldBe(128);
        scorch.Height.ShouldBe(256);
        scorch.DecalScale.ShouldBe(1f, "no $decalscale is 1");
        scorch.Paged.ShouldBeFalse();
    }

    [Test]
    public void Resolve_ASubrectNamingAModelMaterial_CarriesIt()
    {
        // `decals/flesh/blood1_subrect` as shipped: `$modelmaterial` is what `CStudioRenderContext::AddDecal` draws with.
        DecalMaterial blood = Resolver().Resolve("decals/flesh/blood1_subrect").ShouldNotBeNull();

        blood.ModelMaterial.ShouldBe("decals/flesh/blood1");
        blood.Fades.ShouldBeFalse();
        Resolver().Resolve("decals/concrete/shot1_subrect")!.Value.ModelMaterial.ShouldBeNull();
    }

    [Test]
    public void Resolve_AMissingMaterial_IsNull()
    {
        Resolver().Resolve("decals/nothing").ShouldBeNull();
    }

    private static DecalMaterials Resolver()
    {
        Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase)
        {
            ["materials/decals/concrete/shot1_subrect.vmt"] =
                "\"Subrect\" { \"$Material\" \"decals/decals_mod2x\" \"$Pos\" \"512 256\" \"$Size\" \"64 64\" \"$decalscale\" 0.16 }",
            ["materials/decals/flesh/blood1_subrect.vmt"] =
                "\"Subrect\" { \"$Material\" \"decals/decals_mod2x\" \"$Pos\" \"384 64\" \"$Size\" \"64 64\" \"$decalscale\" 0.1 \"$modelmaterial\" \"decals/flesh/blood1\" }",
            ["materials/decals/decals_mod2x.vmt"] = "\"DecalModulate\" { \"$basetexture\" \"decals/decals_atlas\" }",
            ["materials/decals/plain.vmt"] = "\"LightmappedGeneric\" { \"$basetexture\" \"decals/plain\" \"$decal\" 1 }",
        };

        Dictionary<string, (int, int)> sizes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["decals/decals_atlas"] = (1024, 512),
            ["decals/plain"] = (128, 256),
        };

        return new DecalMaterials(
            path => files.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null,
            texture => sizes.TryGetValue(texture, out (int, int) size) ? size : null);
    }
}
