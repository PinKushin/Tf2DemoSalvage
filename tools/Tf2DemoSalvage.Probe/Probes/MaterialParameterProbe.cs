using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// How many of the materials TF2 ships declare a given parameter, and which ones (B334).
/// </summary>
/// <remarks>
/// **The denominator for "is this parameter worth implementing".** `vmt-blocks` answers what is
/// hidden inside a sub-block and `vmt` prints one file; neither answers how many materials in the
/// whole game state a parameter at all, which is the number that decides whether a gap is a
/// visible defect or a curiosity.
///
/// **It reads through <see cref="VmtMaterial"/> rather than grepping the text**, so a parameter
/// stated inside a <c>&gt;=DX90</c> block is counted exactly when the production reader would see
/// it. A grep would answer a different question and answer it confidently — the reason 5,415
/// materials' <c>$selfillum</c> was invisible for months was that it lives inside such a block.
///
/// **Ask for something that must be there before believing an absence.** `$basetexture` is the
/// control: a run reporting few of those has a broken scan rather than a finding
/// (<c>docs/memory/an-empty-search-needs-a-control.md</c>).
///
/// **A second argument prefixed with <c>!</c> EXCLUDES**, which is the form that answers "how many
/// materials fall back to the engine's default" — a question no single count can reach, because
/// a material stating the default value and one omitting the parameter are indistinguishable
/// afterwards. That distinction corrected B334's own claim: cp_process_final's one material at a
/// phong exponent of 150 turned out to STATE 150 rather than to have fallen back to it, and the
/// assertion that found it had been passing for the wrong reason.
///
/// <code>
///   vmt-param $phongexponenttexture     — how many declare it, and ten that do
///   vmt-param $phong !$phongexponent    — how many fall back to the engine's default
///   vmt-param $basetexture              — the control
/// </code>
/// </remarks>
public sealed class MaterialParameterProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vmt-param";

    /// <inheritdoc/>
    public string Summary =>
        "how many shipped materials declare a parameter: vmt-param <$parameter> [!$excluded]";

    /// <summary>How many materials to name, rather than only count.</summary>
    private const int Named = 10;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("Give a parameter, such as: vmt-param $phongexponenttexture");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        string wanted = arguments[0];

        // An argument such as `!$phongexponent` narrows to materials that do NOT state it.
        string? excluded = arguments.Count > 1 && arguments[1].StartsWith('!')
            ? arguments[1][1..]
            : null;

        int materials = 0;
        int unreadable = 0;
        int declaring = 0;
        int control = 0;

        // What the parameter is SET to, which separates "300 materials say 1" from a real spread.
        Dictionary<string, int> values = new(StringComparer.OrdinalIgnoreCase);
        List<string> carrying = [];

        // Which shaders ask for it. A parameter concentrated in one shader is a different piece of
        // work from one spread across five.
        Dictionary<string, int> shaders = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in game.Archives.Paths()
            .Where(path => path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (game.Archives.Read(path) is not { } bytes)
            {
                unreadable++;
                continue;
            }

            materials++;

            VmtMaterial material;

            try
            {
                material = VmtMaterial.Parse(bytes);
            }
            catch (InvalidDataException)
            {
                // A file the reader refuses is counted as unreadable rather than as absent, which
                // is the distinction that stops a parser bug reading as a fact about the game.
                unreadable++;
                continue;
            }

            if (material.Value("$basetexture") is not null)
            {
                control++;
            }

            if (material.Value(wanted) is not { } value ||
                (excluded is not null && material.Value(excluded) is not null))
            {
                continue;
            }

            declaring++;
            values[value] = values.GetValueOrDefault(value) + 1;
            shaders[material.Shader] = shaders.GetValueOrDefault(material.Shader) + 1;

            if (carrying.Count < Named)
            {
                carrying.Add(path);
            }
        }

        output.WriteLine($"{materials} materials read ({unreadable} unreadable)");
        output.WriteLine($"{control} declare $basetexture — the control");
        output.WriteLine(
            $"{declaring} declare {wanted}"
            + (excluded is null ? string.Empty : $" and do NOT declare {excluded}"));

        if (declaring == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine("by shader:");

        foreach ((string shader, int count) in shaders.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine($"  {count,6}  {shader}");
        }

        output.WriteLine();
        output.WriteLine("by value:");

        foreach ((string value, int count) in values.OrderByDescending(entry => entry.Value)
            .Take(Named))
        {
            output.WriteLine($"  {count,6}  {value}");
        }

        output.WriteLine();
        output.WriteLine("carrying it:");

        foreach (string path in carrying)
        {
            output.WriteLine($"  {path}");
        }
    }
}
