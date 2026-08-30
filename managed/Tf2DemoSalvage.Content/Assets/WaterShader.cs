using System;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>Which passes Valve's <c>Water</c> shader draws for a material.</summary>
/// <remarks>
/// **Flags, because the engine's two passes are separate `if`s and both can run.** Reading
/// `water.cpp:560-576` as a chain drops one of them, which is the mistake this type exists to make
/// impossible at the call site.
/// </remarks>
[Flags]
public enum WaterPass
{
    /// <summary>Nothing — which the engine never produces; see <see cref="WaterShader.Pass"/>.</summary>
    None = 0,

    /// <summary><c>DrawReflectionRefraction</c>: the expensive pass, against render targets.</summary>
    ReflectionRefraction = 1,

    /// <summary><c>DrawCheapWater</c>: the cubemap and the normal map, with a fresnel term.</summary>
    Cheap = 2,

    /// <summary><c>Draw()</c> — Valve's own fallback when neither of the others applies.</summary>
    Plain = 4,
}

/// <summary>
/// Valve's <c>Water</c> shader, as far as deciding what to draw.
/// </summary>
/// <remarks>
/// **<c>stdshaders/water.cpp:535</c>, transcribed** (B62, D121). The shader is published, so none of
/// this is inferred.
///
/// **Why it exists at all: a `Water` material declares no <c>$basetexture</c>, and this project drew
/// the missing-material chequer for it.** Water refracts against `_rt_WaterRefraction` and takes its
/// surface from a normal map; there is no base texture and there is not meant to be, so
/// `IsErrorMaterial` is false and the engine has never failed to draw one. The owner found the
/// chequer on `cp_fulgur` and said the real game shows none — which is what sent this to the SDK.
///
/// **Deliberately the DECISION only, with no renderer in sight.** Which pass a material takes is a
/// fact about its parameters, so it can be pinned exactly against the SDK by a test that needs no
/// device, no map and no demo. What each pass then draws is a separate job.
/// </remarks>
public static class WaterShader
{
    /// <summary>Which passes the engine would draw for a water material.</summary>
    /// <param name="hasRefraction"><c>$refracttexture</c> resolves to a texture.</param>
    /// <param name="hasReflection"><c>$reflecttexture</c> resolves to a texture.</param>
    /// <param name="hasEnvmap"><c>$envmap</c> resolves to a texture.</param>
    /// <param name="forceCheap"><c>$forcecheap</c>, or the material is being viewed in the editor.</param>
    /// <param name="forceExpensive"><c>$forceexpensive</c>, or <c>r_waterforceexpensive</c>.</param>
    /// <param name="isDecal"><c>MATERIAL_VAR_DECAL</c>.</param>
    /// <returns>The passes, which is never <see cref="WaterPass.None"/>.</returns>
    /// <remarks>
    /// **`bForceCheap` wins over `bForceExpensive`**, and the engine asserts they are never both
    /// set — so the order of these two lines is load-bearing rather than incidental.
    ///
    /// **`bReflection` needs `bForceExpensive` as well as the texture** on everything but the X360,
    /// which is the platform branch this follows: a reflection texture alone does not enable the
    /// reflection.
    /// </remarks>
    public static WaterPass Pass(
        bool hasRefraction,
        bool hasReflection,
        bool hasEnvmap,
        bool forceCheap,
        bool forceExpensive,
        bool isDecal)
    {
        // if ( bForceCheap ) bForceExpensive = false;
        // else               bForceExpensive = bForceExpensive || $forceexpensive;
        bool expensive = !forceCheap && forceExpensive;

        // On anything but the X360 the reflection needs the force as well as the texture.
        bool reflection = expensive && hasReflection;

        WaterPass passes = WaterPass.None;

        if (!forceCheap && (reflection || hasRefraction))
        {
            passes |= WaterPass.ReflectionRefraction;
        }

        // **A separate `if`, not an else.** Expensive water with a cubemap draws both, and Valve
        // says so in a note above the function wishing it were one pass: "fit the cheap water stuff
        // into the water shader so that we don't have to do 2 passes."
        //
        // The decal exclusion is Valve's, with its reason: "if we are, then don't bother drawing the
        // cheap version for now since we don't have access to env_cubemap".
        if (!reflection && hasEnvmap && !isDecal)
        {
            passes |= WaterPass.Cheap;
        }

        // "We are likely here because of the tools. . . draw something so that we won't go into
        // wireframe-land." — and it is the whole answer to B62: the engine's response to a water
        // material it cannot shade is to draw it plainly, never to mark it missing.
        return passes == WaterPass.None ? WaterPass.Plain : passes;
    }
}
