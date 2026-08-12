using System.Text;

using Tf2DemoSalvage.Core.Assets;

namespace Tf2DemoSalvage.Core.Tests.Assets;

/// <summary>
/// Reading a material file.
/// </summary>
/// <remarks>
/// The fixtures here are real VMT shapes, including the one taken verbatim from
/// <c>concrete/concretefloor007b.vmt</c> in TF2's own archives — tabs, mixed indentation and all.
/// A parser tested only on tidy input meets none of what a shipped material actually looks like.
/// </remarks>
public sealed class VmtMaterialTests
{
    [Test]
    public void Parse_AShippedMaterial_FindsItsShaderAndBaseTexture()
    {
        // Verbatim from the game, whitespace included.
        VmtMaterial material = Parse(
            "\"LightMappedGeneric\"\n" +
            "{\n" +
            "\t\"$basetexture\"\t\"concrete/concretefloor007b\"\n" +
            "\t\"$bumpmap\" \"Concrete/concretefloor007b_height-ssbump\"\n" +
            "        \"$ssbump\" \"1\"\n" +
            "\t\"%keywords\" \"tf\"\n" +
            "}\n");

        material.Shader.ShouldBe("LightMappedGeneric");
        material.BaseTexture.ShouldBe("concrete/concretefloor007b");
    }

    [Test]
    public void Parse_KeysAreCaseInsensitive()
    {
        // Map compilers and hand-written materials disagree about case constantly.
        Parse("\"x\"{\"$BaseTexture\" \"a/b\"}").BaseTexture.ShouldBe("a/b");
    }

    [Test]
    public void Parse_SkipsComments()
    {
        VmtMaterial material = Parse(
            "// a leading comment\n" +
            "\"LightMappedGeneric\"\n{\n" +
            "  // \"$basetexture\" \"wrong/one\"\n" +
            "  \"$basetexture\" \"right/one\"\n}\n");

        material.BaseTexture.ShouldBe("right/one");
    }

    [Test]
    public void Parse_IgnoresKeysInNestedBlocks()
    {
        // A Proxies block carries its own keys, and a $basetexture inside one is not the surface's.
        // Without this the wrong texture is drawn, which looks like a decode bug rather than a
        // parse bug.
        VmtMaterial material = Parse(
            "\"LightMappedGeneric\"\n{\n" +
            "  \"$basetexture\" \"surface/real\"\n" +
            "  \"Proxies\"\n  {\n" +
            "    \"TextureScroll\"\n    {\n" +
            "      \"$basetexture\" \"proxy/wrong\"\n" +
            "    }\n  }\n}\n");

        material.BaseTexture.ShouldBe("surface/real");
    }

    [Test]
    public void Parse_AcceptsUnquotedTokens()
    {
        // Real materials mix quoted and bare tokens freely.
        Parse("LightMappedGeneric\n{\n$basetexture metal/wall\n}\n").BaseTexture.ShouldBe("metal/wall");
    }

    [Test]
    public void Parse_ATruncatedFile_KeepsWhatItRead()
    {
        // A pakfile cut short. Everything before the cut is still a usable material.
        VmtMaterial material = Parse("\"x\"\n{\n\"$basetexture\" \"a/b\"\n\"$bumpmap\" \"unterm");

        material.BaseTexture.ShouldBe("a/b");
    }

    [Test]
    public void Transparency_IsFlaggedByEitherKey()
    {
        Parse("\"x\"{\"$translucent\" \"1\"}").IsTransparent.ShouldBeTrue();
        Parse("\"x\"{\"$alphatest\" \"1\"}").IsTransparent.ShouldBeTrue();

        // The control: a material with neither must be opaque, or every surface blends.
        Parse("\"x\"{\"$basetexture\" \"a/b\"}").IsTransparent.ShouldBeFalse();
    }

    [Test]
    public void Patch_TakesTheIncludedTextureAndTheOverriddenKeys()
    {
        // Patch materials are everywhere in TF2. A reader that does not resolve them sees no
        // $basetexture and draws nothing at all.
        VmtMaterial patch = Parse(
            "\"Patch\"\n{\n" +
            "  \"include\" \"materials/models/base.vmt\"\n" +
            "  \"$color\" \"[1 0 0]\"\n}\n");

        VmtMaterial included = Parse(
            "\"VertexLitGeneric\"\n{\n" +
            "  \"$basetexture\" \"models/base\"\n" +
            "  \"$color\" \"[1 1 1]\"\n}\n");

        patch.IsPatch.ShouldBeTrue();
        patch.Include.ShouldBe("materials/models/base.vmt");

        VmtMaterial merged = VmtMaterial.ApplyPatch(patch, included);

        merged.Shader.ShouldBe("VertexLitGeneric", "the shader comes from the included material");
        merged.BaseTexture.ShouldBe("models/base");
        merged.Value("$color").ShouldBe("[1 0 0]", "the patch's value must win");
    }

    [Test]
    public void Parse_AMaterialWithNoBaseTexture_ReportsNone()
    {
        Parse("\"UnlitGeneric\"\n{\n\"%compilenodraw\" \"1\"\n}\n").BaseTexture.ShouldBeNull();
    }

    private static VmtMaterial Parse(string text) => VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));
}
