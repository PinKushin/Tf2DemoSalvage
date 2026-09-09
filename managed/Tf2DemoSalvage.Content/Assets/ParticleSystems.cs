using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One operator, initializer, emitter or renderer in a particle system.</summary>
/// <param name="Function">The class it names, such as <c>Movement Basic</c>.</param>
/// <param name="Name">Its own name in the file, which is editor text.</param>
/// <param name="Parameters">Everything it declares, by attribute name.</param>
/// <remarks>
/// **One record for all four kinds, because the file makes no structural distinction between
/// them** — they differ only by which array of the definition they appear in, and they carry the
/// same shape of parameter bag. Splitting them into four types here would invent a difference the
/// format does not have.
/// </remarks>
public sealed record ParticleFunction(
    string Function,
    string Name,
    IReadOnlyDictionary<string, DmxValue> Parameters)
{
    /// <summary>A declared number, or a default when the operator does not carry it.</summary>
    /// <param name="named">The attribute name, as the file spells it.</param>
    /// <param name="otherwise">What an absent attribute means.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// **Absent means the DEFAULT and never zero**, which is the trap
    /// `docs/memory/sentinels-conflate-unknown-with-answer.md` names: a `.pcf` omits any parameter
    /// left at its default, so reading absence as zero silently rewrites an effect. Every caller
    /// therefore has to say what the default IS, and cannot get one for free.
    /// </remarks>
    public double Number(string named, double otherwise) =>
        Parameters.TryGetValue(named, out DmxValue value) &&
        value.Type is DmxAttributeType.Real or DmxAttributeType.Whole or DmxAttributeType.Boolean
            ? value.Number
            : otherwise;

    /// <summary>A declared vector, or a default when the operator does not carry it.</summary>
    /// <param name="named">The attribute name.</param>
    /// <param name="otherwise">What an absent attribute means.</param>
    /// <returns>The value.</returns>
    public Vector4 Vector(string named, Vector4 otherwise) =>
        Parameters.TryGetValue(named, out DmxValue value) &&
        value.Type is DmxAttributeType.Vector3 or DmxAttributeType.Vector4
            or DmxAttributeType.Colour or DmxAttributeType.Angle
            ? value.Vector
            : otherwise;

    /// <summary>A declared string, or null.</summary>
    /// <param name="named">The attribute name.</param>
    /// <returns>The value, or null when absent.</returns>
    public string? Text(string named) =>
        Parameters.TryGetValue(named, out DmxValue value) ? value.Text : null;
}

/// <summary>One named particle system, as a <c>.pcf</c> declares it.</summary>
/// <param name="Name">Its name, which is what the game asks for — <c>rockettrail</c>.</param>
/// <param name="Emitters">What decides when particles are born.</param>
/// <param name="Initializers">What sets a new particle's attributes.</param>
/// <param name="Operators">What changes a live particle each frame.</param>
/// <param name="Renderers">What draws them.</param>
/// <param name="Children">Systems this one spawns alongside itself, by name.</param>
/// <param name="Parameters">The definition's own attributes, such as the material.</param>
public sealed record ParticleSystem(
    string Name,
    IReadOnlyList<ParticleFunction> Emitters,
    IReadOnlyList<ParticleFunction> Initializers,
    IReadOnlyList<ParticleFunction> Operators,
    IReadOnlyList<ParticleFunction> Renderers,
    IReadOnlyList<string> Children,
    IReadOnlyDictionary<string, DmxValue> Parameters);

/// <summary>
/// The particle systems a <c>.pcf</c> declares, on top of <see cref="DmxFile"/> (B373).
/// </summary>
/// <remarks>
/// **The second of B373's three parts.** `DmxFile` reads the container; this gives the container its
/// meaning, and a simulator consumes the result. Splitting them is not ceremony: the DMX layout was
/// measured from bytes and the PCF schema is read from the ELEMENTS, so the two have different
/// evidence behind them and different ways of being wrong.
///
/// **The shape, measured on `particles/rockettrail.pcf`:** a `DmeParticleSystemDefinition` carries
/// `emitters`, `initializers`, `operators` and `renderers` as element arrays of
/// `DmeParticleOperator`, plus `children` as an array of `DmeParticleChild`. Every function element
/// names its class in `functionName` — `Movement Basic`, `Lifetime Random`,
/// `render_animated_sprites` — and carries that class's parameters beside it.
///
/// **A parameter a system leaves at its default is ABSENT from the file**, which is why
/// <see cref="ParticleFunction.Number"/> makes every caller state the default rather than handing
/// out zero.
/// </remarks>
public static class ParticleSystems
{
    /// <summary>The element type a particle system definition uses.</summary>
    private const string DefinitionType = "DmeParticleSystemDefinition";

    /// <summary>The element type every emitter, initializer, operator and renderer uses.</summary>
    private const string FunctionType = "DmeParticleOperator";

    /// <summary>The element type a child reference uses.</summary>
    private const string ChildType = "DmeParticleChild";

    /// <summary>Reads every particle system a file declares.</summary>
    /// <param name="file">The whole <c>.pcf</c>.</param>
    /// <returns>Its systems, by name.</returns>
    /// <remarks>
    /// **Keyed by name because that is how the game asks**: `ParticleProp()->Create( "rockettrail",
    /// … )` carries a name and nothing else. A file declaring the same name twice keeps the first,
    /// which matches a lookup built by insertion.
    /// </remarks>
    public static IReadOnlyDictionary<string, ParticleSystem> Read(ReadOnlySpan<byte> file)
    {
        IReadOnlyList<DmxElement> elements = DmxFile.Read(file);

        Dictionary<string, ParticleSystem> systems = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < elements.Count; index++)
        {
            DmxElement element = elements[index];

            if (element.Type != DefinitionType || systems.ContainsKey(element.Name))
            {
                continue;
            }

            systems[element.Name] = new ParticleSystem(
                element.Name,
                Functions(elements, element, "emitters"),
                Functions(elements, element, "initializers"),
                Functions(elements, element, "operators"),
                Functions(elements, element, "renderers"),
                ChildNames(elements, element),
                element.Attributes);
        }

        return systems;
    }

    /// <summary>The functions one of a definition's arrays points at.</summary>
    private static List<ParticleFunction> Functions(
        IReadOnlyList<DmxElement> elements, DmxElement definition, string array)
    {
        if (!definition.Attributes.TryGetValue(array, out DmxValue value) ||
            value.Elements is not { Count: > 0 } references)
        {
            return [];
        }

        List<ParticleFunction> functions = new(references.Count);

        foreach (int reference in references)
        {
            if (reference < 0 || reference >= elements.Count)
            {
                continue;
            }

            DmxElement element = elements[reference];

            if (element.Type != FunctionType)
            {
                continue;
            }

            functions.Add(new ParticleFunction(
                element.Attributes.TryGetValue("functionName", out DmxValue named) &&
                    named.Text is { Length: > 0 } function
                    ? function
                    : string.Empty,
                element.Name,
                element.Attributes));
        }

        return functions;
    }

    /// <summary>The systems a definition spawns alongside itself.</summary>
    /// <remarks>
    /// **A child is a `DmeParticleChild` whose own name is the system it refers to**, which is how
    /// `rockettrail` pulls in `rockettrail_fire` and `rockettrail_burst`. The reference is by NAME
    /// rather than by index, so a child can live in another file.
    /// </remarks>
    private static List<string> ChildNames(
        IReadOnlyList<DmxElement> elements, DmxElement definition)
    {
        if (!definition.Attributes.TryGetValue("children", out DmxValue value) ||
            value.Elements is not { Count: > 0 } references)
        {
            return [];
        }

        List<string> names = new(references.Count);

        foreach (int reference in references)
        {
            if (reference >= 0 && reference < elements.Count &&
                elements[reference].Type == ChildType &&
                elements[reference].Name is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names;
    }
}
