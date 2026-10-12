using System;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A `.phy`'s text section is read by vphysics' own tokenizer, not as KeyValues (B532).</summary>
/// <remarks>
/// **`IVPhysicsKeyParser`, read in `vphysics.dll`:** the block walk `FUN_18002e160` and every block parser beside it
/// (`FUN_18002eeb0` is one) inline the same pair of calls — the tokenizer `FUN_180003640`, then `FUN_1800b9a10` lowering
/// the token, for the key and again for the value:
///
/// <code>
/// NextBlock:   repeat ( key, value ) until value is exactly "{";  the block is named key
/// a block:     repeat ( key, value );  key[0] == '}' → NextBlock and return;  else hand the pair to the handler
/// </code>
///
/// So a value is LOWERED, `/* */` is a comment, `{}()':` are tokens alone, and nothing is conditional.
/// </remarks>
public sealed class PhysicsTextConformanceTests
{
    [Test]
    public void Read_ASolidNamedInCapitals_CarriesTheNameLowered()
    {
        PhysicsModel model = PhysicsModel.Read(Authored("solid {\n\"index\" \"0\"\n\"name\" \"ValveBiped.Bip01_Pelvis\"\n}\n"));

        model.Solids[0].Name.ShouldBe("valvebiped.bip01_pelvis");
    }

    [Test]
    public void Read_ABlockComment_IsSkipped()
    {
        PhysicsModel model = PhysicsModel.Read(Authored("solid {\n\"index\" \"3\"\n/* \"ghost\" */\n\"name\" \"real\"\n}\n"));

        (model.Solids.Count, model.Solids[0].Index, model.Solids[0].Name).ShouldBe((1, 3, "real"));
    }

    /// <remarks>`[$X360]` is three characters of a word to this tokenizer; KeyValues would drop the pair before it.</remarks>
    [Test]
    public void Read_AKeyValuesConditional_IsNotOne()
    {
        PhysicsModel model = PhysicsModel.Read(Authored("solid {\n\"index\" \"0\"\n\"mass\" \"5\"\n\"[$X360]\" \"x\"\n}\n"));

        model.Solids[0].Mass.ShouldBe(5f);
    }

    private static byte[] Authored(string text) =>
    [
        .. BitConverter.GetBytes(16),
        .. BitConverter.GetBytes(0x59485056),
        .. BitConverter.GetBytes(1),
        .. BitConverter.GetBytes(0),
        .. Encoding.ASCII.GetBytes(text),
    ];
}
