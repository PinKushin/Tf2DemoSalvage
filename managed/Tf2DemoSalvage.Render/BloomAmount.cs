namespace Tf2DemoSalvage.Render;

/// <summary><c>GetBloomAmount</c> (`viewpostprocess.cpp:1421-1468`), called once per drawn frame (B514).</summary>
/// <remarks>
/// **The amount walks toward its goal by <c>mat_bloomamount_rate</c> (0.05) of the gap per FRAME**, from a static that
/// starts at one and is never reset — Valve's, frame-rate dependent as written. The goal is the map's
/// <c>env_tonemap_controller</c> bloom scale when it set one (<c>GetCurrentBloomScale</c>, `:763-776`, a zero included),
/// else <c>mat_bloomscale</c> (1). Off — and the static left where it was — when the map has no HDR lighting
/// (<c>MapHasHDRLighting</c>) or <c>mat_hdr_level</c> is 0; scaled by 0.3 under no HDR (level 1, B537);
/// <c>mat_bloom_scalefactor_scalar</c> is 1.
/// </remarks>
public sealed class BloomAmount
{
    /// <summary><c>mat_bloomamount_rate</c>.</summary>
    public const float Rate = 0.05f;

    /// <summary><c>mat_bloomscale</c>'s default.</summary>
    public const float DefaultScale = 1f;

    /// <summary><c>currentBloomAmount</c>.</summary>
    public float Current { get; private set; } = 1f;

    private float _goal = 1f;

    /// <summary>Whether the amount is within 0.002 of its goal — what a comparison capture waits for (D192).</summary>
    public bool Settled => System.MathF.Abs(Current - _goal) < 0.002f;

    /// <summary><c>mat_non_hdr_bloom_scalefactor</c> (<c>viewpostprocess.cpp:66</c>), applied under <c>HDR_TYPE_NONE</c>.</summary>
    public const float NonHdrScaleFactor = 0.3f;

    /// <summary>One frame's amount.</summary>
    /// <param name="enabled">Bloom is on: <c>mat_hdr_level</c> at least 1 on a map with HDR lighting (B537).</param>
    /// <param name="type">The HDR type in use; under none — level 1 — the amount is scaled by <see cref="NonHdrScaleFactor"/>.</param>
    /// <param name="useCustom"><c>g_bUseCustomBloomScale</c>.</param>
    /// <param name="custom"><c>g_flCustomBloomScale</c>.</param>
    /// <returns>The amount the vertical blur scales by, zero when off.</returns>
    public float Next(bool enabled, Content.Bsp.HdrType type, bool useCustom, float custom)
    {
        if (!enabled)
        {
            return 0f;
        }

        _goal = useCustom ? custom : DefaultScale;
        Current = (_goal * Rate) + ((1f - Rate) * Current);

        // `:1460-1463`, after the walk: the static keeps the unscaled amount.
        return type == Content.Bsp.HdrType.None ? Current * NonHdrScaleFactor : Current;
    }
}
