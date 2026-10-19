namespace Tf2DemoSalvage.Render;

/// <summary>
/// `TextureCombine` and `TextureCombinePostLighting` (common_ps_fxc.h:698-766) as HLSL, shared by every pixel shader that
/// combines a `$detail`: the world's and the VGUI surface's.
/// </summary>
/// <remarks>The C# transcription the tests hold these against is <c>DetailCombine</c>.</remarks>
internal static class DetailCombineShader
{
    /// <summary>The two functions, `CombineDetail` and `CombineDetailAfterLighting`.</summary>
    internal const string Source = """

        // Transcribed from TextureCombine in Valve's common_ps_fxc.h. The modes are numbered by
        // the engine and the numbers appear in materials, so they stay as numbers here.
        float4 CombineDetail(float4 albedo, float4 detailColour, int mode, float blend)
        {
            if (mode == 7)
            {
                float3 selected = lerp(detailColour.r, detailColour.a, albedo.a);
                albedo.rgb *= lerp(float3(1, 1, 1), 2.0f * selected, blend);
            }
            if (mode == 0)
            {
                albedo.rgb *= lerp(float3(1, 1, 1), 2.0f * detailColour.rgb, blend);
            }
            if (mode == 1)
            {
                albedo.rgb += blend * detailColour.rgb;
            }
            if (mode == 2)
            {
                albedo.rgb = lerp(albedo.rgb, detailColour.rgb, blend * detailColour.a);
            }
            if (mode == 3)
            {
                albedo = lerp(albedo, detailColour, blend);
            }
            if (mode == 4)
            {
                albedo.rgb = lerp(albedo.rgb, detailColour.rgb, blend * (1.0f - albedo.a));
                albedo.a = detailColour.a;
            }
            if (mode == 8)
            {
                albedo = lerp(albedo, albedo * detailColour, blend);
            }
            if (mode == 9)
            {
                albedo.a = lerp(albedo.a, albedo.a * detailColour.a, blend);
            }
            if (mode == 11)
            {
                // No blend factor in this one, deliberately: Valve's line is a bare multiply.
                albedo.rgb *= dot(detailColour.rgb, 2.0f / 3.0f);
            }
            return albedo;
        }

        // Modes 5 and 6 are self-illumination and are added AFTER the lightmap, which is why they
        // are a second function rather than two more cases above.
        float3 CombineDetailAfterLighting(float3 lit, float4 detailColour, int mode, float blend)
        {
            if (mode == 5)
            {
                lit += blend * detailColour.rgb;
            }
            if (mode == 6)
            {
                float multiplier = (blend >= 0.5f) ? 1.0f / blend : 4.0f * blend;
                float offset = (blend >= 0.5f) ? 1.0f - multiplier : -0.5f * multiplier;
                lit += saturate(multiplier * detailColour.rgb + offset);
            }
            return lit;
        }

        """;
}
