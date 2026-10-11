namespace Tf2DemoSalvage.Content.Assets;

/// <summary>What a reflection is multiplied by before its tint. The values are the pixel shader's mode numbers.</summary>
public enum EnvMapMask
{
    /// <summary>Unmasked.</summary>
    None = 0,

    /// <summary><c>1 - baseColor.a</c> — <c>$basealphaenvmapmask</c> on the generic shaders.</summary>
    InvertedBaseAlpha = 1,

    /// <summary>The bump map's alpha — <c>$normalmapalphaenvmapmask</c>.</summary>
    BumpAlpha = 2,

    /// <summary><c>baseColor.a</c> — the skin shader's default (<c>skin_ps20b.fxc:234</c>).</summary>
    BaseAlpha = 3,

    /// <summary><c>1 - normalTexel.a</c> — the skin shader with <c>$invertphongmask</c>.</summary>
    InvertedBumpAlpha = 4,

    /// <summary>Zero: the reflection is declared and draws nothing.</summary>
    Nothing = 5,
}
