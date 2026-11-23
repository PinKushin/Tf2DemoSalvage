using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;
using Tf2DemoSalvage.Scene.Tests;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The HUD's model panels loading their models through the presenter's model set in the call (B779 review 15 follow-up),
/// which replaced the frame's precache of them that this file used to hold.
/// </summary>
/// <remarks>
/// A HUD with no install behind it, its panels given models by hand: the class panel's root (a CMDL panel), the match doors
/// with an attached model and the round sign (CModelPanels). Nothing is packed before each panel asks.
/// </remarks>
public sealed class VguiHudModelLoadTests
{
    private const string Scout = "models/player/scout.mdl";
    private const string Doors = "models/vgui/match_doors.mdl";
    private const string DoorsLock = "models/vgui/match_doors_lock.mdl";
    private const string Sign = "models/vgui/round_sign.mdl";

    /// <remarks>
    /// **Each panel kind packs its own models into the HUD's set when it reads them**: `SetMDL` takes a handle from
    /// `FindMDL` and reads the header (mdlpanel.cpp:185; imdlcache.h:116-126) — here through the pose parameters' defaults,
    /// which read the model at once (:172) — and a CModelPanel's set-up loads its model and its attached model
    /// (`InitializeAsClientEntity`, basemodelpanel.cpp:407, :472). The control: nothing was packed before.
    /// </remarks>
    [Test]
    public void EachPanelKind_ReadingItsModelsWithNothingPacked_PacksThemIntoTheHudsModelSet()
    {
        (VguiHud hud, EntityModelSet models) = Dressed();

        foreach (string path in (string[])[Scout, Doors, DoorsLock, Sign])
        {
            models.FindMdl(path).ShouldBeNull("the control: " + path);
        }

        hud.PlayerStatus.PlayerClass.PlayerModelPanel.SetMDL(Scout);
        hud.MatchStatus.MatchStartModelPanel.UpdateModel();
        hud.MatchStatus.RoundSignModel.UpdateModel();

        foreach (string path in (string[])[Scout, Doors, DoorsLock, Sign])
        {
            models.FindMdl(path).ShouldNotBeNull(path);
        }
    }

    /// <summary>A HUD on an empty scheme whose match doors and round sign each name a model; every path loads, none is packed.</summary>
    private static (VguiHud Hud, EntityModelSet Models) Dressed()
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        PropModels.ModelFrames frames = new(
            [
                new PropVertex[]
                {
                    new(0f, 0f, 0f, 0f, 0f, MaterialIndex: 1) { Weights = (1f, 0f, 0f), Bones = (0, 0, 0) },
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 1) { Weights = (1f, 0f, 0f), Bones = (0, 0, 0) },
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: SyntheticSkinnedModel.WithOneBone());
        EntityModelSet models = new() { Geometry = _ => frames };

        host.BeginFrame(640, 480);

        VguiHud hud = new(host, models);

        hud.Frame(new HudState(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: []));
        VguiContext context = hud.Viewport.Context.ShouldNotBeNull();

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
