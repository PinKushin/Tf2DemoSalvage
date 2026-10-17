using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The rule for an entity property's storage key (B234, D217 step 4 part 3).</summary>
public sealed class PropertyKeysTests
{
    private static FlatProperty Definition(string table, string name, string path = "", bool elementScoped = false) =>
        new(new SendProperty(SendPropType.Int, name, 0, string.Empty, 0f, 0f, 32, 0), table, null, path, elementScoped);

    /// <remarks>**`Table.Prop`, or the path for an element-scoped property**, whose flat name collides by construction.</remarks>
    [Test]
    public void Of_FlatAndElementScoped_AreTableDotPropAndThePath()
    {
        (PropertyKeys.Of(Definition("DT_KeysTest", "m_iHealth")),
                PropertyKeys.Of(Definition("DT_KeysTest", "m_iRawValue32", "a.m_Attributes.001.m_iRawValue32", elementScoped: true)),
                PropertyKeys.Of(Definition("DT_KeysTest", "m_iRawValue32", "", elementScoped: true)))
            .ShouldBe(("DT_KeysTest.m_iHealth", "a.m_Attributes.001.m_iRawValue32", "DT_KeysTest.m_iRawValue32"));
    }
}
