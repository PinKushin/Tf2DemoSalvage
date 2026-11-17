using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A viewmodel wearable leaves every world list (B771, `econ_wearable.cpp:375-393`).</summary>
public sealed class ViewModelWearablesTests
{
    /// <remarks>
    /// `bUseViewModel` is false for every player a world list draws, so each `CTFWearableVM` goes, in order, to the
    /// first-person pass, and the `CTFWearable`s stay where they were, in order, the control.
    /// </remarks>
    [Test]
    public void MoveOut_ViewmodelWearablesAmongWorldOnes_TakesThemAndKeepsTheRestInOrder()
    {
        List<SceneProp> drawn =
        [
            Item(1, ViewModelWearables.ClassName), Item(2, "CTFWearable"), Item(3, ViewModelWearables.ClassName),
            Item(4, "CTFWearable"),
        ];
        List<SceneProp> taken = [];

        ViewModelWearables.MoveOut(drawn, taken);

        (string.Join(",", drawn.Select(prop => prop.EntityIndex)), string.Join(",", taken.Select(prop => prop.EntityIndex)))
            .ShouldBe(("2,4", "1,3"));
    }

    private static SceneProp Item(int entity, string className) =>
        new(entity, "models/player/items/demo/hat.mdl", SceneModelKind.Studio, new ScenePose(), AttachedTo: 3, OwnedBy: 3)
        {
            ClassName = className,
        };
}
