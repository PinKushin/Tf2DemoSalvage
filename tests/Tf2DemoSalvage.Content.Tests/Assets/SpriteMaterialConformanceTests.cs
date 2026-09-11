using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What a sprite's MATERIAL tells the engine about its quad — <c>$spriteorientation</c> and
/// <c>$spriteorigin</c> (B390).
/// </summary>
/// <remarks>
/// **Written from the engine before this project read either key.** 221 shipped materials declare the
/// first and 202 the second, measured with `vmt-param` against 30,684 materials; B378 had asserted that
/// no TF2 sprite declared either, and `light_glow03` — the lamp halo that is most of what `env_sprite`
/// draws — declares both.
///
/// **Two layers, tested where each lives.** The orientation is translated by the `Sprite` SHADER at
/// material init (`materialsystem/stdshaders/sprite_dx9.cpp:73`); the origin is read afterwards by
/// `CEngineSprite::Init` (`game/client/spritemodel.cpp:311`). Both reach this reader as the material's
/// own text.
/// </remarks>
public sealed class SpriteMaterialConformanceTests
{
    /// <remarks>
    /// **Every name the shader knows, with the engine's own number for it.** These are the CONTROL for
    /// every test below that expects the default: a reader that ignored the key entirely would pass
    /// those, and fail four of these five.
    /// </remarks>
    [TestCase("parallel_upright", SpriteOrientation.ParallelUpright)]
    [TestCase("facing_upright", SpriteOrientation.FacingUpright)]
    [TestCase("vp_parallel", SpriteOrientation.Parallel)]
    [TestCase("oriented", SpriteOrientation.Oriented)]
    [TestCase("vp_parallel_oriented", SpriteOrientation.ParallelOriented)]
    public void SpriteOrientation_ForEachNameTheShaderKnows_IsItsEnumeration(
        string name, SpriteOrientation expected)
    {
        Sprite($"\"$spriteorientation\" \"{name}\"").SpriteOrientation.ShouldBe(expected);
    }

    /// <remarks>
    /// <c>stricmp</c>, so case is ignored. `light_glow03` writes `vp_parallel` in lower case, and a
    /// case-sensitive reader would agree with the engine on it and on nothing written any other way.
    /// </remarks>
    [Test]
    public void SpriteOrientation_NamedInMixedCase_IsStillTranslated()
    {
        Sprite("\"$spriteorientation\" \"VP_Parallel\"").SpriteOrientation
            .ShouldBe(SpriteOrientation.Parallel);
    }

    /// <remarks>
    /// **The trap, and nineteen shipped materials write it.** The parameter is declared
    /// <c>SHADER_PARAM_TYPE_INTEGER</c>, which invites reading `"4"` as four — but the shader compares
    /// the value as a STRING against five names, `"4"` is none of them, and the <c>else</c> takes it:
    /// <c>Warning( "error with $spriteOrientation\n" );</c> then <c>SPR_VP_PARALLEL_UPRIGHT</c>
    /// (`sprite_dx9.cpp:99`). A reader that trusted the declared type would answer
    /// <see cref="SpriteOrientation.ParallelOriented"/>.
    /// </remarks>
    [Test]
    public void SpriteOrientation_WrittenAsANumber_IsRejectedToTheDefault()
    {
        Sprite("\"$spriteorientation\" \"4\"").SpriteOrientation
            .ShouldBe(SpriteOrientation.ParallelUpright);
    }

    [Test]
    public void SpriteOrientation_ForANameTheShaderDoesNotKnow_IsTheDefault()
    {
        Sprite("\"$spriteorientation\" \"sideways\"").SpriteOrientation
            .ShouldBe(SpriteOrientation.ParallelUpright);
    }

    /// <remarks><c>// default case</c> — `sprite_dx9.cpp:105`.</remarks>
    [Test]
    public void SpriteOrientation_WhenTheMaterialDeclaresNone_IsTheDefault()
    {
        Sprite(string.Empty).SpriteOrientation.ShouldBe(SpriteOrientation.ParallelUpright);
    }

    /// <remarks>
    /// **Only the `Sprite` shader translates the name.** Under any other shader the variable reaches
    /// `CEngineSprite::Init` untranslated and is read with <c>GetIntValue()</c> (`spritemodel.cpp:309`),
    /// so `"4"` IS four there — the opposite of the answer under `Sprite`. What `GetIntValue` makes of a
    /// string belongs to the closed material system; <c>atoi</c> is this project's reading of it
    /// everywhere, which makes this expectation an INTERPOLATION. The nineteen shipped materials that
    /// declare the key under another shader are PASSTIME's HUD icons.
    /// </remarks>
    [Test]
    public void SpriteOrientation_UnderAShaderThatIsNotSprite_IsTheRawInteger()
    {
        VmtMaterial.Parse(Encoding.UTF8.GetBytes("""
            "UnlitGeneric"
            {
                "$basetexture" "passtime/hud/passtime_teamlogo_red"
                "$spriteorientation" "4"
            }
            """)).SpriteOrientation.ShouldBe(SpriteOrientation.ParallelOriented);
    }

    /// <remarks>
    /// **Three shipped materials write exactly this**, and it is the value that makes the key matter: the
    /// quad's top edge sits ON the sprite's origin instead of half its height above it.
    /// </remarks>
    [Test]
    public void SpriteOrigin_AtTheTopEdge_IsReadAsWritten()
    {
        Sprite("\"$spriteorigin\" \"[ 0.50 0.00 ]\"").SpriteOrigin.ShouldBe((0.5f, 0f));
    }

    /// <remarks>
    /// Valve's own spelling in one shipped material, and the form `$envmaptint` already taught this
    /// reader to accept: no leading zero.
    /// </remarks>
    [Test]
    public void SpriteOrigin_WrittenWithoutLeadingZeros_KeepsItsValue()
    {
        Sprite("\"$spriteorigin\" \"[ .25 .75 ]\"").SpriteOrigin.ShouldBe((0.25f, 0.75f));
    }

    /// <remarks>
    /// **Absent is not a vector of zeroes.** `CEngineSprite::Init` tests <c>!originVar</c> and centres the
    /// quad, so this reader answers "none" and the centring is the sprite's to apply — answering
    /// <c>(0, 0)</c> here would hang every sprite that omits the key from its top-left corner.
    /// </remarks>
    [Test]
    public void SpriteOrigin_WhenTheMaterialDeclaresNone_IsAbsent()
    {
        Sprite(string.Empty).SpriteOrigin.ShouldBeNull();
    }

    /// <remarks>
    /// **The type test is load-bearing:** <c>if( !originVar || ( originVar-&gt;GetType() !=
    /// MATERIAL_VAR_TYPE_VECTOR ) )</c> takes the default (`spritemodel.cpp:313`). A value written without
    /// brackets is a FLOAT variable, not a vector — the same switch <c>ColorVarsToVector</c> makes for a
    /// colour — so the engine ignores it rather than reading its one component.
    /// </remarks>
    [Test]
    public void SpriteOrigin_WrittenWithoutBrackets_IsNotAVectorAndIsIgnored()
    {
        Sprite("\"$spriteorigin\" \"0.5\"").SpriteOrigin.ShouldBeNull();
    }

    /// <summary>A `Sprite` material carrying the given lines, as `light_glow03` is written.</summary>
    private static VmtMaterial Sprite(string parameters) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes($$"""
            "Sprite"
            {
                "$basetexture" "sprites/light_glow03"
                {{parameters}}
            }
            """));
}
