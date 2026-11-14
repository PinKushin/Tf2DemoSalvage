using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>The engine-side voice ConVars the client's voice playback reads, as engine.dll registers them.</summary>
/// <remarks>
/// **Absent from the SDK** (the voice engine is closed), so read from TF2's x64 <c>engine.dll</c> registrations on
/// 2026-10-15, each a static initializer calling <c>ConVar::ConVar(this, name, default, flags)</c>:
///
/// <code>
/// FUN_180002d70  ConVar @1804e8c50 "voice_enable"      default @18035e7a4 "1"    flags 0x80 (FCVAR_ARCHIVE)
/// FUN_180002ef0  ConVar @1804e88f0 "voice_scale"       default @18035e7a4 "1"    flags 0x80 — the four-argument
///                constructor, so no bounds (MixerConVarConformanceTests holds the null min/max)
/// FUN_180002e70  ConVar @1804e8860 "voice_maxgain"     default @180362108 "10"   flags 0
/// FUN_180002cf0  ConVar @1804e87d0 "voice_avggain"     default @18035e20c "0.5"  flags 0
/// FUN_180002d30  ConVar @1804e8d70 "voice_buffer_ms"   default @180362228 "100"  flags 0x8000 (FCVAR_INTERNAL_USE)
/// FUN_180002db0  ConVar @1804e8a10 "voice_fadeouttime" default @18035dee0 "0.1"  flags 0
/// </code>
///
/// And the overdrive duck's two (B549), read 2026-11-14 on build 11191711:
///
/// <code>
/// FUN_180003070  ConVar @180521000 "voice_overdrive"         default @18036019c "2"    flags 0
/// FUN_1800030b0  ConVar @180521090 "voice_overdrivefadetime" default @180364a40 "0.4"  flags 0
/// FUN_180011c50  ConVar @18077e130 "cl_voice_filter"         default @18035f128 ""     flags 0
/// </code>
///
/// Each is read through its <c>m_pParent</c> at <c>+0x38</c> and its float at <c>+0x54</c>: <c>Voice_AssignChannel</c>
/// (<c>FUN_180057350</c>) reads <c>DAT_1804e8898</c>, <c>DAT_1804e8808</c> and <c>DAT_1804e8928</c> (voice_scale) into
/// <c>CAutoGain::Reset</c>, and <c>DAT_1804e8da8</c> for the jitter pad; <c>Voice_Idle</c> (<c>FUN_180058090</c>) reads
/// <c>DAT_1804e8a48</c> for the fade.
/// </remarks>
public sealed class VoiceConVarConformanceTests
{
    [TestCase("voice_enable", "1")]
    [TestCase("voice_maxgain", "10")]
    [TestCase("voice_avggain", "0.5")]
    [TestCase("voice_buffer_ms", "100")]
    [TestCase("voice_fadeouttime", "0.1")]
    [TestCase("voice_overdrive", "2")]
    [TestCase("voice_overdrivefadetime", "0.4")]
    [TestCase("cl_voice_filter", "")]
    public void Declaration_ForAVoiceConVar_MatchesTheRegistration(string name, string expected)
    {
        EngineConVar declared = EngineConVars.ByName(name);

        declared.Default.ShouldBe(expected);
        declared.Replicated.ShouldBeFalse();
        declared.Cheat.ShouldBeFalse();
    }
}
