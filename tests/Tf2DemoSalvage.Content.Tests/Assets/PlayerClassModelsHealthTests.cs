using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>`TFPlayerClassData_t::m_nMaxHealth`: the class script's <c>"health"</c> (tf_classdata.cpp).</summary>
public sealed class PlayerClassModelsHealthTests
{
    [Test]
    public void MaxHealth_AScriptSayingHealth125_Is125()
    {
        PlayerClassModels classes = PlayerClassModels.Read(path =>
            path == "scripts/playerclasses/scout.txt" ? Encoding.UTF8.GetBytes("\"PlayerClass\"\n{\n\t\"health\"\t\"125\"\n}\n") : null);

        (classes.MaxHealth(1), classes.MaxHealth(2)).ShouldBe((125, (int?)null));
    }
}
