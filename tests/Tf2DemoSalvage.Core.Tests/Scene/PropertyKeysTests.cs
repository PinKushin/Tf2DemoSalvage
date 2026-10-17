using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>An entity property's storage key, one string per key (D217 step 4, part 3).</summary>
public sealed class PropertyKeysTests
{
    private static FlatProperty Definition(string table, string name, string path = "", bool elementScoped = false) =>
        new(new SendProperty(SendPropType.Int, new string(name), 0, string.Empty, 0f, 0f, 32, 0), new string(table), null, path, elementScoped);

    /// <remarks>
    /// **The same key is the same instance**: two definitions built from separately allocated names give one string,
    /// <c>Table.Prop</c>; an element-scoped one keys by its path.
    /// </remarks>
    [Test]
    public void Of_TwoEqualDefinitions_IsOneInstance()
    {
        string first = PropertyKeys.Of(Definition("DT_KeysTest", "m_iHealth"));
        string second = PropertyKeys.Of(Definition("DT_KeysTest", "m_iHealth"));

        first.ShouldBe("DT_KeysTest.m_iHealth");
        second.ShouldBeSameAs(first);
        PropertyKeys.Of(Definition("DT_KeysTest", "m_iRawValue32", "a.m_Attributes.001.m_iRawValue32", elementScoped: true))
            .ShouldBe("a.m_Attributes.001.m_iRawValue32");
    }
}
