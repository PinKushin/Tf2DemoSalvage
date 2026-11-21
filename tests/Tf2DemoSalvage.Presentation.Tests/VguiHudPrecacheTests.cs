using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The HUD's panel models as a frame hands them to the model set's precache (B779 review 4) [no engine counterpart: the engine
/// loads a panel's model in its <c>SetupModel</c>; precaching them on the frame is this viewer's].
/// </summary>
/// <remarks>
/// A HUD with no install behind it, its panels given models by hand: the class panel's root, the match doors with an attached
/// model, and the round sign — the panels <c>VguiHud.AddModelsToPrecache</c> walks, in its order.
/// </remarks>
public sealed class VguiHudPrecacheTests
{
    private const string Scout = "models/player/scout.mdl";
    private const string Doors = "models/vgui/match_doors.mdl";
    private const string DoorsLock = "models/vgui/match_doors_lock.mdl";
    private const string Sign = "models/vgui/round_sign.mdl";

    /// <remarks>
    /// **The HUD lists every model each panel names, in the walk's order** (review 4 item 3): the class panel's, the doors'
    /// and its attachment, the round sign's. The item panels make none here — a `.res` file makes them, and none is read.
    /// </remarks>
    [Test]
    public void AddModelsToPrecache_PanelsWithModels_ListsEachPanelsModelsInOrder()
    {
        (VguiHud hud, _) = Dressed();
        List<string> precache = [];

        hud.AddModelsToPrecache(precache);

        precache.ShouldBe([Scout, Doors, DoorsLock, Sign]);
    }

    /// <remarks>
    /// **An unchanged frame's whole precache route allocates nothing** (review 4 item 1): the HUD filling the presenter's kept
    /// list, an attached-model panel among its panels, then the model set comparing it. Least over eight frames after a first
    /// that packs. The control: the list is not empty, so every panel's walk ran.
    /// </remarks>
    [Test]
    public void AddModelsToPrecacheThenPrecacheChanged_AnUnchangedFrame_AllocatesNothing()
    {
        (VguiHud hud, EntityModelSet models) = Dressed();
        List<string> kept = [];
        long least = long.MaxValue;

        Frame(hud, models, kept);
        kept.Count.ShouldBe(4, "the control");

        for (int run = 0; run < 8; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Frame(hud, models, kept);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
    }

    /// <summary>One frame's precache, as `FramePresenter.BuildOverlay` runs it.</summary>
    private static void Frame(VguiHud hud, EntityModelSet models, List<string> kept)
    {
        kept.Clear();
        hud.AddModelsToPrecache(kept);
        models.PrecacheChanged(kept);
    }

    /// <summary>A HUD on an empty scheme whose class panel, match doors and round sign each name a model.</summary>
    private static (VguiHud Hud, EntityModelSet Models) Dressed()
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        EntityModelSet models = new();

        host.BeginFrame(640, 480);

        VguiHud hud = new(host, models);

        hud.Frame(new HudState(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: []));
        VguiContext context = hud.Viewport.Context.ShouldNotBeNull();

        hud.PlayerStatus.PlayerClass.PlayerModelPanel.SetMDL(Scout);
        hud.MatchStatus.MatchStartModelPanel.ApplySettings(Block(Doors, DoorsLock), context);
        hud.MatchStatus.RoundSignModel.ApplySettings(Block(Sign, null), context);

        return (hud, models);
    }

    private static KeyValuesTree Block(string model, string? attached) => KeyValuesTree.Load(
        Encoding.UTF8.GetBytes($$"""
            "Panel"
            {
                "fieldName" "Panel" "wide" "100" "tall" "100"
                "model" { "modelname" "{{model}}" {{(attached is null ? string.Empty : $"\"attached_model\" {{ \"modelname\" \"{attached}\" }}")}} }
            }
            """),
        "panel.res",
        _ => null);
}
