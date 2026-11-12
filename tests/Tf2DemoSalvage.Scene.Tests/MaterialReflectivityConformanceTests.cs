using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What a static prop's ray cube multiplies a struck surface's light by (B683).</summary>
/// <remarks>
/// **The material's reflectivity, not the map's texdata.** `0x1801b8e30` (engine.dll, build 11191711), the 162-ray cube,
/// asks the struck surface's `IMaterial` (`GetReflectivity`, vtable `+0xf8`). `materialsystem.dll` `FUN_180006a00` fills it
/// from the `$baseTexture`'s `ITexture::GetReflectivity` (vtable `+0x120`), the VTF header's `reflectivity` at offset 32.
/// The texdata copy is what vbsp read when the map was compiled, and it goes stale when the texture is rebuilt: measured
/// 0.386 against the header's 0.408 on `cp_process_f12`'s `2pipe_straight_64` material.
/// </remarks>
public sealed class MaterialReflectivityConformanceTests
{
    [Test]
    public void Of_AMaterialWithABaseTexture_IsItsVtfHeadersReflectivity()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["materials/metal/pipe.vmt"] = Encoding.UTF8.GetBytes("\"LightmappedGeneric\" { \"$basetexture\" \"metal/pipe_base\" }"),
            ["materials/metal/pipe_base.vtf"] = Header(0.408f, 0.407f, 0.363f),
        };

        MaterialReflectivity.Of("metal/pipe", name => files.GetValueOrDefault(name))
            .ShouldBe(new Vector3(0.408f, 0.407f, 0.363f));
    }

    [Test]
    public void Of_AMaterialThatDoesNotLoad_IsNull()
    {
        // The control: no file is no answer, so the caller's own fallback is visible rather than a zero.
        MaterialReflectivity.Of("metal/missing", _ => null).ShouldBeNull();
    }

    [Test]
    public void Reflectivities_AStaleTexdata_TakesTheHeaderAndKeepsTexdataOnlyForAnUnresolvedMaterial()
    {
        // What the cube multiplies each struck texdata by. The stale texdata of the first entry must lose to its header;
        // the second names no file, so its texdata is all there is.
        Dictionary<string, byte[]> files = new()
        {
            ["materials/metal/pipe.vmt"] = Encoding.UTF8.GetBytes("\"LightmappedGeneric\" { \"$basetexture\" \"metal/pipe_base\" }"),
            ["materials/metal/pipe_base.vtf"] = Header(0.408f, 0.407f, 0.363f),
        };

        Tf2DemoSalvage.Content.Bsp.BspMaterial[] texdata =
        [
            new("metal/pipe", (0.386f, 0.399f, 0.344f), 64, 64),
            new("metal/missing", (0.25f, 0.5f, 0.75f), 64, 64),
        ];

        Func<int, Vector3> resolved = LevelLighting.ReflectivityOf(texdata, name => files.GetValueOrDefault(name));
        resolved(0).ShouldBe(new Vector3(0.408f, 0.407f, 0.363f));
        resolved(1).ShouldBe(new Vector3(0.25f, 0.5f, 0.75f));
        resolved(2).ShouldBe(Vector3.Zero, "outside the table");

        // The control: with no lookup, both keep the map's copy.
        Func<int, Vector3> mapOnly = LevelLighting.ReflectivityOf(texdata, null);
        mapOnly(0).ShouldBe(new Vector3(0.386f, 0.399f, 0.344f));
        mapOnly(1).ShouldBe(new Vector3(0.25f, 0.5f, 0.75f));
    }

    [Test]
    public void Of_BadContent_IsNullAndNeverThrows()
    {
        // The texdata read this replaces could not fail a map load, so none of these may.
        Dictionary<string, byte[]> notVtf = new()
        {
            ["materials/metal/pipe.vmt"] = Encoding.UTF8.GetBytes("\"LightmappedGeneric\" { \"$basetexture\" \"metal/pipe_base\" }"),
            ["materials/metal/pipe_base.vtf"] = new byte[80],
        };

        MaterialReflectivity.Of("metal/pipe", name => notVtf.GetValueOrDefault(name)).ShouldBeNull("no VTF signature");
        MaterialReflectivity.Of("metal/pipe", name => name.EndsWith(".vmt", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes("{{{") : null)
            .ShouldBeNull("a VMT that will not parse");
        MaterialReflectivity.Of("metal/pipe", _ => throw new System.IO.InvalidDataException("bad pak entry"))
            .ShouldBeNull("a lookup that throws on a malformed entry");
    }

    /// <summary>A VTF header long enough to hold `reflectivity` (offset 32, three floats).</summary>
    private static byte[] Header(float red, float green, float blue)
    {
        byte[] header = new byte[80];
        Encoding.ASCII.GetBytes("VTF\0").CopyTo(header, 0);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(32), red);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(36), green);
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(40), blue);
        return header;
    }
}
