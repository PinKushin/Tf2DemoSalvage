using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CPotteryWheelPanel</c>/<c>CMDLPanel</c>'s portable half (<see cref="VguiModelPanel"/>) and
/// <c>CBaseModelPanel</c>'s <c>.res</c> parsing (<see cref="VguiBaseModelPanel"/>).
/// </summary>
public sealed class VguiModelPanelConformanceTests
{
    private const int ScreenTall = 1080;

    [Test]
    public void Ambient_ANewPanel_IsPoint4OnEveryFaceLikeCreateDefaultLights()
    {
        // `CreateDefaultLights` (potterywheelpanel.cpp:316-321): `m_vecAmbientCube[i].Init(0.4f, 0.4f, 0.4f, 1.0f)`
        // for all six faces.
        VguiModelPanel panel = new(null, "model");

        panel.Ambient.PositiveX.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.NegativeX.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.PositiveY.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.NegativeY.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.PositiveZ.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.NegativeZ.ShouldBe((0.4f, 0.4f, 0.4f));
    }

    [Test]
    public void Sun_ANewPanel_IsOneWhiteLightPointingStraightDown()
    {
        // `CreateDefaultLights` (potterywheelpanel.cpp:325-327): `MATERIAL_LIGHT_DIRECTIONAL`, colour
        // (1, 1, 1), direction (0, 0, -1).
        VguiModelPanel panel = new(null, "model");

        panel.Sun.ShouldBe(new SunLight(1f, 1f, 1f, 0f, 0f, -1f));
    }

    [Test]
    public void Camera_ANewPanel_MatchesThePotteryWheelPanelConstructor()
    {
        // potterywheelpanel.cpp:250-252.
        VguiModelPanel.NearZ.ShouldBe(3f);
        VguiModelPanel.FarZ.ShouldBe(16384f * 1.73205080757f);

        VguiModelPanel panel = new(null, "model");

        panel.FieldOfView.ShouldBe(30f);
    }

    [Test]
    public void ParseLightsFromKV_ADirectionalEntry_ReplacesTheSun()
    {
        // `ParseLightsFromKV` (potterywheelpanel.cpp:392-413): the first `directional` entry's colour and
        // (normalised) direction become the light.
        VguiModelPanel panel = new(null, "model");
        KeyValuesTree lights = Resource("""
            lights
            {
                "light1"
                {
                    "name" "directional"
                    "color" "1 0 0"
                    "direction" "0 3 4"
                }
            }
            """).Find("lights")!;

        panel.ParseLightsFromKV(lights);

        panel.Sun.Red.ShouldBe(1f);
        panel.Sun.Green.ShouldBe(0f);
        panel.Sun.Blue.ShouldBe(0f);
        panel.Sun.DirectionX.ShouldBe(0f);
        panel.Sun.DirectionY.ShouldBe(0.6f, 0.0001f);
        panel.Sun.DirectionZ.ShouldBe(0.8f, 0.0001f);
    }

    [Test]
    public void ApplySettings_ALightsBlock_IsParsedIntoTheSun()
    {
        VguiModelPanel panel = new(null, "model");
        KeyValuesTree resource = Resource("""
            lights
            {
                "light1"
                {
                    "name" "directional"
                    "color" "0 1 0"
                    "direction" "0 0 1"
                }
            }
            """);

        panel.ApplySettings(resource, Context());

        panel.Sun.Green.ShouldBe(1f);
        panel.Sun.DirectionZ.ShouldBe(1f);
    }

    [Test]
    public void Paint_NoModelName_EmitsNoPaint3DCall()
    {
        VguiModelPanel panel = new(null, "model") { ResolveModel = _ => SyntheticSkinnedModel.WithOneBone() };
        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        surface.Draws.ShouldBeEmpty();
    }

    [Test]
    public void Paint_ARootModel_EmitsOneDrawCarryingItsPath()
    {
        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/scout.mdl",
            ResolveModel = _ => SyntheticSkinnedModel.WithOneBone(),
            Wide = 100,
            Tall = 50,
        };

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        surface.Draws.Count.ShouldBe(1);
        surface.Draws[0].Models.Count.ShouldBe(1);
        surface.Draws[0].Models[0].ModelPath.ShouldBe("models/player/scout.mdl");

        // `Paint3D`'s rectangle is the panel's own, panel-relative (`IVguiSurface.Paint3D`'s remarks).
        surface.Draws[0].Left.ShouldBe(0);
        surface.Draws[0].Top.ShouldBe(0);
        surface.Draws[0].Right.ShouldBe(100);
        surface.Draws[0].Bottom.ShouldBe(50);
    }

    [Test]
    public void Paint_ARootAndAMergeModel_EmitsBothInOneDraw()
    {
        // Two bones, one shared name ("root") and one not — `SyntheticSkinnedModel.WithBones`'s own note: a
        // fixture sharing every bone name cannot see a merge failure, so the weapon-shaped model here keeps
        // one bone the wearer does not have.
        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/demo.mdl",
            ResolveModel = path => path == "models/player/demo.mdl"
                ? SyntheticSkinnedModel.WithBones("root")
                : SyntheticSkinnedModel.WithBones("root", "muzzle"),
            Wide = 100,
            Tall = 100,
        };

        panel.MergeModels.Add("models/weapons/c_models/c_stickybomb_launcher.mdl");

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        surface.Draws.Count.ShouldBe(1);
        surface.Draws[0].Models.Count.ShouldBe(2);
        surface.Draws[0].Models[0].ModelPath.ShouldBe("models/player/demo.mdl");
        surface.Draws[0].Models[1].ModelPath.ShouldBe("models/weapons/c_models/c_stickybomb_launcher.mdl");
    }

    [Test]
    public void Paint_AMergeModelSharingABoneName_TakesTheRootsBoneToWorldForThatBone()
    {
        // `CMDLPanel::OnPaint3D` (mdlpanel.cpp:493): `SetupBonesWithBoneMerge` folds the merge model's shared-named
        // bones from the root's already-built bone-to-world. `AnimatingEntity.Follows` is this project's version of
        // that (WeaponMergeContentTests already proves it for a real weapon/player pair); this checks the panel
        // actually wires it rather than merely holding both models unmerged.
        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/demo.mdl",
            ResolveModel = path => path == "models/player/demo.mdl"
                ? SyntheticSkinnedModel.WithBones("root")
                : SyntheticSkinnedModel.WithBones("root", "muzzle"),
            Wide = 100,
            Tall = 100,
        };

        panel.MergeModels.Add("models/weapons/c_models/c_stickybomb_launcher.mdl");

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        float[] rootBone = surface.Draws[0].Models[0].Bones![0];
        float[] mergeBone = surface.Draws[0].Models[1].Bones![0];

        mergeBone.ShouldBe(rootBone);
    }

    [Test]
    public void ModelsToPrecache_ARootAndMergeModels_YieldsRootFirst()
    {
        VguiModelPanel panel = new(null, "model") { ModelName = "models/player/scout.mdl" };

        panel.MergeModels.Add("models/weapons/c_models/c_scattergun.mdl");

        panel.ModelsToPrecache().ShouldBe(
            ["models/player/scout.mdl", "models/weapons/c_models/c_scattergun.mdl"]);
    }

    [Test]
    public void ParseModelResInfo_AModelBlock_ReadsNameSkinAnglesAndOrigin()
    {
        // basemodel_panel.cpp:88-99, defaults at :95-96.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree modelBlock = Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
                "skin" "2"
                "angles_y" "90"
                "origin_x" "50"
                "spotlight" "1"
            }
            """).Find("model")!;

        panel.ParseModelResInfo(modelBlock);

        panel.ModelName.ShouldBe("models/player/scout.mdl");
        panel.Skin.ShouldBe(2);
        panel.ModelAngles.ShouldBe((0f, 90f, 0f));
        panel.ModelOrigin.ShouldBe((50f, 5f, 5f));
        panel.UseSpotlight.ShouldBeTrue();
    }

    [Test]
    public void ParseModelResInfo_NoOriginGiven_DefaultsTo110_5_5()
    {
        // basemodel_panel.cpp:95: `GetFloat( "origin_x", 110.0 )`, `"origin_y", 5.0`, `"origin_z", 5.0`.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree modelBlock = Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
            }
            """).Find("model")!;

        panel.ParseModelResInfo(modelBlock);

        panel.ModelOrigin.ShouldBe((110f, 5f, 5f));
    }

    [Test]
    public void ApplySettings_AModelBlock_IsFoundAmongTheResourcesOtherKeys()
    {
        // `CBaseModelPanel::ApplySettings` (basemodel_panel.cpp:46-83) reads `fov` itself and then finds the
        // `model` sub-block by name among the resource's other keys.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree resource = Resource("""
            xpos 0
            fov 45
            model
            {
                "modelname" "models/player/scout.mdl"
            }
            """);

        panel.ApplySettings(resource, Context());

        panel.FieldOfView.ShouldBe(45f);
        panel.ModelName.ShouldBe("models/player/scout.mdl");
    }

    [Test]
    public void ApplySettings_AFractionalFov_Truncates()
    {
        // `inResourceData->GetInt( "fov", flFOV )` (basemodel_panel.cpp:56) is GetInt, not GetFloat — KeyValues'
        // GetInt does `atoi`, which truncates toward zero rather than rounding.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree resource = Resource("fov 45.9");

        panel.ApplySettings(resource, Context());

        panel.FieldOfView.ShouldBe(45f);
    }

    [Test]
    public void ParseModelAnimInfo_TwoAnimationsOneDefault_RecordsBoth()
    {
        // basemodel_panel.cpp:122-134.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree modelBlock = Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
                "animation"
                {
                    "name" "idle"
                    "sequence" "idle"
                    "default" "1"
                }
                "animation"
                {
                    "name" "taunt"
                    "sequence" "taunt"
                }
            }
            """).Find("model")!;

        panel.ParseModelResInfo(modelBlock);

        panel.Animations.Count.ShouldBe(2);
        panel.Animations[0].Name.ShouldBe("idle");
        panel.Animations[0].Default.ShouldBeTrue();
        panel.Animations[1].Name.ShouldBe("taunt");
        panel.Animations[1].Default.ShouldBeFalse();
    }

    /// <summary>Wraps a body in a root block, the way <c>Panel::ApplySettings</c>'s caller already has one.</summary>
    private static KeyValuesTree Resource(string body) =>
        KeyValuesTree.Load(Encoding.UTF8.GetBytes("Resource\n{\n" + body + "\n}"), "test.res", _ => null);

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, ScreenTall), scheme.Find("Fonts")!, 1920, ScreenTall, "english");
    }

    /// <summary>Every <see cref="IVguiSurface"/> member no-op except <see cref="IVguiSurface.Paint3D"/>, which records.</summary>
    private sealed class RecordingModelSurface : IVguiSurface
    {
        public List<(int Left, int Top, int Right, int Bottom, float[] Camera, IReadOnlyList<ModelInstance> Models)> Draws { get; } = [];

        public float AlphaMultiplier { get; set; } = 1f;

        public void PushMakeCurrent(VguiPanel panel, bool useInset)
        {
        }

        public void PopMakeCurrent(VguiPanel panel)
        {
        }

        public void Paint3D(int left, int top, int right, int bottom, float[] camera, IReadOnlyList<ModelInstance> models) =>
            Draws.Add((left, top, right, bottom, camera, models));

        public void DrawSetColor((byte Red, byte Green, byte Blue, byte Alpha) color)
        {
        }

        public void DrawFilledRect(int x0, int y0, int x1, int y1)
        {
        }

        public void DrawFilledRectFade(int x0, int y0, int x1, int y1, int alpha0, int alpha1, bool horizontal)
        {
        }

        public void DrawOutlinedRect(int x0, int y0, int x1, int y1)
        {
        }

        public void DrawSetTexture(string? texture)
        {
        }

        public void DrawTexturedQuad(float x0, float y0, float x1, float y1, float s0, float t0, float s1, float t1)
        {
        }

        public void DrawTexturedPolygon(System.ReadOnlySpan<VguiVertex> vertices)
        {
        }

        public void DrawTexturedRect(int x0, int y0, int x1, int y1)
        {
        }

        public void DrawTexturedSubRect(int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1)
        {
        }

        public (int Wide, int Tall) DrawGetTextureSize(string texture) => (0, 0);

        public void DrawSetTextFont(VguiFontAmalgam font)
        {
        }

        public void DrawSetTextColor((byte Red, byte Green, byte Blue, byte Alpha) color)
        {
        }

        public void DrawSetTextPos(int x, int y)
        {
        }

        public (int X, int Y) DrawGetTextPos() => (0, 0);

        public void DrawUnicodeChar(char character, VguiFontDrawType drawType = VguiFontDrawType.Default)
        {
        }

        public void DrawPrintText(string text, VguiFontDrawType drawType = VguiFontDrawType.Default)
        {
        }

        public int GetFontTall(VguiFontAmalgam font) => 0;

        public (int A, int B, int C) GetCharAbcWide(VguiFontAmalgam font, char character) => (0, 0, 0);

        public int GetCharacterWidth(VguiFontAmalgam font, char character) => 0;
    }
}
