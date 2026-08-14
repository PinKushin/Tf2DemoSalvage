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
        "$decalscale",
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
