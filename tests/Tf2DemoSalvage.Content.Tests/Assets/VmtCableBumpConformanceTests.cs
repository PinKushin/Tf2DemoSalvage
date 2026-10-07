using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The <c>Cable</c> shader's normal map: <c>SHADER_PARAM( BUMPMAP, …, "cable/cablenormalmap", … )</c> (`cable_dx9.cpp:26`),
/// bound for every cable whatever its flags (`:111`) (B478).
/// </summary>
public sealed class VmtCableBumpConformanceTests
{
    /// <remarks>
    /// **The shipped `cable/pure_white` names its own** — <c>cable\cableflatnormalmap</c> — slashes and all, which the
    /// texture lookup normalises; **a cable naming none takes the parameter's default**; another shader has no cable bump.
    /// </remarks>
    [TestCase("\"Cable\" { \"$basetexture\" \"cable\\pure_white\" \"$bumpmap\" \"cable\\cableflatnormalmap\" }", "cable\\cableflatnormalmap")]
    [TestCase("\"Cable\" { \"$basetexture\" \"cable\\black\" }", "cable/cablenormalmap")]
    [TestCase("\"UnlitGeneric\" { \"$basetexture\" \"cable\\black\" \"$bumpmap\" \"cable\\cablenormalmap\" }", null)]
    public void CableBumpMap_AMaterial_IsItsOwnOrTheShadersDefault(string text, string? expected)
    {
        VmtMaterial.Parse(Encoding.UTF8.GetBytes(text)).CableBumpMap.ShouldBe(expected);
    }
}
