using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Whether a model material needs the frame-buffer copy apart from its cloak (B711): <c>NeedsPowerOfTwoFrameBufferTexture</c>.
/// </summary>
/// <remarks>
/// - VertexLitGeneric answers true for <c>$sheenpassenabled</c>, with no per-frame test (`vertexlitgeneric_dx9.cpp:286-301`).
/// - Otherwise every override answers <c>MATERIAL_VAR2_NEEDS_POWER_OF_TWO_FRAME_BUFFER_TEXTURE</c>, which these shaders set in
///   their init: Refract (`refract_dx9_helper.cpp:59`), Cloak (`cloak_dx9_helper.cpp:40`), Predator (`predator.cpp:39`),
///   Aftershock (`aftershock_helper.cpp:21`), Portal_Refract (`portal_refract_helper.cpp:19`), and Core and Portal without a
///   <c>$basetexture</c> (`core_dx9.cpp:61-64`, `portal.cpp:34-41`).
/// - The cloak pass's own answer is per frame and is the instance's, not the material's.
/// </remarks>
public sealed class FrameBufferUseConformanceTests
{
    [TestCase("VertexLitGeneric", "\"$sheenpassenabled\" \"1\"", true)]
    [TestCase("VertexLitGeneric", "\"$sheenpassenabled\" \"0\"", false)]
    [TestCase("VertexLitGeneric", "\"$cloakpassenabled\" \"1\"", false)]
    [TestCase("VertexLitGeneric", "\"$basetexture\" \"a\"", false)]
    [TestCase("Refract", "\"$normalmap\" \"a\"", true)]
    [TestCase("Cloak", "", true)]
    [TestCase("Predator", "", true)]
    [TestCase("Aftershock", "", true)]
    [TestCase("Portal_Refract", "", true)]
    [TestCase("Core", "", true)]
    [TestCase("Core", "\"$basetexture\" \"a\"", false)]
    [TestCase("Portal", "", true)]
    [TestCase("Portal", "\"$basetexture\" \"a\"", false)]
    [TestCase("UnlitGeneric", "", false)]
    public void Needs_AShaderAndItsParameters_IsTheShadersAnswer(string shader, string body, bool expected) =>
        FrameBufferUse.Needs(VmtMaterial.Parse(Encoding.UTF8.GetBytes($"\"{shader}\" {{ {body} }}"))).ShouldBe(expected);
}
