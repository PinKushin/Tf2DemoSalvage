using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A fixed array's element keys, built once (B613), must be the strings the decoder stores them under.</summary>
/// <remarks>
/// A wrong key is silent: the lookup finds nothing and the value takes its default, so a cached key that differed from
/// the formatted one by a digit would read as an entity that never sent the property. These pin the spelling, inside the
/// built range and past it.
/// </remarks>
public sealed class ElementKeysTests
{
    [TestCase(0, "m_hMyWeapons.000")]
    [TestCase(7, "m_hMyWeapons.007")]
    [TestCase(47, "m_hMyWeapons.047")]
    public void Indexer_InsideTheBuiltRange_IsThePrefixAndAThreeDigitElement(int element, string expected) =>
        new ElementKeys("m_hMyWeapons", 48)[element].ShouldBe(expected);

    [TestCase(48, "m_hMyWeapons.048")]
    [TestCase(1234, "m_hMyWeapons.1234")]
    public void Indexer_PastTheBuiltRange_IsFormattedTheSameWay(int element, string expected) =>
        new ElementKeys("m_hMyWeapons", 48)[element].ShouldBe(expected);

    [Test]
    public void Indexer_InsideTheBuiltRange_ReturnsOneInstancePerElement()
    {
        ElementKeys keys = new("m_iAmmo", 32);

        keys[5].ShouldBeSameAs(keys[5]);
    }
}
