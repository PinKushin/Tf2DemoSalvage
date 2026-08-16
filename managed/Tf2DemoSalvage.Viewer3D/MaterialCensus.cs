using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Counts what a map's materials ask for that this renderer does not do.
/// </summary>
/// <remarks>
/// **Written because the log went silent while a control point drew as a black disc.** Every
/// material on cp_process resolved successfully, so nothing was reported — the viewer logs what
/// fails to LOAD and never what a surface resolved TO. The gap that mattered, 43 of 189 materials
/// declaring <c>$envmap</c> (B55), took an hour of throwaway probes to find and is one line here.
///
/// **A report built only from failures reads clean while every instance quietly falls back.** That
/// is the same finding as <c>measure-the-output-not-the-capability</c>, and this is the general
/// fix for it: state what was asked for, not only what went wrong.
///
/// The three sets below are the whole design. Anything not named in either list is unimplemented
/// and gets counted, so a parameter this project has never heard of appears the first time a map
/// uses it rather than being silently lumped in with the ones deliberately skipped.
/// </remarks>
internal static class MaterialCensus
{
    /// <summary>Parameters the renderer actually reads.</summary>
    /// <remarks>
    /// Kept beside the code that reads them: every entry here is consumed in <c>MapAssets</c>,
    /// <c>VmtMaterial</c> or <c>WorldRenderer</c>. Adding a feature means moving its parameter into
    /// this set, which is a one-line change that makes the census stop reporting it.
    /// </remarks>
    private static readonly HashSet<string> Implemented = new(StringComparer.OrdinalIgnoreCase)
    {
        "$basetexture",
        "$basetexture2",

        // Implemented together with UnLitTwoTexture and the Modulate blend: the second texture is
        // decoded and multiplied, $nocull turns culling off for its material, and $mod2x selects
        // the doubled modulate factors. Moved here the moment they were consumed, which is what
        // keeps this census worth reading.
        //
        // **$modblend and $decalscale were listed here and read by NOTHING**, which is the failure
        // this list can have that no other test would catch: a census stops reporting a name the
        // moment it appears here, so a wrong entry silences the very report meant to find it. Both
        // were caught by cross-checking this set against the SDK's own declarations, which also
        // showed that neither appears in source-sdk-2013 at all — $modblend is in TF2's shipped
        // VMTs and in no published shader, so what the engine does with it is not knowable from
        // the SDK.
        "$texture2",
        "$nocull",
        "$mod2x",
        "$halflambert",
        "$bumpmap",
        "$ssbump",
        "$detail",
        "$detailscale",
        "$detailblendfactor",
        "$detailblendmode",
        "$detailtint",
        "$translucent",
        "$alphatest",
        "$additive",
        "$selfillum",
        "$selfillumtint",
        "$decal",
        "%compilenodraw",
        "include",
    };

    /// <summary>Parameters with no bearing on how a surface is drawn.</summary>
    /// <remarks>
    /// **Not "unimplemented" in any sense worth a line of log** — these are not ours to implement.
    /// <c>$surfaceprop</c> picks a footstep sound, <c>%keywords</c> is a search tag for Hammer, and
    /// the compile flags are instructions to vbsp that were obeyed before the map shipped.
    ///
    /// Separated rather than folded into <see cref="Implemented"/> because the two mean different
    /// things, and a later reader deciding whether a parameter is safe to ignore needs to know
    /// which list it was put in and why.
    /// </remarks>
    private static readonly HashSet<string> NoRenderingEffect = new(StringComparer.OrdinalIgnoreCase)
    {
        "$surfaceprop",
        "$surfaceprop2",
        "%keywords",
        "%compiletrigger",
        "%compileclip",
        "%compileplayerclip",
        "%compilesky",
        "%compilehint",
        "%compileskip",
        "%tooltexture",
        "%compilenonsolid",
        "%compilepassbullets",
        "%compileladder",
        "%compilewater",
        "%compileorigin",
    };

    /// <summary>Which SHADERS the map's materials name that this project does not implement.</summary>
    /// <param name="shaders">Each material's shader name.</param>
    /// <returns>Each unimplemented shader with how many materials use it, commonest first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shaders"/> is null.</exception>
    /// <remarks>
    /// **The census counted parameters and never the shader, which is a declaration in itself.**
    /// <c>Modulate</c> names no parameter this project did not already know — it multiplies the
    /// framebuffer purely by being <c>Modulate</c> — so it passed the parameter census in silence
    /// while every capture point drew as a dark slab. A material's shader decides what its
    /// parameters MEAN, so it is the first thing that should be reported as unhandled, not the one
    /// thing that never was.
    ///
    /// Names only what changes the picture. A shader this project treats as its generic case is
    /// not "unimplemented" in the sense that matters, so the list below is the set whose behaviour
    /// is actually reproduced rather than every string TF2 ships.
    /// </remarks>
    public static IReadOnlyList<(string Shader, int Materials)> UnimplementedShaders(
        IEnumerable<string?> shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);

        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);

        foreach (string? shader in shaders)
        {
            // **Null, not just empty.** ResolvedMaterial is a record STRUCT, so a defaulted one has
            // a null Shader however the positional parameter's `= ""` reads — a material that could
            // not be resolved at all arrives that way. Same shape as every other default this
            // project has been bitten by: the value is legal, nothing reports it, and here it threw
            // rather than lying, which is the better half of the bargain.
            if (string.IsNullOrEmpty(shader) || ImplementedShaders.Contains(shader))
            {
                continue;
            }

            counts[shader] = counts.TryGetValue(shader, out int seen) ? seen + 1 : 1;
        }

        return
        [
            .. counts
                .Select(entry => (Shader: entry.Key, Materials: entry.Value))
                .OrderByDescending(entry => entry.Materials)
                .ThenBy(entry => entry.Shader, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>The parameters this project reads, for a test that checks the claim.</summary>
    /// <remarks>
    /// **Exposed because a wrong entry in this set silences the report meant to catch it.** A name
    /// listed here stops being censused, so a parameter claimed and never read is invisible for
    /// ever — which is exactly what happened to <c>$modblend</c> and <c>$decalscale</c>, both listed
    /// and read by nothing. They were caught by diffing this set against the SDK's own declarations,
    /// and that diff needs to see the set.
    /// </remarks>
    internal static IReadOnlyCollection<string> ImplementedParameters => Implemented;

    /// <summary>The shaders this project reproduces, for the same check.</summary>
    internal static IReadOnlyCollection<string> ImplementedShaderNames => ImplementedShaders;

    /// <summary>The shaders whose behaviour this project actually reproduces.</summary>
    /// <remarks>
    /// Everything else falls back to a lit or unlit base texture, which is right often enough to
    /// hide a wrong one — <c>Modulate</c> looked exactly like an opaque material until someone
    /// stood in front of a capture point.
    /// </remarks>
    private static readonly HashSet<string> ImplementedShaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "LightmappedGeneric",
        "VertexLitGeneric",
        "UnlitGeneric",
        "WorldVertexTransition",
        "UnLitTwoTexture",
        "Modulate",
        "Patch",
    };

    /// <summary>Which unimplemented parameters a map's materials ask for, commonest first.</summary>
    /// <param name="declared">Each material's declared parameter names, one collection per material.</param>
    /// <returns>Parameter and the number of materials declaring it, descending by count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declared"/> is null.</exception>
    /// <remarks>
    /// **Counted once per material, not once per declaration.** A patched material can name the
    /// same key in the patch and in what it includes, and counting declarations would report more
    /// materials than the map contains — a number that gets quoted into a document and then
    /// disbelieved.
    ///
    /// Ties break by name so the same map always logs the same order; a census that reshuffles
    /// between runs cannot be diffed, which is most of what it is for.
    /// </remarks>
    public static IReadOnlyList<(string Parameter, int Materials)> Unimplemented(
        IEnumerable<IReadOnlyCollection<string>> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);

        foreach (IReadOnlyCollection<string> material in declared)
        {
            if (material is null)
            {
                continue;
            }

            foreach (string parameter in material
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Where(name => !Implemented.Contains(name) && !NoRenderingEffect.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[parameter] = counts.TryGetValue(parameter, out int seen) ? seen + 1 : 1;
            }
        }

        return
        [
            .. counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => (pair.Key, pair.Value)),
        ];
    }
}
