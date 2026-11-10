using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Tests.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What a material resolves to when a texture it names does not load, or it names none (B713, B714): through the
/// production <c>Resolve</c>, over a hand-built pakfile and no game.
/// </summary>
public sealed class MaterialFallbackConformanceTests
{
    private static MapTexture? Resolve(string vmt, byte[]? vtf = null)
    {
        Dictionary<string, byte[]> files = new() { ["materials/test/surface.vmt"] = Encoding.UTF8.GetBytes(vmt) };

        if (vtf is not null)
        {
            files["materials/test/colour.vtf"] = vtf;
        }

        return MapAssets.ResolveVguiMaterial(
            NullLogger.Instance, "test/surface", PakFile.Read(Zip(files)), GameArchives.Open(null), 0);
    }

    private static byte[] Vtf(uint flags)
    {
        byte[] file = VtfFixture.Build(VtfFormat.Rgba8888, 2, 2, 1, new byte[16]);

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), flags);

        return file;
    }

    private static byte[] Zip(Dictionary<string, byte[]> files)
    {
        using MemoryStream stream = new();

        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] bytes) in files)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    /// <remarks>
    /// **A base texture that does not load is the error texture, with the material's own flags** (`FindTexture`): it is
    /// tagged so the inventories still count it missing, and since the error texture has alpha, `$translucent` blends it.
    /// </remarks>
    [Test]
    public void Resolve_ABaseTextureThatDoesNotLoad_IsTheErrorTextureUnderTheMaterialsFlags()
    {
        MapTexture texture = Resolve("\"UnlitGeneric\"\n{\n\"$basetexture\" \"test/absent\"\n\"$translucent\" 1\n}")
            .ShouldNotBeNull();

        (texture.IsErrorTexture, texture.Width, texture.IsTranslucent).ShouldBe((true, ErrorTexture.Size, true));
        texture.Image.Top.ToArray().ShouldBe(ErrorTexture.Rgba());
    }

    /// <remarks>
    /// **`$translucent` blends only over a texture whose flags carry alpha** (`EvaluateBlendRequirements`): the same VMT
    /// over a VTF with no alpha flag is opaque, and over one with <c>EIGHTBITALPHA</c> blends.
    /// </remarks>
    [TestCase(0u, false)]
    [TestCase(0x2000u, true)]
    public void Resolve_TranslucentOverAVtf_BlendsByItsAlphaFlags(uint flags, bool translucent)
    {
        MapTexture texture = Resolve("\"UnlitGeneric\"\n{\n\"$basetexture\" \"test/colour\"\n\"$translucent\" 1\n}", Vtf(flags))
            .ShouldNotBeNull();

        (texture.IsErrorTexture, texture.IsTranslucent).ShouldBe((false, translucent));
    }

    /// <remarks>
    /// **A secondary texture that does not load is the error texture too** (B714): the material still details, by the
    /// chequer, where this used to drop its `$detail` as if never named. The control is a loaded detail.
    /// </remarks>
    [Test]
    public void Resolve_ADetailTextureThatDoesNotLoad_DetailsByTheErrorTexture()
    {
        static string Material(string detail) =>
            "\"UnlitGeneric\"\n{\n\"$basetexture\" \"test/colour\"\n\"$detail\" \"test/" + detail + "\"\n}";

        MapTexture missing = Resolve(Material("absent"), Vtf(0u)).ShouldNotBeNull();
        MapTexture loaded = Resolve(Material("colour"), Vtf(0u)).ShouldNotBeNull();

        missing.Vgui.ShouldNotBeNull().Detail.ShouldNotBeNull().Texture.IsErrorTexture.ShouldBeTrue();
        loaded.Vgui.ShouldNotBeNull().Detail.ShouldNotBeNull().Texture.IsErrorTexture.ShouldBeFalse();
    }

    /// <remarks>
    /// **A generic shader with no `$basetexture` binds a standard texture** (`lightmappedgeneric_dx9_helper.cpp:380-391`):
    /// white, or black when the material has an envmap. One texel, not the chequer.
    /// </remarks>
    [TestCase("", 255)]
    [TestCase("\"$envmap\" \"env_cubemap\"", 0)]
    public void Resolve_AGenericMaterialWithNoBaseTexture_IsWhiteOrBlackUnderAnEnvmap(string body, int level)
    {
        MapTexture texture = Resolve("\"LightmappedGeneric\"\n{\n" + body + "\n}").ShouldNotBeNull();

        (texture.Width, texture.IsErrorTexture).ShouldBe((1, false));
        texture.Image.Top.ToArray().ShouldBe([(byte)level, (byte)level, (byte)level, 255]);
    }
}
