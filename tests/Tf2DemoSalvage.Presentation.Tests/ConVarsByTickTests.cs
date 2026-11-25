using System;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The client ConVars each demo tick of a frame reads (B839).</summary>
public sealed class ConVarsByTickTests
{
    private static readonly Func<string, string?> Now = static name => name == "hud_deathnotice_time" ? "9" : "now";

    /// <remarks>
    /// **A run made on a tick is first read by the next one** (`_Host_RunFrame`, `engine.dll` build 1007 FUN_1801a5eb0:
    /// `Cbuf_Execute`, then the client step reading the next tick's packets): the run made on tick 10 set
    /// `hud_deathnotice_time` from 3 to 9, so tick 10's events read 3 and tick 11's read 9. Another ConVar reads as now,
    /// and the name is matched as the console matches it, in any case.
    /// </remarks>
    [Test]
    public void At_TheTickARunWasMadeOn_ReadsTheValueItReplaced()
    {
        ConVarsByTick byTick = new();
        byTick.BeginFrame();
        byTick.BeforeTick(10, _ => byTick.Set("hud_deathnotice_time", "3"));

        Func<string, string?> tenth = byTick.At(10, Now).ShouldNotBeNull();

        (tenth("hud_deathnotice_time"), tenth("HUD_DEATHNOTICE_TIME"), tenth("other")).ShouldBe(("3", "3", "now"));
        byTick.At(11, Now).ShouldBeNull("the next tick reads them as they are now");
    }

    /// <remarks>
    /// **Two runs set one ConVar: each tick reads the value in force after the runs before it** — 1 before both, 2 between,
    /// 3 after.
    /// </remarks>
    [Test]
    public void At_TwoRunsSettingOneConVar_EachTickReadsItsOwn()
    {
        ConVarsByTick byTick = new();
        byTick.BeginFrame();
        byTick.BeforeTick(10, _ => byTick.Set("x", "1"));
        byTick.BeforeTick(11, _ => byTick.Set("x", "2"));

        (byTick.At(10, Now)!("x"), byTick.At(11, Now)!("x"), byTick.At(12, Now)).ShouldBe(("1", "2", null));
    }

    /// <remarks>
    /// **A set made outside a tick's run is in force for every tick of the frame** — a key, the frame's top run, a
    /// load — and a new frame forgets the last one's runs.
    /// </remarks>
    [Test]
    public void At_ASetOutsideARunOrALastFramesRun_IsInForceForEveryTick()
    {
        ConVarsByTick byTick = new();
        byTick.BeginFrame();
        byTick.Set("x", "1");
        byTick.At(0, Now).ShouldBeNull("outside a run");

        byTick.BeforeTick(10, _ => byTick.Set("x", "2"));
        byTick.At(10, Now).ShouldNotBeNull("the control: inside a run it is kept");
        byTick.BeginFrame();

        byTick.At(10, Now).ShouldBeNull();
    }

    /// <remarks>**A throw inside a run ends the run**: a set after it is outside one.</remarks>
    [Test]
    public void BeforeTick_ARunThatThrows_EndsTheRun()
    {
        ConVarsByTick byTick = new();
        byTick.BeginFrame();

        Should.Throw<InvalidOperationException>(() => byTick.BeforeTick(10, static _ => throw new InvalidOperationException("run")));
        byTick.Set("x", "1");

        byTick.At(9, Now).ShouldBeNull();
    }
}
