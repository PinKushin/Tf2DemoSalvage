using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Which pass Valve's <c>Water</c> shader draws — <c>stdshaders/water.cpp:535</c>.
/// </summary>
/// <remarks>
/// **Written off the SDK before anything implemented it** (B62). The shader is published, so this is
/// transcription rather than a guess about a closed system — which matters here because the wrong
/// guess is what this project already made: a `Water` material declares no <c>$basetexture</c>, and
/// the viewer drew the magenta chequer for it. The owner, on `cp_fulgur`: *"the real tf2 doesnt show
/// the purple and black texture anywhere on this map"*.
///
/// **The decision, verbatim:**
///
/// <code>
///   bool bForceCheap = (params[FORCECHEAP]->GetIntValue() != 0) || UsingEditor( params );
///   if ( bForceCheap )  bForceExpensive = false;
///   else                bForceExpensive = bForceExpensive || (params[FORCEEXPENSIVE]->GetIntValue() != 0);
///
///   bool bRefraction = params[REFRACTTEXTURE]->IsTexture();
///   bool bReflection = bForceExpensive &amp;&amp; params[REFLECTTEXTURE]->IsTexture();
///
///   if ( !bForceCheap &amp;&amp; ( bReflection || bRefraction ) )
///       DrawReflectionRefraction( ... );
///
///   if ( !bReflection &amp;&amp; params[ENVMAP]->IsTexture() &amp;&amp; !IS_FLAG_SET( MATERIAL_VAR_DECAL ) )
///       DrawCheapWater( ... );
///
///   if ( !bDrewSomething )
///       Draw();   // "We are likely here because of the tools. . . draw something so that
///                 //  we won't go into wireframe-land."
/// </code>
///
/// **Three things about it are easy to get wrong and are each pinned below.**
///
/// 1. **Both passes can run.** They are separate `if`s, not a chain — expensive water with an envmap
///    draws the cheap pass over it. A reading as if-else silently drops one.
/// 2. **`bReflection` requires `bForceExpensive`**, not merely the texture. On anything but the
///    X360 a reflection texture alone is not enough.
/// 3. **`Draw()` is Valve's own fallback**, with its own comment, for a material where neither
///    applies. So "draw something plain" is the engine's answer rather than an invention — which is
///    what the chequer was.
///
/// **This is the decision only.** It says which pass, not how to shade it, and it is deliberately
/// separable: the path a material takes is a fact about the material, and can be tested with no
/// renderer at all.
/// </remarks>
public sealed class WaterShaderConformanceTests
{
    [Test]
    public void Pass_WithNeitherAnEnvmapNorRefraction_IsThePlainFallback()
    {
        // `cp_fulgur`'s `water/water_well_beneath`, if its refraction texture is absent: no envmap
        // (the shipped VMT comments it out — "bottom materials shouldn't use $envmap!!!"), so the
        // cheap pass is skipped and `Draw()` is what is left.
        WaterPass pass = WaterShader.Pass(
            hasRefraction: false, hasReflection: false, hasEnvmap: false,
            forceCheap: false, forceExpensive: false, isDecal: false);

        pass.ShouldBe(WaterPass.Plain);
    }

    [Test]
    public void Pass_WithARefractionTexture_IsReflectionRefraction()
    {
        // What the shipped `water_well_beneath` actually gets: `$refracttexture
        // "_rt_WaterRefraction"` is set, so `bRefraction` is true and the expensive pass draws —
        // and with no envmap the cheap pass does not.
        WaterPass pass = WaterShader.Pass(
            hasRefraction: true, hasReflection: false, hasEnvmap: false,
            forceCheap: false, forceExpensive: false, isDecal: false);

        pass.ShouldBe(WaterPass.ReflectionRefraction);
    }

    [Test]
    public void Pass_WithRefractionAndAnEnvmap_DrawsBOTH()
    {
        // **The `if`s are separate, not a chain.** This is the case a reading as if-else gets wrong,
        // and it is the common one for above-water materials.
        WaterPass pass = WaterShader.Pass(
            hasRefraction: true, hasReflection: false, hasEnvmap: true,
            forceCheap: false, forceExpensive: false, isDecal: false);

        pass.ShouldBe(WaterPass.ReflectionRefraction | WaterPass.Cheap);
    }

    [Test]
    public void Pass_WithAnEnvmapAlone_IsCheapWater()
    {
        WaterShader.Pass(
            hasRefraction: false, hasReflection: false, hasEnvmap: true,
            forceCheap: false, forceExpensive: false, isDecal: false)
            .ShouldBe(WaterPass.Cheap);
    }

    [Test]
    public void Pass_ForcedCheap_SkipsTheExpensivePassEvenWithRefraction()
    {
        // `if ( !bForceCheap && ( bReflection || bRefraction ) )`. Without the envmap that leaves
        // nothing, so the fallback answers — which is the engine's behaviour and not a hole.
        WaterShader.Pass(
            hasRefraction: true, hasReflection: false, hasEnvmap: false,
            forceCheap: true, forceExpensive: false, isDecal: false)
            .ShouldBe(WaterPass.Plain);

        WaterShader.Pass(
            hasRefraction: true, hasReflection: false, hasEnvmap: true,
            forceCheap: true, forceExpensive: false, isDecal: false)
            .ShouldBe(WaterPass.Cheap);
    }

    [Test]
    public void Pass_AReflectionTexture_CountsOnlyWhenExpensiveIsForced()
    {
        // `bool bReflection = bForceExpensive && params[REFLECTTEXTURE]->IsTexture();` — the
        // texture alone is not enough off the X360. Without `$forceexpensive` this material has
        // neither pass and falls back.
        WaterShader.Pass(
            hasRefraction: false, hasReflection: true, hasEnvmap: false,
            forceCheap: false, forceExpensive: false, isDecal: false)
            .ShouldBe(WaterPass.Plain);

        WaterShader.Pass(
            hasRefraction: false, hasReflection: true, hasEnvmap: false,
            forceCheap: false, forceExpensive: true, isDecal: false)
            .ShouldBe(WaterPass.ReflectionRefraction);
    }

    [Test]
    public void Pass_WithAReflection_SuppressesTheCheapPassEvenWithAnEnvmap()
    {
        // `if( !bReflection && params[ENVMAP]->IsTexture() ... )`. Reflection and cheap water are
        // two ways of drawing the same thing, so the engine never does both.
        WaterShader.Pass(
            hasRefraction: false, hasReflection: true, hasEnvmap: true,
            forceCheap: false, forceExpensive: true, isDecal: false)
            .ShouldBe(WaterPass.ReflectionRefraction);
    }

    [Test]
    public void Pass_ADecal_NeverDrawsCheapWater()
    {
        // Valve's comment: "Use $decal to see if we are a decal or not. . if we are, then don't
        // bother drawing the cheap version for now since we don't have access to env_cubemap".
        WaterShader.Pass(
            hasRefraction: false, hasReflection: false, hasEnvmap: true,
            forceCheap: false, forceExpensive: false, isDecal: true)
            .ShouldBe(WaterPass.Plain);
    }

    [Test]
    public void Pass_IsNeverNothing()
    {
        // **The property that matters most for B62**, stated once over the whole input space rather
        // than case by case: a `Water` material always draws SOMETHING. The magenta chequer was this
        // project answering "nothing drawable here" for a material the engine has never failed to
        // draw, and no combination of these six flags produces that answer.
        foreach (bool refraction in new[] { false, true })
        {
            foreach (bool reflection in new[] { false, true })
            {
                foreach (bool envmap in new[] { false, true })
                {
                    foreach (bool cheap in new[] { false, true })
                    {
                        foreach (bool expensive in new[] { false, true })
                        {
                            foreach (bool decal in new[] { false, true })
                            {
                                WaterShader.Pass(refraction, reflection, envmap, cheap, expensive, decal)
                                    .ShouldNotBe(
                                        WaterPass.None,
                                        $"refraction {refraction}, reflection {reflection}, envmap "
                                        + $"{envmap}, cheap {cheap}, expensive {expensive}, decal {decal}");
                            }
                        }
                    }
                }
            }
        }
    }
}
