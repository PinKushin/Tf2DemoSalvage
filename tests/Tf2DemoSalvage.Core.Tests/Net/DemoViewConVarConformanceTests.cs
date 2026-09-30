using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The four ConVars <c>CDemoPlayer::InterpolateViewpoint</c> reads (B56), as <c>engine.dll</c> registers them.
/// </summary>
/// <remarks>
/// **None of them is in the SDK**: they are engine ConVars, so the registrations were read out of the x64
/// <c>engine.dll</c> (project <c>tf2enginex64</c>). Each is a <c>ConVar::ConVar</c> call (<c>0x180283e80</c>) whose
/// flags argument is zeroed (<c>XOR R9D,R9D</c>), so none is replicated, cheat-protected or userinfo: they are the
/// watcher's own, read from his config (D69, D190).
///
/// | ConVar | registered at | default | read by <c>0x180072180</c> as |
/// |---|---|---|---|
/// | <c>demo_interpolateview</c> | <c>0x180003bea</c> | "1" (<c>0x18035e7a4</c>) | <c>m_nValue</c>, +0x58 |
/// | <c>demo_interplimit</c> | <c>0x180003baa</c> | "4000" (<c>0x180367ea8</c>) | <c>m_fValue</c>, +0x54 |
/// | <c>demo_avellimit</c> | <c>0x180003a2a</c> | "2000" (<c>0x180367f1c</c>) | <c>m_fValue</c>, +0x54 |
/// | <c>demo_legacy_rollback</c> | <c>0x180003c2a</c> | "1" (<c>0x18035e7a4</c>) | <c>m_nValue</c>, +0x58 |
///
/// The reads go through each ConVar's <c>m_pParent</c> at +0x38, which is why the addresses
/// <c>InterpolateViewpoint</c> dereferences sit 0x38 past the objects the registrations construct.
/// </remarks>
public sealed class DemoViewConVarConformanceTests
{
    /// <summary>The four, with the defaults their registrations pass.</summary>
    private static readonly (string Name, string Default)[] Registered =
    [
        ("demo_interpolateview", "1"),
        ("demo_interplimit", "4000"),
        ("demo_avellimit", "2000"),
        ("demo_legacy_rollback", "1"),
    ];

    [Test]
    public void Declarations_ForTheDemoViewConVars_AreTheirRegistrationsDefaults()
    {
        foreach ((string name, string expected) in Registered)
        {
            EngineConVars.ByName(name).Default.ShouldBe(expected, name);
        }
    }

    [Test]
    public void Declarations_ForTheDemoViewConVars_AreTheWatchersRatherThanTheServers()
    {
        foreach ((string name, _) in Registered)
        {
            EngineConVar declared = EngineConVars.ByName(name);

            declared.Replicated.ShouldBeFalse($"{name} is registered with no flags");
            declared.Cheat.ShouldBeFalse($"{name} is registered with no flags");
            declared.UserInfo.ShouldBeFalse($"{name} is registered with no flags");
        }
    }

    /// <summary>That the installed build's own dump agrees with the registrations, flags column empty.</summary>
    [Test]
    public void ShippedCvarList_ForEveryDemoViewConVar_AgreesWithItsRegistration()
    {
        string listing = System.IO.File.ReadAllText(GameInstall.RequireFile("cvarlist.log"));

        foreach ((string name, string expected) in Registered)
        {
            Match row = Regex.Match(
                listing,
                $"^{Regex.Escape(name)} +: +([^ ]+) +: *([^:]*):",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(10));

            row.Success.ShouldBeTrue($"{name} appears in the game's own convar dump");
            row.Groups[1].Value.ShouldBe(expected, name);
            row.Groups[2].Value.Trim().ShouldBeEmpty($"{name} carries no flags");
        }
    }
}
