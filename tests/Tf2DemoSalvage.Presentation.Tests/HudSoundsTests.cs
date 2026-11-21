namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The seed a HUD sound's stream takes: a seek, and the next run, hear the same draws (B784).</summary>
public sealed class HudSoundsTests
{
    /// <remarks>
    /// The value is FNV-1a over the name as <see cref="SequenceHash"/> takes it (6957591437312382778, low 32 bits
    /// -1588734150), plus `tick · 131` and the first seed (2^20), computed outside this code. Red first against the old
    /// formula, `string.GetHashCode`, which .NET randomizes per process. That <see cref="HudSounds.Emit"/> draws from this
    /// seed is <c>ScriptWaveDeckConformanceTests.Emit_AHudSound_DrawsFromItsStableSeed</c>.
    /// </remarks>
    [Test]
    public void SeedFor_AScriptAtATick_IsTheFnvOfItsName() => HudSounds.SeedFor(100, "Hud.Hint").ShouldBe(-1587672474);
}
