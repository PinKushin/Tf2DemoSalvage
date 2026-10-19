using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>Every shipped particle definition read through the compact parameters, against the DMX reader's own (finding 80).</summary>
/// <remarks>
/// **The oracle is the old form itself**: <see cref="DmxFile.Read"/>'s attribute dictionaries, which the definitions used to
/// carry as they were. Every parameter of every definition and every function, in every file the manifest names, must read
/// back the same value through <see cref="ParticleParameterDictionary"/>, with nothing added and nothing lost.
/// </remarks>
[TestFixture]
public sealed class ParticleDefinitionSweepConformanceTests
{
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Read_EveryManifestDefinition_ReadsEveryParameterAsTheDmxReaderDid()
    {
        GameArchives archives = GameArchives.Open(GameInstall.Require());
        int definitions = 0;
        int parameters = 0;

        foreach (string path in ParticleManifest.Files(archives.Read))
        {
            if (archives.Read(path) is not { Length: > 0 } file)
            {
                continue;
            }

            IReadOnlyList<DmxElement> elements = DmxFile.Read(file);
            IReadOnlyDictionary<string, ParticleSystem> systems = ParticleSystems.Read(file);

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            // A repeated name keeps the LATER one (B555), but no shipped file repeats one (ParticleDuplicateNamesDiagnostic),
            // so the first met is the only one; the guard only skips what the dictionary could not hold twice.
            foreach (DmxElement definition in elements.Where(element => element.Type == "DmeParticleSystemDefinition"))
            {
                if (!seen.Add(definition.Name) || !systems.TryGetValue(definition.Name, out ParticleSystem? system))
                {
                    continue;
                }

                definitions++;
                parameters += Same(system.Parameters, definition.Attributes, $"{path}:{definition.Name}");

                foreach ((string array, IReadOnlyList<ParticleFunction> functions) in new[]
                {
                    ("emitters", system.Emitters), ("initializers", system.Initializers), ("operators", system.Operators),
                    ("renderers", system.Renderers), ("constraints", system.Constraints),
                })
                {
                    List<DmxElement> declared = definition.Attributes.TryGetValue(array, out DmxValue value) && value.Elements is { } references
                        ? [.. references.Where(at => at >= 0 && at < elements.Count).Select(at => elements[at]).Where(one => one.Type == "DmeParticleOperator")]
                        : [];

                    functions.Count.ShouldBe(declared.Count, $"{path}:{definition.Name} {array}");

                    for (int at = 0; at < functions.Count; at++)
                    {
                        parameters += Same(functions[at].Parameters, declared[at].Attributes, $"{path}:{definition.Name} {array}[{at}]");
                    }
                }
            }
        }

        definitions.ShouldBeGreaterThan(5000, "the control: the manifest declares thousands of systems");
        parameters.ShouldBeGreaterThan(0);
        TestContext.Out.WriteLine($"{definitions} definitions, {parameters} parameters compared");
    }

    /// <remarks>
    /// **A shared definition must not be written by playing it** (finding 80): one copy serves every map, so a write would
    /// leak between them. The store is hashed, every system the rocket reaches is stepped for five seconds, and hashed again.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Step_EveryDefinitionARocketReaches_LeavesTheStoreUnchanged()
    {
        GameArchives archives = GameArchives.Open(GameInstall.Require());
        Dictionary<string, ParticleSystem> store = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in ParticleManifest.Files(archives.Read))
        {
            if (archives.Read(path) is { Length: > 0 } file)
            {
                foreach ((string name, ParticleSystem one) in ParticleSystems.Read(file))
                {
                    store.TryAdd(name, one);
                }
            }
        }

        string before = Hash(store);
        ParticleSystem trail = store.First(pair => pair.Key.StartsWith("rockettrail", StringComparison.OrdinalIgnoreCase)).Value;
        ParticleEffect effect = new(trail, store);

        for (int tick = 0; tick < 330; tick++)
        {
            effect.Step(ParticleControlPoint.Unoriented(new Vector3(tick, 0f, 0f)), 1f / 66f);
        }

        effect.Particles.Count.ShouldBeGreaterThan(0, "the control: the trail emitted, so its definitions were read");
        Hash(store).ShouldBe(before);
    }

    private static int Same(IReadOnlyDictionary<string, DmxValue> compact, IReadOnlyDictionary<string, DmxValue> oracle, string where)
    {
        compact.Count.ShouldBe(oracle.Count, where);

        foreach ((string name, DmxValue value) in oracle)
        {
            compact.TryGetValue(name, out DmxValue found).ShouldBeTrue($"{where} {name}");
            Text(found).ShouldBe(Text(value), $"{where} {name}");
        }

        return oracle.Count;
    }

    private static string Text(DmxValue value) => string.Create(
        CultureInfo.InvariantCulture,
        $"{value.Type}|{value.Number:R}|{value.Text}|{value.Vector}|{string.Join(',', value.Elements ?? [])}|{string.Join(',', value.Numbers ?? [])}");

    private static string Hash(IReadOnlyDictionary<string, ParticleSystem> store)
    {
        StringBuilder all = new();

        foreach ((string name, ParticleSystem system) in store.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            all.Append(name);

            foreach (IReadOnlyDictionary<string, DmxValue> bag in new[] { system.Parameters }
                .Concat(system.Emitters.Concat(system.Initializers).Concat(system.Operators).Concat(system.Renderers).Concat(system.Constraints)
                    .Select(function => function.Parameters)))
            {
                foreach ((string key, DmxValue value) in bag.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    all.Append(key).Append('=').Append(Text(value)).Append(';');
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString())));
    }
}
