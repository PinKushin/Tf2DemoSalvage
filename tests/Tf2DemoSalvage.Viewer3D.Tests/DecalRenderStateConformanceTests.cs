using System;
using System.IO;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The render state Valve gives a surface marking, and the buffer its constants are calibrated for.
/// </summary>
/// <remarks>
/// **Written late, which is the point of writing it down.** Three changes went in tonight without a
/// conformance test between them — the depth buffer format (D48), depth writes on the overlay pass,
/// and the decal bias constants — and each was then argued about from screenshots. A citation in a
/// commit message is not a test; it does not redden when someone changes the value back.
/// </remarks>
public sealed class DecalRenderStateConformanceTests
{
    [Test]
    public void DecalShaders_EveryOne_DisablesDepthWrites()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        // **Every decal shader, not one.** A single file could be a special case; the whole family
        // agreeing is the convention. These are the shaders a sprayed decal uses — an overlay's
        // material is ordinarily LightmappedGeneric, so applying their behaviour to overlays is an
        // interpolation (D44), and the reason is recorded on _decalDepth rather than implied here.
        foreach (string shader in new[]
        {
            "src/materialsystem/stdshaders/DecalModulate_dx9.cpp",
            "src/materialsystem/stdshaders/decalmodulate.cpp",
            "src/materialsystem/stdshaders/decal.cpp",
        })
        {
            string? text = SourceSdk.Text(shader);

            if (text is null)
            {
                continue;
            }

            text.ShouldContain(
                "EnableDepthWrites( false )",
                Case.Sensitive,
                $"{shader}: a decal that writes depth makes everything drawn afterwards test " +
                "against a surface that is not there");

            text.ShouldContain(
                "EnablePolyOffset( SHADER_POLYOFFSET_DECAL )",
                Case.Sensitive,
                $"{shader}: the offset and the depth-write behaviour are one arrangement");
        }
    }

    [Test]
    public void DecalBias_ValvesConstants_AreMinus262144AndMinusHalf()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/public/materialsystem/materialsystem_config.h")
            ?? throw new InvalidOperationException("materialsystem_config.h is missing");

        text.ShouldContain("m_DepthBias_Decal = -262144;");
        text.ShouldContain("m_SlopeScaleDepthBias_Decal = -0.5f;");

        // The control: the same struct sets a normal bias of zero and a shadow bias of the opposite
        // sign, so a loose match could not have produced the pair above.
        text.ShouldContain("m_DepthBias_Normal = 0.0f;");
        text.ShouldContain("m_DepthBias_ShadowMap = 262144;");
    }

    [Test]
    public void DepthBuffers_TheWindowAndTheOffscreenTarget_UseTheSameFormat()
    {
        // **A depth constant means nothing without its format (D48), so the two buffers must agree.**
        // D3D11 scales a rasteriser's DepthBias by a factor the format decides — a fixed 1/2^24 for
        // UNORM, data-dependent for FLOAT. If the window and the offscreen target differ, a captured
        // picture places decals differently from the viewer it is supposed to photograph, and the
        // capture is what tests and screenshots are read from.
        //
        // Asserted on the source rather than on a device, because creating two swap chains to
        // compare them costs a GPU and this is a statement about the code.
        string root = RepositoryRoot();

        string device = File.ReadAllText(
            Path.Combine(root, "managed", "Tf2DemoSalvage.Viewer3D", "Device3D.cs"));

        string offscreen = File.ReadAllText(
            Path.Combine(root, "managed", "Tf2DemoSalvage.Viewer3D", "OffscreenTarget.cs"));

        device.ShouldContain("Format.FormatD24UnormS8Uint");
        offscreen.ShouldContain("Format.FormatD24UnormS8Uint");

        // And neither may quietly go back to the float buffer, which is what made every depth
        // constant in this renderer mean something other than what it said.
        device.ShouldNotContain("FormatD32Float");
        offscreen.ShouldNotContain("FormatD32Float");
    }

    /// <summary>Walks up from the test binary to the repository root.</summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);

        while (at is not null && !File.Exists(Path.Combine(at.FullName, "Tf2DemoSalvage.slnx")))
        {
            at = at.Parent;
        }

        return at?.FullName ?? throw new DirectoryNotFoundException("repository root not found");
    }
}
