using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Probe;

/// <summary>
/// One question asked of a real demo, answered in numbers.
/// </summary>
/// <remarks>
/// **A probe reports and asserts nothing** (D38). It exists to find out what is actually in a
/// recording — how many props a spy is wearing, which weapon a player holds, where a material came
/// from — and the answer is read by a person, not by a runner.
///
/// **Adding one means adding a file and nothing else.** <see cref="Program"/> discovers
/// implementations by reflection, so there is no dispatch table to edit and no registration to
/// forget. That is the open/closed rule the repository's standards ask for, and here it also
/// removes the one thing that made probes-as-tests tempting: NUnit discovered them for free.
/// </remarks>
public interface IProbe
{
    /// <summary>The name typed on the command line. Kebab case.</summary>
    public string Name { get; }

    /// <summary>One line saying what question this answers, for the listing.</summary>
    public string Summary { get; }

    /// <summary>Runs the probe.</summary>
    /// <param name="output">Where the report goes.</param>
    /// <param name="arguments">Anything after the probe's name.</param>
    public void Run(TextWriter output, IReadOnlyList<string> arguments);
}
