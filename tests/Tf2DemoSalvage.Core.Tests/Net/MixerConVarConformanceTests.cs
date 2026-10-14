using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The watcher's own sound ConVars — <c>volume</c>, <c>snd_musicvolume</c>, <c>snd_mute_losefocus</c>,
/// <c>voice_scale</c> — as TF2 registers them.
/// </summary>
/// <remarks>
/// **Engine-side, so absent from the SDK** (the same absence <see cref="SoundConVarConformanceTests"/> controls for).
/// Read from a running TF2 on 2026-10-14 with `help name`, which prints the default and the `min.`/`max.` only when
/// the registration sets them (`tier1/convar.cpp:1432-1436`):
///
/// <code>
/// "volume" = "1.0" min. 0.000000 max. 1.000000   archive   - Sound volume
/// "snd_musicvolume" = "…" ( def. "1.0" ) min. 0.000000 max. 1.000000   archive   - Music volume
/// "snd_mute_losefocus" = "1"   archive notconnected
/// "voice_scale" = "1"   archive
/// </code>
///
/// None is replicated or cheat-protected: all four are `archive`, the watcher's own, saved in `config.cfg`.
/// </remarks>
public sealed class MixerConVarConformanceTests
{
    [TestCase("volume", "1.0", 0f, 1f)]
    [TestCase("snd_musicvolume", "1.0", 0f, 1f)]
    public void Declaration_ForABoundedVolume_MatchesTheRegistration(string name, string expected, float min, float max)
    {
        EngineConVar declared = EngineConVars.ByName(name);

        declared.Default.ShouldBe(expected);
        declared.Min.ShouldBe(min);
        declared.Max.ShouldBe(max);
        declared.Replicated.ShouldBeFalse();
        declared.Cheat.ShouldBeFalse();
    }

    [TestCase("snd_mute_losefocus", "1")]
    [TestCase("voice_scale", "1")]
    public void Declaration_ForAnUnboundedSoundConVar_HasNoBounds(string name, string expected)
    {
        EngineConVar declared = EngineConVars.ByName(name);

        declared.Default.ShouldBe(expected);
        declared.Min.ShouldBeNull("help prints no min.");
        declared.Max.ShouldBeNull("help prints no max.");
        declared.Replicated.ShouldBeFalse();
        declared.Cheat.ShouldBeFalse();
    }
}
