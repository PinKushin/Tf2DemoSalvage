using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFPlayerModelPanel` (tf_playermodelpanel.cpp) dressing the class model, on synthetic models and a synthetic schema.</summary>
public sealed class TfPlayerModelPanelConformanceTests
{
    private const string ScoutModel = "models/player/scout.mdl";
    private const string Scattergun = "models/weapons/w_scattergun.mdl";
    private const string Pistol = "models/weapons/w_pistol.mdl";
    private const string Hat = "models/player/items/scout/hat.mdl";

    private const string Schema = """
        "items_game"
        {
            "items"
            {
                "13" { "item_slot" "primary" "baseitem" "1" "used_by_classes" { "scout" "1" } "model_player" "models/weapons/w_scattergun.mdl" "static_attrs" { "weapon_uses_stattrak_module" "models/weapons/c_models/stattrack.mdl" } }
                "23" { "item_slot" "secondary" "baseitem" "1" "used_by_classes" { "scout" "1" } "model_player" "models/weapons/w_pistol.mdl" }
                "50"
                {
                    "item_slot" "head"
                    "used_by_classes" { "scout" "1" }
                    "model_player" "models/player/items/scout/hat.mdl"
                    "visuals" { "use_per_class_bodygroups" "1" "player_bodygroups" { "hat" "1" } }
                }
                "1069" { "item_slot" "action" "act_as_weapon" "1" "item_class" "tf_weapon_spellbook" "used_by_classes" { "scout" "1" } "model_player" "models/weapons/w_pistol.mdl" }
                "51" { "item_slot" "misc" "used_by_classes" { "scout" "1" } "visuals" { "skin" "3" } "model_player" "models/player/items/scout/hat.mdl" }
            }
            "attributes"
            {
                "142" { "name" "set item tint rgb" }
                "261" { "name" "set item tint rgb 2" }
                "1000" { "name" "player skin override" }
                "134" { "name" "attach particle effect" }
                "834" { "name" "paintkit_proto_def_index" "stored_as_integer" "1" }
                "214" { "name" "kill eater" "stored_as_integer" "1" }
                "724" { "name" "weapon_stattrak_module_scale" }
                "2001" { "name" "weapon_uses_stattrak_module" }
            }
            "attribute_controlled_attached_particles" { "cosmetic_unusual_effects" { "13" { "system" "superrare_burning1" } } }
        }
        """;

    private static ItemSchema Items() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private static (TfPlayerModelPanel Panel, FakeCache Cache) Panel(params string[] loaded)
    {
        HudViewport viewport = new()
        {
            Items = Items(),
            ClassModels = PlayerClassModels.Read(path => path == "scripts/playerclasses/scout.txt"
                ? Encoding.UTF8.GetBytes("\"PlayerClass\" { \"model\" \"models/player/scout.mdl\" }")
                : null),
        };

        FakeCache cache = new(loaded);

        return (new TfPlayerModelPanel(viewport, "classmodelpanel", cache), cache);
    }

    private static TfItemView Item(int definition, params EconAttributeValue[] attributes)
    {
        Dictionary<int, EconAttributeValue> byIndex = [];

        foreach (EconAttributeValue attribute in attributes)
        {
            byIndex[attribute.DefinitionIndex] = attribute;
        }

        return new TfItemView(definition, 6, byIndex);
    }

    [Test]
    public void UpdateModelPanel_AHeldScattergun_IsTheClassModelWithItMergedAndStandingPrimary()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Pistol);

        // tf_hud_playerstatus.cpp:479-512.
        panel.ClearCarriedItems();
        panel.SetToPlayerClass(1);
        panel.SetTeam(3);
        panel.AddCarriedItem(Item(23));
        panel.AddCarriedItem(Item(13));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary).ShouldBeTrue();

        panel.ModelName.ShouldBe(ScoutModel);
        panel.MergeMdls.ShouldHaveSingleItem().Path.ShouldBe(Scattergun, "only the held weapon; a weapon is not a wearable");
        panel.MergeMdls[0].Skin.ShouldBe(1, "no item skin: SetMDLSkinForTeam's team skin for BLU (:1185)");
        panel.Skin.ShouldBe(1, "UpdatePreviewVisuals: BLU's player skin");
        panel.Sequence.ShouldBe(0, "ACT_MP_STAND_PRIMARY is sequence 0 (:1015)");
        panel.CurrentSlotIndex.ShouldBe(ItemSchema.LoadoutSlotPrimary);
    }

    [Test]
    public void HoldFirstValidItem_NothingCarried_HoldsTheBasePrimary()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Pistol);

        panel.SetToPlayerClass(1);

        panel.HeldItem.ShouldNotBeNull().DefinitionIndex.ShouldBe(13, "GetBaseItemForClass( scout, PRIMARY ) (:291)");
        panel.MergeMdls.ShouldHaveSingleItem().Path.ShouldBe(Scattergun);
        panel.Skin.ShouldBe(0, "SetToPlayerClass ends on SetTeam( TF_TEAM_RED ) (:250)");
    }

    [Test]
    public void EquipAllWearables_AHatWithBodygroups_HidesTheHatGroupAndSetsItsClassBodygroup()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);

        panel.SetToPlayerClass(1);
        panel.SetTeam(2);
        panel.AddCarriedItem(Item(13));
        panel.AddCarriedItem(Item(50));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        // The fake's radix is 16 per group; "hat" is group 1 on the class model: 1 * 16.
        panel.Body.ShouldBe(16);
        VguiMdl hat = panel.GetMergeMDL(Hat).ShouldNotBeNull();
        hat.Body.ShouldBe(0 + (1 - 1) * 16, "SetBodygroup( merge, 0, 1, class - 1 ): the scout is class 1 (:1223)");
        hat.Skin.ShouldBe(0, "no visuals skin: the RED team skin");
    }

    [Test]
    public void SetMDLSkinForTeam_AVisualsSkin_IsTheItemsSkin()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);

        panel.SetToPlayerClass(1);
        panel.SetTeam(3);
        panel.AddCarriedItem(Item(13));
        panel.AddCarriedItem(Item(51));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        panel.GetMergeMDL(Hat).ShouldNotBeNull().Skin.ShouldBe(3);
    }

    [Test]
    public void AddCarriedItem_OnBlu_PaintsWithTheSecondColor()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);

        panel.SetToPlayerClass(1);
        panel.SetTeam(3);
        panel.AddCarriedItem(Item(13));

        // 0xFF0000 and 0x0000FF as floats — the attribute's VALUE is the packed color.
        panel.AddCarriedItem(Item(51, new EconAttributeValue(142, BitConverter.SingleToInt32Bits(0xFF0000)),
            new EconAttributeValue(261, BitConverter.SingleToInt32Bits(0x0000FF))));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        panel.ItemsToCarry[1].ForceBlueTeam.ShouldBeTrue("kEconItemFlagClient_ForceBlueTeam (:1097)");
        panel.GetMergeMDL(Hat).ShouldNotBeNull().Paint.ShouldBe((0f, 0f, 1f));
    }

    [Test]
    public void UpdatePreviewVisuals_PlayerSkinOverride_IsTheZombieSkin()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);

        panel.SetToPlayerClass(1);
        panel.AddCarriedItem(Item(51, new EconAttributeValue(1000, BitConverter.SingleToInt32Bits(1f))));
        panel.SetTeam(3);

        panel.Skin.ShouldBe(5, "BLU's 1 + 4 (c_tf_player.cpp:7747)");
        TfPlayerModelPanel.AdjustSkinIndexForZombie(8, 1).ShouldBe(23, "the spy's mask skins, +22");
    }

    [Test]
    public void OnModelLoadComplete_AModelNotYetLoaded_MergesTheFrameItLoads()
    {
        (TfPlayerModelPanel panel, FakeCache cache) = Panel(ScoutModel);

        panel.SetToPlayerClass(1);

        panel.MergeMdls.ShouldBeEmpty("the callback waits for the model");
        panel.ModelsToPrecache().ShouldBe([ScoutModel, Scattergun], "the waiting model is asked for, or it never loads");

        cache.Load(Scattergun);
        panel.Paint(new VguiModelPanelConformanceTests.RecordingModelSurface(), VguiModelPanelConformanceTests.Context());

        panel.MergeMdls.ShouldHaveSingleItem().Path.ShouldBe(Scattergun);
    }

    /// <summary>A system that emits every step, drawn as sprites, with no material key — the materials' "" entry.</summary>
    private static ParticleSystem Emitter(string name) => new(
        name,
        [new ParticleFunction("emit_continuously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal) { ["emission_rate"] = new DmxValue(DmxAttributeType.Real, 66d) })],
        [new ParticleFunction("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal) { ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 1d), ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 1d) })],
        [],
        [new ParticleFunction("render_animated_sprites", "draw", new Dictionary<string, DmxValue>(StringComparer.Ordinal))],
        [],
        new Dictionary<string, DmxValue>(StringComparer.Ordinal));

    private static void GiveParticles(TfPlayerModelPanel panel, params string[] names)
    {
        Dictionary<string, ParticleSystem> systems = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in names)
        {
            systems[name] = Emitter(name);
        }

        panel.ParticleSystems = systems;
        panel.ParticleMaterials = new Dictionary<string, ParticleMaterial>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = new ParticleMaterial(null, [], SpriteBlend.Translucent),
        };
        panel.FrameTime = 1f / 66f;
        panel.ApplySettings(KeyValuesTree.Load(Encoding.UTF8.GetBytes("r { }"), "r.res", _ => null), VguiModelPanelConformanceTests.Context());
    }

    [Test]
    public void SetEyeGlowEffect_WithAnEyeglowAttachment_RendersTheGlowThere()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun);
        GiveParticles(panel, "killstreak_t1_lvl1", "killstreak_t0_lvl1_flash");
        panel.SetToPlayerClass(1);

        // tf_hud_playerstatus.cpp:400 hands over the effect and colors; UpdateEyeGlows builds it at eyeglow_R (:1731-1783).
        panel.SetEyeGlowEffect("killstreak_t1_lvl1", Vector3.One, Vector3.UnitX, forceUpdate: true, playSparks: true);

        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();
        panel.Paint(surface, VguiModelPanelConformanceTests.Context());

        panel.ParticleSystemNames[6].ShouldBe("killstreak_t1_lvl1", "SYSTEM_EYEGLOW_RIGHT");
        panel.ParticleSystemNames[8].ShouldBe("killstreak_t0_lvl1_flash", "SYSTEM_EYESPARK_RIGHT: sparks with a non-zero color 1 (:1759)");
        panel.ParticleSystemNames[5].ShouldBeNull("no eyeglow_L on this model (:1732)");
        surface.Particles.ShouldHaveSingleItem().ShouldNotBeEmpty("PostPaint3D renders them after the models (basemodel_panel.cpp:904-912)");
    }

    [Test]
    public void RenderingMergedModel_AnUnusualHat_RunsItsAttachedParticle()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);
        GiveParticles(panel, "superrare_burning1");

        panel.SetToPlayerClass(1);
        panel.AddCarriedItem(Item(13));
        panel.AddCarriedItem(Item(51, new EconAttributeValue(134, BitConverter.SingleToInt32Bits(13f))));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        panel.Paint(new VguiModelPanelConformanceTests.RecordingModelSurface(), VguiModelPanelConformanceTests.Context());

        panel.ParticleSystemNames[0].ShouldBe("superrare_burning1", "SYSTEM_HEAD: a misc item matches the HEAD row first (:1439)");
    }

    [Test]
    public void UpdateActionSlotEffects_AHeldSpellbook_RunsItsHandEffect()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Pistol);
        GiveParticles(panel, "spellbook_minor_burning");

        panel.SetToPlayerClass(1);
        panel.AddCarriedItem(Item(1069));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotAction).ShouldBeTrue();

        panel.Paint(new VguiModelPanelConformanceTests.RecordingModelSurface(), VguiModelPanelConformanceTests.Context());

        // `m_bDrawActionSlotEffects` for a `tf_weapon_spellbook` (:761-764), then GetHandEffect's fancy book (:830-841).
        panel.ParticleSystemNames[4].ShouldBe("spellbook_minor_burning", "SYSTEM_ACTIONSLOT");
        TfPlayerModelPanel.SpellBookHandEffect(1069, 1).ShouldBe("spellbook_major_burning");
        TfPlayerModelPanel.SpellBookHandEffect(5605, 0).ShouldBe("spellbook_rainbow");
        TfPlayerModelPanel.SpellBookHandEffect(1070, 0).ShouldBe("spellbook_minor_fire");
    }

    private const string StatTrak = "models/weapons/c_models/stattrack.mdl";

    [Test]
    public void RenderStatTrack_AStrangePaintkittedWeapon_DrawsItsModuleScaledOnTheWeapon()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, StatTrak);
        GiveParticles(panel);

        panel.SetToPlayerClass(1);
        panel.AddCarriedItem(Item(13,
            new EconAttributeValue(834, 350),
            new EconAttributeValue(214, 12),
            new EconAttributeValue(724, BitConverter.SingleToInt32Bits(0.5f))));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();
        panel.Paint(surface, VguiModelPanelConformanceTests.Context());

        // tf_playermodelpanel.cpp:550-616, :1508-1511, :1567-1574: bone-merged onto the weapon, every axis halved.
        ModelInstance module = System.Linq.Enumerable.Single(surface.Draws.ShouldHaveSingleItem().Models, model => model.ModelPath == StatTrak);
        module.Bones.ShouldNotBeNull()[0][0].ShouldBe(0.5f, 0.0001f);
        module.SkinSwap.ShouldBeNull("the fake has no skin table; the skin is the team's (:1276)");
        panel.StatTrackModel.Skin.ShouldBe(0, "RED (:1276)");
    }

    [Test]
    public void RenderStatTrack_ANotStrangeWeapon_DrawsNoModule()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, StatTrak);
        GiveParticles(panel);

        panel.SetToPlayerClass(1);
        panel.AddCarriedItem(Item(13, new EconAttributeValue(834, 350)));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();
        panel.Paint(surface, VguiModelPanelConformanceTests.Context());

        panel.StatTrackModel.Disabled.ShouldBeTrue("quality 6 and no kill eater: not strange (:557-575)");
        surface.Draws.ShouldHaveSingleItem().Models.ShouldNotContain(model => model.ModelPath == StatTrak);
    }

    /// <summary>Models by path with the activities the panel asks for, and bodygroups as radix-16 digits.</summary>
    private sealed class FakeCache(IEnumerable<string> loaded) : IMdlCache
    {
        private readonly HashSet<string> _loaded = new(loaded, StringComparer.Ordinal);

        public void Load(string path) => _loaded.Add(path);

        public PropModels.ModelFrames? FindMdl(string path)
        {
            if (!_loaded.Contains(path))
            {
                return null;
            }

            IReadOnlyList<StudioAttachment>? attachments = path == ScoutModel
                ?
                [
                    new StudioAttachment("eyeglow_R", 0u, 0, [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 64f]),
                    new StudioAttachment("effect_hand_R", 0u, 0, [1f, 0f, 0f, 10f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 30f]),
                ]
                : null;

            // One bone, `bip_head`, so an attachment and a particle's default bone have something to hang from.
            PropModels.SkinnedModel activities = SyntheticSkinnedModel.WithActivities(
                ("stand_primary", "ACT_MP_STAND_PRIMARY"), ("stand_secondary", "ACT_MP_STAND_SECONDARY"));

            return new PropModels.ModelFrames(
                [], new Dictionary<int, (int, int, float)>(), [], [],
                Skinned: SyntheticSkinnedModel.WithBones("bip_head") with { Sequences = activities.Sequences, Groups = activities.Groups },
                Attachments: attachments);
        }

        public int FindBodygroup(string modelPath, string group) => group switch
        {
            "headphones" => 0,
            "hat" => 1,
            _ => -1,
        };

        public int SetBodygroup(string modelPath, int group, int value, int body)
        {
            int scale = 1 << (4 * group);

            return body - ((body / scale) % 16 * scale) + (value * scale);
        }
    }
}
