using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A particle operator's parameters held as sorted arrays, behind the dictionary's read contract (finding 80).</summary>
/// <remarks>
/// **A dictionary per operator was most of 239 MB per map** (`Entry[]` 184 MB on f12). The reads are by name, a handful
/// per operator, so sorted keys and a binary search answer them with one array of each instead of a hash table.
/// </remarks>
public sealed class ParticleParameterDictionaryTests
{
    private static readonly Dictionary<string, DmxValue> Declared = new(System.StringComparer.Ordinal)
    {
        ["radius_min"] = new DmxValue(DmxAttributeType.Real, Number: 2d),
        ["Radius_min"] = new DmxValue(DmxAttributeType.Real, Number: 7d),
        ["material"] = new DmxValue(DmxAttributeType.Text, Text: "effects/x.vmt"),
        ["color1"] = new DmxValue(DmxAttributeType.Colour, Vector: new System.Numerics.Vector4(1f, 2f, 3f, 4f)),
    };

    [Test]
    public void TryGetValue_EveryDeclaredName_IsTheDictionarysValueOrdinally()
    {
        ParticleParameterDictionary compact = ParticleParameterDictionary.Of(Declared);

        compact.Count.ShouldBe(4);

        foreach ((string name, DmxValue value) in Declared)
        {
            compact.TryGetValue(name, out DmxValue found).ShouldBeTrue(name);
            found.ShouldBe(value, name);
            compact[name].ShouldBe(value, name);
        }

        compact.ToDictionary(pair => pair.Key, pair => pair.Value).ShouldBe(Declared, ignoreOrder: true);
    }

    [Test]
    public void TryGetValue_AnAbsentName_IsFalse()
    {
        ParticleParameterDictionary compact = ParticleParameterDictionary.Of(Declared);

        compact.TryGetValue("radius_max", out _).ShouldBeFalse();
        compact.TryGetValue("RADIUS_MIN", out _).ShouldBeFalse("ordinal, as the DMX reader's dictionary is");
        compact.ContainsKey("material").ShouldBeTrue();
        ParticleParameterDictionary.Of(new Dictionary<string, DmxValue>()).TryGetValue("x", out _).ShouldBeFalse();
    }
}
