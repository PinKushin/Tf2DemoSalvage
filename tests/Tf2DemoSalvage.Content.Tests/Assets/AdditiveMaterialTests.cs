using System;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Materials the engine ADDS rather than paints.
/// </summary>
/// <remarks>
/// **The distinction decides whether a light cone is a glow or a black shape.** Source returns
/// BT_ADD for <c>$additive</c>, so the dark parts of the texture contribute nothing; a renderer
/// that draws it opaque puts a solid black cone under every lamp.
/// </remarks>
public sealed class AdditiveMaterialTests
{
    [Test]
    public void Parse_AnAdditiveMaterial_SaysSo()
    {
        VmtMaterial material = VmtMaterial.Parse(Encoding.UTF8.GetBytes("""
            "UnlitGeneric"
            {
                "$basetexture" "effects/light_cone"
                "$additive" "1"
            }
            """));

        material.IsAdditive.ShouldBeTrue();
    }

    [Test]
    public void Parse_AnOrdinaryMaterial_IsNotAdditive()
    {
        // The control. Without it, "detects additive" and "says everything is additive" are the
        // same result - and the second would make the whole map vanish.
        VmtMaterial material = VmtMaterial.Parse(Encoding.UTF8.GetBytes("""
            "LightmappedGeneric"
            {
                "$basetexture" "concrete/concretewall012"
            }
            """));

        material.IsAdditive.ShouldBeFalse();
    }

    [Test]
    public void Parse_AdditiveSetToZero_IsNotAdditive()
    {
        // Materials do write the key off, and treating any presence as true would drop them.
        VmtMaterial material = VmtMaterial.Parse(Encoding.UTF8.GetBytes("""
            "UnlitGeneric"
            {
                "$basetexture" "effects/thing"
                "$additive" "0"
            }
            """));

        material.IsAdditive.ShouldBeFalse();
    }
}
