using System;
using System.Reflection;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A directional local light — Valve's `color.w` type code in `VertexAttenInternal` and `CosineTermInternal`
/// (common_vs_fxc.h:806, :817) — as the model constants carry it and the shader compiles it.
/// </summary>
public sealed class DirectionalLocalLightConformanceTests
{
    private const int Slots = 4;

    [Test]
    public void WriteLocalLights_ADirectionalLamp_CarriesItsTypeDirectionAndFarPosition()
    {
        float[] contents = new float[WorldRenderer.LocalLightBase + (Slots * 4 * 5)];
        LocalLight lamp = new(0f, 0f, 0f, 0.5f, 0.25f, 1f, 1f, 0f, 0f, 0f, (0f, 0.6f, 0.8f), Directional: true);

        WorldRenderer.WriteLocalLights(contents, [lamp]);

        int position = WorldRenderer.LocalLightBase;
        int direction = WorldRenderer.LocalLightBase + (Slots * 12);

        // `m_Position = m_Direction * 2.0e6` (lightdesc.cpp:41-42), the type in w.
        contents.AsSpan(position, 4).ToArray().ShouldBe([0f, 0.6f * 2.0e6f, 0.8f * 2.0e6f, 1f]);
        contents.AsSpan(direction, 4).ToArray().ShouldBe([0f, 0.6f, 0.8f, 0f], "not a spot: dir.w is 0");
        contents[WorldRenderer.LocalLightBase + (Slots * 8) + 3].ShouldBe(1f, "one lamp");
    }

    [Test]
    public void WriteLocalLights_APointLamp_LeavesTheTypeZero()
    {
        float[] contents = new float[WorldRenderer.LocalLightBase + (Slots * 4 * 5)];

        WorldRenderer.WriteLocalLights(contents, [new LocalLight(1f, 2f, 3f, 1f, 1f, 1f, 1f, 0f, 0f, 0f)]);

        contents[WorldRenderer.LocalLightBase + 3].ShouldBe(0f);
    }

    [TestCase("VsMain", "vs_5_0")]
    [TestCase("PsMain", "ps_5_0")]
    public void Compile_TheModelShader_Succeeds(string entry, string profile)
    {
        MethodInfo compile = typeof(WorldRenderer).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Static)!;
        using D3DCompiler compiler = D3DCompiler.GetApi();

        ComPtr<ID3D10Blob> blob = (ComPtr<ID3D10Blob>)compile.Invoke(null, [compiler, entry, profile])!;

        blob.GetBufferSize().ShouldBeGreaterThan(0u, "bytecode, where a compile error throws");
        blob.Dispose();
    }
}
