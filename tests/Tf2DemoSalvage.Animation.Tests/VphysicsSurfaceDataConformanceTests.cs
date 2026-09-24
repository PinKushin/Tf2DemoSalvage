using System.Linq;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// vphysics' surface-properties parser — <c>FUN_180018740</c>, its key parser <c>FUN_18002e6c0</c> and its tokenizer
/// <c>FUN_180003640</c> — against what the shipped binary's own props object held after the same texts (B369).
/// </summary>
/// <remarks>
/// **Every expected surface was printed by `vphysics-materials parse edges`**, which handed these eight texts, in this order, to
/// the loaded library's global props object through its slot 1 and read each surface back through slots 7 and 9. The same probe's
/// `parse` mode runs the game's three manifest files first: 83, 93 and 100 surfaces, every name and parameter agreeing.
/// </remarks>
public sealed class VphysicsSurfaceDataConformanceTests
{
    private static readonly string[] Texts =
    [
        "\"Edge_One\" { \"friction\" \"0.25\" // a comment\n \"elasticity\" \"2.5e-1xyz\" }",
        "edge_two { friction .5 base edge_one density 7 /* a block */ dampening -3 }",
        "\"edge_one\" { \"thickness\" \"4\" }",
        "edge_three { friction(0.3) elasticity: 0.6 }",
        "\"$material_index_shadow\" { \"elasticity\" \"0.75\" }",
        "unwanted value edge_four { \"friction\" \"inf\" \"elasticity\" \"nan\" \"density\" \"+1e3\" }",
        "edge_five { \"friction\" \"0.9\"",
        "café_six { friction 0.4 } \"edge_seven\" { friction 0.45 }",
    ];

    /// <remarks>
    /// **What each text proves**, in the binary's own answer: names are lowercased and a comment and trailing letters are skipped
    /// (`edge_one`); `base` overwrites the keys before it, copying the base as it stood then (`edge_two` has `edge_one`'s friction
    /// but not the thickness a later text gave it); a redefinition keeps what it does not name; `(`, `)` and `:` are tokens of
    /// their own, so `edge_three` swallows its `}` as a value and the text ends inside the block, which drops it; the shadow surface
    /// is made after the first text and reached by name; a pair before a block is skipped, and `inf` and `nan` are the C runtime's;
    /// a text ending inside its block adds nothing; and a byte past `0x7f` outside quotes separates tokens.
    /// </remarks>
    [Test]
    public void ParseSurfaceData_TheEdgeTextsInOrder_LeaveTheBinarysSurfaces()
    {
        VphysicsSurfaceProps props = new([]);

        foreach (string text in Texts)
        {
            props.ParseSurfaceData(Encoding.Latin1.GetBytes(text));
        }

        props.ShadowSurface.ShouldBe(1);
        props.Surfaces.Select(surface => surface.Name).ShouldBe(["edge_one", "$MATERIAL_INDEX_SHADOW", "edge_two", "edge_four", "edge_seven"]);
        Bits(props.Surfaces[0]).ShouldBe([0x3e800000, 0x3e800000, 0x00000000, 0x40800000, 0x00000000]);
        Bits(props.Surfaces[1]).ShouldBe([0x3f4ccccd, 0x3f400000, 0x00000000, 0x00000000, 0x00000000]);
        Bits(props.Surfaces[2]).ShouldBe([0x3e800000, 0x3e800000, 0x40e00000, 0x00000000, unchecked((int)0xc0400000)]);
        Bits(props.Surfaces[3]).ShouldBe([0x7f800000, 0x7fffffff, 0x447a0000, 0x00000000, 0x00000000]);
        Bits(props.Surfaces[4]).ShouldBe([0x3ee66666, 0x00000000, 0x00000000, 0x00000000, 0x00000000]);
    }

    /// <remarks>
    /// `gamematerial` (`FUN_180018740`): one non-digit character is uppercased — the key parser lowercased it — and
    /// anything else is `atoi`'d. `base` copies it with the physics, and a block that never sets it keeps what it
    /// started from, which for a new name is <c>default</c>'s.
    /// </remarks>
    [Test]
    public void ParseSurfaceData_GameMaterial_IsUppercasedNumberedAndInherited()
    {
        VphysicsSurfaceProps props = new([]);

        props.ParseSurfaceData(Encoding.Latin1.GetBytes(
            "\"default\" { \"gamematerial\" \"c\" } " +
            "\"metal\" { \"gamematerial\" \"M\" } " +
            "\"odd\" { \"gamematerial\" \"77\" } " +
            "\"grate\" { \"base\" \"metal\" } " +
            "\"plain\" { \"friction\" \"0.5\" }"));

        int Game(string name) => props.Surfaces.Single(surface => surface.Name == name).GameMaterial;

        Game("default").ShouldBe('C');
        Game("metal").ShouldBe('M');
        Game("odd").ShouldBe(77);
        Game("grate").ShouldBe('M');
        Game("plain").ShouldBe('C');
    }

    [Test]
    public void ParseSurfaceData_BulletImpact_IsReadAndInherited()
    {
        // `bulletimpact` names the sound `PlayImpactSound` plays; `base` copies it, and a block that never sets it keeps
        // what it started from.
        VphysicsSurfaceProps props = new([]);

        props.ParseSurfaceData(Encoding.Latin1.GetBytes(
            "\"default\" { \"bulletimpact\" \"Default.BulletImpact\" } " +
            "\"metal\" { \"bulletimpact\" \"Metal.BulletImpact\" } " +
            "\"grate\" { \"base\" \"metal\" } " +
            "\"plain\" { \"friction\" \"0.5\" }"));

        string? Sound(string name) => props.Surfaces.Single(surface => surface.Name == name).BulletImpactSound;

        // Lowercased as every value `ParseKeyValue` reads; sound scripts are looked up without case.
        Sound("metal").ShouldBe("metal.bulletimpact");
        Sound("grate").ShouldBe("metal.bulletimpact");
        Sound("plain").ShouldBe("default.bulletimpact");
    }

    /// <remarks>
    /// `physicssound::PlayImpactSounds` (`vphysics_sound.h:40`) picks `impacthard` or `impactsoft` by the struck surface's
    /// `audiohardnessfactor` against this one's `impacthardthreshold`, and by `audiohardminvelocity`.
    /// </remarks>
    [Test]
    public void ParseSurfaceData_ImpactSoundsAndAudio_AreReadAndInherited()
    {
        VphysicsSurfaceProps props = new([]);

        props.ParseSurfaceData(Encoding.Latin1.GetBytes(
            "\"default\" { \"impacthard\" \"Default.ImpactHard\" \"impactsoft\" \"Default.ImpactSoft\" " +
            "\"audiohardnessfactor\" \"1.0\" \"impactHardThreshold\" \"0.5\" \"audioHardMinVelocity\" \"0\" } " +
            "\"flesh\" { \"impacthard\" \"Flesh.ImpactHard\" \"audiohardnessfactor\" \"0.25\" \"audioHardMinVelocity\" \"500\" }"));

        VphysicsSurface flesh = props.Surfaces.Single(surface => surface.Name == "flesh");

        flesh.Sounds.ImpactHard.ShouldBe("flesh.impacthard");
        flesh.Sounds.ImpactSoft.ShouldBe("default.impactsoft");
        flesh.Audio.ShouldBe(new SurfaceAudio(HardnessFactor: 0.25f, HardThreshold: 0.5f, HardVelocityThreshold: 500f));
    }

    [Test]
    public void ParseSurfaceData_StepSounds_AreReadAndInherited()
    {
        // `stepleft` and `stepright` name what `PlayStepSound` plays for each foot (`baseplayer_shared.cpp`); inherited
        // through `base` like every other sound name.
        VphysicsSurfaceProps props = new([]);

        props.ParseSurfaceData(Encoding.Latin1.GetBytes(
            "\"default\" { \"stepleft\" \"Default.StepLeft\" \"steprIght\" \"Default.StepRight\" } " +
            "\"metal\" { \"stepleft\" \"SolidMetal.StepLeft\" \"stepright\" \"SolidMetal.StepRight\" } " +
            "\"grate\" { \"base\" \"metal\" } " +
            "\"plain\" { \"friction\" \"0.5\" }"));

        SurfaceSoundNames Sounds(string name) => props.Surfaces.Single(surface => surface.Name == name).Sounds;

        Sounds("grate").ShouldBe(new SurfaceSoundNames("solidmetal.stepleft", "solidmetal.stepright", null));
        Sounds("plain").ShouldBe(new SurfaceSoundNames("default.stepleft", "default.stepright", null));
        Sounds("plain").ImpactHard.ShouldBeNull();
    }

    private static int[] Bits(VphysicsSurface surface) =>
    [
        System.BitConverter.SingleToInt32Bits(surface.Physics.Friction),
        System.BitConverter.SingleToInt32Bits(surface.Physics.Elasticity),
        System.BitConverter.SingleToInt32Bits(surface.Physics.Density),
        System.BitConverter.SingleToInt32Bits(surface.Physics.Thickness),
        System.BitConverter.SingleToInt32Bits(surface.Physics.Dampening),
    ];
}
