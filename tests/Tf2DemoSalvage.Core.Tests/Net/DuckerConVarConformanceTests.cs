using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>The speech ducker's ConVars (B719), as engine.dll registers them.</summary>
/// <remarks>
/// **Absent from the SDK** (the mixer is closed), so read from TF2's x64 <c>engine.dll</c> build 11191711 registrations,
/// each a static initializer calling <c>ConVar::ConVar(this, name, default, flags)</c>, the four-argument constructor,
/// so no bounds:
///
/// <code>
/// FUN_180001aa0  ConVar @1804a6ea0 "snd_ducking_off"       default @1803607a4 "1"    flags 0x80 (FCVAR_ARCHIVE)
/// FUN_180001ae0  ConVar @1804a6c60 "snd_ducktovolume"      default @1803602b8 "0.55" flags 0x80
/// FUN_1800019e0  ConVar @1804a6cf0 "snd_duckerattacktime"  default @18036020c "0.5"  flags 0x80
/// FUN_180001a20  ConVar @1804a6d80 "snd_duckerreleasetime" default @1803602f0 "2.5"  flags 0x80
/// FUN_180001a60  ConVar @1804a6e10 "snd_duckerthreshold"   default @180360310 "0.15" flags 0x80
/// </code>
///
/// <c>S_Update</c> (<c>FUN_180030870</c>) reads each through its <c>m_pParent</c> at <c>+0x38</c>: the int at
/// <c>+0x58</c> of <c>snd_ducking_off</c> (<c>0x180030e97</c>), the float at <c>+0x54</c> of the other four.
/// </remarks>
public sealed class DuckerConVarConformanceTests
{
    [TestCase("snd_ducking_off", "1")]
    [TestCase("snd_ducktovolume", "0.55")]
    [TestCase("snd_duckerattacktime", "0.5")]
    [TestCase("snd_duckerreleasetime", "2.5")]
    [TestCase("snd_duckerthreshold", "0.15")]
    public void Declaration_ForADuckerConVar_MatchesTheRegistration(string name, string expected)
    {
        EngineConVar declared = EngineConVars.ByName(name);

        declared.Default.ShouldBe(expected);
        declared.Replicated.ShouldBeFalse();
        declared.Cheat.ShouldBeFalse();
        declared.Min.ShouldBeNull();
        declared.Max.ShouldBeNull();
    }
}
