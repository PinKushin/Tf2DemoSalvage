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

    /// <remarks>
    /// **Absent is FALSE, whatever the declaration's third argument says** (B521). <c>SHADER_PARAM( IGNOREVERTEXCOLORS,
    /// SHADER_PARAM_TYPE_BOOL, "1", … )</c> (`sprite_dx9.cpp:39`) names a default the material system never applies:
    /// the string is only what <c>GetParamDefault</c> returns to a tool. What a parameter nobody set holds is decided by
    /// <c>CShaderSystem::InitShaderParameters</c> — *read in disassembly*, `materialsystem.dll` `0x18004fe52`-`0x18004ff40`:
    /// for each parameter still undefined after the shader's own <c>InitShaderParams</c>, a switch on its type whose
    /// <c>BOOL</c> and <c>INTEGER</c> cases both land on <c>SetIntValue( 0 )</c> (jump table `0x18004ff74`, entries 1 and
    /// 8 to `0x18004fe9d`), and nothing in the loop calls for the default string.
    ///
    /// **The published file says the same thing twice** — *read from published source*: <c>HDRCOLORSCALE</c> is declared
    /// <c>"1.0"</c> and <c>NOSRGB</c> <c>"0"</c>, and <c>SHADER_INIT_PARAMS</c> still sets the first to 1 by hand and the
    /// second to ONE, under "Disable sRGB reads and writes by default" (`:58-67`). A declared default that applied would
    /// make the first redundant and the second wrong.
    ///
    /// **The wrong turn, kept**: B391 read the declaration as the default, so every additive sprite and every beam shaft
    /// drew its texture at full strength — a spotlight's 64-of-255 brightness, its shade-out and its axis fade all
    /// multiplied into a vertex colour the shader was believed to drop. `glow_test02` and `light_glow03` name no
    /// `$ignorevertexcolors`, and nor do most sprites.
    /// </remarks>
    [Test]
    public void IgnoresVertexColors_WhenTheMaterialDeclaresNone_IsFalse()
    {
        Sprite(string.Empty).IgnoresVertexColors.ShouldBeFalse();
    }

    /// <remarks>
    /// **Nothing in the shader initialises it either**: the name appears three times in `sprite_dx9.cpp` — its
    /// declaration and the two additive branches that read it — so the material system's zero is what those branches see.
    /// </remarks>
    [Test]
    public void IgnoreVertexColors_InTheSpriteShaderSource_IsDeclaredAndReadButNeverSet()
    {
        SourceSdk.Require();

        string shader = SourceSdk.Text("src/materialsystem/stdshaders/sprite_dx9.cpp")
            ?? throw new System.InvalidOperationException("sprite_dx9.cpp is missing from the SDK");

        System.Text.RegularExpressions.Regex.Count(shader, "IGNOREVERTEXCOLORS").ShouldBe(3);
        shader.ShouldContain("if( !params[ IGNOREVERTEXCOLORS ]->GetIntValue() )");
    }

    [Test]
    public void IgnoresVertexColors_SetToOne_IsTrue()
    {
        Sprite("\"$ignorevertexcolors\" \"1\"").IgnoresVertexColors.ShouldBeTrue();
    }

    [Test]
    public void IgnoresVertexColors_SetToZero_IsFalse()
    {
        Sprite("\"$ignorevertexcolors\" \"0\"").IgnoresVertexColors.ShouldBeFalse();
    }

    /// <remarks>
    /// **A Sprite material works in GAMMA space unless it says otherwise** (B521) — *read from published source*:
    /// <c>if ( !params[NOSRGB]-&gt;IsDefined() ) params[NOSRGB]-&gt;SetIntValue( 1 )</c>, under "Disable sRGB reads and
    /// writes by default" (`sprite_dx9.cpp:63-67`). This default IS applied, because the shader sets it itself — the
    /// opposite of <c>$ignorevertexcolors</c> above, whose declared "1" nobody applies. With it set the texture is
    /// bound with <c>EnableSRGBRead( false )</c>, the vertex colour is not converted (<c>sprite_vs20.fxc:52-56</c>),
    /// the pixel shader scales by <c>TONEMAP_SCALE_GAMMA</c>, and the write is <c>EnableSRGBWrite( false )</c>: the
    /// stored texel is added to the stored frame. Neither lamp material names <c>$nosrgb</c>.
    /// </remarks>
    [Test]
    public void SpriteNoSrgb_WhenTheMaterialDeclaresNone_IsTrue()
    {
        Sprite(string.Empty).SpriteNoSrgb.ShouldBeTrue();
    }

    /// <remarks>The control: a material that asks for sRGB gets the linear path the viewer always drew.</remarks>
    [TestCase("0", false)]
    [TestCase("1", true)]
    public void SpriteNoSrgb_WhenTheMaterialSetsIt_IsWhatItSays(string value, bool expected)
    {
        Sprite($"\"$nosrgb\" \"{value}\"").SpriteNoSrgb.ShouldBe(expected);
    }

    /// <remarks>
    /// **The four lines the gamma path rests on, as the published shader has them.** A reading of the SDK that stops
    /// being true of the file should fail here rather than in a picture.
    /// </remarks>
    [Test]
    public void SpriteShaderSource_ForANoSrgbMaterial_ReadsAndWritesStoredValuesAtTheGammaScale()
    {
        SourceSdk.Require();

        string shader = SourceSdk.Text("src/materialsystem/stdshaders/sprite_dx9.cpp")
            ?? throw new System.InvalidOperationException("sprite_dx9.cpp is missing from the SDK");
        string pixel = SourceSdk.Text("src/materialsystem/stdshaders/sprite_ps2x.fxc")
            ?? throw new System.InvalidOperationException("sprite_ps2x.fxc is missing from the SDK");

        shader.ShouldContain("params[NOSRGB]->SetIntValue( 1 );");
        shader.ShouldContain("pShaderShadow->EnableSRGBRead( SHADER_SAMPLER0, bSRGB );");
        shader.ShouldContain("s_pShaderShadow->EnableSRGBWrite( bSRGB || ( IsOSX() && !g_pHardwareConfig->FakeSRGBWrite() ) );");
        pixel.ShouldContain("result = FinalOutput( sample, fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_GAMMA );");
    }

    /// <remarks>
    /// **Both halves of `SetPixelShaderConstant( 0, COLOR, ALPHA )` at once** (`BaseVSShader.cpp:69-78`):
    /// a scalar color is broadcast — <c>val[0] = val[1] = val[2] = pPixelVar-&gt;GetFloatValue()</c> — and
    /// the alpha is copied with no clamp, so two stays two. The lit shaders' modulation clamps it to
    /// one; reading this through that would give one.
    /// </remarks>
    [Test]
    public void SpriteConstantColor_ForAScalarColorAndAnAlphaAboveOne_BroadcastsAndDoesNotClamp()
    {
        Sprite("\"$color\" \"0.5\"\n\"$alpha\" \"2\"").SpriteConstantColor.ShouldBe((0.5f, 0.5f, 0.5f, 2f));
    }

    /// <remarks>
    /// **Opaque white by default**: <c>$color</c> starts white, and `SHADER_INIT_PARAMS` sets an
    /// undefined <c>$alpha</c> to one (`sprite_dx9.cpp:53-56`) — so a material naming neither adds its
    /// texture unchanged.
    /// </remarks>
    [Test]
    public void SpriteConstantColor_WhenTheMaterialDeclaresNone_IsOpaqueWhite()
    {
        Sprite(string.Empty).SpriteConstantColor.ShouldBe((1f, 1f, 1f, 1f));
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
