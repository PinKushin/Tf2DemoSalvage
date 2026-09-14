namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>vphysics' generator — <c>FUN_18007d5c0</c> over the seed at <c>180124fe8</c> (B369).</summary>
/// <remarks>
/// <code>
/// seed = seed·75   (IMUL, the low 32 bits);   return (float)(seed &amp; 0xffff)·DAT_1800fd2a0   (1/65536f)
/// </code>
/// **The binary keeps one seed for the whole process, starting at 1**, and every draw anywhere advances it; the unit PSI draws
/// its rest-test countdown from it (`FUN_180075c80`). This is an instance, so two simulations here do not share a phase — a
/// replay can match the rule but not the binary's phase, which depends on every draw since the game started.
/// </remarks>
public sealed class IvpRandom
{
    /// <summary><c>DAT_1800fd2a0</c>: <c>1/65536f</c>.</summary>
    private const float Scale = 1f / 65536f;

    /// <summary>The seed, <c>180124fe8</c>; 1 before the first draw.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Advances the seed and draws from its low word.</summary>
    /// <returns>A float in <c>[0, 1)</c>.</returns>
    public float Next()
    {
        Seed = unchecked(Seed * 75);

        return IvpMath.Mulss(Seed & 0xffff, Scale);
    }
}
