using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A material that did not load is the error material (B713).</summary>
public sealed class ErrorMaterialTableConformanceTests
{
    /// <remarks>
    /// **`FindMaterial` answers the error material, `UnlitGeneric`** (`materialsystem.dll` <c>0x180015750</c>,
    /// <c>0x180012b00</c>), so a material whose VMT did not resolve is recorded with that shader and draws its chequer
    /// unlit (<c>ModelMaterialShading.TakesDiffuseLighting</c>). The control: a material that loaded keeps its own shader
    /// even with no base texture, as `FindTexture`'s error texture leaves the shader alone.
    /// </remarks>
    [Test]
    public void Add_AMaterialThatDidNotResolve_IsTheUnlitErrorMaterial()
    {
        MaterialTable table = new();
        BspMaterial entry = new("missing/surface", (0.5f, 0.5f, 0.5f), 128, 128);

        int missing = table.Add(entry, default);
        int textureless = table.Add(entry, new ResolvedMaterial(null, null, null, null, Shader: "LightmappedGeneric"));

        table.Shaders[missing].ShouldBe("UnlitGeneric");
        ModelMaterialShading.TakesDiffuseLighting(table.Shaders[missing]).ShouldBeFalse();
        table.Shaders[textureless].ShouldBe("LightmappedGeneric");
    }
}
