using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Where an entity's sounds are: `C_BaseEntity::GetSoundSpatialization` (`c_baseentity.cpp:1864`), the abs origin, and
/// for a brush model the middle of its bounds added (`:1891-1899`), B724.
/// </summary>
public sealed class SoundOriginsConformanceTests
{
    private static readonly BspModel[] Brushes =
    [
        new((-8f, -8f, -8f), (8f, 8f, 8f), default, 0, 0, 0),
        new((0f, -20f, 10f), (40f, 20f, 50f), default, 0, 0, 0),
    ];

    private static readonly ScenePose At = new() { X = 100f, Y = 200f, Z = 300f };

    [Test]
    public void Of_ABrushModel_IsItsOriginPlusTheMiddleOfItsBounds()
    {
        SoundOrigins.Of(At, "*1", Brushes).ShouldBe((120f, 200f, 330f));
    }

    [TestCase("models/props/door.mdl")]
    [TestCase(null)]
    [TestCase("*9")]
    public void Of_AStudioModelNoModelOrABrushTheMapDoesNotHave_IsItsOrigin(string? model)
    {
        SoundOrigins.Of(At, model, Brushes).ShouldBe((100f, 200f, 300f));
    }

    [Test]
    public void Of_NoBrushModelsRead_IsItsOrigin()
    {
        SoundOrigins.Of(At, "*1", null).ShouldBe((100f, 200f, 300f));
    }
}
