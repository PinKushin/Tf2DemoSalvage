using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `ivgui()->AddTickSignal` and `CVGui::RunFrame`'s tick walk, *read from disassembly* (`vgui2.dll` x64): `AddTickSignal`
/// and `AddTickSignalToHead` (`0x1800196b0`, `0x1800196e0`) share `0x180019fd0`, which for a panel already listed only
/// resets its interval and next tick, so "to head" moves nothing; a new one is inserted at the tail, or at the head.
/// `RunFrame` (`0x18001b200`) walks the list from the tail to the head, skips an entry whose next tick has not come, and
/// counts the next from the tick that fired.
/// </summary>
public sealed class VguiTickSignalConformanceTests
{
    [Test]
    public void Due_BeforeItsIntervalHasPassed_IsFalse()
    {
        VguiTickInterval tick = new(100);
        tick.Start(1000);

        tick.Due(1099).ShouldBeFalse();
    }

    [Test]
    public void Due_OnceItsIntervalHasPassed_FiresAndCountsTheNextFromThatTick()
    {
        VguiTickInterval tick = new(100);
        tick.Start(1000);

        tick.Due(1100).ShouldBeTrue();
        tick.Due(1150).ShouldBeFalse();
        tick.Due(1230).ShouldBeTrue("due at 1200, fired late at 1230");
        tick.Due(1329).ShouldBeFalse("the next counts from 1230, not from 1200");
        tick.Due(1330).ShouldBeTrue();
    }

    [Test]
    public void Due_NeverStarted_StartsAndWaitsAnInterval()
    {
        VguiTickInterval tick = new(100);

        tick.Due(500).ShouldBeFalse();
        tick.Due(599).ShouldBeFalse();
        tick.Due(600).ShouldBeTrue();
    }

    [Test]
    public void Due_WithNoInterval_FiresEveryCall()
    {
        VguiTickInterval tick = new(0);

        tick.Due(500).ShouldBeTrue();
        tick.Due(500).ShouldBeTrue();
    }

    [Test]
    public void Think_TwoTickSignals_TicksTheTailFirst()
    {
        Frames frames = new();
        HudViewport viewport = frames.Viewport;
        List<string> order = [];
        viewport.AddTickSignal(new Recorder("first", order));
        viewport.AddTickSignal(new Recorder("second", order));

        frames.Think(At(0f));

        order.ShouldBe(["second", "first"]);
    }

    [Test]
    public void AddTickSignalToHead_ANewPanel_TicksLast()
    {
        Frames frames = new();
        HudViewport viewport = frames.Viewport;
        List<string> order = [];
        viewport.AddTickSignal(new Recorder("tail", order));
        viewport.AddTickSignalToHead(new Recorder("head", order));

        frames.Think(At(0f));

        order.ShouldBe(["tail", "head"]);
    }

    [Test]
    public void AddTickSignalToHead_AnAlreadyListedPanel_KeepsItsPlace()
    {
        Frames frames = new();
        HudViewport viewport = frames.Viewport;
        List<string> order = [];
        Recorder middle = new("middle", order);
        viewport.AddTickSignal(new Recorder("first", order));
        viewport.AddTickSignal(middle);
        viewport.AddTickSignal(new Recorder("last", order));

        viewport.AddTickSignalToHead(middle);
        frames.Think(At(0f));

        order.ShouldBe(["last", "middle", "first"], "moved to the head, it would tick last");
    }

    [Test]
    public void Think_ATickSignalWithAnInterval_TicksOnlyOnceItHasPassedSinceItWasAdded()
    {
        Frames frames = new();
        HudViewport viewport = frames.Viewport;
        List<string> order = [];
        frames.Think(At(1f));
        viewport.AddTickSignal(new Recorder("slow", order), 100);

        frames.Think(At(1.099f));
        order.ShouldBeEmpty();

        frames.Think(At(1.1f));
        order.ShouldBe(["slow"]);
    }

    [Test]
    public void AddTickSignal_AnAlreadyListedPanel_RestartsItsInterval()
    {
        Frames frames = new();
        HudViewport viewport = frames.Viewport;
        List<string> order = [];
        Recorder slow = new("slow", order);
        frames.Think(At(1f));
        viewport.AddTickSignal(slow, 100);
        frames.Think(At(1.05f));

        viewport.AddTickSignal(slow, 100);
        frames.Think(At(1.1f));
        order.ShouldBeEmpty("restarted at 1.05, due at 1.15");

        frames.Think(At(1.15f));
        order.ShouldBe(["slow"]);
    }

    [Test]
    public void Think_ATickRemovingAnotherNotYetTicked_SkipsIt()
    {
        Frames frames = new();
        HudViewport viewport = frames.Viewport;
        List<string> order = [];
        Recorder victim = new("victim", order);
        viewport.AddTickSignal(victim);
        viewport.AddTickSignal(new Recorder("remover", order, () => viewport.RemoveTickSignal(victim)));

        frames.Think(At(0f));
        frames.Think(At(0f));

        order.ShouldBe(["remover", "remover"]);
    }

    /// <remarks>`0x180019fd0` finds the marked entry by its panel and clears the mark, so it keeps its place and still ticks.</remarks>
    [Test]
    public void Think_ATickRemovingAndReaddingAnotherNotYetTicked_StillTicksIt()
    {
        Frames frames = new();
        HudViewport viewport = frames.Viewport;
        List<string> order = [];
        Recorder victim = new("victim", order);
        viewport.AddTickSignal(victim);
        viewport.AddTickSignal(new Recorder("remover", order, () =>
        {
            viewport.RemoveTickSignal(victim);
            viewport.AddTickSignal(victim);
        }));

        frames.Think(At(0f));

        order.ShouldBe(["remover", "victim"]);
    }

    private static HudState At(float realTime) => new(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: 100, Alive: true) { RealTime = realTime };

    /// <summary>A viewport whose think traversal runs: the frame's state, then `SolveTraverse`, which reaches its `OnThink`.</summary>
    private sealed class Frames
    {
        private readonly VguiContext _context = Context();

        public HudViewport Viewport { get; }

        public Frames() => Viewport = new HudViewport { Wide = 640, Tall = 480, Context = _context };

        public void Think(HudState state)
        {
            Viewport.Think(state);
            VguiLayout.SolveTraverse(Viewport, _context);
        }

        private static VguiContext Context()
        {
            KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
            VguiScheme colours = VguiScheme.Load(scheme);

            return new VguiContext(colours, VguiBorders.Load(scheme, colours, 960), scheme.Find("Fonts")!, 1280, 960, "english");
        }
    }

    private sealed class Recorder(string name, List<string> order, System.Action? onTick = null) : IVguiTickSignal
    {
        public void OnTick()
        {
            order.Add(name);
            onTick?.Invoke();
        }
    }
}
